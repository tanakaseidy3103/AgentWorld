using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AgentWorld.Tests
{
    /// Regression: the model may not report a measured number that Unity did not measure in this task.
    public class ResultReportingPlayModeTests
    {
        const string FailingSkill = "count_fails_for_test";

        [UnitySetUp]
        public IEnumerator Load()
        {
            WorldSaveManager.PathOverride = Path.Combine(Path.GetTempPath(), "aw_results.json");
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

        static IEnumerator RunGrace(string planJson, List<string> output)
        {
            var grace = AgentHub.Find("grace");
            AgentHub.Llm = new MockLlm(planJson);
            Assert.IsTrue(grace.Ai.Assign("how many people are in the lab?", output.Add, out var err), err);
            var end = Time.time + 60f;
            while (grace.Ai.Busy && Time.time < end) yield return null;
        }

        static string Said(List<string> output) => string.Join("\n", output.Where(l => l.StartsWith("[Grace] \"")));

        [UnityTest]
        public IEnumerator FabricatedCount_AfterCountPeople_IsReplacedByTheMeasuredResult()
        {
            var output = new List<string>();
            yield return RunGrace("{\"actions\":[{\"type\":\"count_people\",\"target\":\"lab\"},{\"type\":\"speak\",\"message\":\"There are 7 people in the lab.\"}]}", output);
            var log = string.Join("\n", output);
            Assert.AreEqual(AgentStatus.Completed, AgentHub.Find("grace").Ai.Status, log);
            var measured = output.First(l => l.Contains("near lab, not counting me") && !l.Contains("\"")).Substring("[Grace] ".Length);
            StringAssert.DoesNotContain("7 people", Said(output), log);
            StringAssert.Contains($"\"{measured}\"", Said(output), "the spoken line is Unity's SkillResult");
            StringAssert.Contains("speak replaced by the measured count_people result", log);
        }

        [UnityTest]
        public IEnumerator CountClaimedBeforeCountPeopleRuns_IsSkipped()
        {
            var output = new List<string>();
            yield return RunGrace("{\"actions\":[{\"type\":\"speak\",\"message\":\"There are 2 people in the lab.\"},{\"type\":\"count_people\",\"target\":\"lab\"}]}", output);
            var log = string.Join("\n", output);
            Assert.AreEqual(AgentStatus.Completed, AgentHub.Find("grace").Ai.Status, log);
            StringAssert.Contains("speak skipped: Llama stated a result before any skill measured it", log);
            Assert.AreEqual("", Said(output), log);
        }

        [UnityTest]
        public IEnumerator FailedSkill_NeverLetsAPredictedNumberBeSpoken()
        {
            if (SkillRegistry.Get(FailingSkill) == null)
                SkillRegistry.Register(new Skill
                {
                    Id = FailingSkill, Name = "Failing count", Roles = new[] { "explorer" }, ReportsResult = true,
                    Parameters = new string[0], Description = "test only", Safety = "test only", Example = "{}",
                    Validate = (a, _) => null,
                    Execute = (c, ct) => Task.FromException<string>(new Exception("sensor broken")),
                });
            AgentConfigs.Use(AgentConfigs.Parse($"{{\"agents\":[{{\"id\":\"grace\",\"role\":\"explorer\",\"skills\":[\"{FailingSkill}\",\"speak\"]}}]}}", out _));
            var output = new List<string>();
            yield return RunGrace($"{{\"actions\":[{{\"type\":\"{FailingSkill}\"}},{{\"type\":\"speak\",\"message\":\"There are 3 people here.\"}}]}}", output);
            var log = string.Join("\n", output);
            Assert.AreEqual(AgentStatus.Failed, AgentHub.Find("grace").Ai.Status, log);
            StringAssert.Contains("sensor broken", log);
            StringAssert.DoesNotContain("3 people", Said(output), log);
        }

        [UnityTest, Explicit("needs Ollama running with the configured Llama model")]
        public IEnumerator LiveLlama_GraceCountsPeople_SpeaksOnlyTheMeasuredCount()
        {
            AgentHub.Llm = null; // real Ollama client
            var grace = AgentHub.Find("grace");
            var output = new List<string>();
            Assert.IsTrue(grace.Ai.Assign("how many people are in the lab right now?", output.Add, out var err), err);
            var end = Time.time + 180f;
            while (grace.Ai.Busy && Time.time < end) yield return null;
            var log = string.Join("\n", output);
            Debug.Log("[live-count]\n" + log);
            Assert.AreEqual(AgentStatus.Completed, grace.Ai.Status, log);
            var measured = output.FirstOrDefault(l => l.Contains("not counting me") && !l.Contains("\""));
            Assert.IsNotNull(measured, "Llama should pick count_people\n" + log);
            foreach (var said in output.Where(l => l.StartsWith("[Grace] \"") && ResultReporting.ClaimsResult(l)))
                StringAssert.Contains(measured.Substring("[Grace] ".Length), said, "spoken numbers come from the SkillResult\n" + log);
        }

        [UnityTest]
        public IEnumerator ResultFromAnEarlierTask_IsNeverReusedAsStaleData()
        {
            var first = new List<string>();
            yield return RunGrace("{\"actions\":[{\"type\":\"count_people\",\"target\":\"lab\"}]}", first);
            Assert.IsTrue(first.Any(l => l.Contains("not counting me")), string.Join("\n", first));
            yield return new WaitForSeconds(LlamaAgentBrain.MinSecondsBetweenCalls + 0.2f);

            // New task: the claim comes before this task's own measurement, so the earlier count must not be used.
            var second = new List<string>();
            yield return RunGrace("{\"actions\":[{\"type\":\"speak\",\"message\":\"Still 2 people.\"},{\"type\":\"count_people\",\"target\":\"plaza\"}]}", second);
            var log = string.Join("\n", second);
            StringAssert.Contains("speak skipped", log);
            Assert.IsFalse(second.Any(l => l.Contains("near lab")), "no stale lab result in the new task\n" + log);
            Assert.AreEqual("", Said(second), log);
        }
    }
}
