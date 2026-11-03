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
                else return (d - pos).normalized;
            }
            if (moveTarget is Vector2 t && Vector2.Distance(pos, t) > 0.3f) return (t - pos).normalized;
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
            if (string.IsNullOrWhiteSpace(goal)) { error = "tarefa vazia"; return false; }
            if (Busy) { error = $"{Name} está ocupado ({Status}); use 'agent stop {npc.id}'"; return false; }
            if (Time.time - lastCall < MinSecondsBetweenCalls) { error = "devagar: aguarde alguns segundos entre pedidos"; return false; }
            lastCall = Time.time;
            cts = new CancellationTokenSource();
            _ = Run(goal.Trim(), output ?? (_ => { }), cts.Token);
            return true;
        }

        public void Stop() => cts?.Cancel();

        async Task Run(string goal, Action<string> output, CancellationToken ct)
        {
            Goal = goal;
            void Say(string line) { output($"[{Name}] {line}"); }
            try
            {
                Set(AgentStatus.Thinking, "consultando " + AgentHub.Llm.Model);
                Say($"Pensando... (Llama: {AgentHub.Llm.Model})");
                var plan = await MakePlan(goal, ct);

                Set(AgentStatus.Planning, plan.goal);
                output($"[Llama] plano: {string.Join(" -> ", plan.actions)}");
                for (var i = 0; i < plan.actions.Count; i++)
                {
                    var act = plan.actions[i];
                    output($"[Unity] {i + 1}/{plan.actions.Count} {act}");
                    await Execute(act, Say, ct);
                }

                LastResult = "concluído: " + goal;
                Remember($"Completed task '{goal}'");
                Set(AgentStatus.Completed, "");
                Say("Tarefa concluída.");
            }
            catch (OperationCanceledException)
            {
                LastResult = "cancelado: " + goal;
                Set(AgentStatus.Cancelled, "");
                Say("Tarefa cancelada.");
            }
            catch (Exception e)
            {
                LastResult = "falhou: " + e.Message;
                Remember($"Failed task '{goal}': {e.Message}");
                Set(AgentStatus.Failed, e.Message);
                Say("Falhou: " + e.Message);
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
                Set(AgentStatus.Thinking, "replanejando");
            }
        }

        /// Main-thread timeout: cancels the request if the model takes too long.
        async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var task = call(linked.Token);
            var start = Time.realtimeSinceStartup;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup - start > AgentHub.LlmTimeoutSeconds)
                {
                    linked.Cancel();
                    throw new TimeoutException($"LLM não respondeu em {AgentHub.LlmTimeoutSeconds:0}s");
                }
                await Awaitable.NextFrameAsync(ct);
            }
            return await task;
        }

        string PlanSystemPrompt(List<string> locations) =>
