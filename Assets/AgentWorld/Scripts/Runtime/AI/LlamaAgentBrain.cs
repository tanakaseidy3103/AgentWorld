using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    public enum AgentStatus { Idle, Thinking, Planning, Moving, Acting, Completed, Failed, Cancelled }

    /// Shared AI services. Tests swap Llm for a deterministic mock.
    public static class AgentHub
    {
        static ILlmClient llm;
        public static ILlmClient Llm { get => llm ??= new OllamaClient(); set => llm = value; }
        public static float LlmTimeoutSeconds = 120f;
        public static float MoveTimeoutSeconds = 40f;

        public static IEnumerable<NpcAgent> Agents =>
            UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None).Where(n => n.Brain is LlamaAgentBrain).OrderBy(n => n.id);

        public static NpcAgent Find(string key)
        {
            key = (key ?? "").ToLowerInvariant();
            return Agents.FirstOrDefault(n => n.id == key || n.agentRole == key || n.displayName.ToLowerInvariant() == key);
        }

        /// Facts about the live world that go into every prompt, so plans reference real places.
        public static string WorldSummary()
        {
            var sb = new StringBuilder("Locations (id: description @ position):\n");
            foreach (var l in Landmark.All.OrderBy(l => l.id))
                sb.Append($"- {l.id}: {l.description} @ ({l.transform.position.x:0},{l.transform.position.y:0})\n");
            sb.Append("Agents:\n");
            foreach (var n in Agents)
                sb.Append($"- {n.displayName} ({n.agentRole}): {((LlamaAgentBrain)n.Brain).Status}\n");
            return sb.ToString();
        }
    }

    /// LLM-driven NPC. The model only writes a JSON plan; Unity validates and executes it step by step.
    public class LlamaAgentBrain : IAgentBrain
    {
        public const int MemoryCap = 12, ChatCap = 6;
        public const float MinSecondsBetweenCalls = 2f;
        const float WorkSpeed = 1.8f; // agents on a task walk faster than idle wandering

        static readonly Dictionary<string, string> Personalities = new Dictionary<string, string>
        {
            ["builder"] = "practical, cheerful and proud of solid work; speaks briefly",
            ["explorer"] = "curious and observant; loves describing what is around",
            ["assistant"] = "friendly guide who explains the world and tells the player which agent to ask",
        };

        readonly NpcAgent npc;
        readonly WanderBrain idle = new WanderBrain();
        CancellationTokenSource cts;
        Vector2? moveTarget, detour;
        float detourUntil, lastCall = -99f;
        bool chatting;

        public AgentStatus Status { get; private set; } = AgentStatus.Idle;
        public string Goal { get; private set; } = "";
        public string Detail { get; private set; } = "";
        public string LastResult { get; private set; } = "";
        public List<string> Memory { get; } = new List<string>();
        public List<string> ChatHistory { get; } = new List<string>();
        public bool Busy => cts != null;
        /// Until this time the agent shows a "decision made" indicator (set when a Llama decision is accepted).
        public float DecisionUntil { get; private set; }
        public void MarkDecision() => DecisionUntil = Time.time + 3f;
        public string Role => npc.agentRole;
        string Name => npc.displayName;

        public LlamaAgentBrain(NpcAgent npc) => this.npc = npc;

        // --- IAgentBrain ----------------------------------------------------------

        public Vector2 Steer(NpcAgent agent, float dt)
        {
            if (!Busy) return idle.Steer(agent, dt);
            Vector2 pos = agent.transform.position;
            if (detour is Vector2 d)
            {
                if (Vector2.Distance(pos, d) < 0.2f || Time.time > detourUntil) detour = null;
                else return (d - pos).normalized * WorkSpeed;
            }
            if (moveTarget is Vector2 t && Vector2.Distance(pos, t) > 0.3f) return (t - pos).normalized * WorkSpeed;
            return Vector2.zero;
        }

        public void OnStuck(NpcAgent agent)
        {
            if (!Busy) { idle.OnStuck(agent); return; }
            if (moveTarget == null) return;
            // Sidestep around whatever blocks us, alternating sides.
            var to = (moveTarget.Value - (Vector2)agent.transform.position).normalized;
            var side = new Vector2(-to.y, to.x) * (UnityEngine.Random.value < 0.5f ? 1.2f : -1.2f);
            detour = (Vector2)agent.transform.position + side - to * 0.3f;
            detourUntil = Time.time + 1f;
        }

        public string Talk(NpcAgent agent, GameObject user) => idle.Talk(agent, user);

        // --- Tasks ---------------------------------------------------------------

        public bool Assign(string goal, Action<string> output, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(goal)) { error = "empty task"; return false; }
            if (Busy) { error = $"{Name} is busy ({Status}); use 'agent stop {npc.id}'"; return false; }
            if (Time.time - lastCall < MinSecondsBetweenCalls) { error = "slow down: wait a few seconds between requests"; return false; }
            lastCall = Time.time;
            cts = new CancellationTokenSource();
            _ = Run(goal.Trim(), output ?? (_ => { }), cts.Token);
            return true;
        }

        public void Stop() => cts?.Cancel();

        /// Runs already-validated actions for a mission (no LLM call). False if busy, failed or cancelled.
        public Task<bool> Perform(string goal, List<AgentAction> actions, Action<string> output, CancellationToken ct)
        {
            if (Busy) return Task.FromResult(false);
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return Guarded(goal, output ?? (_ => { }), say => ExecuteAll(actions, output ?? (_ => { }), say, cts.Token));
        }

        Task Run(string goal, Action<string> output, CancellationToken ct) => Guarded(goal, output, async say =>
        {
            Set(AgentStatus.Thinking, "asking " + AgentHub.Llm.Model);
            say($"Thinking... (Llama: {AgentHub.Llm.Model})");
            var plan = await MakePlan(goal, ct);
            Set(AgentStatus.Planning, plan.goal);
            output($"[Llama] plano: {string.Join(" -> ", plan.actions)}");
            await ExecuteAll(plan.actions, output, say, ct);
        });

        async Task ExecuteAll(List<AgentAction> actions, Action<string> output, Action<string> say, CancellationToken ct)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                output($"[Unity] {i + 1}/{actions.Count} {actions[i]}");
                await Execute(actions[i], say, ct);
            }
        }

        /// Shared status/memory bookkeeping around a task body; never throws.
        async Task<bool> Guarded(string goal, Action<string> output, Func<Action<string>, Task> body)
        {
            Goal = goal;
            void Say(string line) { output($"[{Name}] {line}"); }
            try
            {
                await body(Say);
                LastResult = "completed: " + goal;
                Remember($"Completed task '{goal}'");
                Set(AgentStatus.Completed, "");
                Say("Task completed.");
                return true;
            }
            catch (OperationCanceledException)
            {
                LastResult = "cancelled: " + goal;
                Set(AgentStatus.Cancelled, "");
                Say("Task cancelled.");
                return false;
            }
            catch (Exception e)
            {
                LastResult = "failed: " + e.Message;
                Remember($"Failed task '{goal}': {e.Message}");
                Set(AgentStatus.Failed, e.Message);
                Say("Failed: " + e.Message);
                return false;
            }
            finally
            {
                moveTarget = detour = null;
                cts?.Dispose();
                cts = null;
            }
        }

        async Task<AgentPlan> MakePlan(string goal, CancellationToken ct)
        {
            var locations = Landmark.All.Select(l => l.id).ToList();
            var feedback = "";
            for (var attempt = 1; ; attempt++)
            {
                var raw = await WithTimeout(t => AgentHub.Llm.Chat(PlanSystemPrompt(locations), PlanUserPrompt(goal) + feedback, true, t), ct);
                var plan = PlanValidator.Parse(raw, out var parseError);
                var errors = plan == null ? new List<string> { parseError } : PlanValidator.Validate(plan, locations, Role);
                if (errors.Count == 0) return plan;
                if (attempt >= 2) throw new Exception("plano do LLM rejeitado: " + string.Join("; ", errors));
                // One retry with the validator's complaints, then give up.
                feedback = "\nYour previous plan was REJECTED: " + string.Join("; ", errors) + ". Return a corrected JSON plan.";
                Set(AgentStatus.Thinking, "replanning");
            }
        }

        /// Main-thread timeout: cancels the request if the model takes too long.
        public static async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var task = call(linked.Token);
            var start = Time.realtimeSinceStartup;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup - start > AgentHub.LlmTimeoutSeconds)
                {
                    linked.Cancel();
                    throw new TimeoutException($"LLM did not answer within {AgentHub.LlmTimeoutSeconds:0}s");
                }
                await Awaitable.NextFrameAsync(ct);
            }
            return await task;
        }

        string PlanSystemPrompt(List<string> locations) =>
