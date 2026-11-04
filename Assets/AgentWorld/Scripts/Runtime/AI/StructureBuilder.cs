using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    /// Spawns agent-built structures: picks a free site, animates construction, persists the result.
    public class StructureBuilder : MonoBehaviour
    {
        public static StructureBuilder Instance { get; private set; }
        public const int MaxStructures = 12;

        public GameObject housePrefab;
        /// Fenced crop field: same footprint as a house, trigger collider so people can walk through it.
        public GameObject farmPrefab;
        /// Playable area for new buildings (feet position) and the lab interior to keep clear.
        public Rect buildArea = new Rect(-18, -13, 36, 25);
        public Rect keepClear = new Rect(-7.5f, 2.5f, 15, 11);
        /// Village roads (WorldBuilder's Path_H / Path_V, padded) never get built on.
        public Rect[] roads = { new Rect(-20, -3.4f, 40, 2.8f), new Rect(-1.4f, -15, 2.8f, 18.5f) };
        public float buildSeconds = 4f;

        static readonly Vector2 Footprint = new Vector2(2.8f, 1.4f), FootOffset = new Vector2(0, 0.7f);
        const float Margin = 1.2f; // leaves a walkway around every house so nobody gets boxed in

        public IEnumerable<BuiltStructure> Structures => FindObjectsByType<BuiltStructure>(FindObjectsSortMode.None).OrderBy(s => s.id);

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public bool IsFree(Vector2 feet, float margin = Margin)
        {
            var foot = new Rect(feet.x - 1.4f, feet.y, 2.8f, 1.4f);
            if (!buildArea.Contains(feet) || keepClear.Overlaps(foot) || roads.Any(r => r.Overlaps(foot))) return false;
            return Physics2D.OverlapBox(feet + FootOffset, Footprint + Vector2.one * margin, 0f) == null;
        }

        /// Nearest free spot to `near`, searched in rings; null when the area is full.
        public Vector2? FindSite(Vector2 near)
        {
            for (var r = 2.5f; r <= 9f; r += 1f)
            for (var i = 0; i < 16; i++)
            {
                var ang = i * Mathf.PI / 8f;
                var p = near + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                p = new Vector2(Mathf.Round(p.x * 2) / 2, Mathf.Round(p.y * 2) / 2);
                if (IsFree(p)) return p;
            }
            return null;
        }

        /// How many houses still fit near a place (same ring search as FindSite, sites kept apart). Deterministic.
        public int CountSites(Vector2 near, int max = 5)
        {
            var found = new List<Vector2>();
            for (var r = 2.5f; r <= 9f && found.Count < max; r += 1f)
            for (var i = 0; i < 16 && found.Count < max; i++)
            {
                var ang = i * Mathf.PI / 8f;
                var p = near + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                p = new Vector2(Mathf.Round(p.x * 2) / 2, Mathf.Round(p.y * 2) / 2);
                if (IsFree(p) && found.All(f => Mathf.Abs(f.x - p.x) > Footprint.x + Margin || Mathf.Abs(f.y - p.y) > Footprint.y + Margin)) found.Add(p);
            }
            return found.Count;
        }

        public GameObject Prefab(string type) => type == "house" ? housePrefab : type == "farm" ? farmPrefab : null;

        /// Visible construction: scaffolding and dust while the structure rises semi-transparent, then it becomes solid.
        public async Task<BuiltStructure> Construct(string type, Vector2 site, string builder, Action<float> progress, CancellationToken ct)
        {
            var prefab = Prefab(type) ?? throw new Exception("estrutura indisponível: " + type);
            if (Structures.Count() >= MaxStructures) throw new Exception($"limite de {MaxStructures} construções atingido");

            var go = Instantiate(prefab, site, Quaternion.identity);
            var id = NextId(type);
            var scaffold = new GameObject("Scaffold").AddComponent<SpriteRenderer>();
            scaffold.transform.position = site;
            scaffold.sprite = RuntimeArt.Scaffold();
            scaffold.sortingOrder = 30;
            var nextPuff = 0f;
            go.name = "Built_" + id;
            var col = go.GetComponent<Collider2D>();
            var sr = go.GetComponent<SpriteRenderer>();
            if (col) col.enabled = false;
            try
            {
                for (var t = 0f; t < buildSeconds; t += Time.deltaTime)
                {
                    var k = t / buildSeconds;
                    go.transform.localScale = new Vector3(1, Mathf.Lerp(0.15f, 1f, k), 1);
                    if (sr) sr.color = new Color(1, 1, 1, Mathf.Lerp(0.3f, 0.85f, k));
                    progress?.Invoke(k);
                    if (t >= nextPuff) { FxPuff.Spawn(site + new Vector2(UnityEngine.Random.Range(-1.3f, 1.3f), UnityEngine.Random.Range(0f, 0.4f))); nextPuff = t + 0.2f; }
                    await Awaitable.NextFrameAsync(ct);
                }
                // Don't close the walls on someone standing in the footprint (farms are walkable).
                while (col && !col.isTrigger && Physics2D.OverlapBoxAll(site + FootOffset, Footprint, 0f).Any(c => c.attachedRigidbody != null))
                    await Awaitable.WaitForSecondsAsync(0.25f, ct);
            }
            catch { Destroy(go); throw; }
            finally { Destroy(scaffold.gameObject); }

            go.transform.localScale = Vector3.one;
            if (sr) sr.color = Color.white;
            if (col) col.enabled = true;
            progress?.Invoke(1f);
            return Register(go, id, type, builder);
        }

        BuiltStructure Register(GameObject go, string id, string type, string builder)
        {
            var b = go.AddComponent<BuiltStructure>();
            b.id = id; b.type = type; b.builtBy = builder;
            var lm = go.AddComponent<Landmark>();
            lm.id = id;
            lm.description = $"{type} construída por {builder}";
            return b;
        }

        string NextId(string type)
        {
            var n = 1;
            while (Structures.Any(s => s.id == $"{type}_built_{n}")) n++;
            return $"{type}_built_{n}";
        }

        public List<StructureState> Capture() =>
            Structures.Select(s => new StructureState { id = s.id, type = s.type, builtBy = s.builtBy, position = s.transform.position }).ToList();

        public void Restore(List<StructureState> states)
        {
            foreach (var s in Structures) { s.gameObject.SetActive(false); Destroy(s.gameObject); }
            if (states == null) return;
            foreach (var s in states.Where(s => Prefab(s.type) != null).Take(MaxStructures))
            {
                var go = Instantiate(Prefab(s.type), s.position, Quaternion.identity);
                go.name = "Built_" + s.id;
                Register(go, s.id, s.type, s.builtBy);
            }
        }
    }
}
