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
            if (Ai != null && !GetComponent<AgentIndicator>()) gameObject.AddComponent<AgentIndicator>();
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
            var p = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.15f);
            if (p.z < 0) return;
            var size = Mathf.Max(13, TerminalUI.FontSize(Screen.height) * 3 / 4);
            if (size != labelSize) BuildLabelStyles(size);

            // Name plate under the feet-anchored sprite's head: "Ada — Coordinator" + live activity word.
            var name = Ai == null ? displayName : $"{displayName} — {RoleTitle}";
            var w = nameStyle.CalcSize(new GUIContent(name)).x + size;
            var top = Screen.height - p.y - size * (Ai == null ? 1.6f : 2.7f);
            GUI.Box(new Rect(p.x - w / 2, top, w, size * 1.5f), name, nameStyle);
            if (Ai != null)
            {
                var activity = Activity;
                var aw = activityStyle.CalcSize(new GUIContent(activity)).x + size * 0.8f;
                GUI.Box(new Rect(p.x - aw / 2, top + size * 1.5f, aw, size * 1.2f), activity, activityStyle);
            }
            if (Time.time < speechUntil)
            {
                var bw = size * 18f;
                var bh = bubbleStyle.CalcHeight(new GUIContent(Speech), bw);
                GUI.Box(new Rect(p.x - bw / 2, top - bh - size * 0.3f, bw, bh), Speech, bubbleStyle);
            }
        }

        public string RoleTitle => AgentConfigs.For(id)?.title is string t && t != "" ? t
            : agentRole switch { "assistant" => "Coordinator", "explorer" => "Explorer", "builder" => "Builder", _ => role };

        /// One-word activity derived only from the brain's real state (no decorative states).
        public string Activity
        {
            get
            {
                var ai = Ai;
                if (ai == null) return "";
                switch (ai.Status)
                {
                    case AgentStatus.Thinking:
                    case AgentStatus.Planning: return "Planning";
                    case AgentStatus.Moving: return agentRole == "explorer" ? "Exploring" : "Moving";
                    case AgentStatus.Acting:
                        if (ai.Detail.StartsWith("construindo")) return "Building";
                        if (ai.Detail.StartsWith("inspecionando")) return "Exploring";
                        if (ai.Detail.StartsWith("falando")) return "Talking";
                        if (ai.Detail.StartsWith("esperando")) return "Waiting";
                        return "Acting";
                    case AgentStatus.Failed: return "Failed";
                    case AgentStatus.Cancelled: return "Cancelled";
                    default:
                        if (Time.time < speechUntil) return "Talking";
                        return ai.Detail != "" && MissionControl.Running ? "Waiting" : "Idle";
                }
            }
        }

        int labelSize = -1;
        GUIStyle nameStyle, activityStyle, bubbleStyle;

        void BuildLabelStyles(int size)
        {
            labelSize = size;
            Texture2D Bg(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
            nameStyle = new GUIStyle { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white, background = Bg(new Color(0.05f, 0.06f, 0.08f, 0.78f)) } };
            activityStyle = new GUIStyle { fontSize = size * 4 / 5, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.85f, 0.35f), background = Bg(new Color(0.05f, 0.06f, 0.08f, 0.6f)) } };
            bubbleStyle = new GUIStyle { fontSize = size * 4 / 5, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(size / 2, size / 2, size / 3, size / 3),
                normal = { textColor = new Color(0.1f, 0.1f, 0.12f), background = Bg(new Color(1f, 0.98f, 0.92f, 0.95f)) } };
        }
    }
}
