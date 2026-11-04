using UnityEngine;

namespace AgentWorld
{
    /// Bobbing icon above an AI agent that shows what it is doing right now:
    /// Ada thinking/deciding, Grace scouting, Linus hammering, and done/failed flashes.
    public class AgentIndicator : MonoBehaviour
    {
        NpcAgent npc;
        SpriteRenderer icon;
        AgentStatus last;
        float changedAt;
        Vector3 baseScale;

        void Start()
        {
            npc = GetComponent<NpcAgent>();
            baseScale = transform.localScale;
            var go = new GameObject("TaskIcon");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0.7f, 0.95f, 0);
            icon = go.AddComponent<SpriteRenderer>();
            icon.sortingOrder = 50;
        }

        void LateUpdate()
        {
            var ai = npc ? npc.Ai : null;
            if (ai == null) return;
            if (ai.Status != last) { last = ai.Status; changedAt = Time.time; }

            var name = Pick(ai);
            icon.enabled = name != null;
            if (name == null) { transform.localScale = baseScale; return; }
            icon.sprite = RuntimeArt.Icon(name);

            var t = Time.time;
            icon.transform.localPosition = new Vector3(0.7f, 0.95f + Mathf.Sin(t * 4f) * 0.06f, 0);
            var hammering = name == "build";
            icon.transform.localRotation = Quaternion.Euler(0, 0, hammering ? Mathf.Sin(t * 14f) * 35f : 0f);
            // Linus visibly works: a quick squash in rhythm with the hammer.
            transform.localScale = hammering ? new Vector3(baseScale.x, baseScale.y * (1f - Mathf.Abs(Mathf.Sin(t * 14f)) * 0.08f), baseScale.z) : baseScale;
        }

        string Pick(LlamaAgentBrain ai)
        {
            if (Time.time < ai.DecisionUntil) return "decide";
            switch (ai.Status)
            {
                case AgentStatus.Thinking:
                case AgentStatus.Planning: return "think";
                case AgentStatus.Moving: return ai.Role == "explorer" ? "scout" : "walk";
                case AgentStatus.Acting: return ai.Role == "builder" && ai.Detail.StartsWith("building") ? "build" : ai.Role == "explorer" ? "scout" : "think";
                case AgentStatus.Completed: return Time.time - changedAt < 3f ? "done" : null;
                case AgentStatus.Failed:
                case AgentStatus.Cancelled: return Time.time - changedAt < 5f ? "fail" : null;
                default: return null;
            }
        }
    }
}
