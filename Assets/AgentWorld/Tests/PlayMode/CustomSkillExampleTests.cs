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
    /// The worked example from docs/DEVELOPER_GUIDE.md section 4: the shipped count_people skill on an existing agent.
    public class CustomSkillExampleTests
    {
        // The skill itself ships in Assets/AgentWorld/Scripts/Runtime/AI/CustomSkills.cs (the guide quotes it).
        // agents.json entry for Grace with the new skill added:
        const string GraceConfig = @"{""agents"":[
            {""id"":""grace"",""role"":""explorer"",""title"":""Explorer"",""skills"":[""move_to"",""inspect"",""report_findings"",""count_people"",""speak"",""wait""]}]}";

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
            Assert.IsNotNull(SkillRegistry.Get("count_people"), "CustomSkills registers it at startup");
            var cfg = AgentConfigs.Parse(GraceConfig, out var errors);
            CollectionAssert.IsEmpty(errors, "the new skill is accepted once registered");
            AgentConfigs.Use(cfg);

            var grace = AgentHub.Find("explorer");
            AgentHub.Llm = new MockLlm("{\"actions\":[{\"type\":\"count_people\",\"target\":\"lab\"}]}");
            var output = new System.Collections.Generic.List<string>();
            Assert.IsTrue(grace.Ai.Assign("how many people are in the lab?", output.Add, out _));
            var end = Time.time + 45f; // Grace walks to the lab first
            while (grace.Ai.Busy && Time.time < end) yield return null;
            Assert.AreEqual(AgentStatus.Completed, grace.Ai.Status, string.Join("\n", output));
            Assert.IsTrue(output.Any(l => l.Contains(" near lab, not counting me")), string.Join("\n", output));

            // Linus has no count_people in this config: the validator refuses the same plan.
            var plan = PlanValidator.Parse("{\"actions\":[{\"type\":\"count_people\",\"target\":\"lab\"}]}", out _);
            Assert.IsTrue(PlanValidator.Validate(plan, new[] { "lab" }, "builder").Any(e => e.Contains("does not have skill 'count_people'")));
        }

        [UnityTest]
        public IEnumerator SkillLibraryTester_RunsOnlyValidatedActions()
        {
            var grace = AgentHub.Find("grace");
            Assert.IsFalse(SkillLibrary.Run(AgentHub.Find("linus"), "{\"type\":\"count_people\",\"target\":\"lab\"}", null, out var denied));
            StringAssert.Contains("does not have skill 'count_people'", denied[0]);
            Assert.IsFalse(grace.Ai.Busy, "a rejected test never starts a task");

            var output = new System.Collections.Generic.List<string>();
            Assert.IsTrue(SkillLibrary.Run(grace, "{\"type\":\"count_people\",\"target\":\"lab\"}", output.Add, out var errors), string.Join("\n", errors));
            var end = Time.time + 45f;
            while (grace.Ai.Busy && Time.time < end) yield return null;
            Assert.AreEqual(AgentStatus.Completed, grace.Ai.Status, string.Join("\n", output));
            Assert.IsTrue(output.Any(l => l.Contains(" near lab, not counting me")), string.Join("\n", output));
        }

        [Test]
        public void UnregisteredSkillInJson_IsRejected()
        {
            AgentConfigs.Parse(@"{""agents"":[{""id"":""grace"",""role"":""explorer"",""skills"":[""teleport""]}]}", out var errors);
            Assert.IsTrue(errors.Any(e => e.Contains("unknown skill 'teleport'")), "JSON alone cannot create skills");
        }
    }
}