$@"You are {Name}, the {Role} agent living in AgentWorld, a 2D pixel-art village. Personality: {Personality}.
You act ONLY by returning one JSON object with this schema:
{{""agent"":""{Role}"",""goal"":""<short goal>"",""actions"":[<action>, ...]}}
Allowed actions (your skills; nothing else exists):
{SkillLines}
Rules:
- At most 6 actions.
- Valid location ids: {string.Join(", ", locations)}. Never invent ids or coordinates.
- If the requested place does not exist, choose the most sensible valid id and say so in a speak action.
- Include EVERY step needed to fully complete the request. {(AgentConfigs.Has(Role, "build_house") ? "Building only happens with a build action." : "You cannot build.")}
- Write speak messages in the same language as the player's request.
- Output JSON only.
Example request ""{(AgentConfigs.Has(Role, "build_house") ? "build a house by the plaza" : "check the plaza")}"" ->
{(AgentConfigs.Has(Role, "build_house")
    ? @"{""agent"":""builder"",""goal"":""Build a house near the plaza"",""actions"":[{""type"":""speak"",""message"":""On my way!""},{""type"":""move_to"",""target"":""plaza""},{""type"":""build"",""structure"":""house"",""location"":""plaza""},{""type"":""speak"",""message"":""The house is ready!""}]}"
    : $@"{{""agent"":""{Role}"",""goal"":""Inspect the plaza"",""actions"":[{{""type"":""move_to"",""target"":""plaza""}},{{""type"":""inspect"",""target"":""plaza""}},{{""type"":""speak"",""message"":""I checked the plaza.""}}]}}")}";

        string SkillLines => string.Join("\n", AgentConfigs.SkillsFor(Role).Select(SkillRegistry.Get).Where(k => k != null).Select(k => $"{k.Example}   ({k.Description})"));

        string PlanUserPrompt(string goal) =>
            $"{AgentHub.WorldSummary()}Your recent memory:\n{(Memory.Count == 0 ? "- (nothing yet)" : string.Join("\n", Memory.Select(m => "- " + m)))}\nPlayer request: {goal}";

        string Personality => AgentConfigs.For(npc.id)?.personality is string p && p != "" ? p : Personalities.TryGetValue(Role, out var d) ? d : "helpful";

        async Task Execute(AgentAction act, Action<string> say, CancellationToken ct)
        {
            var skill = SkillRegistry.Get(SkillRegistry.IdFor(act)) ?? throw new Exception("skill desconhecida: " + act.type);
            await skill.Execute(new SkillContext { Npc = npc, Brain = this, Action = act, Say = say }, ct);
        }

        /// What an inspection sees at a landmark (nearby landmarks and people), from live scene data.
        public static string Describe(Landmark target)
        {
            Vector2 p = target.transform.position;
            var near = Landmark.All.Where(l => l != target && Vector2.Distance(p, l.transform.position) < 7f).Select(l => l.id).OrderBy(s => s).ToList();
            var people = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None)
                .Where(n => Vector2.Distance(p, n.transform.position) < 5f).Select(n => n.displayName).ToList();
            return $"{target.id} ({target.description}). Nearby: {(near.Count > 0 ? string.Join(", ", near) : "nothing")}. People: {(people.Count > 0 ? string.Join(", ", people) : "nobody")}.";
        }

        // Lab walls only open at the bottom doorway (x≈0); route through it when entering or leaving.
        static readonly Rect LabInside = new Rect(-6f, 4f, 12f, 8f);
        static readonly Vector2 DoorIn = new Vector2(0, 5f), DoorOut = new Vector2(0, 2f);

        internal async Task MoveTo(Vector2 dest, string label, CancellationToken ct)
        {
            Set(AgentStatus.Moving, "walking to " + label);
            var points = new List<Vector2>();
            bool fromLab = LabInside.Contains(npc.transform.position), toLab = LabInside.Contains(dest);
            if (fromLab && !toLab) points.AddRange(new[] { DoorIn, DoorOut });
            if (!fromLab && toLab) points.AddRange(new[] { DoorOut, DoorIn });
            points.Add(dest);

            foreach (var p in points)
            {
                moveTarget = p;
                var start = Time.time;
                while (Vector2.Distance(npc.transform.position, p) > 0.3f)
                {
                    if (Time.time - start > AgentHub.MoveTimeoutSeconds) throw new Exception($"could not reach {label}");
                    await Awaitable.NextFrameAsync(ct);
                }
            }
            moveTarget = null;
        }

        // --- Conversation --------------------------------------------------------

        /// Free-form chat (E on the NPC). Plain text reply; never executes actions.
        public bool Chat(string message, Action<string> output, out string error)
        {
            error = null;
            if (chatting) { error = $"{Name} is still answering..."; return false; }
            if (Time.time - lastCall < MinSecondsBetweenCalls) { error = "slow down: wait a few seconds"; return false; }
            lastCall = Time.time;
            chatting = true;
            _ = ChatAsync(message.Trim(), output);
            return true;
        }

        async Task ChatAsync(string message, Action<string> output)
        {
            try
            {
                var system = $@"You are {Name}, the {Role} agent of AgentWorld, a 2D pixel-art village. Personality: {Personality}.
Current task: {(Busy ? Goal + " (" + Status + ")" : "none")}. Last result: {(LastResult == "" ? "none" : LastResult)}.
{AgentHub.WorldSummary()}Your memory:
{(Memory.Count == 0 ? "- (nothing yet)" : string.Join("\n", Memory.Select(m => "- " + m)))}
Reply in 1-3 short sentences, in the player's language, staying in character.
You cannot act during chat. If the player wants something done, tell them to type 'agent {Role} <task>' in a lab computer terminal.";
                var user = string.Join("\n", ChatHistory.Append("Player: " + message)) + $"\n{Name}:";
                var reply = (await WithTimeout(t => AgentHub.Llm.Chat(system, user, false, t), CancellationToken.None)).Trim();
                if (reply.Length > 400) reply = reply.Substring(0, 400);
                AddCapped(ChatHistory, "Player: " + message, ChatCap);
                AddCapped(ChatHistory, $"{Name}: {reply}", ChatCap);
                npc.Say(reply);
                output($"[{Name}] {reply}");
            }
            catch (Exception e) { output($"[{Name}] (AI unavailable: {e.Message})"); }
            finally { chatting = false; }
        }

        // --- State ---------------------------------------------------------------

        void Set(AgentStatus s, string detail) { Status = s; Detail = detail ?? ""; }

        /// For skill executors: show what the agent is doing right now.
        internal void SetActivity(AgentStatus s, string detail) => Set(s, detail);

        /// Lets a mission show what an idle agent is doing (e.g. Ada thinking about the plan).
        public void Show(AgentStatus s, string detail) { if (!Busy) Set(s, detail); }

        public void Remember(string fact) => AddCapped(Memory, fact, MemoryCap);

        static void AddCapped(List<string> list, string item, int cap)
        {
            list.Add(item);
            while (list.Count > cap) list.RemoveAt(0);
        }

        public AgentState Capture() => new AgentState
        {
            id = npc.id, role = Role, goal = Goal, lastResult = LastResult,
            // An interrupted task is not resumed after load.
            status = (Busy ? AgentStatus.Idle : Status).ToString(),
            memory = new List<string>(Memory),
        };

        public void Restore(AgentState s)
        {
            Stop();
            Goal = s.goal ?? ""; LastResult = s.lastResult ?? "";
            Status = Enum.TryParse<AgentStatus>(s.status, out var st) ? st : AgentStatus.Idle;
            Memory.Clear();
            if (s.memory != null) Memory.AddRange(s.memory.Skip(Math.Max(0, s.memory.Count - MemoryCap)));
        }
    }
}
