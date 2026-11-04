using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgentWorld
{
    [Serializable]
    public class AgentAction
    {
        public string type, target, structure, location, message;
        public float seconds;

        public override string ToString() => type switch
        {
            "move_to" => $"move_to {target}",
            "build" => $"build {structure} @ {location}",
            "speak" => $"speak \"{message}\"",
            "inspect" => $"inspect {target}",
            "wait" => $"wait {seconds:0.#}s",
            _ => type,
        };
    }

    [Serializable]
    public class AgentPlan
    {
        public string agent, goal;
        public List<AgentAction> actions = new List<AgentAction>();
    }

    /// Turns raw LLM text into a plan and rejects anything outside the allowed action set.
    /// The LLM never supplies coordinates or code: only action names and known location ids.
    public static class PlanValidator
    {
        public const int MaxActions = 8;

        public static AgentPlan Parse(string raw, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) { error = "empty LLM response"; return null; }
            // Small models sometimes wrap JSON in prose or ``` fences.
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) { error = "LLM response contains no JSON"; return null; }
            try
            {
                var plan = JsonUtility.FromJson<AgentPlan>(raw.Substring(a, b - a + 1));
                if (plan?.actions == null || plan.actions.Count == 0) { error = "plan has no actions"; return null; }
                return plan;
            }
            catch (Exception e) { error = "invalid JSON: " + e.Message; return null; }
        }

        /// Checks every action against the skill registry, the agent's configured skills and the live
        /// location list. Normalizes ids in place ("near_lake" -> "lake") and returns every problem found.
        public static List<string> Validate(AgentPlan plan, ICollection<string> locations, string role)
        {
            var errors = new List<string>();
            if (plan.actions.Count > MaxActions) errors.Add($"plan has {plan.actions.Count} actions (max {MaxActions})");
            var allowed = AgentConfigs.SkillsFor(role);
            for (var i = 0; i < plan.actions.Count; i++)
            {
                var act = plan.actions[i];
                var at = $"action {i + 1}";
                if (act != null)
                {
                    act.type = act.type?.Trim().ToLowerInvariant();
                    act.structure = act.structure?.Trim().ToLowerInvariant();
                }
                var id = SkillRegistry.IdFor(act);
                var skill = SkillRegistry.Get(id);
                if (skill == null)
                {
                    errors.Add(act?.type == "build" ? $"{at}: invalid structure '{act.structure}'" : $"{at}: unknown action type '{act?.type}'");
                    continue;
                }
                if (!allowed.Contains(id)) errors.Add($"{at}: {role} does not have skill '{id}'");
                var problem = skill.Validate(act, locations);
                if (problem != null) errors.Add($"{at}: {problem}");
            }
            return errors;
        }

        public static string Loc(string s)
        {
            s = (s ?? "").Trim().Trim('<', '>', '"', '\'', ' ').ToLowerInvariant().Replace(' ', '_');
            return s.StartsWith("near_") ? s.Substring(5) : s;
        }
    }
}
