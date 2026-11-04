using UnityEngine;

namespace AgentWorld
{
    /// Minimal IMGUI terminal. Player input is blocked while it is open.
    public class TerminalUI : MonoBehaviour
    {
        public static Computer Current { get; private set; }
        public static bool IsOpen => Current != null;

        static int openedFrame;
        string input = "";
        Vector2 scroll;
        int seenLines;

        GUIStyle panel, title, text, field, prompt;
        int styledFor = -1;

        public static void Open(Computer computer)
        {
            Current = computer;
            openedFrame = Time.frameCount;
        }

        public static void Close() => Current = null;

        /// Font size tracks screen height (18px at 720p) so text stays readable at any Game view size.
        public static int FontSize(float screenHeight) => Mathf.Clamp(Mathf.RoundToInt(screenHeight / 720f * 18f), 14, 48);

        void OnDisable() => Close();

        void OnGUI()
        {
            // Skip the frame that opened us so the E keystroke doesn't land in the text field.
            if (!IsOpen || Time.frameCount == openedFrame) return;

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Close(); e.Use(); return; }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                Current.Submit(input);
                input = "";
                e.Use();
            }
            // Agents print asynchronously; follow the newest line.
            if (Current.log.Count != seenLines) { seenLines = Current.log.Count; scroll.y = float.MaxValue; }

            var size = FontSize(Screen.height);
            if (size != styledFor) BuildStyles(size);

            float w = Mathf.Min(Screen.width * 0.9f, size * 60f), h = Screen.height * 0.82f;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUILayout.BeginArea(rect, panel);
            GUILayout.Label($"Terminal - {Current.id}    [Enter] enviar   [Esc] fechar", title);
            scroll = GUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label(string.Join("\n", Current.log), text);
            GUILayout.EndScrollView();
            GUILayout.Space(size * 0.4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(">", prompt, GUILayout.ExpandWidth(false));
            GUI.SetNextControlName("term");
            input = GUILayout.TextField(input, field);
            GUILayout.EndHorizontal();
            GUI.FocusControl("term");
            GUILayout.EndArea();
        }

        void BuildStyles(int size)
        {
            styledFor = size;
            var pad = size / 2;
            panel = new GUIStyle { normal = { background = Solid(new Color(0.05f, 0.07f, 0.06f, 0.96f)) }, padding = new RectOffset(pad, pad, pad, pad) };
            title = new GUIStyle { fontSize = size, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.35f) }, margin = new RectOffset(0, 0, 0, pad) };
            text = new GUIStyle { fontSize = size, wordWrap = true, normal = { textColor = new Color(0.75f, 1f, 0.8f) } };
            prompt = new GUIStyle(text) { fontStyle = FontStyle.Bold, wordWrap = false, padding = new RectOffset(0, pad / 2, pad / 2, 0) };
            var fieldBg = Solid(new Color(0.16f, 0.2f, 0.18f));
            field = new GUIStyle(text)
            {
                wordWrap = false,
                padding = new RectOffset(pad / 2, pad / 2, pad / 2, pad / 2),
                normal = { background = fieldBg, textColor = Color.white },
                focused = { background = fieldBg, textColor = Color.white },
            };
            GUI.skin.settings.cursorColor = Color.white;
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
