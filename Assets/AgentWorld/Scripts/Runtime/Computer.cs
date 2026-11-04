using System.Collections.Generic;
using UnityEngine;

namespace AgentWorld
{
    public class Computer : MonoBehaviour, IInteractable
    {
        public string id = "pc";
        public List<string> log = new List<string>();

        /// Swap for a Living IDE / LLM backend in MVP 2 without touching the UI.
        public ITerminalBackend Backend { get; set; } = new LocalShellBackend();

        public string Prompt => "Use computer " + id;

        public void Interact(GameObject user) => TerminalUI.Open(this);

        public void Submit(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            log.Add("> " + input);
            var output = Backend.Execute(input.Trim(), this);
            if (output == LocalShellBackend.Clear) { log.Clear(); return; }
            if (!string.IsNullOrEmpty(output)) log.Add(output);
        }
    }
}
