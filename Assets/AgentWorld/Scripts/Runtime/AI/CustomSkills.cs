using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentWorld
{
    /// Skills added on top of the built-ins with nothing but the public SkillRegistry.Register API.
    /// count_people is the worked example in docs/DEVELOPER_GUIDE.md; agents.json gives it to Grace.
    public static class CustomSkills
    {
        public const float CountRadius = 5f;

        public static void RegisterAll()
        {
            if (SkillRegistry.Get("count_people") == null) SkillRegistry.Register(CountPeople());
        }

        // --- Guide example: start -------------------------------------------------
        public static Skill CountPeople() => new Skill
        {
            Id = "count_people",
            Name = "Count people",
            Origin = "custom (CustomSkills.cs)",
            Description = "Walk to a location and count the other characters within 5 units of it.",
            Roles = new[] { "explorer", "assistant" },
            Parameters = new[] { "target: location id" },
            Safety = "Read-only; the count is computed by Unity, never by the model.",
            Example = "{\"type\":\"count_people\",\"target\":\"<location id>\"}",
            // 1. Validation: normalize the id and require a real landmark. Return an error to reject.
            Validate = (a, locations) =>
            {
                a.target = PlanValidator.Loc(a.target);
                return locations.Contains(a.target) ? null : $"invalid location '{a.target}' in target";
            },
            // 2. Execution on Unity's main thread with real world data.
            Execute = async (c, ct) =>
            {
                var place = Landmark.Find(c.Action.target);
                await c.Brain.MoveTo(place.StandPoint, c.Action.target, ct);
                c.Brain.SetActivity(AgentStatus.Acting, "counting people");
                Vector2 p = place.transform.position;
                var people = Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None).Where(n => n != c.Npc).Select(n => n.transform)
                    .Concat(Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Select(x => x.transform))
                    .Count(t => Vector2.Distance(p, t.position) < CountRadius);
                var line = $"{people} {(people == 1 ? "person" : "people")} near {c.Action.target}, not counting me";
                c.Brain.Remember($"Counted {line}");
                c.Say(line);
                c.Npc.Say(line);
                return line;
            },
        };
        // --- Guide example: end ---------------------------------------------------
    }
}
