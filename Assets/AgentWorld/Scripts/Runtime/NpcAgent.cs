using UnityEngine;

namespace AgentWorld
{
    /// Decides what an NPC does: WanderBrain for ambient NPCs, LlamaAgentBrain for AI agents.
    public interface IAgentBrain
    {
        Vector2 Steer(NpcAgent agent, float deltaTime);
        string Talk(NpcAgent agent, GameObject user);
        void OnStuck(NpcAgent agent);
    }

    public class WanderBrain : IAgentBrain
    {
        Vector2 target;
        float wait;
        bool hasTarget;
        int line;

        public void Retarget() => hasTarget = false;
        public void OnStuck(NpcAgent agent) => Retarget();

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
        /// builder / explorer / assistant gets an LLM brain; empty keeps the wander brain.
        public string agentRole = "";
        public float speed = 1.5f;
        public float wanderRadius = 2.5f;
        [TextArea] public string[] lines;

        public IAgentBrain Brain { get; set; }
        public Vector2 Home { get; set; }
        public string Speech { get; private set; }

        Rigidbody2D rb;
        float speechUntil, freezeUntil;
        float stuckTimer;
        Computer chat;

        public string Prompt => "Falar com " + displayName;
        public LlamaAgentBrain Ai => Brain as LlamaAgentBrain;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            Home = transform.position;
            Brain ??= string.IsNullOrEmpty(agentRole) ? new WanderBrain() : new LlamaAgentBrain(this);
        }

        void FixedUpdate()
        {
            var dir = Time.time < freezeUntil ? Vector2.zero : Brain.Steer(this, Time.fixedDeltaTime);
            // Blocked by a wall: pick a new target instead of pushing forever.
            stuckTimer = dir != Vector2.zero && rb.linearVelocity.magnitude < 0.05f ? stuckTimer + Time.fixedDeltaTime : 0f;
            if (stuckTimer > 1f) { Brain.OnStuck(this); stuckTimer = 0f; }
            rb.linearVelocity = dir * speed;
        }

        public void Interact(GameObject user)
        {
            if (Ai != null) { TerminalUI.Open(Chat()); return; }
            Say(Brain.Talk(this, user));
            freezeUntil = speechUntil;
        }

        /// Speech bubble that doesn't stop the NPC (agents talk while they work).
        public void Say(string text)
        {
            Speech = text;
            speechUntil = Time.time + Mathf.Clamp(text.Length * 0.08f, 3f, 8f);
        }

        // Chat window reuses the terminal UI with a backend that forwards to the LLM.
        Computer Chat()
        {
            if (chat) return chat;
            var go = new GameObject("Chat");
            go.transform.SetParent(transform, false);
            chat = go.AddComponent<Computer>();
            chat.id = "conversa com " + displayName;
            chat.Backend = new NpcChatBackend(this);
            chat.log.Add($"Você está falando com {displayName} ({agentRole}), movido por Llama local. Esc fecha.");
            return chat;
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
            var label = Ai == null ? displayName : $"{displayName} [{agentRole}]\n{Ai.Status} {Ai.Detail}";
            GUI.Label(new Rect(p.x - 90, y - (Ai == null ? 0 : 16), 180, 36), label, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
            if (Time.time < speechUntil)
                GUI.Box(new Rect(p.x - 140, y - 62, 280, 44), Speech, new GUIStyle(GUI.skin.box) { wordWrap = true });
        }
    }
}
