using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AgentWorld.Tests
{
    /// Skill registry, agent configuration and evaluation report: pure logic, no scene.
    public class PlatformEditModeTests
    {
        static readonly string[] Locs = { "lake", "plaza", "lab" };

        [TearDown] public void Reset() => AgentConfigs.Reload();

        [Test]
        public void Registry_HasRealExecutorsForEveryBuiltInSkill()
        {
            var ids = SkillRegistry.All.Select(s => s.Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "move_to", "inspect", "report_findings", "build_house", "build_farm", "speak", "wait", "count_people" }, ids);
            foreach (var s in SkillRegistry.All)
            {
                Assert.IsNotNull(s.Execute, s.Id);
                Assert.IsNotNull(s.Validate, s.Id);
                Assert.IsNotEmpty(s.Roles, s.Id);
                Assert.IsNotEmpty(s.Description, s.Id);
            }
            Assert.Throws<System.ArgumentException>(() => SkillRegistry.Register(SkillRegistry.Get("wait")), "duplicate ids are refused");
            Assert.Throws<System.ArgumentException>(() => SkillRegistry.Register(new Skill { Id = "fly" }), "skills need a validator and an executor");
        }

        [Test]
        public void Registry_MapsModelActionsToSkills()
        {
            Assert.AreEqual("build_house", SkillRegistry.IdFor(new AgentAction { type = "build", structure = "house" }));
            Assert.AreEqual("build_farm", SkillRegistry.IdFor(new AgentAction { type = "build", structure = "farm" }));
            Assert.AreEqual("move_to", SkillRegistry.IdFor(new AgentAction { type = "move_to" }));
            Assert.IsNull(SkillRegistry.Get(SkillRegistry.IdFor(new AgentAction { type = "build", structure = "bridge" })));
        }

        [Test]
        public void Config_FileShipsWithProject_AndIsValid()
        {
            AgentConfigs.Reload();
            Assert.AreEqual(AgentConfigs.FileName, AgentConfigs.Source);
            CollectionAssert.IsEmpty(AgentConfigs.Errors);
            CollectionAssert.AreEquivalent(new[] { "ada", "grace", "linus" }, AgentConfigs.Current.agents.Select(a => a.id).ToList());
            CollectionAssert.Contains(AgentConfigs.SkillsFor("builder"), "build_house");
            CollectionAssert.DoesNotContain(AgentConfigs.SkillsFor("explorer"), "build_house");
        }

        [Test]
        public void Config_InvalidSkillsAndRolesAreReportedAndDropped()
        {
            var cfg = AgentConfigs.Parse(@"{""agents"":[
                {""id"":""Grace"",""role"":""explorer"",""skills"":[""move_to"",""fly"",""build_house""]},
                {""id"":""zed"",""role"":""wizard"",""skills"":[""speak""]},
                {""id"":""grace"",""role"":""explorer"",""skills"":[]}]}", out var errors);
            Assert.AreEqual(1, cfg.agents.Count);
            CollectionAssert.AreEqual(new[] { "move_to" }, cfg.agents[0].skills);
            Assert.IsTrue(errors.Any(e => e.Contains("unknown skill 'fly'")));
            Assert.IsTrue(errors.Any(e => e.Contains("'build_house' is not allowed")));
            Assert.IsTrue(errors.Any(e => e.Contains("unknown role 'wizard'")));
            Assert.IsTrue(errors.Any(e => e.Contains("duplicate")));
            Assert.IsNull(AgentConfigs.Parse("{ not json", out errors));
            Assert.IsNotEmpty(errors);
        }

        [Test]
        public void Validator_EnforcesConfiguredSkillsPerAgent()
        {
            var plan = PlanValidator.Parse(@"{""actions"":[{""type"":""report_findings"",""target"":""lake""}]}", out _);
            CollectionAssert.IsEmpty(PlanValidator.Validate(plan, Locs, "explorer"));
            plan = PlanValidator.Parse(@"{""actions"":[{""type"":""report_findings"",""target"":""lake""}]}", out _);
            Assert.IsTrue(PlanValidator.Validate(plan, Locs, "builder").Any(e => e.Contains("does not have skill 'report_findings'")));

            // A config that removes build_farm from Linus makes farm plans invalid.
            AgentConfigs.Use(AgentConfigs.Parse(@"{""agents"":[{""id"":""linus"",""role"":""builder"",""skills"":[""move_to"",""build_house""]}]}", out _));
            plan = PlanValidator.Parse(@"{""actions"":[{""type"":""build"",""structure"":""farm"",""location"":""lake""}]}", out _);
            Assert.IsTrue(PlanValidator.Validate(plan, Locs, "builder").Any(e => e.Contains("'build_farm'")));
        }

        [Test]
        public void Evaluation_ReportUsesMeasuredState()
        {
            var m = new MissionState
            {
                objective = "Build a small village near the lake", scenario = "village", model = "llama3.2", status = "Succeeded",
                housesWanted = 2, housesBuilt = 2, invalidResponses = 1, durationSeconds = 61.84f,
            };
            m.tasks.Add(new MissionTask { agent = "Grace", description = "inspecionar lake", status = "Done" });
            m.tasks.Add(new MissionTask { agent = "Linus", description = "construir casa", status = "Failed" });
            m.log.Add("[Llama/Ada] explorar: lake");
            var r = Evaluation.Build(m);
            Assert.IsTrue(r.passed);
            Assert.AreEqual(1, r.tasksDone);
            Assert.AreEqual(1, r.tasksFailed);
            Assert.AreEqual(61.8f, r.durationSeconds, 0.01f);
            var text = Evaluation.Format(r);
            StringAssert.Contains("Houses built: 2/2", text);
            StringAssert.Contains("Invalid LLM responses (validator retries): 1", text);
            StringAssert.Contains("PASS", text);
            m.status = "Partial";
            Assert.IsFalse(Evaluation.Build(m).passed);
            var json = JsonUtility.ToJson(r);
            StringAssert.Contains("\"model\":\"llama3.2\"", json);
        }
    }
}
