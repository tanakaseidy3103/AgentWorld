using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    /// Structured message Grace writes to shared memory; facts come from Unity, not from the model.
    [Serializable] public class SiteReport { public string location, nearby, reportedBy; public int freeSites; }
    [Serializable] public class MissionTask { public int id, attempts; public string agent, description, status = "Pending"; }

    [Serializable]
    public class MissionState
    {
        public string objective = "", status = "None", result = "";
        public int housesWanted, housesBuilt;
        public List<MissionTask> tasks = new List<MissionTask>();
        public List<SiteReport> sharedMemory = new List<SiteReport>();
        public List<string> log = new List<string>();
    }

    /// Multi-agent mission: Ada (Llama) delegates -> Grace explores (Unity facts) -> Ada (Llama) picks sites -> Linus builds.
    /// The model makes the decisions; every decision is validated against live world state before anything moves.
    public static class MissionControl
    {
        public const int MaxExplore = 3, MaxHouses = 3, MaxRetries = 2, LogCap = 40;
        static readonly string[] Live = { "Planning", "Exploring", "Selecting", "Building" };

        [Serializable] public class Delegation { public List<string> explore = new List<string>(); public int houses; public string message; }
        [Serializable] public class SiteChoice { public List<string> build_at = new List<string>(); public string reason; }

        public static MissionState State { get; private set; } = new MissionState();
        public static bool Running => cts != null;
        static CancellationTokenSource cts;
        static Action<string> output = _ => { };

        public static bool Start(string objective, Action<string> print, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(objective)) { error = "missão vazia"; return false; }
            if (Running) { error = "já existe uma missão em andamento (mission stop)"; return false; }
            var team = new[] { "assistant", "explorer", "builder" }.Select(AgentHub.Find).ToArray();
            if (team.Any(a => a == null)) { error = "Ada, Grace e Linus precisam estar na cena"; return false; }
            if (team.Any(a => a.Ai.Busy)) { error = "um agente está ocupado; use agent stop"; return false; }

            State = new MissionState { objective = objective.Trim(), status = "Planning" };
            output = print ?? (_ => { });
            cts = new CancellationTokenSource();
            _ = Run(team[0], team[1], team[2], cts.Token);
            return true;
        }

        public static void Stop() => cts?.Cancel();

        static async Task Run(NpcAgent ada, NpcAgent grace, NpcAgent linus, CancellationToken ct)
        {
            var builder = StructureBuilder.Instance;
            try
            {
                // 1. Ada delegates (Llama decision).
                ada.Ai.Show(AgentStatus.Thinking, "planejando a missão");
                Log($"[Ada] Pensando na missão (Llama: {AgentHub.Llm.Model})...");
                var plan = await AskJson<Delegation>("Ada", DelegationPrompt(), $"{AgentHub.WorldSummary()}Mission: {State.objective}", ValidateDelegation, ct);
                State.housesWanted = plan.houses;
                Log($"[Llama/Ada] explorar: {string.Join(", ", plan.explore)} | casas: {plan.houses} | \"{plan.message}\"");
                if (!string.IsNullOrWhiteSpace(plan.message)) ada.Say(plan.message);
                ada.Ai.Show(AgentStatus.Idle, "aguardando relatórios");

                // 2. Grace explores; Unity measures real free space and writes it to shared memory.
                State.status = "Exploring";
                foreach (var loc in plan.explore)
                {
                    var task = AddTask("Grace", $"inspecionar {loc}");
                    var ok = await grace.Ai.Perform($"inspecionar {loc}", Actions(("move_to", loc), ("inspect", loc)), Log, ct);
                    ct.ThrowIfCancellationRequested();
                    if (!ok) { Finish(task, "Failed"); Log($"[Unity] Grace não conseguiu inspecionar {loc}: {grace.Ai.LastResult}"); continue; }
                    var lm = Landmark.Find(loc);
                    var report = new SiteReport
                    {
                        location = loc, reportedBy = "Grace",
                        freeSites = builder ? builder.CountSites(lm.transform.position) : 0,
                        nearby = string.Join(", ", Landmark.All.Where(l => l != lm && Vector2.Distance(l.transform.position, lm.transform.position) < 7f).Select(l => l.id).OrderBy(s => s)),
                    };
                    State.sharedMemory.Add(report);
                    ada.Ai.Remember($"Grace reported {loc}: {report.freeSites} free building sites");
                    Finish(task, "Done");
                    Log($"[Grace -> memória] {loc}: {report.freeSites} terrenos livres (perto: {(report.nearby == "" ? "nada" : report.nearby)})");
                }
                if (!State.sharedMemory.Any(r => r.freeSites > 0)) throw new Exception("nenhum local explorado tem espaço para construir");

                // 3. Ada chooses sites from the reports (Llama decision, validated against capacity).
                State.status = "Selecting";
                ada.Ai.Show(AgentStatus.Thinking, "escolhendo terrenos");
                var choice = await AskJson<SiteChoice>("Ada", SelectionPrompt(), SelectionUser(), ValidateChoice, ct);
                Log($"[Llama/Ada] construir em: {string.Join(", ", choice.build_at)} | motivo: \"{choice.reason}\"");
                ada.Ai.Show(AgentStatus.Idle, "acompanhando a obra");
                ada.Say(choice.reason);

                // 4. Linus builds each approved site; Unity retries elsewhere if a site fills up.
                State.status = "Building";
                foreach (var site in choice.build_at)
                {
                    var task = AddTask("Linus", $"construir casa perto de {site}");
                    var target = site;
                    var built = false;
                    while (task.attempts <= MaxRetries)
                    {
                        if (builder.CountSites(Landmark.Find(target).transform.position) == 0)
                        {
                            var alt = State.sharedMemory.Where(r => r.location != target && builder.CountSites(Landmark.Find(r.location).transform.position) > 0)
                                .OrderByDescending(r => choice.build_at.Contains(r.location)).Select(r => r.location).FirstOrDefault();
                            if (alt == null) break;
                            Log($"[Unity] recuperação: {target} sem espaço, tentando {alt}");
                            target = alt;
                        }
                        task.attempts++;
                        var ok = await linus.Ai.Perform($"construir casa perto de {target}", Actions(("move_to", target), ("build", target)), Log, ct);
                        ct.ThrowIfCancellationRequested();
                        if (ok) { State.housesBuilt++; built = true; break; }
                        Log($"[Unity] tentativa {task.attempts} falhou: {linus.Ai.LastResult}");
                    }
                    Finish(task, built ? "Done" : "Failed");
                }

                State.status = State.housesBuilt >= State.housesWanted ? "Succeeded" : State.housesBuilt > 0 ? "Partial" : "Failed";
                State.result = $"{State.housesBuilt}/{State.housesWanted} casas construídas";
            }
            catch (OperationCanceledException)
            {
                State.status = "Cancelled";
                State.result = $"cancelada ({State.housesBuilt} casas construídas)";
                foreach (var a in new[] { grace, linus }) a.Ai.Stop();
            }
            catch (Exception e)
            {
                State.status = State.housesBuilt > 0 ? "Partial" : "Failed";
                State.result = e.Message;
            }
            finally
            {
                foreach (var t in State.tasks.Where(t => t.status == "Running" || t.status == "Pending")) t.status = State.status == "Cancelled" ? "Cancelled" : "Failed";
                ada.Ai.Show(AgentStatus.Idle, "");
                Log($"[Unity] Missão {State.status}: {State.result}");
                ada.Say($"Missão {State.status}: {State.result}");
                cts?.Dispose();
                cts = null;
            }
        }

        // --- Llama decisions -------------------------------------------------------

        static async Task<T> AskJson<T>(string who, string system, string user, Func<T, List<string>> validate, CancellationToken ct) where T : class
        {
            var feedback = "";
            for (var attempt = 1; ; attempt++)
            {
                var raw = await LlamaAgentBrain.WithTimeout(t => AgentHub.Llm.Chat(system, user + feedback, true, t), ct);
                T value = null;
                var errors = new List<string>();
                try
                {
                    int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
                    if (a < 0 || b <= a) errors.Add("resposta sem JSON");
                    else value = JsonUtility.FromJson<T>(raw.Substring(a, b - a + 1));
                }
                catch (Exception e) { errors.Add("JSON inválido: " + e.Message); }
                if (value != null) errors.AddRange(validate(value));
                if (errors.Count == 0) return value;
                Log($"[Unity] resposta de {who} rejeitada (tentativa {attempt}/{MaxRetries}): {string.Join("; ", errors)}");
                if (attempt >= MaxRetries) throw new Exception($"{who}: plano do LLM rejeitado após {attempt} tentativas");
                feedback = $"\nYour previous answer was REJECTED: {string.Join("; ", errors)}. Return corrected JSON.";
            }
        }

        /// Small models echo prompt placeholders ("<lake>", "near lake"); normalize formatting only, never meaning.
        static string Norm(string id) => (id ?? "").Trim().Trim('<', '>', '"', '\'', ' ').ToLowerInvariant().Replace(' ', '_').Replace("near_", "");

        static List<string> Ids => Landmark.All.Where(l => l.GetComponent<BuiltStructure>() == null).Select(l => l.id).OrderBy(s => s).ToList();

        static string DelegationPrompt() =>
