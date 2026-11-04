using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AgentWorld.Tests
{
    public class MissionPlayModeTests
    {
        StructureBuilder structures;

        static string Delegation(string explore, int houses) =>
            $"{{\"explore\":[{string.Join(",", explore.Split(',').Select(s => $"\"{s}\""))}],\"houses\":{houses},\"message\":\"Grace, explore; Linus, prepare.\"}}";
        static string Choice(string sites) =>
            $"{{\"build_at\":[{string.Join(",", sites.Split(',').Select(s => $"\"{s}\""))}],\"reason\":\"Best free land.\"}}";

        [UnitySetUp]
        public IEnumerator LoadWorld()
        {
            WorldSaveManager.PathOverride = Path.Combine(Path.GetTempPath(), "aw_mission.json");
            File.Delete(WorldSaveManager.PathOverride);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return new WaitForFixedUpdate();
            foreach (var n in AgentHub.Agents) n.speed = 8f;
            structures = StructureBuilder.Instance;
            structures.buildSeconds = 0.3f;
        }

        [TearDown]
        public void Cleanup()
        {
            MissionControl.Restore(null);
            AgentHub.Llm = null;
            AgentHub.LlmTimeoutSeconds = 120f;
            File.Delete(WorldSaveManager.PathOverride);
            WorldSaveManager.PathOverride = null;
        }

        static IEnumerator WaitMission(float timeout = 90f)
        {
            var end = Time.time + timeout;
            while (MissionControl.Running && Time.time < end) yield return null;
            Assert.IsFalse(MissionControl.Running, "mission did not finish in time");
        }

        static string Log => string.Join("\n", MissionControl.State.log);

        [UnityTest]
        public IEnumerator Mission_AdaDelegates_GraceExplores_LinusBuilds_AndPersists()
        {
            var llm = new MockLlm(Delegation("lake,plaza", 2), Choice("lake,plaza"));
            AgentHub.Llm = llm;
            Assert.IsTrue(MissionControl.Start("build a small village near the lake", null, out var err), err);
            yield return WaitMission();

            var m = MissionControl.State;
            Assert.AreEqual("Succeeded", m.status, Log);
            Assert.AreEqual(2, llm.Calls, "two Llama decisions: delegation + site selection");
            Assert.AreEqual(2, m.housesBuilt);
            Assert.AreEqual(2, structures.Structures.Count());
            // Grace's structured reports are real measurements of the world.
            CollectionAssert.AreEquivalent(new[] { "lake", "plaza" }, m.sharedMemory.Select(r => r.location).ToList());
            Assert.IsTrue(m.sharedMemory.All(r => r.reportedBy == "Grace" && r.freeSites > 0));
            Assert.IsTrue(m.tasks.All(t => t.status == "Done"), string.Join(", ", m.tasks.Select(t => t.description + ":" + t.status)));
            Assert.AreEqual(2, m.tasks.Count(t => t.agent == "Grace"));
            Assert.AreEqual(2, m.tasks.Count(t => t.agent == "Linus"));
            StringAssert.Contains("[Llama/Ada]", Log);
            Assert.IsTrue(AgentHub.Find("assistant").Ai.Memory.Any(x => x.Contains("Grace reported lake")));

            var mgr = Object.FindAnyObjectByType<WorldSaveManager>();
            mgr.Save();
            MissionControl.Restore(null);
            structures.Restore(null);
            yield return null;
            Assert.IsTrue(mgr.Load());
            yield return null;
            Assert.AreEqual("Succeeded", MissionControl.State.status);
            Assert.AreEqual(2, MissionControl.State.sharedMemory.Count);
            Assert.AreEqual(2, structures.Structures.Count());
        }

        [UnityTest]
        public IEnumerator Mission_InvalidLocation_RetriesOnce_ThenFails()
        {
            var llm = new MockLlm(Delegation("volcano", 2));
            AgentHub.Llm = llm;
            Assert.IsTrue(MissionControl.Start("build near the volcano", null, out _));
            yield return WaitMission();
            Assert.AreEqual("Failed", MissionControl.State.status);
            Assert.AreEqual(2, llm.Calls);
            StringAssert.Contains("local inválido 'volcano'", Log);
            Assert.IsEmpty(structures.Structures);
        }

        [UnityTest]
        public IEnumerator Mission_SelectionOfUnexploredSite_IsRejected()
        {
            var llm = new MockLlm(Delegation("lake", 1), Choice("east_field"));
            AgentHub.Llm = llm;
            Assert.IsTrue(MissionControl.Start("build near the lake", null, out _));
            yield return WaitMission();
            Assert.AreEqual("Failed", MissionControl.State.status);
            Assert.AreEqual(3, llm.Calls, "delegation + selection + one retry");
            StringAssert.Contains("não foi explorado", Log);
            Assert.IsEmpty(structures.Structures);
        }

        [UnityTest]
        public IEnumerator Mission_RecoversWhenChosenSiteFillsUp()
        {
            AgentHub.Llm = new MockLlm(Delegation("lake,north_meadow", 1), Choice("lake")) { Delay = 0.5f };
            Assert.IsTrue(MissionControl.Start("build near the lake", null, out _));
            while (MissionControl.State.sharedMemory.Count < 2 && MissionControl.Running) yield return null;
            // Lake area becomes unbuildable after Grace reported it (someone else took the land).
            structures.keepClear = new Rect(-20, -20, 40, 23.5f);
            yield return WaitMission();
            Assert.AreEqual("Succeeded", MissionControl.State.status, Log);
            StringAssert.Contains("recuperação: lake sem espaço, tentando north_meadow", Log);
            Assert.Greater(structures.Structures.Single().transform.position.y, 3f);
        }

        [UnityTest]
        public IEnumerator Mission_Stop_Cancels()
        {
            AgentHub.Llm = new MockLlm(Delegation("lake", 1)) { Delay = 10f };
            Assert.IsTrue(MissionControl.Start("build", null, out _));
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(MissionControl.Start("another", null, out _), "only one mission at a time");
            MissionControl.Stop();
            yield return WaitMission(5f);
            Assert.AreEqual("Cancelled", MissionControl.State.status);
            Assert.IsTrue(AgentHub.Agents.All(a => !a.Ai.Busy));
        }

        [UnityTest]
        public IEnumerator Mission_OllamaOffline_FailsClearly()
        {
            AgentHub.Llm = new OllamaClient("http://127.0.0.1:1", "llama3.2");
            Assert.IsTrue(MissionControl.Start("build a village", null, out _));
            yield return WaitMission(15f);
            Assert.AreEqual("Failed", MissionControl.State.status);
            StringAssert.Contains("indisponível", MissionControl.State.result);
        }

        [UnityTest]
        public IEnumerator Terminal_MissionCommands()
        {
            var pc = Object.FindObjectsByType<Computer>(FindObjectsSortMode.None).First(c => c.id == "lab-pc-1");
            pc.Submit("mission status");
            StringAssert.Contains("nenhuma missão", pc.log.Last());
            AgentHub.Llm = new MockLlm(Delegation("plaza", 1), Choice("plaza"));
            pc.Submit("mission build one house at the plaza");
            StringAssert.Contains("missão iniciada", pc.log.Last());
            yield return WaitMission();
            pc.Submit("mission status");
            StringAssert.Contains("Succeeded", pc.log.Last());
            StringAssert.Contains("plaza", pc.log.Last());
        }

        /// Real Llama decisions end to end. Opt-in: -testFilter AgentWorld.Tests.MissionPlayModeTests.LiveLlama_VillageMission
        [UnityTest, Explicit("needs Ollama running with the configured Llama model")]
        public IEnumerator LiveLlama_VillageMission()
        {
            AgentHub.Llm = new OllamaClient();
            Assert.IsTrue(MissionControl.Start("Build a small village near the lake", l => Debug.Log("[live] " + l), out var err), err);
            yield return WaitMission(400f);
            Debug.Log($"[live] RESULT {MissionControl.State.status}: {MissionControl.State.result}");
            Assert.That(MissionControl.State.status, Is.EqualTo("Succeeded").Or.EqualTo("Partial"), Log);
            Assert.AreEqual(MissionControl.State.housesBuilt, structures.Structures.Count());
        }
    }
}
