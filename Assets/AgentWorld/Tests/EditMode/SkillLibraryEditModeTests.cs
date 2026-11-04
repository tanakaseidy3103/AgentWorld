using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AgentWorld.Tests
{
    /// Skill Library: the safe tester's validation messages, availability, and the generated docs.
    public class SkillLibraryEditModeTests
    {
        static readonly string[] Locs = { "lake", "plaza", "lab" };

        [TearDown] public void Reset() => AgentConfigs.Reload();

        [Test]
        public void Tester_AcceptsAValidCustomSkillAction_ForGrace()
        {
            CollectionAssert.IsEmpty(SkillLibrary.Check("explorer", "{\"type\":\"count_people\",\"target\":\"near lab\"}", Locs, out var plan));
            Assert.AreEqual("lab", plan.actions[0].target, "ids are normalized like model output");
        }

        [Test]
        public void Tester_ExplainsEveryKindOfRejection()
        {
            StringAssert.Contains("does not have skill 'count_people'", SkillLibrary.Check("builder", "{\"type\":\"count_people\",\"target\":\"lab\"}", Locs, out _)[0]);
            var badPlace = SkillLibrary.Check("explorer", "{\"type\":\"count_people\",\"target\":\"moon\"}", Locs, out _);
            Assert.IsTrue(badPlace.Any(e => e.Contains("invalid location 'moon'")) && badPlace.Any(e => e.StartsWith("valid location ids: lab, lake, plaza")), string.Join("\n", badPlace));
            Assert.IsTrue(SkillLibrary.Check("explorer", "{\"type\":\"water_crops\",\"target\":\"lab\"}", Locs, out _).Any(e => e.Contains("documentation-only")));
            StringAssert.Contains("no JSON", SkillLibrary.Check("explorer", "count people please", Locs, out _)[0]);
        }

        [Test]
        public void Tester_NeverRunsModelGeneratedCode()
        {
            var errors = SkillLibrary.Check("builder", "{\"type\":\"exec\",\"code\":\"System.IO.File.Delete(\\\"world.json\\\")\"}", Locs, out _);
            StringAssert.Contains("unknown action type 'exec'", errors[0]);
            Assert.IsNull(SkillRegistry.Get("exec"));
        }

        [Test]
        public void ResultReporting_OnlyMeasuredResultsAreSpoken()
        {
            var measured = new SkillResult { SkillId = "count_people", Text = "2 people near lab, not counting me" };
            Assert.IsNull(ResultReporting.Rewrite("There are 7 people in the lab.", null), "no measurement yet: skip");
            Assert.AreEqual(measured.Text, ResultReporting.Rewrite("There are 7 people in the lab.", measured));
            Assert.AreEqual(measured.Text, ResultReporting.Rewrite("Nobody is there.", measured));
            Assert.AreEqual("Let me check the lab!", ResultReporting.Rewrite("Let me check the lab!", null), "speech without a result claim is kept");
            Assert.IsTrue(SkillRegistry.Get("count_people").ReportsResult && SkillRegistry.Get("report_findings").ReportsResult);
        }

        [Test]
        public void Availability_DistinguishesRoleFromConfiguration()
        {
            var s = SkillRegistry.Get("count_people");
            Assert.AreEqual("custom (CustomSkills.cs)", s.Origin);
            Assert.AreEqual("yes", SkillLibrary.Availability(s, "explorer"));
            StringAssert.Contains("not enabled in agents.json", SkillLibrary.Availability(s, "assistant"));
            StringAssert.Contains("role 'builder' is not allowed", SkillLibrary.Availability(s, "builder"));
            Assert.IsTrue(SkillRegistry.DocumentationOnly.All(d => SkillRegistry.Get(d.Id) == null), "documentation-only ideas are never registered");
        }

        [Test]
        public void DeveloperGuide_SkillReference_IsUpToDate()
        {
            var guide = File.ReadAllText(Path.Combine(Application.dataPath, "..", "docs", "DEVELOPER_GUIDE.md")).Replace("\r\n", "\n");
            var regenerated = SkillLibrary.ReplaceDocs(guide);
            Assert.IsNotNull(regenerated, "guide is missing the SKILLS:BEGIN/END markers");
            Assert.AreEqual(regenerated, guide, "run AgentWorld > Update Skill Docs (or -executeMethod AgentWorld.EditorTools.WorldBuilder.UpdateSkillDocs)");
            foreach (var s in SkillRegistry.All) StringAssert.Contains($"`{s.Id}`", guide);
        }
    }
}