$@"You are Ada, the coordinator agent of AgentWorld, a 2D pixel-art village. You lead two agents:
- Grace (explorer): walks to locations and reports how many free building sites each has.
- Linus (builder): builds houses.
Given the player's mission, decide which locations Grace must explore and how many houses Linus should build.
Return ONLY JSON with keys explore (list of location ids), houses (number 1-{MaxHouses}) and message (one short sentence to your team).
Rules: explore 1 to {MaxExplore} DIFFERENT ids, only from: {string.Join(", ", Ids)}.
Prefer locations named in the mission plus open areas next to them. A village means {MaxHouses} houses.
Write the message in the same language as the mission.
Example: {{""explore"":[""plaza"",""east_field""],""houses"":2,""message"":""Grace, check the plaza and the east field; Linus, get ready.""}}";

        static List<string> ValidateDelegation(Delegation d)
        {
            var errors = new List<string>();
            d.explore = (d.explore ?? new List<string>()).Select(Norm).Distinct().ToList();
            if (d.explore.Count == 0) errors.Add("explore vazio");
            if (d.explore.Count > MaxExplore) d.explore = d.explore.Take(MaxExplore).ToList();
            errors.AddRange(d.explore.Where(id => !Ids.Contains(id)).Select(id => $"local inválido '{id}'"));
            if (d.houses < 1) errors.Add("houses deve ser de 1 a " + MaxHouses);
            d.houses = Mathf.Clamp(d.houses, 1, MaxHouses);
            if (d.message?.Length > 200) d.message = d.message.Substring(0, 200);
            return errors;
        }

        static string SelectionPrompt() =>
