using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    /// Contract between an in-world computer and whatever runs behind it.
    /// Execute stays synchronous; long-running work (LLM agents) prints to host.log later via Print.
    public interface ITerminalBackend
    {
        string Execute(string input, Computer host);
    }

    public class LocalShellBackend : ITerminalBackend
    {
        public const string Clear = "\u0000clear";

        const string Help =
@"commands: help, echo <text>, whoami, agents, time, ide, clear
AI agents (local Llama via Ollama):
  agent list                 agents and their state
  agent status               details (goal, result, memory)
  agent builder <task>       e.g. agent builder build a house near the lake
  agent explorer <task>      e.g. agent explorer inspect the lake
  agent assistant <task>     e.g. agent assistant explain the lab
  agent stop <id|role>       cancel the task
  world status | world structures
  ai status                  test the Ollama connection
  mission <objective>        multi-agent mission: Ada delegates, Grace explores, Linus builds
                             e.g. mission build a small village near the lake
  mission status | mission stop
  skills                     registered skills and who may use them (StreamingAssets/agents.json)
  eval list | eval run <scenario> | eval report   evaluation with measured metrics
[Tab] show/hide Mission Control   [M] mission bar
free sentences (3+ words) go to Ada, who picks the specialist:
  e.g. Find a safe place to build | What is Linus doing? | Explain the current mission
[Llama] = model decision   [Unity] = action executed by the game";

        public string Execute(string input, Computer host)
        {
            var parts = input.Split(new[] { ' ' }, 2);
            var arg = parts.Length > 1 ? parts[1].Trim() : "";
            switch (parts[0].ToLowerInvariant())
            {
                case "help": return Help;
                case "echo": return arg;
                case "whoami": return host != null ? host.id : "?";
                case "time": return DateTime.Now.ToString("HH:mm:ss");
                case "clear": return Clear;
                case "ide": return "Living IDE (prototype): use 'agent ...' to give goals to the Llama agents. See help.";
                case "agents":
                    var npcs = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None);
                    return npcs.Length == 0 ? "no agents"
                        : string.Join("\n", npcs.OrderBy(n => n.id).Select(n => $"{n.displayName} ({n.role}) @ {((Vector2)n.transform.position).ToString("F1")}"));
                case "agent": return Agent(arg, host);
                case "world": return World(arg);
                case "mission": return Mission(arg, host);
                case "skills": return Skills();
                case "eval": return Eval(arg, host);
                case "ai": return arg == "status" ? AiStatus(host) : "usage: ai status";
                default:
                    // Free-form sentences go to Ada's specialist routing; short unknown words keep the old error.
                    if (input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= 3)
                        return ExpertRouter.Ask(input, Print(host), out var rerr) ? "[Ada] choosing the right specialist (Llama)..." : "error: " + rerr;
                    return $"unknown command: {parts[0]} (type help)";
            }
        }

        static string Agent(string arg, Computer host)
        {
            var parts = arg.Split(new[] { ' ' }, 2);
            var sub = parts[0].ToLowerInvariant();
            var rest = parts.Length > 1 ? parts[1].Trim() : "";
            var agents = AgentHub.Agents.ToList();
            switch (sub)
            {
                case "list":
                    return agents.Count == 0 ? "no AI agents" : string.Join("\n", agents.Select(n => $"{n.id,-6} {n.agentRole,-9} {n.Ai.Status} {n.Ai.Detail}"));
                case "status":
                    return agents.Count == 0 ? "no AI agents" : string.Join("\n", agents.Select(n =>
                        $"{n.displayName} ({n.agentRole}) - {n.Ai.Status}\n  goal: {Or(n.Ai.Goal)}\n  result: {Or(n.Ai.LastResult)}\n  memory: {n.Ai.Memory.Count} items"));
                case "stop":
                    var target = AgentHub.Find(rest);
                    if (target == null) return "agent not found: " + rest;
                    if (!target.Ai.Busy) return $"{target.displayName} has no active task";
                    target.Ai.Stop();
                    return $"cancelling {target.displayName}'s task...";
                case "":
                    return "usage: agent list | status | <role> <task> | stop <id>";
                default:
                    var agent = AgentHub.Find(sub);
                    if (agent == null) return $"unknown agent: {sub} (see agent list)";
                    if (rest == "") return $"usage: agent {sub} <task>";
                    return agent.Ai.Assign(rest, Print(host), out var error) ? $"[Unity] task sent to {agent.displayName}" : "error: " + error;
            }
        }

        static string World(string arg)
        {
            var builder = StructureBuilder.Instance;
            var built = builder ? builder.Structures.ToList() : new System.Collections.Generic.List<BuiltStructure>();
            switch (arg.ToLowerInvariant())
            {
                case "status":
                    return $"places: {string.Join(", ", Landmark.All.Select(l => l.id).OrderBy(s => s))}\n" +
                           $"agent-built structures: {built.Count}/{StructureBuilder.MaxStructures}\nAI agents: {AgentHub.Agents.Count()}";
                case "structures":
                    return built.Count == 0 ? "no structures built by agents yet"
                        : string.Join("\n", built.Select(s => $"{s.id} by {s.builtBy} @ {((Vector2)s.transform.position).ToString("F1")}"));
                default: return "usage: world status | world structures";
            }
        }

        static string AiStatus(Computer host)
        {
            _ = Ping(Print(host));
            return $"[Unity] checking Ollama at {LlmConfig.Endpoint}...";
        }

        static async Task Ping(Action<string> output)
        {
            var llm = AgentHub.Llm;
            try
            {
                var models = await llm.ListModels(CancellationToken.None);
                var installed = models.Any(m => m == llm.Model || m == llm.Model + ":latest");
                output("Ollama: connected");
                output($"Model: {llm.Model} {(installed ? "(installed)" : $"(NOT installed: run 'ollama pull {llm.Model}')")}");
                output($"Agents: {AgentHub.Agents.Count()}");
            }
            catch (Exception e) { output("Ollama: unavailable - " + e.Message); }
        }

        internal static string Mission(string arg, Computer host)
        {
            var m = MissionControl.State;
            switch (arg.ToLowerInvariant())
            {
                case "": return "usage: mission <objective> | mission status | mission stop";
                case "stop":
                    if (!MissionControl.Running) return "no mission running";
                    MissionControl.Stop();
                    return "cancelling mission...";
                case "status":
                    if (m.status == "None") return "no mission yet";
                    return $"mission: {m.objective}\nstatus: {m.status} {m.result}\nhouses: {m.housesBuilt}/{m.housesWanted}\n" +
                           string.Join("\n", m.tasks.Select(t => $"  #{t.id} {t.agent}: {t.description} [{t.status}]")) +
                           (m.sharedMemory.Count == 0 ? "" : "\nshared memory:\n" + string.Join("\n", m.sharedMemory.Select(r => $"  {r.location}: {r.freeSites} free sites ({r.reportedBy})")));
                default:
                    return MissionControl.Start(arg, Print(host), out var error) ? $"[Unity] mission started: {arg}" : "error: " + error;
            }
        }

        static string Skills()
        {
            var lines = SkillRegistry.All.Select(s => $"{s.Id,-16} {s.Description}  [params: {string.Join(", ", s.Parameters)}]");
            var agents = AgentHub.Agents.Select(n => $"{n.displayName} ({n.RoleTitle}): {string.Join(", ", AgentConfigs.SkillsFor(n.agentRole))}");
            var errors = AgentConfigs.Errors.Count == 0 ? "" : "\nconfiguration errors:\n  " + string.Join("\n  ", AgentConfigs.Errors);
            return "registered skills:\n  " + string.Join("\n  ", lines) + $"\nagents (source: {AgentConfigs.Source}):\n  " + string.Join("\n  ", agents) + errors;
        }

        static string Eval(string arg, Computer host)
        {
            var parts = arg.Split(new[] { ' ' }, 2);
            switch (parts[0].ToLowerInvariant())
            {
                case "list": return string.Join("\n", Evaluation.Scenarios.Select(s => $"{s.Id,-12} expects {string.Join("|", s.Expected)}: \"{s.Objective}\" - {s.Description}"));
                case "run":
                    var id = parts.Length > 1 ? parts[1] : "";
                    return Evaluation.Run(id, Print(host), out var err) ? $"[Unity] evaluation '{id}' started; the report appears at the end" : "error: " + err;
                case "report":
                    if (MissionControl.State.status == "None") return "no mission to evaluate";
                    if (MissionControl.Running) return "mission still running";
                    var r = Evaluation.Build(MissionControl.State);
                    var path = Evaluation.Export(r);
                    return Evaluation.Format(r) + (path != null ? "\nJSON: " + path : "");
                default: return "usage: eval list | eval run <scenario> | eval report";
            }
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "-" : s;

        public static Action<string> Print(Computer host) => line => { if (host) host.log.Add(line); };
    }

    /// Mission bar (M): every line is a natural-language mission; 'status' / 'stop' control the current one.
    public class MissionBackend : ITerminalBackend
    {
        public string Execute(string input, Computer host) =>
            input.ToLowerInvariant() switch
            {
                "clear" => LocalShellBackend.Clear,
                "status" or "stop" => LocalShellBackend.Mission(input, host),
                _ => ExpertRouter.Ask(input, LocalShellBackend.Print(host), out var error) ? "[Ada] choosing the right specialist (Llama)..." : "error: " + error,
            };
    }

    /// Chat window behind an AI NPC: every line goes to the LLM as conversation.
    public class NpcChatBackend : ITerminalBackend
    {
        readonly NpcAgent npc;
        public NpcChatBackend(NpcAgent npc) => this.npc = npc;

        public string Execute(string input, Computer host)
        {
            if (input.ToLowerInvariant() == "clear") return LocalShellBackend.Clear;
            return npc.Ai.Chat(input, LocalShellBackend.Print(host), out var error) ? $"[{npc.displayName}] thinking..." : "error: " + error;
        }
    }
}
