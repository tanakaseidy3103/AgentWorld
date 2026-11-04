using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    /// "Expert finder": Ada uses Llama to route a free-form request to the specialist who can handle it.
    /// Llama only picks an intent from a fixed list; Unity executes it (mission, scouting, or a factual status answer).
    public static class ExpertRouter
    {
        static readonly (string role, string name, string focus)[] Specialists =
        {
            ("assistant", "Ada", "planning, coordination, task delegation, explaining the mission"),
            ("explorer", "Grace", "exploration, terrain inspection, counting free building land"),
            ("builder", "Linus", "construction, building feasibility, placement"),
        };

        /// Skill profile per specialist: focus plus the skills actually registered for that agent.
        public static IEnumerable<(string role, string name, string skills)> Profiles =>
            Specialists.Select(t => (t.role, t.name, $"{t.focus}; skills: {string.Join(", ", AgentConfigs.SkillsFor(t.role))}"));

        public static readonly string[] Intents = { "mission", "scout", "status", "capabilities", "explain", "unsupported" };
        public const string Supported = "build houses and farms (mission ...), explore/inspect places (Grace), ask what an agent is doing or can do, explain the current mission";

        [Serializable]
        public class Route { public string intent, agent, reason, message; }

        /// Last routing decision, for Mission Control.
        public static string LastRoute { get; private set; } = "";
        public static bool Busy { get; private set; }

        public static bool Ask(string request, Action<string> output, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(request)) { error = "pedido vazio"; return false; }
            if (Busy) { error = "Ada is still routing the previous request"; return false; }
            if (AgentHub.Find("assistant") == null) { error = "Ada is not in the scene"; return false; }
            Busy = true;
            _ = RouteAsync(request.Trim(), output ?? (_ => { }));
            return true;
        }

        static async Task RouteAsync(string request, Action<string> output)
        {
            var ada = AgentHub.Find("assistant");
            ada.Ai.Show(AgentStatus.Thinking, "choosing a specialist");
            try
            {
                var raw = await LlamaAgentBrain.WithTimeout(t => AgentHub.Llm.Chat(SystemPrompt(), $"{AgentHub.WorldSummary()}Player request: {request}", true, t), CancellationToken.None);
                var route = Parse(raw, out var problem);
                if (route == null) { output($"[Unity] Ada could not classify the request ({problem}). Try: {Supported}."); return; }
                ada.Ai.MarkDecision();
                var who = Profiles.FirstOrDefault(p => p.role == RoleOf(route));
                LastRoute = $"\"{Trunc(request, 50)}\" → {route.intent}{(who.name != null ? " · " + who.name : "")}";
                output($"[Llama/Ada] specialist: {route.intent}{(who.name != null ? $" → {who.name} ({who.skills})" : "")} | \"{route.reason}\"");
                Execute(route, request, output);
            }
            catch (Exception e) { output($"[Unity] Ada unavailable: {e.Message}"); }
            finally
            {
                ada.Ai.Show(AgentStatus.Idle, "");
                Busy = false;
            }
        }

        public static Route Parse(string raw, out string problem)
        {
            problem = null;
            int a = (raw ?? "").IndexOf('{'), b = (raw ?? "").LastIndexOf('}');
            if (a < 0 || b <= a) { problem = "response contains no JSON"; return null; }
            Route r;
            try { r = JsonUtility.FromJson<Route>(raw.Substring(a, b - a + 1)); }
            catch (Exception e) { problem = "invalid JSON: " + e.Message; return null; }
            r.intent = (r.intent ?? "").Trim().ToLowerInvariant();
            r.agent = (r.agent ?? "").Trim().ToLowerInvariant();
            if (!Intents.Contains(r.intent)) { problem = $"unknown intent '{r.intent}'"; return null; }
            r.reason = Trunc(r.reason ?? "", 160);
            r.message = Trunc(r.message ?? "", 200);
            return r;
        }

        static void Execute(Route r, string request, Action<string> output)
        {
            switch (r.intent)
            {
                case "mission":
                    if (!AgentConfigs.Has("builder", "build_house")) { output("[Unity] Linus does not have the build_house skill; the mission cannot run."); break; }
                    output(MissionControl.Start(request, output, out var err) ? "[Unity] mission started: Ada coordinates, Grace explores, Linus builds" : "[Unity] " + err);
                    break;
                case "scout":
                    var grace = AgentHub.Find("explorer");
                    if (!AgentConfigs.Has("explorer", "inspect")) { output("[Unity] Grace does not have the inspect skill configured."); break; }
                    output(grace.Ai.Assign(request, output, out var gerr) ? "[Unity] task sent to Grace" : "[Unity] " + gerr);
                    break;
                case "status":
                    var target = AgentHub.Find(r.agent) ?? AgentHub.Agents.FirstOrDefault(n => request.ToLowerInvariant().Contains(n.displayName.ToLowerInvariant()));
                    output(target == null ? Team() : Describe(target));
                    break;
                case "explain":
                    output(MissionSummary());
                    break;
                case "capabilities":
                    var asked = AgentHub.Find(r.agent);
                    output(asked == null ? string.Join("\n", AgentHub.Agents.Select(Capabilities)) : Capabilities(asked));
                    break;
                default:
                    var msg = string.IsNullOrWhiteSpace(r.message) ? "The team can't do that." : r.message;
                    AgentHub.Find("assistant").Say(msg);
                    output($"[Ada] {msg} I can help with: {Supported}.");
                    break;
            }
        }

        // Status answers are built from live state, never from the model.
        static string Describe(NpcAgent n) =>
            $"[Unity] {n.displayName} ({n.RoleTitle}): {n.Activity}{(n.Ai.Detail != "" ? " - " + n.Ai.Detail : "")}. Goal: {(n.Ai.Goal == "" ? "-" : n.Ai.Goal)}. Last result: {(n.Ai.LastResult == "" ? "-" : n.Ai.LastResult)}.";

        static string Team() => string.Join("\n", AgentHub.Agents.Select(Describe));

        /// Answered from the skill registry, so it can never promise something unimplemented.
        public static string Capabilities(NpcAgent n) =>
            $"[Unity] {n.displayName} ({n.RoleTitle}) can: " + string.Join("; ", AgentConfigs.SkillsFor(n.agentRole).Select(SkillRegistry.Get).Where(s => s != null).Select(s => $"{s.Id} - {s.Description}"));

        public static string MissionSummary()
        {
            var m = MissionControl.State;
            if (m.status == "None") return "[Unity] No mission yet. Press M and describe one, e.g. build a small village near the lake.";
            var built = $"{m.housesBuilt}/{m.housesWanted} houses" + (m.farmsWanted > 0 ? $", {m.farmsBuilt}/{m.farmsWanted} farm" : "");
            var reports = m.sharedMemory.Count == 0 ? "no reports" : string.Join(", ", m.sharedMemory.Select(r => $"{r.location}={r.freeSites} free"));
            return $"[Unity] Mission \"{m.objective}\": {m.status}. {built}. Grace's reports: {reports}. {m.result}";
        }

        static string RoleOf(Route r) => r.intent switch
        {
            "scout" => "explorer",
            "mission" => "builder",
            "status" or "capabilities" => AgentHub.Find(r.agent)?.agentRole ?? "",
            _ => "assistant",
        };

        static string SystemPrompt() =>
