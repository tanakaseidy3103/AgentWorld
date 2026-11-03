using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AgentWorld
{
    [Serializable] public class EntityState { public string id; public Vector2 position; }
    [Serializable] public class ComputerState { public string id; public List<string> log = new List<string>(); }

    [Serializable]
    public class WorldState
    {
        public int version = 1;
        public Vector2 player;
        public List<EntityState> npcs = new List<EntityState>();
        public List<ComputerState> computers = new List<ComputerState>();
    }

    public static class SaveSystem
    {
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "world.json");

        public static void Save(WorldState state, string path)
        {
            // Write-then-swap so a crash mid-write never corrupts the previous save.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(state, true));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public static WorldState Load(string path)
        {
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<WorldState>(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogWarning($"Save invalido em {path}: {e.Message}"); return null; }
        }
    }

    /// Captures/applies world state. F5 saves, F9 loads; loads on start and saves on quit.
    public class WorldSaveManager : MonoBehaviour
    {
        public static string PathOverride; // tests point this at a temp file
        public bool autoLoad = true;

        public string SavePath => PathOverride ?? SaveSystem.DefaultPath;
        string toast;
        float toastUntil;

        void Start()
        {
            if (autoLoad && Load()) Notify("Mundo carregado");
        }

        void Update()
        {
            if (TerminalUI.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.F5)) { Save(); Notify("Mundo salvo"); }
            if (Input.GetKeyDown(KeyCode.F9)) Notify(Load() ? "Mundo carregado" : "Nenhum save encontrado");
        }

        void OnApplicationQuit() => Save();

        public WorldState Capture()
        {
            var s = new WorldState();
            var player = FindAnyObjectByType<PlayerController>();
            if (player) s.player = player.transform.position;
            foreach (var n in FindObjectsByType<NpcAgent>(FindObjectsSortMode.None))
                s.npcs.Add(new EntityState { id = n.id, position = n.transform.position });
            foreach (var c in FindObjectsByType<Computer>(FindObjectsSortMode.None))
                s.computers.Add(new ComputerState { id = c.id, log = new List<string>(c.log) });
            return s;
        }

        public void Apply(WorldState s)
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player) player.Teleport(s.player);
            foreach (var n in FindObjectsByType<NpcAgent>(FindObjectsSortMode.None))
            {
                var e = s.npcs.Find(x => x.id == n.id);
                if (e != null) n.Teleport(e.position);
            }
            foreach (var c in FindObjectsByType<Computer>(FindObjectsSortMode.None))
            {
                var e = s.computers.Find(x => x.id == c.id);
                if (e != null) c.log = new List<string>(e.log);
            }
        }

        public void Save() => SaveSystem.Save(Capture(), SavePath);

        public bool Load()
        {
            var s = SaveSystem.Load(SavePath);
            if (s == null) return false;
            Apply(s);
            return true;
        }

        void Notify(string msg) { toast = msg; toastUntil = Time.time + 2f; }

        void OnGUI()
        {
            GUI.Label(new Rect(10, 8, 600, 22), "WASD mover | E interagir | F5 salvar | F9 carregar");
            if (Time.time < toastUntil) GUI.Box(new Rect(Screen.width - 210, 10, 200, 26), toast);
        }
    }
}
