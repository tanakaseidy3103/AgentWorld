using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AgentWorld
{
    [Serializable] public class EntityState { public string id; public Vector2 position; }
    [Serializable] public class ComputerState { public string id; public List<string> log = new List<string>(); }
    [Serializable] public class StructureState { public string id, type, builtBy; public Vector2 position; }
    [Serializable] public class AgentState { public string id, role, goal, status, lastResult; public List<string> memory = new List<string>(); }

    [Serializable]
    public class WorldState
    {
        // v2 adds structures + agents; v1 saves load with both lists empty.
        public int version = 3;
        public Vector2 player;
        public List<EntityState> npcs = new List<EntityState>();
        public List<ComputerState> computers = new List<ComputerState>();
        public List<StructureState> structures = new List<StructureState>();
        public List<AgentState> agents = new List<AgentState>();
        public MissionState mission = new MissionState(); // v3
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
}
