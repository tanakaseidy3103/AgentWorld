using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AgentWorld
{
    [Serializable]
    public class AgentConfig
    {
        public string id, role, title, personality;
        public List<string> skills = new List<string>();
    }

    [Serializable]
    public class AgentConfigFile
    {
        public int version = 1;
        public List<AgentConfig> agents = new List<AgentConfig>();
    }

    /// Agent setup from StreamingAssets/agents.json (editable next to the built .exe too).
    /// Configures the existing NPCs (matched by id); it does not spawn new characters.
    public static class AgentConfigs
    {
        public const string FileName = "agents.json";
        public static readonly string[] Roles = { "assistant", "explorer", "builder" };

        static AgentConfigFile current;
        public static List<string> Errors { get; private set; } = new List<string>();
        public static string Source { get; private set; } = "defaults";

        public static AgentConfigFile Current => current ??= Load();

        public static AgentConfigFile Defaults() => new AgentConfigFile
        {
            agents =
            {
                new AgentConfig { id = "ada", role = "assistant", title = "Coordinator",
                    personality = "friendly guide who explains the world and tells the player which agent to ask",
                    skills = { "move_to", "inspect", "speak", "wait" } },
                new AgentConfig { id = "grace", role = "explorer", title = "Explorer",
                    personality = "curious and observant; loves describing what is around",
                    skills = { "move_to", "inspect", "report_findings", "speak", "wait" } },
                new AgentConfig { id = "linus", role = "builder", title = "Builder",
                    personality = "practical, cheerful and proud of solid work; speaks briefly",
                    skills = { "move_to", "inspect", "build_house", "build_farm", "speak", "wait" } },
            },
        };

        static AgentConfigFile Load()
        {
            var path = Path.Combine(Application.streamingAssetsPath, FileName);
            if (!File.Exists(path)) { Errors = new List<string>(); Source = "defaults"; return Defaults(); }
            var cfg = Parse(File.ReadAllText(path), out var errors);
            Errors = errors;
            Source = FileName;
            foreach (var e in errors) Debug.LogWarning($"AgentWorld {FileName}: {e}");
            return cfg ?? Defaults();
        }

        /// Parses and validates; invalid skills are dropped (and reported), never silently kept.
        public static AgentConfigFile Parse(string json, out List<string> errors)
        {
            errors = new List<string>();
            AgentConfigFile cfg;
            try { cfg = JsonUtility.FromJson<AgentConfigFile>(json); }
            catch (Exception e) { errors.Add("invalid JSON: " + e.Message + " (using defaults)"); return null; }
            if (cfg?.agents == null || cfg.agents.Count == 0) { errors.Add("no agents defined (using defaults)"); return null; }

            var seen = new HashSet<string>();
            foreach (var a in cfg.agents.ToList())
            {
                a.id = (a.id ?? "").Trim().ToLowerInvariant();
                a.role = (a.role ?? "").Trim().ToLowerInvariant();
                if (a.id == "" || !seen.Add(a.id)) { errors.Add($"agent with empty or duplicate id '{a.id}' ignored"); cfg.agents.Remove(a); continue; }
                if (!Roles.Contains(a.role)) { errors.Add($"{a.id}: unknown role '{a.role}' (use {string.Join(", ", Roles)})"); cfg.agents.Remove(a); continue; }
                a.skills = (a.skills ?? new List<string>()).Select(s => (s ?? "").Trim().ToLowerInvariant()).Distinct().ToList();
                foreach (var s in a.skills.ToList())
                {
                    var skill = SkillRegistry.Get(s);
                    if (skill == null) { errors.Add($"{a.id}: unknown skill '{s}' removed"); a.skills.Remove(s); }
                    else if (!skill.Roles.Contains(a.role)) { errors.Add($"{a.id}: skill '{s}' is not allowed for role {a.role}; removed"); a.skills.Remove(s); }
                }
            }
            return cfg.agents.Count == 0 ? null : cfg;
        }

        /// Tests and tools can swap the config at runtime.
        public static void Use(AgentConfigFile cfg, List<string> errors = null)
        {
            current = cfg ?? Defaults();
            Errors = errors ?? new List<string>();
            Source = cfg == null ? "defaults" : "runtime";
        }

        /// Re-read agents.json on next access.
        public static void Reload() => current = null;

        public static AgentConfig For(string id) => Current.agents.FirstOrDefault(a => a.id == id);

        public static List<string> SkillsFor(string role) =>
            Current.agents.FirstOrDefault(a => a.role == role)?.skills ?? new List<string>();

        public static bool Has(string role, string skill) => SkillsFor(role).Contains(skill);
    }
}
