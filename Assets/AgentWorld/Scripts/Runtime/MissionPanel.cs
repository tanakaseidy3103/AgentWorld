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

        public static string Ollama { get; private set; } = "checking...";
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
            if (!(FindAnyObjectByType<TerminalUI>() is TerminalUI ui)) return;
            if (FindAnyObjectByType<MissionPanel>() == null) ui.gameObject.AddComponent<MissionPanel>();
            if (FindAnyObjectByType<InspectionMarkers>() == null) ui.gameObject.AddComponent<InspectionMarkers>();
        }

        /// Screen width the panel occupies on the right (0 when hidden); the terminal docks beside it.
        public static float ReservedWidth { get; private set; }

        Computer console;

        /// Natural-language mission bar (M): a terminal whose every line is a mission for the team.
        public Computer MissionConsole()
        {
            if (console) return console;
            var go = new GameObject("MissionConsole");
            go.transform.SetParent(transform, false);
            console = go.AddComponent<Computer>();
            console.id = "team mission";
            console.Backend = new MissionBackend();
            console.log.Add("Describe a mission in plain language (e.g. build a small village near the lake).");
            console.log.Add("Or ask something: \"Find a safe place to build\", \"What is Linus doing?\". Ada (Llama) picks the specialist.");
            console.log.Add("The team can explore and build houses and farms. 'status' or 'stop' control the current mission.");
            return console;
        }

        void OnDisable() => ReservedWidth = 0;

        void Update()
        {
            if (!TerminalUI.IsOpen && Input.GetKeyDown(KeyCode.Tab)) visible = !visible;
            if (!TerminalUI.IsOpen && Input.GetKeyDown(KeyCode.M)) TerminalUI.Open(MissionConsole());
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
                Ollama = installed ? $"connected · {llm.Model}" : $"model {llm.Model} not installed";
            }
            catch { Connected = false; Ollama = "offline (start Ollama)"; }
            finally { pinging = false; }
        }

        void OnGUI()
        {
            if (!visible) { ReservedWidth = 0; return; }
            var size = Mathf.Max(14, Mathf.RoundToInt(TerminalUI.FontSize(Screen.height) * 0.72f));
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
            ReservedWidth = w + size * 0.5f;
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
            sb.Append($"<b><color=#ffd95a>MISSION CONTROL</color></b>  <size={small}>[M] new mission · [Tab] hide</size>\n");
            sb.Append($"Meta Llama (Ollama): <color={(Connected ? "#7dff9a" : "#ff7a7a")}>{Ollama}</color>\n\n");

            sb.Append(m.status == "None"
                ? $"<b>Mission:</b> none\n<size={small}>press M and type: build a small village near the lake</size>\n"
                : $"<b>Mission:</b> {Esc(m.objective)}\n<b>Status:</b> <color={StatusColor(m.status)}>{m.status}</color>  houses {m.housesBuilt}/{m.housesWanted}{(m.farmsWanted > 0 ? $"  farm {m.farmsBuilt}/{m.farmsWanted}" : "")}\n{Esc(m.result)}\n");

            if (ExpertRouter.LastRoute != "") sb.Append($"<size={small}><color=#c9a0ff>Specialist:</color> {Esc(ExpertRouter.LastRoute)}</size>\n");

            sb.Append("\n<b>Team</b>\n");
            foreach (var n in AgentHub.Agents)
            {
                sb.Append($"<b>{n.displayName}</b> <color=#9ab>{n.RoleTitle}</color>  <color={ActivityColor(n.Activity)}>{n.Activity}</color> <size={small}>{Esc(n.Ai.Detail)}</size>\n");
                sb.Append($"<size={small}><color=#7a8a99>   skills: {string.Join(", ", AgentConfigs.SkillsFor(n.agentRole))}</color></size>\n");
            }
            if (AgentConfigs.Errors.Count > 0) sb.Append($"<size={small}><color=#ff7a7a>agents.json: {AgentConfigs.Errors.Count} error(s) - type skills in a terminal</color></size>\n");

            if (m.conversation.Count > 0)
            {
                sb.Append($"\n<b>Agent conversation</b>  <size={small}><color=#c9a0ff>generated by Llama</color></size>\n");
                foreach (var c in m.conversation.Skip(m.conversation.Count - 3))
                    sb.Append($"<color=#ffd95a>{c.from}</color> → {Esc(c.to)}: <color=#e6d7ff>{Esc(c.text)}</color>\n");
            }

            var decisions = m.log.Where(l => l.StartsWith("[Llama/Ada]")).ToList();
            if (decisions.Count > 0)
            {
                sb.Append($"\n<b>Meta Llama decisions</b>\n");
                foreach (var l in decisions.Skip(decisions.Count - 2)) sb.Append(Colorize(l)).Append('\n');
            }

            var checks = m.log.Where(l => l.Contains("rejected") || l.Contains("recovery:") || l.Contains("did not answer")).ToList();
            sb.Append($"\n<b>Unity validation</b>  <size={small}>(every decision is checked before execution)</size>\n");
            if (checks.Count == 0) sb.Append(m.status == "None" ? "-\n" : "<color=#7dff9a>✔ decisions accepted without corrections</color>\n");
            foreach (var l in checks.Skip(checks.Count - 3)) sb.Append(Colorize(l)).Append('\n');

            if (m.tasks.Count > 0)
            {
                sb.Append("\n<b>Tasks</b>\n");
                foreach (var t in m.tasks.Skip(m.tasks.Count - 4))
                    sb.Append($"<color={StatusColor(t.status)}>{Mark(t.status)}</color> {t.agent}: {Esc(t.description)}{(t.attempts > 1 ? $" (attempt {t.attempts})" : "")}\n");
            }
            var linus = AgentHub.Find("builder");
            if (linus != null && linus.Activity == "Building") sb.Append($"<color=#ffd95a>Construction: {Esc(linus.Ai.Detail)}</color>\n");

            if (m.sharedMemory.Count > 0 || m.knowledge.Count > 0)
            {
                sb.Append("\n<b>Shared memory</b>\n");
                foreach (var r in m.sharedMemory) sb.Append($"{r.location}: {r.freeSites} free sites <color=#9ab>(Grace, measured now)</color>\n");
                foreach (var r in m.knowledge.Where(k => m.sharedMemory.All(s => s.location != k.location)))
                    sb.Append($"<color=#9ab>{r.location}: {r.freeSites} free (earlier mission, will be re-measured)</color>\n");
            }

            if (m.status is "Succeeded" or "Partial" or "Failed" or "Cancelled" or "Unsupported")
                sb.Append($"\n<b>Outcome:</b> <color={StatusColor(m.status)}>{m.status}</color> - {Esc(m.result)}\n");
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

        static string ActivityColor(string a) => a switch
        {
            "Idle" => "#bbbbbb",
            "Failed" or "Cancelled" => "#ff7a7a",
            "Building" => "#ffb35a",
            "Talking" => "#e6d7ff",
            _ => "#7dff9a",
        };

        static string Mark(string s) => s == "Done" ? "✔" : s == "Running" ? "…" : s == "Failed" ? "✘" : "•";

        static string StatusColor(string s) => s switch
        {
            "Done" or "Completed" or "Succeeded" => "#7dff9a",
            "Failed" or "Cancelled" or "Interrupted" => "#ff7a7a",
            "Partial" or "Unsupported" => "#ffb35a",
            "Idle" or "None" or "Pending" => "#bbbbbb",
            _ => "#ffd95a",
        };
    }
}
