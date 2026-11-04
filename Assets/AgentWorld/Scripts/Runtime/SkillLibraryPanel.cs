using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgentWorld
{
    /// Skill Library (K): every registered skill, the documentation-only examples, who may use each one,
    /// and a safe tester (dry-run validation, then an optional real run on the chosen agent). Esc closes.
    public class SkillLibraryPanel : MonoBehaviour
    {
        public static bool Visible { get; private set; }

        string selected;
        string agentId = "grace";
        string json = "";
        readonly List<string> results = new List<string>();
        Vector2 listScroll, detailScroll;
        GUIStyle panel, title, text, field, button, active;
        int styledFor = -1;
        int openedFrame;

        public static void Show(bool on) => Visible = on;

        void OnDisable() => Visible = false;

        void Update()
        {
            if (!Visible && !TerminalUI.IsOpen && Input.GetKeyDown(KeyCode.K)) { Visible = true; openedFrame = Time.frameCount; }
        }

        void OnGUI()
        {
            if (!Visible) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Visible = false; e.Use(); return; }

            var size = Mathf.Max(14, Mathf.RoundToInt(TerminalUI.FontSize(Screen.height) * 0.8f));
            if (size != styledFor) BuildStyles(size);

            var skills = SkillRegistry.All.ToList();
            if (selected == null) Select(skills.First().Id);
            if (e.type == EventType.KeyDown) Keys(e, skills);
            if (e.type == EventType.KeyDown && (e.character == '\t' || e.character == '\n')) e.Use(); // keep them out of the JSON field
            float m = size * 0.5f, w = Screen.width - MissionPanel.ReservedWidth - m * 3f, h = Screen.height - m * 2f;
            GUILayout.BeginArea(new Rect(m, m, w, h), panel);
            GUILayout.Label("SKILL LIBRARY   [PgUp/PgDn] skill  [Tab] agent  [Enter] validate  [Ctrl+Enter] run  [Esc] close", title);
            GUILayout.BeginHorizontal();

            // Left: every skill, implemented first, then documentation-only ideas.
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Width(w * 0.27f));
            GUILayout.Label("<b>Implemented</b>", text);
            foreach (var s in skills)
                if (GUILayout.Button($"{s.Id}{(s.Origin == "built-in" ? "" : "  <color=#ffd95a>custom</color>")}", selected == s.Id ? active : button)) Select(s.Id);
            GUILayout.Space(size);
            GUILayout.Label("<b>Documentation only</b>\n<size=" + size * 4 / 5 + "><color=#9ab>not registered, cannot run</color></size>", text);
            foreach (var d in SkillRegistry.DocumentationOnly)
                if (GUILayout.Button($"<color=#8a8a8a>{d.Id}</color>", selected == d.Id ? active : button)) Select(d.Id);
            GUILayout.EndScrollView();

            GUILayout.Space(size);
            detailScroll = GUILayout.BeginScrollView(detailScroll);
            var skill = SkillRegistry.Get(selected);
            if (skill != null) Details(skill, size);
            else Idea(SkillRegistry.DocumentationOnly.First(d => d.Id == selected));
            Tester(size);
            GUILayout.EndScrollView();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Select(string id)
        {
            selected = id;
            var s = SkillRegistry.Get(id);
            json = s != null ? s.Example.Replace("<location id>", "lab").Replace("<one short sentence>", "Hello!") : $"{{\"type\":\"{id}\",\"target\":\"lab\"}}";
            results.Clear();
        }

        void Details(Skill s, int size)
        {
            var small = size * 4 / 5;
            GUILayout.Label($"<b><size={size * 5 / 4}>{s.Name}</size></b>  <color=#9ab>{s.Id}</color>   <color=#7dff9a>IMPLEMENTED</color> · {Esc(s.Origin)}\n" +
                            $"{Esc(s.Description)}\n\n" +
                            $"<b>Parameters</b>  {Esc(string.Join("; ", s.Parameters))}\n" +
                            $"<b>Model writes</b>  <color=#c9a0ff>{Esc(s.Example)}</color>\n" +
                            $"<b>Permissions</b>  roles allowed: {string.Join(", ", s.Roles)}; enabled per agent in agents.json\n" +
                            $"<b>Safety</b>  {Esc(s.Safety)}\n\n<b>Who can perform it</b>", text);
            foreach (var n in AgentHub.Agents)
            {
                var why = SkillLibrary.Availability(s, n.agentRole);
                GUILayout.Label($"  <color={(why == "yes" ? "#7dff9a>✔" : "#ff7a7a>✘")}</color> <b>{n.displayName}</b> ({n.RoleTitle})  <size={small}>{why}</size>", text);
            }
        }

        void Idea(DocumentedSkillIdea d) =>
            GUILayout.Label($"<b>{d.Id}</b>   <color=#ffb35a>DOCUMENTATION ONLY</color>\n{Esc(d.Description)}\n\n" +
                            $"Not implemented: {Esc(d.Missing)}. It is not in the registry, so no agent can use it and any plan naming it is rejected.\n" +
                            "Try the tester below to see the validation error.", text);

        void Tester(int size)
        {
            GUILayout.Space(size);
            GUILayout.Label("<b>Test a skill</b>  <size=" + size * 4 / 5 + ">Validate = dry run, nothing executes.  Run = validate, then the agent performs it for real.</size>", text);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Agent:", text, GUILayout.ExpandWidth(false));
            foreach (var n in AgentHub.Agents)
                if (GUILayout.Button(n.displayName, n.id == agentId ? active : button, GUILayout.ExpandWidth(false))) agentId = n.id;
            GUILayout.EndHorizontal();
            GUI.SetNextControlName("skilljson");
            json = GUILayout.TextField(json, field);
            if (Time.frameCount > openedFrame + 1 && GUI.GetNameOfFocusedControl() == "") GUI.FocusControl("skilljson");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate (dry run)", button, GUILayout.ExpandWidth(false))) Validate();
            if (GUILayout.Button($"Run on {AgentHub.Find(agentId)?.displayName}", button, GUILayout.ExpandWidth(false))) Run();
            GUILayout.EndHorizontal();
            if (results.Count > 0) GUILayout.Label(string.Join("\n", results.Skip(results.Count - 10)), text);
        }

        void Validate()
        {
            var agent = AgentHub.Find(agentId);
            if (agent == null) return;
            results.Clear();
            var errors = SkillLibrary.Check(agent, json, out _);
            results.Add(errors.Count == 0 ? $"<color=#7dff9a>✔ VALID for {agent.displayName}: Unity would accept this action (nothing was executed)</color>" : $"<color=#ff7a7a>✘ REJECTED for {agent.displayName}</color>");
            results.AddRange(errors.Select(x => "  • " + Esc(x)));
        }

        void Run()
        {
            var agent = AgentHub.Find(agentId);
            if (agent == null) return;
            results.Clear();
            if (SkillLibrary.Run(agent, json, line => results.Add(Esc(line)), out var errors))
                results.Add($"<color=#7dff9a>✔ validated; {agent.displayName} is performing it (watch the world)</color>");
            else
            {
                results.Add($"<color=#ff7a7a>✘ not run: validation failed for {agent.displayName}</color>");
                results.AddRange(errors.Select(x => "  • " + Esc(x)));
            }
        }

        // Keyboard: PgUp/PgDn browse every entry, Tab cycles agents, Enter validates, Ctrl+Enter runs.
        void Keys(Event e, List<Skill> skills)
        {
            var ids = skills.Select(s => s.Id).Concat(SkillRegistry.DocumentationOnly.Select(d => d.Id)).ToList();
            var agents = AgentHub.Agents.Select(n => n.id).ToList();
            switch (e.keyCode)
            {
                case KeyCode.PageDown: case KeyCode.PageUp:
                    Select(ids[(ids.IndexOf(selected) + (e.keyCode == KeyCode.PageDown ? 1 : ids.Count - 1)) % ids.Count]); break;
                case KeyCode.Tab:
                    if (agents.Count > 0) agentId = agents[(agents.IndexOf(agentId) + 1) % agents.Count]; break;
                case KeyCode.Return: case KeyCode.KeypadEnter:
                    if (e.control) Run(); else Validate(); break;
                default: return;
            }
            e.Use();
        }

        // Player and model text must not inject rich-text tags.
        static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        void BuildStyles(int size)
        {
            styledFor = size;
            var pad = size / 2;
            panel = new GUIStyle { normal = { background = Solid(new Color(0.04f, 0.06f, 0.05f, 0.94f)) }, padding = new RectOffset(pad, pad, pad, pad) };
            title = new GUIStyle { fontSize = size, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.35f) }, margin = new RectOffset(0, 0, 0, pad) };
            text = new GUIStyle { fontSize = size, richText = true, wordWrap = true, normal = { textColor = new Color(0.85f, 0.95f, 0.88f) }, margin = new RectOffset(0, 0, 2, 2) };
            var bg = Solid(new Color(0.14f, 0.18f, 0.16f));
            field = new GUIStyle(text) { wordWrap = false, padding = new RectOffset(pad / 2, pad / 2, pad / 2, pad / 2), normal = { background = bg, textColor = Color.white }, focused = { background = bg, textColor = Color.white } };
            button = new GUIStyle(text) { padding = new RectOffset(pad, pad, pad / 3, pad / 3), margin = new RectOffset(0, pad / 2, 2, 2), normal = { background = bg, textColor = Color.white }, hover = { background = Solid(new Color(0.22f, 0.28f, 0.25f)), textColor = Color.white } };
            active = new GUIStyle(button) { normal = { background = Solid(new Color(0.3f, 0.42f, 0.2f)), textColor = Color.white } };
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
