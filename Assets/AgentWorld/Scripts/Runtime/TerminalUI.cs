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

        public static void Open(Computer computer)
        {
            Current = computer;
            openedFrame = Time.frameCount;
        }

        public static void Close() => Current = null;

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

            var rect = new Rect(Screen.width / 2f - 380, Screen.height / 2f - 230, 760, 460);
            GUILayout.BeginArea(rect, "Terminal - " + Current.id + "  (Esc fecha)", GUI.skin.window);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(string.Join("\n", Current.log));
            GUILayout.EndScrollView();
            GUI.SetNextControlName("term");
            input = GUILayout.TextField(input);
            GUI.FocusControl("term");
            GUILayout.EndArea();
        }
    }
}
