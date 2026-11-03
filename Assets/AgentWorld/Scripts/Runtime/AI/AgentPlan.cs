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
        public static readonly string[] Actions = { "move_to", "speak", "build", "inspect", "wait" };
        public static readonly string[] Structures = { "house" };

        public static AgentPlan Parse(string raw, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw)) { error = "resposta vazia do LLM"; return null; }
            // Small models sometimes wrap JSON in prose or ``` fences.
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a < 0 || b <= a) { error = "resposta do LLM não contém JSON"; return null; }
            try
            {
                var plan = JsonUtility.FromJson<AgentPlan>(raw.Substring(a, b - a + 1));
                if (plan?.actions == null || plan.actions.Count == 0) { error = "plano sem ações"; return null; }
                return plan;
            }
            catch (Exception e) { error = "JSON inválido: " + e.Message; return null; }
        }

        /// Normalizes location ids in place ("near_lake" -> "lake") and returns every problem found.
        public static List<string> Validate(AgentPlan plan, ICollection<string> locations, string role)
        {
            var errors = new List<string>();
            if (plan.actions.Count > MaxActions) errors.Add($"plano com {plan.actions.Count} ações (máx {MaxActions})");
            for (var i = 0; i < plan.actions.Count; i++)
            {
                var act = plan.actions[i];
                var at = $"ação {i + 1}";
                if (act == null || !Actions.Contains(act.type = act.type?.Trim().ToLowerInvariant()))
                {
                    errors.Add($"{at}: tipo desconhecido '{act?.type}'");
                    continue;
                }
                switch (act.type)
                {
                    case "move_to":
                    case "inspect":
                        act.target = Loc(act.target);
                        if (!locations.Contains(act.target)) errors.Add($"{at}: local inválido '{act.target}'");
                        break;
                    case "build":
                        if (role != "builder") errors.Add($"{at}: só o builder pode construir");
                        act.structure = act.structure?.Trim().ToLowerInvariant();
                        if (!Structures.Contains(act.structure)) errors.Add($"{at}: estrutura inválida '{act.structure}'");
                        act.location = Loc(string.IsNullOrEmpty(act.location) ? act.target : act.location);
                        if (!locations.Contains(act.location)) errors.Add($"{at}: local inválido '{act.location}'");
                        break;
                    case "speak":
                        if (string.IsNullOrWhiteSpace(act.message)) errors.Add($"{at}: speak sem mensagem");
                        else if (act.message.Length > 200) act.message = act.message.Substring(0, 200);
                        break;
                    case "wait":
                        if (act.seconds <= 0 || act.seconds > 10) act.seconds = Mathf.Clamp(act.seconds, 0.5f, 10f);
                        break;
                }
            }
            return errors;
        }

        static string Loc(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant().Replace(' ', '_');
            return s.StartsWith("near_") ? s.Substring(5) : s;
        }
    }
}
