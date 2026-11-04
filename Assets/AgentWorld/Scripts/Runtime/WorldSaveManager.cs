using System.Collections.Generic;
using UnityEngine;

namespace AgentWorld
{
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
            {
                s.npcs.Add(new EntityState { id = n.id, position = n.transform.position });
                if (n.Ai != null) s.agents.Add(n.Ai.Capture());
            }
            var builder = FindAnyObjectByType<StructureBuilder>();
            if (builder) s.structures = builder.Capture();
            s.mission = MissionControl.Capture();
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
                var a = s.agents?.Find(x => x.id == n.id);
                if (a != null) n.Ai?.Restore(a);
            }
            var builder = FindAnyObjectByType<StructureBuilder>();
            if (builder) builder.Restore(s.structures);
            MissionControl.Restore(s.mission);
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
            GUI.Label(new Rect(10, 8, 800, 22), "WASD mover | E interagir (PC = terminal, NPC = conversa IA) | M missão | Tab painel | F5 salvar | F9 carregar | terminal: help");
            if (Time.time < toastUntil) GUI.Box(new Rect(Screen.width - 210, 10, 200, 26), toast);
        }
    }
}
