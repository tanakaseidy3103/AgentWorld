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
@"comandos: help, echo <texto>, whoami, agents, time, ide, clear
agentes IA (Llama local via Ollama):
  agent list                 agentes e estado
  agent status               detalhes (objetivo, resultado, memória)
  agent builder <tarefa>     ex: agent builder construa uma casa perto do lago
  agent explorer <tarefa>    ex: agent explorer inspecione o lago
  agent assistant <tarefa>   ex: agent assistant explique o laboratório
  agent stop <id|papel>      cancela a tarefa
  world status | world structures
  ai status                  testa conexão com o Ollama
  mission <objetivo>         missão multi-agente: Ada delega, Grace explora, Linus constrói
                             ex: mission build a small village near the lake
  mission status | mission stop
[Tab] mostra/esconde o painel Mission Control
[Llama] = decisão do modelo   [Unity] = ação executada pelo jogo";

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
                case "ide": return "Living IDE (protótipo): use 'agent ...' para dar objetivos aos agentes Llama. Veja help.";
                case "agents":
                    var npcs = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None);
                    return npcs.Length == 0 ? "nenhum agente"
                        : string.Join("\n", npcs.OrderBy(n => n.id).Select(n => $"{n.displayName} ({n.role}) @ {((Vector2)n.transform.position).ToString("F1")}"));
                case "agent": return Agent(arg, host);
                case "world": return World(arg);
                case "mission": return Mission(arg, host);
                case "ai": return arg == "status" ? AiStatus(host) : "uso: ai status";
                default: return $"comando desconhecido: {parts[0]} (digite help)";
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
                    return agents.Count == 0 ? "nenhum agente IA" : string.Join("\n", agents.Select(n => $"{n.id,-6} {n.agentRole,-9} {n.Ai.Status} {n.Ai.Detail}"));
                case "status":
                    return agents.Count == 0 ? "nenhum agente IA" : string.Join("\n", agents.Select(n =>
                        $"{n.displayName} ({n.agentRole}) - {n.Ai.Status}\n  objetivo: {Or(n.Ai.Goal)}\n  resultado: {Or(n.Ai.LastResult)}\n  memória: {n.Ai.Memory.Count} itens"));
                case "stop":
                    var target = AgentHub.Find(rest);
                    if (target == null) return "agente não encontrado: " + rest;
                    if (!target.Ai.Busy) return $"{target.displayName} não tem tarefa ativa";
                    target.Ai.Stop();
                    return $"cancelando tarefa de {target.displayName}...";
                case "":
                    return "uso: agent list | status | <papel> <tarefa> | stop <id>";
                default:
                    var agent = AgentHub.Find(sub);
                    if (agent == null) return $"agente desconhecido: {sub} (veja agent list)";
                    if (rest == "") return $"uso: agent {sub} <tarefa>";
                    return agent.Ai.Assign(rest, Print(host), out var error) ? $"[Unity] tarefa enviada para {agent.displayName}" : "erro: " + error;
            }
        }

        static string World(string arg)
        {
            var builder = StructureBuilder.Instance;
            var built = builder ? builder.Structures.ToList() : new System.Collections.Generic.List<BuiltStructure>();
            switch (arg.ToLowerInvariant())
            {
                case "status":
                    return $"locais: {string.Join(", ", Landmark.All.Select(l => l.id).OrderBy(s => s))}\n" +
                           $"construções de agentes: {built.Count}/{StructureBuilder.MaxStructures}\nagentes IA: {AgentHub.Agents.Count()}";
                case "structures":
                    return built.Count == 0 ? "nenhuma construção feita por agentes ainda"
                        : string.Join("\n", built.Select(s => $"{s.id} por {s.builtBy} @ {((Vector2)s.transform.position).ToString("F1")}"));
                default: return "uso: world status | world structures";
            }
        }

        static string AiStatus(Computer host)
        {
            _ = Ping(Print(host));
            return $"[Unity] verificando Ollama em {LlmConfig.Endpoint}...";
        }

        static async Task Ping(Action<string> output)
        {
            var llm = AgentHub.Llm;
            try
            {
                var models = await llm.ListModels(CancellationToken.None);
                var installed = models.Any(m => m == llm.Model || m == llm.Model + ":latest");
                output("Ollama: conectado");
                output($"Modelo: {llm.Model} {(installed ? "(instalado)" : $"(NÃO instalado: rode 'ollama pull {llm.Model}')")}");
                output($"Agentes: {AgentHub.Agents.Count()}");
            }
            catch (Exception e) { output("Ollama: indisponível - " + e.Message); }
        }

        static string Mission(string arg, Computer host)
        {
            var m = MissionControl.State;
            switch (arg.ToLowerInvariant())
            {
                case "": return "uso: mission <objetivo> | mission status | mission stop";
                case "stop":
                    if (!MissionControl.Running) return "nenhuma missão em andamento";
                    MissionControl.Stop();
                    return "cancelando missão...";
                case "status":
                    if (m.status == "None") return "nenhuma missão ainda";
                    return $"missão: {m.objective}\nestado: {m.status} {m.result}\ncasas: {m.housesBuilt}/{m.housesWanted}\n" +
                           string.Join("\n", m.tasks.Select(t => $"  #{t.id} {t.agent}: {t.description} [{t.status}]")) +
                           (m.sharedMemory.Count == 0 ? "" : "\nmemória compartilhada:\n" + string.Join("\n", m.sharedMemory.Select(r => $"  {r.location}: {r.freeSites} terrenos livres ({r.reportedBy})")));
                default:
                    return MissionControl.Start(arg, Print(host), out var error) ? $"[Unity] missão iniciada: {arg}" : "erro: " + error;
            }
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "-" : s;

        public static Action<string> Print(Computer host) => line => { if (host) host.log.Add(line); };
    }

    /// Chat window behind an AI NPC: every line goes to the LLM as conversation.
    public class NpcChatBackend : ITerminalBackend
    {
        readonly NpcAgent npc;
        public NpcChatBackend(NpcAgent npc) => this.npc = npc;

        public string Execute(string input, Computer host)
        {
            if (input.ToLowerInvariant() == "clear") return LocalShellBackend.Clear;
            return npc.Ai.Chat(input, LocalShellBackend.Print(host), out var error) ? $"[{npc.displayName}] pensando..." : "erro: " + error;
        }
    }
}
