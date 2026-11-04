using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    /// What a skill executor gets: the acting agent, the validated action and a way to report.
    public class SkillContext
    {
        public NpcAgent Npc;
        public LlamaAgentBrain Brain;
        public AgentAction Action;
        public Action<string> Say;
    }

    /// A capability an agent may use. The LLM only names it in JSON; Unity validates the parameters
    /// (Validate) and runs the executor (Execute). There is no code path from model text to code.
    public class Skill
    {
        public string Id, Name, Description, Safety;
        /// Roles allowed to hold this skill at all (agents.json can only narrow this).
        public string[] Roles;
        /// Parameter schema shown to the model and in docs, e.g. "target: location id".
        public string[] Parameters;
        /// One JSON action the model can copy, e.g. {"type":"move_to","target":"<location id>"}.
        public string Example;
        /// Returns null when valid, otherwise an error. May normalize the action in place.
        public Func<AgentAction, ICollection<string>, string> Validate;
        /// Runs on Unity's main thread; returns a short result for logs and memory.
        public Func<SkillContext, CancellationToken, Task<string>> Execute;
    }

    public static class SkillRegistry
    {
        static readonly Dictionary<string, Skill> skills = new Dictionary<string, Skill>();

        public static IEnumerable<Skill> All => skills.Values.OrderBy(s => s.Id);
        public static Skill Get(string id) => id != null && skills.TryGetValue(id, out var s) ? s : null;

        /// Model actions keep the original MVP format; "build" + structure maps to build_house / build_farm.
        public static string IdFor(AgentAction a) => a?.type == "build" ? "build_" + (a.structure ?? "") : a?.type;

        public static void Register(Skill s)
        {
            if (string.IsNullOrWhiteSpace(s?.Id)) throw new ArgumentException("skill sem Id");
            if (s.Validate == null || s.Execute == null) throw new ArgumentException($"skill '{s.Id}' precisa de Validate e Execute");
            if (skills.ContainsKey(s.Id)) throw new ArgumentException($"skill '{s.Id}' já registrada");
            skills[s.Id] = s;
        }

        static SkillRegistry() => RegisterBuiltIns();

        static string NeedLocation(string id, ICollection<string> locations, string field) =>
            locations.Contains(id) ? null : $"local inválido '{id}' em {field}";

        static void RegisterBuiltIns()
        {
            var everyone = new[] { "assistant", "explorer", "builder" };

            Register(new Skill
            {
                Id = "move_to", Name = "Move to", Roles = everyone,
                Description = "Walk to a named location (route through the lab door when needed).",
                Parameters = new[] { "target: location id" },
                Safety = "Only known landmark ids; no coordinates from the model.",
                Example = "{\"type\":\"move_to\",\"target\":\"<location id>\"}",
                Validate = (a, locs) => NeedLocation(a.target = PlanValidator.Loc(a.target), locs, "target"),
                Execute = async (c, ct) => { await c.Brain.MoveTo(Landmark.Find(c.Action.target).StandPoint, c.Action.target, ct); return "arrived at " + c.Action.target; },
            });

            Register(new Skill
            {
                Id = "inspect", Name = "Inspect", Roles = everyone,
                Description = "Walk to a location and describe what is nearby (landmarks, people).",
                Parameters = new[] { "target: location id" },
                Safety = "Read-only.",
                Example = "{\"type\":\"inspect\",\"target\":\"<location id>\"}",
                Validate = (a, locs) => NeedLocation(a.target = PlanValidator.Loc(a.target), locs, "target"),
                Execute = async (c, ct) =>
                {
                    var target = Landmark.Find(c.Action.target);
                    await c.Brain.MoveTo(target.StandPoint, c.Action.target, ct);
                    c.Brain.SetActivity(AgentStatus.Acting, "inspecionando");
                    var report = LlamaAgentBrain.Describe(target);
                    c.Brain.Remember($"Inspected {c.Action.target}: {report}");
                    c.Say(report);
                    c.Npc.Say(report);
                    return report;
                },
            });

            Register(new Skill
            {
                Id = "report_findings", Name = "Report findings", Roles = new[] { "explorer" },
                Description = "Measure how many free building sites a location has right now and report it.",
                Parameters = new[] { "target: location id" },
                Safety = "Read-only; the number is computed by Unity, never by the model.",
                Example = "{\"type\":\"report_findings\",\"target\":\"<location id>\"}",
                Validate = (a, locs) => NeedLocation(a.target = PlanValidator.Loc(a.target), locs, "target"),
                Execute = (c, ct) =>
                {
                    var sb = StructureBuilder.Instance ?? throw new Exception("sistema de construção ausente");
                    var free = sb.CountSites(Landmark.Find(c.Action.target).transform.position);
                    var line = $"{c.Action.target}: {free} terrenos livres para construir";
                    c.Brain.Remember($"Measured {c.Action.target}: {free} free building sites");
                    c.Say(line);
                    c.Npc.Say(line);
                    return Task.FromResult(line);
                },
            });

            foreach (var (type, label) in new[] { ("house", "casa"), ("farm", "fazenda") })
                Register(new Skill
                {
                    Id = "build_" + type, Name = "Build " + type, Roles = new[] { "builder" },
                    Description = type == "house" ? "Build a house on the nearest free site next to a location." : "Build a walkable fenced farm on the nearest free site next to a location.",
                    Parameters = new[] { "location: location id" },
                    Safety = "Unity picks the exact site: inside the map, off roads and the lab, no overlap, clearance around it; max 12 structures.",
                    Example = $"{{\"type\":\"build\",\"structure\":\"{type}\",\"location\":\"<location id>\"}}",
                    Validate = (a, locs) => NeedLocation(a.location = PlanValidator.Loc(string.IsNullOrEmpty(a.location) ? a.target : a.location), locs, "location"),
                    Execute = async (c, ct) =>
                    {
                        var builder = StructureBuilder.Instance ?? throw new Exception("sistema de construção ausente");
                        var lm = Landmark.Find(c.Action.location);
                        var site = builder.FindSite(lm.transform.position) ?? throw new Exception($"sem espaço livre perto de {c.Action.location}");
                        await c.Brain.MoveTo(site + new Vector2(0, -1.2f), "canteiro", ct);
                        c.Brain.SetActivity(AgentStatus.Acting, "construindo");
                        c.Say($"Construindo {type} em ({site.x:0.#}, {site.y:0.#})...");
                        var built = await builder.Construct(type, site, c.Npc.displayName, k => c.Brain.SetActivity(AgentStatus.Acting, $"construindo {k * 100:0}%"), ct);
                        c.Brain.Remember($"Built {built.id} near {c.Action.location}");
                        c.Say($"{built.id} pronta perto de {c.Action.location}.");
                        return $"built {built.id} ({label})";
                    },
                });

            Register(new Skill
            {
                Id = "speak", Name = "Speak", Roles = everyone,
                Description = "Say one short sentence in a speech bubble.",
                Parameters = new[] { "message: text (max 200 chars)" },
                Safety = "Text only; shown escaped in the UI.",
                Example = "{\"type\":\"speak\",\"message\":\"<one short sentence>\"}",
                Validate = (a, _) =>
                {
                    if (string.IsNullOrWhiteSpace(a.message)) return "speak sem mensagem";
                    if (a.message.Length > 200) a.message = a.message.Substring(0, 200);
                    return null;
                },
                Execute = async (c, ct) =>
                {
                    c.Brain.SetActivity(AgentStatus.Acting, "falando");
                    c.Npc.Say(c.Action.message);
                    c.Say($"\"{c.Action.message}\"");
                    await Awaitable.WaitForSecondsAsync(1.5f, ct);
                    return "said: " + c.Action.message;
                },
            });

            Register(new Skill
            {
                Id = "wait", Name = "Wait", Roles = everyone,
                Description = "Pause for a few seconds.",
                Parameters = new[] { "seconds: 0.5 to 10" },
                Safety = "Clamped to 0.5-10 s.",
                Example = "{\"type\":\"wait\",\"seconds\":2}",
                Validate = (a, _) => { if (a.seconds <= 0 || a.seconds > 10) a.seconds = Mathf.Clamp(a.seconds, 0.5f, 10f); return null; },
                Execute = async (c, ct) =>
                {
                    c.Brain.SetActivity(AgentStatus.Acting, "esperando");
                    await Awaitable.WaitForSecondsAsync(c.Action.seconds, ct);
                    return $"waited {c.Action.seconds:0.#}s";
                },
            });
        }
    }
}
