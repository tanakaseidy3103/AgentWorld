using System.Collections.Generic;
using UnityEngine;

namespace AgentWorld
{
    /// Survey flags at every place Grace reported to shared memory (green = buildable, red = full).
    /// Derived from mission state, so flags come back after loading a save.
    public class InspectionMarkers : MonoBehaviour
    {
        readonly Dictionary<string, SpriteRenderer> flags = new Dictionary<string, SpriteRenderer>();
        readonly Dictionary<string, int> freeSites = new Dictionary<string, int>();

        void Update()
        {
            var reports = MissionControl.State.sharedMemory;
            var seen = new HashSet<string>();
            foreach (var r in reports)
            {
                seen.Add(r.location);
                var lm = Landmark.Find(r.location);
                if (lm == null) continue;
                if (!flags.TryGetValue(r.location, out var sr))
                {
                    var go = new GameObject("Flag_" + r.location);
                    go.transform.SetParent(transform, false);
                    go.transform.position = (Vector2)lm.transform.position + new Vector2(1.6f, 0.4f);
                    sr = flags[r.location] = go.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = 1;
                }
                sr.sprite = RuntimeArt.Flag(r.freeSites > 0);
                freeSites[r.location] = r.freeSites;
            }
            foreach (var id in new List<string>(flags.Keys))
                if (!seen.Contains(id)) { Destroy(flags[id].gameObject); flags.Remove(id); freeSites.Remove(id); }
        }

        void OnGUI()
        {
            if (SkillLibraryPanel.Visible) return; // the library is modal; world labels would draw over it
            var cam = Camera.main;
            if (cam == null || flags.Count == 0) return;
            var size = Mathf.Max(12, TerminalUI.FontSize(Screen.height) * 2 / 3);
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            foreach (var kv in flags)
            {
                var p = cam.WorldToScreenPoint(kv.Value.transform.position + Vector3.up * 1.25f);
                if (p.z < 0) continue;
                GUI.Label(new Rect(p.x - size * 3, Screen.height - p.y - size, size * 6, size * 1.4f), $"{freeSites[kv.Key]} free", style);
            }
        }
    }
}
