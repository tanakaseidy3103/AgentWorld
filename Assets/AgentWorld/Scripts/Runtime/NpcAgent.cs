using UnityEngine;

namespace AgentWorld
{
    /// Decides what an NPC does. WanderBrain today; an LLM-backed brain in a later MVP.
    public interface IAgentBrain
    {
        Vector2 Steer(NpcAgent agent, float deltaTime);
        string Talk(NpcAgent agent, GameObject user);
    }

    public class WanderBrain : IAgentBrain
    {
        Vector2 target;
        float wait;
        bool hasTarget;
        int line;

        public void Retarget() => hasTarget = false;

        public Vector2 Steer(NpcAgent agent, float dt)
        {
            Vector2 pos = agent.transform.position;
            if (wait > 0) { wait -= dt; return Vector2.zero; }
            if (!hasTarget)
            {
                target = agent.Home + Random.insideUnitCircle.normalized * Random.Range(0.8f, agent.wanderRadius);
                hasTarget = true;
            }
            var to = target - pos;
            if (to.magnitude < 0.15f) { hasTarget = false; wait = Random.Range(0.5f, 2f); return Vector2.zero; }
            return to.normalized;
        }

        public string Talk(NpcAgent agent, GameObject user)
        {
            if (agent.lines == null || agent.lines.Length == 0) return "...";
            return agent.lines[line++ % agent.lines.Length];
        }
    }

    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcAgent : MonoBehaviour, IInteractable
    {
        public string id = "npc";
        public string displayName = "NPC";
        public string role = "";
        public float speed = 1.5f;
        public float wanderRadius = 2.5f;
        [TextArea] public string[] lines;

        public IAgentBrain Brain { get; set; }
        public Vector2 Home { get; set; }
        public string Speech { get; private set; }

        Rigidbody2D rb;
        float speechUntil;
        float stuckTimer;

        public string Prompt => "Falar com " + displayName;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            Home = transform.position;
            Brain ??= new WanderBrain();
        }

        void FixedUpdate()
        {
            var talking = Time.time < speechUntil;
            var dir = talking ? Vector2.zero : Brain.Steer(this, Time.fixedDeltaTime);
            // Blocked by a wall: pick a new target instead of pushing forever.
            stuckTimer = dir != Vector2.zero && rb.linearVelocity.magnitude < 0.05f ? stuckTimer + Time.fixedDeltaTime : 0f;
            if (stuckTimer > 1f) { (Brain as WanderBrain)?.Retarget(); stuckTimer = 0f; }
            rb.linearVelocity = dir * speed;
        }

        public void Interact(GameObject user)
        {
            Speech = Brain.Talk(this, user);
            speechUntil = Time.time + 4f;
        }

        public void Teleport(Vector2 position)
        {
            rb.position = position;
            transform.position = position;
        }

        void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var p = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
            if (p.z < 0) return;
            var y = Screen.height - p.y;
            GUI.Label(new Rect(p.x - 60, y, 120, 20), displayName, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
            if (Time.time < speechUntil)
                GUI.Box(new Rect(p.x - 140, y - 46, 280, 44), Speech, new GUIStyle(GUI.skin.box) { wordWrap = true });
        }
    }
}