$@"You are Ada, the coordinator agent of AgentWorld. Grace explored locations and wrote reports to shared memory.
Choose where Linus builds exactly {State.housesWanted} house(s).
Return ONLY this JSON, e.g. {{""build_at"":[""plaza"",""plaza""],""reason"":""The plaza has the most free land.""}}
Rules:
- build_at may ONLY contain these ids: {string.Join(", ", State.sharedMemory.Where(r => r.freeSites > 0).Select(r => r.location))}.
- A location may repeat at most as many times as its free sites.
- If the mission names a place (e.g. ""near the lake"") and it has free sites, put the houses there first.
Write the reason in the same language as the mission.";

        static string SelectionUser() =>
            $"Mission: {State.objective}\nShared memory (Grace's reports):\n" +
            string.Join("\n", State.sharedMemory.Select(r => $"- location id: {r.location} | free sites: {r.freeSites} | landmarks close by (not buildable ids): {(r.nearby == "" ? "none" : r.nearby)}"));

        static List<string> ValidateChoice(SiteChoice c)
        {
            var errors = new List<string>();
            c.build_at = (c.build_at ?? new List<string>()).Select(Norm).Take(State.housesWanted).ToList();
            if (c.build_at.Count == 0) errors.Add("build_at vazio");
            foreach (var g in c.build_at.GroupBy(s => s))
            {
                var r = State.sharedMemory.FirstOrDefault(x => x.location == g.Key);
                if (r == null) errors.Add($"'{g.Key}' não foi explorado");
                else if (g.Count() > r.freeSites) errors.Add($"'{g.Key}' só tem {r.freeSites} terrenos livres");
            }
            if (string.IsNullOrWhiteSpace(c.reason)) c.reason = "Locais escolhidos.";
            return errors;
        }

        // --- Helpers ---------------------------------------------------------------

        static List<AgentAction> Actions(params (string type, string loc)[] steps) => steps.Select(s =>
            s.type == "build" ? new AgentAction { type = "build", structure = "house", location = s.loc } : new AgentAction { type = s.type, target = s.loc }).ToList();

        static MissionTask AddTask(string agent, string description)
        {
            var t = new MissionTask { id = State.tasks.Count + 1, agent = agent, description = description, status = "Running" };
            State.tasks.Add(t);
            return t;
        }

        static void Finish(MissionTask t, string status) => t.status = status;

        static void Log(string line)
        {
            State.log.Add(line);
            while (State.log.Count > LogCap) State.log.RemoveAt(0);
            output(line);
            Debug.Log("[mission] " + line);
        }

        public static MissionState Capture()
        {
            var copy = JsonUtility.FromJson<MissionState>(JsonUtility.ToJson(State));
            if (Live.Contains(copy.status)) copy.status = "Interrupted"; // missions are not resumed after load
            return copy;
        }

        public static void Restore(MissionState s)
        {
            Stop();
            State = s ?? new MissionState();
            if (Live.Contains(State.status)) State.status = "Interrupted";
        }
    }
}
