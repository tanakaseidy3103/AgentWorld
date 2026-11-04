using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AgentWorld.Tests
{
    /// The exact example from docs/DEVELOPER_GUIDE.md section 4: a new C# skill + giving it to an existing agent.
    public class CustomSkillExampleTests
    {
        // --- Guide example: start -------------------------------------------------
        public static void RegisterCountPeople()
        {
            if (SkillRegistry.Get("count_people") != null) return; // registry is global; register once
            SkillRegistry.Register(new Skill
            {
                Id = "count_people",
                Name = "Count people",
                Description = "Count how many characters are within 5 units of a location.",
                Roles = new[] { "explorer", "assistant" },
                Parameters = new[] { "target: location id" },
                Safety = "Read-only.",
                Example = "{\"type\":\"count_people\",\"target\":\"<location id>\"}",
                Validate = (a, locations) =>
                {
                    a.target = PlanValidator.Loc(a.target);
                    return locations.Contains(a.target) ? null : $"invalid location '{a.target}'";
                },
                Execute = (c, ct) =>
                {
                    Vector2 p = Landmark.Find(c.Action.target).transform.position;
                    var n = Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None).Count(x => Vector2.Distance(p, x.transform.position) < 5f);
                    var line = $"{n} people near {c.Action.target}";
                    c.Say(line);
                    return Task.FromResult(line);
                },
            });
        }

        // agents.json entry for Grace with the new skill added:
        const string GraceConfig = @"{""agents"":[
            {""id"":""grace"",""role"":""explorer"",""title"":""Explorer"",""skills"":[""move_to"",""inspect"",""report_findings"",""count_people"",""speak"",""wait""]}]}";
        // --- Guide example: end ---------------------------------------------------

        [UnitySetUp]
        public IEnumerator Load()
        {
            WorldSaveManager.PathOverride = Path.Combine(Path.GetTempPath(), "aw_customskill.json");
            File.Delete(WorldSaveManager.PathOverride);
            SceneManager.LoadScene("Main");
            yield return null;
        }

        [TearDown]
        public void Cleanup()
        {
            AgentConfigs.Reload();
            AgentHub.Llm = null;
            File.Delete(WorldSaveManager.PathOverride);
            WorldSaveManager.PathOverride = null;
        }

        [UnityTest]
        public IEnumerator NewSkill_ConfiguredForGrace_RunsFromAPlan_AndIsDeniedToLinus()
        {
            RegisterCountPeople();
            var cfg = AgentConfigs.Parse(GraceConfig, out var errors);
            CollectionAssert.IsEmpty(errors, "the new skill is accepted once registered");
            AgentConfigs.Use(cfg);

            var grace = AgentHub.Find("explorer");
            AgentHub.Llm = new MockLlm("{\"actions\":[{\"type\":\"count_people\",\"target\":\"lab\"}]}");
            var output = new System.Collections.Generic.List<string>();
            Assert.IsTrue(grace.Ai.Assign("how many people are in the lab?", output.Add, out _));
            var end = Time.time + 10f;
            while (grace.Ai.Busy && Time.time < end) yield return null;
            Assert.AreEqual(AgentStatus.Completed, grace.Ai.Status, string.Join("\n", output));
            Assert.IsTrue(output.Any(l => l.Contains("people near lab")), string.Join("\n", output));

            // Linus has no count_people in this config: the validator refuses the same plan.
            var plan = PlanValidator.Parse("{\"actions\":[{\"type\":\"count_people\",\"target\":\"lab\"}]}", out _);
            Assert.IsTrue(PlanValidator.Validate(plan, new[] { "lab" }, "builder").Any(e => e.Contains("does not have skill 'count_people'")));
        }

        [Test]
        public void UnregisteredSkillInJson_IsRejected()
        {
            AgentConfigs.Parse(@"{""agents"":[{""id"":""grace"",""role"":""explorer"",""skills"":[""teleport""]}]}", out var errors);
            Assert.IsTrue(errors.Any(e => e.Contains("unknown skill 'teleport'")), "JSON alone cannot create skills");
        }
    }
}