$@"You are Ada, the coordinator of three AI agents in AgentWorld. Route the player's request to the right specialist.
Team skills:
{string.Join("\n", Profiles.Select(p => $"- {p.name} ({p.role}): {p.skills}"))}
The team can ONLY build houses and farms. It cannot build roads, bridges, castles or anything else, cannot change water or terrain, cannot demolish.
Return ONLY JSON: {{""intent"":""<one of: mission, scout, status, explain, unsupported>"",""agent"":""<ada|grace|linus>"",""reason"":""<short>"",""message"":""<only for unsupported: one short sentence explaining why, in the player's language>""}}
- mission: the player wants houses, farms or a village built (Ada plans, Grace scouts, Linus builds).
- scout: find/inspect/explore places or land, without building.
- status: asks what an agent is doing (set agent to that agent).
- capabilities: asks what an agent can do or build (set agent).
- explain: asks about the current mission or what happened.
- unsupported: anything the team cannot do.
Examples: ""Build a small village near the lake"" -> mission. ""Find a safe place to build"" -> scout. ""Ask Grace to explore"" -> scout. ""What is Linus doing?"" -> status, agent linus. ""What can Linus build?"" -> capabilities, agent linus. ""Explain the current mission"" -> explain. ""Build a bridge over the lake"" -> unsupported.";

        static string Trunc(string s, int n) => s.Length > n ? s.Substring(0, n) : s;
    }
}