$@"You are {Name}, the {Role} agent living in AgentWorld, a 2D pixel-art village. Personality: {Personality}.
You act ONLY by returning one JSON object with this schema:
{{""agent"":""{Role}"",""goal"":""<short goal>"",""actions"":[<action>, ...]}}
Allowed actions (nothing else exists):
{{""type"":""move_to"",""target"":""<location id>""}}
{{""type"":""build"",""structure"":""house"",""location"":""<location id>""}}   (builder only; builds a house next to that location)
{{""type"":""inspect"",""target"":""<location id>""}}   (look around a location and report)
{{""type"":""speak"",""message"":""<one short sentence>""}}
{{""type"":""wait"",""seconds"":2}}
Rules:
- At most 6 actions.
- Valid location ids: {string.Join(", ", locations)}. Never invent ids or coordinates.
- If the requested place does not exist, choose the most sensible valid id and say so in a speak action.
- Include EVERY step needed to fully complete the request. {(Role == "builder" ? "Building only happens with a build action." : "You cannot build.")}
- Write speak messages in the same language as the player's request.
- Output JSON only.
Example request ""{(Role == "builder" ? "build a house by the plaza" : "check the plaza")}"" ->
{(Role == "builder"
    ? @"{""agent"":""builder"",""goal"":""Build a house near the plaza"",""actions"":[{""type"":""speak"",""message"":""On my way!""},{""type"":""move_to"",""target"":""plaza""},{""type"":""build"",""structure"":""house"",""location"":""plaza""},{""type"":""speak"",""message"":""The house is ready!""}]}"
    : $@"{{""agent"":""{Role}"",""goal"":""Inspect the plaza"",""actions"":[{{""type"":""move_to"",""target"":""plaza""}},{{""type"":""inspect"",""target"":""plaza""}},{{""type"":""speak"",""message"":""I checked the plaza.""}}]}}")}";

        string PlanUserPrompt(string goal) =>
            $"{AgentHub.WorldSummary()}Your recent memory:\n{(Memory.Count == 0 ? "- (nothing yet)" : string.Join("\n", Memory.Select(m => "- " + m)))}\nPlayer request: {goal}";

        string Personality => Personalities.TryGetValue(Role, out var p) ? p : "helpful";

        async Task Execute(AgentAction act, Action<string> say, CancellationToken ct)
        {
            switch (act.type)
            {
                case "move_to":
                    await MoveTo(Landmark.Find(act.target).StandPoint, act.target, ct);
                    break;
                case "build":
                    var builder = StructureBuilder.Instance ?? throw new Exception("sistema de construção ausente");
                    var lm = Landmark.Find(act.location);
                    var site = builder.FindSite(lm.transform.position) ?? throw new Exception($"sem espaço livre perto de {act.location}");
                    await MoveTo(site + new Vector2(0, -1.2f), "canteiro", ct);
                    Set(AgentStatus.Acting, "construindo");
                    say($"Construindo {act.structure} em ({site.x:0.#}, {site.y:0.#})...");
                    var built = await builder.Construct(act.structure, site, Name, k => Detail = $"construindo {k * 100:0}%", ct);
                    Remember($"Built {built.id} near {act.location}");
                    say($"{built.id} pronta perto de {act.location}.");
                    break;
                case "inspect":
                    var target = Landmark.Find(act.target);
                    await MoveTo(target.StandPoint, act.target, ct);
                    Set(AgentStatus.Acting, "inspecionando");
                    var report = Inspect(target);
                    Remember($"Inspected {act.target}: {report}");
                    say(report);
                    npc.Say(report);
                    break;
                case "speak":
                    Set(AgentStatus.Acting, "falando");
                    npc.Say(act.message);
                    say($"\"{act.message}\"");
                    await Awaitable.WaitForSecondsAsync(1.5f, ct);
                    break;
                case "wait":
                    Set(AgentStatus.Acting, "esperando");
                    await Awaitable.WaitForSecondsAsync(act.seconds, ct);
                    break;
            }
        }

        static string Inspect(Landmark target)
        {
            Vector2 p = target.transform.position;
            var near = Landmark.All.Where(l => l != target && Vector2.Distance(p, l.transform.position) < 7f).Select(l => l.id).OrderBy(s => s).ToList();
            var people = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None)
                .Where(n => Vector2.Distance(p, n.transform.position) < 5f).Select(n => n.displayName).ToList();
            return $"{target.id} ({target.description}). Perto: {(near.Count > 0 ? string.Join(", ", near) : "nada")}. Pessoas: {(people.Count > 0 ? string.Join(", ", people) : "ninguém")}.";
        }

        // Lab walls only open at the bottom doorway (x≈0); route through it when entering or leaving.
        static readonly Rect LabInside = new Rect(-6f, 4f, 12f, 8f);
        static readonly Vector2 DoorIn = new Vector2(0, 5f), DoorOut = new Vector2(0, 2f);

        async Task MoveTo(Vector2 dest, string label, CancellationToken ct)
        {
            Set(AgentStatus.Moving, "indo para " + label);
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
                    if (Time.time - start > AgentHub.MoveTimeoutSeconds) throw new Exception($"não consegui chegar em {label}");
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
            if (chatting) { error = $"{Name} ainda está respondendo..."; return false; }
            if (Time.time - lastCall < MinSecondsBetweenCalls) { error = "devagar: aguarde alguns segundos"; return false; }
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
            catch (Exception e) { output($"[{Name}] (IA indisponível: {e.Message})"); }
            finally { chatting = false; }
        }

        // --- State ---------------------------------------------------------------

        void Set(AgentStatus s, string detail) { Status = s; Detail = detail ?? ""; }

        void Remember(string fact) => AddCapped(Memory, fact, MemoryCap);

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
