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
                scroll.y = float.MaxValue;
                e.Use();
            }

            var rect = new Rect(Screen.width / 2f - 320, Screen.height / 2f - 200, 640, 400);
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
