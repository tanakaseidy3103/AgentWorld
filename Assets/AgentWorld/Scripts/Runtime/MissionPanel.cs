using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AgentWorld
{
    /// Side panel showing what the AI is doing: Ollama/model, mission, agents, tasks, shared memory, recent decisions.
    /// Tab toggles it. Hidden while the terminal is open so the two never overlap.
    public class MissionPanel : MonoBehaviour
    {
        public bool visible = true;
        public float pingSeconds = 15f;

        public static string Ollama { get; private set; } = "verificando...";
        public static bool Connected { get; private set; }

        float nextPing;
        bool pinging;
        GUIStyle box, text;
        int styledFor = -1;
        Vector2 scroll;

        // Scenes generated before the panel existed still get it (no Rebuild World needed).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Ensure()
        {
            if (FindAnyObjectByType<MissionPanel>() == null && FindAnyObjectByType<TerminalUI>() is TerminalUI ui) ui.gameObject.AddComponent<MissionPanel>();
        }

        void Update()
        {
            if (!TerminalUI.IsOpen && Input.GetKeyDown(KeyCode.Tab)) visible = !visible;
            if (!pinging && Time.unscaledTime >= nextPing) Ping();
        }

        async void Ping()
        {
            pinging = true;
            nextPing = Time.unscaledTime + pingSeconds;
            var llm = AgentHub.Llm;
            try
            {
                var models = await llm.ListModels(CancellationToken.None);
                var installed = models.Any(m => m == llm.Model || m == llm.Model + ":latest");
                Connected = installed;
                Ollama = installed ? $"conectado · {llm.Model}" : $"modelo {llm.Model} não instalado";
            }
            catch { Connected = false; Ollama = "offline (inicie o Ollama)"; }
            finally { pinging = false; }
        }

        void OnGUI()
        {
            if (!visible || TerminalUI.IsOpen) return;
            var size = Mathf.Max(14, Mathf.RoundToInt(TerminalUI.FontSize(Screen.height) * 0.8f));
            if (size != styledFor)
            {
                styledFor = size;
                var bg = new Texture2D(1, 1);
                bg.SetPixel(0, 0, new Color(0.04f, 0.06f, 0.05f, 0.88f));
                bg.Apply();
                box = new GUIStyle { normal = { background = bg }, padding = new RectOffset(size / 2, size / 2, size / 2, size / 2) };
                text = new GUIStyle { fontSize = size, richText = true, wordWrap = true, normal = { textColor = new Color(0.85f, 0.95f, 0.88f) } };
            }

            float w = Mathf.Min(Screen.width * 0.34f, size * 30f), h = Screen.height - size * 3f;
            GUILayout.BeginArea(new Rect(Screen.width - w - size * 0.5f, size * 2f, w, h), box);
            scroll = GUILayout.BeginScrollView(scroll, GUIStyle.none, GUIStyle.none);
            GUILayout.Label(Body(size * 4 / 5), text);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static string Body(int small)
        {
            var m = MissionControl.State;
            var sb = new StringBuilder();
            sb.Append($"<b><color=#ffd95a>MISSION CONTROL</color></b>  <size={small}>[Tab]</size>\n");
            sb.Append($"Meta Llama (Ollama): <color={(Connected ? "#7dff9a" : "#ff7a7a")}>{Ollama}</color>\n\n");

            sb.Append(m.status == "None"
                ? $"<b>Missão:</b> nenhuma\n<size={small}>terminal: mission build a small village near the lake</size>\n"
                : $"<b>Missão:</b> {Esc(m.objective)}\n<b>Estado:</b> <color={StatusColor(m.status)}>{m.status}</color>  casas {m.housesBuilt}/{m.housesWanted}  {Esc(m.result)}\n");

            sb.Append("\n<b>Agentes</b>\n");
            foreach (var n in AgentHub.Agents)
                sb.Append($"{n.displayName} <color=#9ab>({n.agentRole})</color>: <color={StatusColor(n.Ai.Status.ToString())}>{n.Ai.Status}</color> {Esc(n.Ai.Detail)}\n");

            if (m.tasks.Count > 0)
            {
                sb.Append("\n<b>Tarefas</b>\n");
                foreach (var t in m.tasks.Skip(m.tasks.Count - 6))
                    sb.Append($"<color={StatusColor(t.status)}>{Mark(t.status)}</color> {t.agent}: {Esc(t.description)}{(t.attempts > 1 ? $" (tentativa {t.attempts})" : "")}\n");
            }
            if (m.sharedMemory.Count > 0)
            {
                sb.Append("\n<b>Memória compartilhada</b>\n");
                foreach (var r in m.sharedMemory) sb.Append($"{r.location}: {r.freeSites} terrenos livres <color=#9ab>({r.reportedBy})</color>\n");
            }
            var decisions = m.log.Where(l => l.StartsWith("[Llama")).ToList();
            if (decisions.Count > 0)
            {
                sb.Append($"\n<b>Decisões do Llama</b>  <size={small}>(validadas pelo Unity)</size>\n");
                foreach (var l in decisions) sb.Append(Colorize(l)).Append('\n');
            }
            var actions = m.log.Where(l => !l.StartsWith("[Llama")).ToList();
            if (actions.Count > 0)
            {
                sb.Append($"\n<b>Ações recentes</b>  <size={small}><color=#8fd3ff>[Unity]</color> execução determinística</size>\n");
                foreach (var l in actions.Skip(actions.Count - 6)) sb.Append(Colorize(l)).Append('\n');
            }
            return sb.ToString();
        }

        static string Colorize(string line)
        {
            var safe = Esc(line);
            if (line.StartsWith("[Llama")) return $"<color=#c9a0ff>{safe}</color>";
            if (line.StartsWith("[Unity]")) return $"<color=#8fd3ff>{safe}</color>";
            return safe;
        }

        // Model and player text must not inject rich-text tags.
        static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        static string Mark(string s) => s == "Done" ? "✔" : s == "Running" ? "…" : s == "Failed" ? "✘" : "•";

        static string StatusColor(string s) => s switch
        {
            "Done" or "Completed" or "Succeeded" => "#7dff9a",
            "Failed" or "Cancelled" or "Interrupted" => "#ff7a7a",
            "Partial" => "#ffb35a",
            "Idle" or "None" or "Pending" => "#bbbbbb",
            _ => "#ffd95a",
        };
    }
}
