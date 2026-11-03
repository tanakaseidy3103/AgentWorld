using UnityEngine;

namespace AgentWorld
{
    /// Anything the player can use with E (computers, NPCs; later doors, Living IDE panels).
    public interface IInteractable
    {
        string Prompt { get; }
        void Interact(GameObject user);
    }

    public class Interactor : MonoBehaviour
    {
        public float radius = 1.3f;
        public IInteractable Current { get; private set; }

        void Update()
        {
            if (TerminalUI.IsOpen) return;
            Current = FindNearest();
            if (Input.GetKeyDown(KeyCode.E)) TryInteract();
        }

        public bool TryInteract()
        {
            Current = FindNearest();
            if (Current == null) return false;
            Current.Interact(gameObject);
            return true;
        }

        IInteractable FindNearest()
        {
            IInteractable best = null;
            var bestDist = float.MaxValue;
            foreach (var hit in Physics2D.OverlapCircleAll(transform.position, radius))
            {
                if (hit.gameObject == gameObject) continue;
                var candidate = hit.GetComponentInParent<IInteractable>();
                if (candidate == null) continue;
                var d = Vector2.Distance(transform.position, hit.transform.position);
                if (d < bestDist) { bestDist = d; best = candidate; }
            }
            return best;
        }

        void OnGUI()
        {
            if (Current == null || TerminalUI.IsOpen) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 18 };
            GUI.Box(new Rect(Screen.width / 2f - 160, Screen.height - 70, 320, 36), "[E] " + Current.Prompt, style);
        }
    }
}
