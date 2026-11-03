using System;
using System.Linq;
using UnityEngine;

namespace AgentWorld
{
    /// Contract between an in-world computer and whatever runs behind it
    /// (local shell today; Living IDE / LLM agents in MVP 2).
    public interface ITerminalBackend
    {
        string Execute(string input, Computer host);
    }

    public class LocalShellBackend : ITerminalBackend
    {
        public const string Clear = "\u0000clear";

        public string Execute(string input, Computer host)
        {
            var parts = input.Split(new[] { ' ' }, 2);
            var arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0].ToLowerInvariant())
            {
                case "help": return "comandos: help, echo <texto>, whoami, agents, time, ide, clear";
                case "echo": return arg;
                case "whoami": return host != null ? host.id : "?";
                case "time": return DateTime.Now.ToString("HH:mm:ss");
                case "clear": return Clear;
                case "ide": return "Living IDE: reservado para o MVP 2.";
                case "agents":
                    var npcs = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None);
                    return npcs.Length == 0 ? "nenhum agente"
                        : string.Join("\n", npcs.OrderBy(n => n.id).Select(n => $"{n.displayName} ({n.role}) @ {((Vector2)n.transform.position).ToString("F1")}"));
                default: return $"comando desconhecido: {parts[0]} (digite help)";
            }
        }
    }
}
