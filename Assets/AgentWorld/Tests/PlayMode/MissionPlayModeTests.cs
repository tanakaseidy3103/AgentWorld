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

        // Dialogue (free-text) calls get a fixed line so JSON replies stay in order.
        static MockLlm Mock(params string[] json) => new MockLlm(json) { TextReply = "I measured the land; there is room." };

        static MockLlm Delayed(float delay, params string[] json) { var m = Mock(json); m.Delay = delay; return m; }

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
            var llm = Mock(Delegation("lake,plaza", 2), Choice("lake,plaza"));
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
        public IEnumerator Mission_UnsupportedRequest_IsExplainedHonestly()
        {
            var llm = Mock("{\"feasible\":\"no\",\"explore\":[],\"houses\":0,\"message\":\"We can only build houses and farms, not castles.\"}");
            AgentHub.Llm = llm;
            Assert.IsTrue(MissionControl.Start("build a giant castle on the lake", null, out _));
            yield return WaitMission();
            Assert.AreEqual("Unsupported", MissionControl.State.status);
            StringAssert.Contains("not castles", MissionControl.State.result);
            Assert.AreEqual(1, llm.Calls);
            Assert.IsEmpty(MissionControl.State.tasks, "nothing was delegated");
            Assert.IsEmpty(structures.Structures);
        }

        [UnityTest]
        public IEnumerator Mission_VillageWithFarm_BuildsWalkableFarm_AndPersists()
        {
            AgentHub.Llm = Mock(
                "{\"feasible\":\"yes\",\"explore\":[\"lake\",\"east_field\"],\"houses\":1,\"farms\":1,\"message\":\"Go!\"}",
                "{\"build_at\":[\"lake\"],\"farm_at\":\"east_field\",\"reason\":\"Farm on open grass.\"}");
            Assert.IsTrue(MissionControl.Start("build a village with a farm", null, out _));
            yield return WaitMission();
            var m = MissionControl.State;
            Assert.AreEqual("Succeeded", m.status, Log);
            Assert.AreEqual(1, m.farmsBuilt);
            var farm = structures.Structures.Single(s => s.type == "farm");
            Assert.IsTrue(farm.GetComponent<Collider2D>().isTrigger, "farms must not trap anyone");
            Assert.Less(Vector2.Distance(farm.transform.position, Landmark.Find("east_field").transform.position), 10f);
            Assert.IsFalse(structures.IsFree(farm.transform.position), "nothing else can be built on the farm");

            var mgr = Object.FindAnyObjectByType<WorldSaveManager>();
            mgr.Save();
            structures.Restore(null);
            yield return null;
            Assert.IsTrue(mgr.Load());
            yield return null;
            Assert.AreEqual(1, structures.Structures.Count(s => s.type == "farm"));
            Assert.AreEqual(1, structures.Structures.Count(s => s.type == "house"));
        }

        [UnityTest]
        public IEnumerator Mission_FarmOnUnexploredSite_IsRejected()
        {
            var llm = Mock(
                "{\"feasible\":\"yes\",\"explore\":[\"lake\"],\"houses\":1,\"farms\":1,\"message\":\"Go\"}",
                "{\"build_at\":[\"lake\"],\"farm_at\":\"north_meadow\",\"reason\":\"x\"}");
            AgentHub.Llm = llm;
            Assert.IsTrue(MissionControl.Start("village", null, out _));
            yield return WaitMission();
            Assert.AreEqual("Failed", MissionControl.State.status);
            StringAssert.Contains("'north_meadow' não foi explorado", Log);
            Assert.IsEmpty(structures.Structures);
        }

        [UnityTest]
        public IEnumerator MissionBar_StartsMissionFromNaturalLanguage()
        {
            AgentHub.Llm = Mock("{\"intent\":\"mission\",\"agent\":\"linus\",\"reason\":\"build request\"}", Delegation("plaza", 1), Choice("plaza"));
            var panel = Object.FindAnyObjectByType<MissionPanel>();
            var bar = panel.MissionConsole();
            bar.Submit("please build one house at the plaza");
            StringAssert.Contains("especialista", bar.log.Last());
            while (ExpertRouter.Busy) yield return null;
            Assert.IsTrue(bar.log.Any(l => l.Contains("missão iniciada")), string.Join("\n", bar.log));
            yield return WaitMission();
            Assert.AreEqual("Succeeded", MissionControl.State.status);
            bar.Submit("status");
            StringAssert.Contains("Succeeded", bar.log.Last());
        }

        [UnityTest]
        public IEnumerator Agents_ShowTaskIndicators()
        {
            AgentHub.Llm = Delayed(0.3f, Delegation("plaza", 1), Choice("plaza"));
            Assert.IsTrue(MissionControl.Start("one house at the plaza", null, out _));
            var seen = new System.Collections.Generic.HashSet<string>();
            var activities = new System.Collections.Generic.HashSet<string>();
            while (MissionControl.Running)
            {
                foreach (var n in AgentHub.Agents)
                {
                    var icon = n.transform.Find("TaskIcon")?.GetComponent<SpriteRenderer>();
                    if (icon && icon.enabled && icon.sprite) seen.Add(n.agentRole + ":" + icon.sprite.name);
                    activities.Add(n.displayName + ":" + n.Activity);
                }
                yield return null;
            }
            CollectionAssert.IsSubsetOf(new[] { "assistant:think", "explorer:scout", "builder:build" }, seen.ToList(), string.Join(", ", seen));
            // Name-plate activity words follow the real state machine.
            CollectionAssert.IsSubsetOf(new[] { "Ada:Planning", "Grace:Exploring", "Grace:Talking", "Linus:Talking", "Linus:Building" }, activities.ToList(), string.Join(", ", activities));
            Assert.AreEqual("Coordinator", AgentHub.Find("assistant").RoleTitle);
            Assert.IsNotNull(GameObject.Find("Flag_plaza"), "Grace marks inspected places");
        }

        [UnityTest]
        public IEnumerator Mission_AgentsTalk_AndDialogueFeedsAdasDecision()
        {
            var llm = Mock(Delegation("lake,plaza", 2), Choice("lake,plaza"));
            AgentHub.Llm = llm;
            Assert.IsTrue(MissionControl.Start("build a small village near the lake", null, out _));
            yield return WaitMission();
            var c = MissionControl.State.conversation;
            Assert.AreEqual("Succeeded", MissionControl.State.status, Log);
            Assert.AreEqual(2, llm.TextCalls, "Grace and Linus each speak once");
            CollectionAssert.AreEqual(new[] { "Ada", "Grace", "Linus", "Ada" }, c.Select(m => m.from).ToList());
            Assert.AreEqual("Ada", c[1].to);
            Assert.AreEqual("Linus", c[3].to);
            // Grace's prompt carries the measured numbers; Ada's site choice sees the discussion.
            Assert.IsTrue(llm.Prompts.Any(p => p.Contains("Your measurements: lake:") && p.Contains("free building sites")));
            Assert.IsTrue(llm.Prompts.Any(p => p.Contains("Team discussion:") && p.Contains("Grace: I measured")));
        }

        [UnityTest]
        public IEnumerator Mission_DialogueFailure_IsReported_MissionContinues()
        {
            AgentHub.Llm = new MockLlm(Delegation("plaza", 1), Choice("plaza")) { FailText = true };
            Assert.IsTrue(MissionControl.Start("one house at the plaza", null, out _));
            yield return WaitMission();
            Assert.AreEqual("Succeeded", MissionControl.State.status, Log);
            StringAssert.Contains("Grace não respondeu", Log);
            Assert.IsFalse(MissionControl.State.conversation.Any(m => m.from == "Grace"), "no fake line when Llama failed");
        }

        [UnityTest]
        public IEnumerator Mission_ReusesKnowledge_ButRemeasures()
        {
            AgentHub.Llm = Mock(Delegation("plaza", 1), Choice("plaza"));
            Assert.IsTrue(MissionControl.Start("one house at the plaza", null, out _));
            yield return WaitMission();
            var before = MissionControl.State.sharedMemory.Single().freeSites;
            var llm = Mock(Delegation("plaza", 1), Choice("plaza"));
            AgentHub.Llm = llm;
            Assert.IsTrue(MissionControl.Start("another house at the plaza", null, out _));
            yield return WaitMission();
            Assert.IsTrue(llm.Prompts[0].Contains($"plaza={before} free"), "earlier measurement offered to Ada as a hint");
            Assert.AreEqual(1, MissionControl.State.sharedMemory.Count, "Grace re-measured in this mission; stale data is never used to build");
            Assert.AreEqual("plaza", MissionControl.State.knowledge.Single().location);
            Assert.AreEqual(2, structures.Structures.Count());
        }

        [UnityTest]
        public IEnumerator ExpertRouter_RoutesStatusExplainAndUnsupported()
        {
            var output = new System.Collections.Generic.List<string>();
            AgentHub.Llm = new MockLlm("{\"intent\":\"status\",\"agent\":\"linus\",\"reason\":\"asks about Linus\"}");
            Assert.IsTrue(ExpertRouter.Ask("What is Linus doing?", output.Add, out _));
            while (ExpertRouter.Busy) yield return null;
            Assert.IsTrue(output.Any(l => l.Contains("Linus (Builder): Idle")), string.Join("\n", output));
            StringAssert.Contains("Linus", ExpertRouter.LastRoute);

            output.Clear();
            AgentHub.Llm = new MockLlm("{\"intent\":\"explain\",\"agent\":\"ada\",\"reason\":\"x\"}");
            ExpertRouter.Ask("Explain the current mission", output.Add, out _);
            while (ExpertRouter.Busy) yield return null;
            Assert.IsTrue(output.Any(l => l.Contains("Nenhuma missão ainda")));

            output.Clear();
            AgentHub.Llm = new MockLlm("{\"intent\":\"unsupported\",\"agent\":\"ada\",\"reason\":\"x\",\"message\":\"We cannot build bridges.\"}");
            ExpertRouter.Ask("Build a bridge over the lake", output.Add, out _);
            while (ExpertRouter.Busy) yield return null;
            Assert.IsTrue(output.Any(l => l.Contains("We cannot build bridges.") && l.Contains("Posso ajudar")));

            output.Clear();
            AgentHub.Llm = new MockLlm("{\"intent\":\"teleport\"}");
            ExpertRouter.Ask("teleport me somewhere nice", output.Add, out _);
            while (ExpertRouter.Busy) yield return null;
            Assert.IsTrue(output.Any(l => l.Contains("não conseguiu classificar")));
            Assert.IsEmpty(structures.Structures);
        }

        [UnityTest]
        public IEnumerator Terminal_FreeSentence_IsRoutedToScout()
        {
            AgentHub.Llm = new MockLlm("{\"intent\":\"scout\",\"agent\":\"grace\",\"reason\":\"inspection\"}",
                "{\"actions\":[{\"type\":\"move_to\",\"target\":\"plaza\"},{\"type\":\"inspect\",\"target\":\"plaza\"}]}");
            var pc = Object.FindObjectsByType<Computer>(FindObjectsSortMode.None).First(c => c.id == "lab-pc-1");
            pc.Submit("find a safe place to build");
            StringAssert.Contains("especialista", pc.log.Last());
            while (ExpertRouter.Busy) yield return null;
            var grace = AgentHub.Find("explorer");
            var end = Time.time + 30f;
            while ((grace.Ai.Busy || grace.Ai.Status == AgentStatus.Idle) && Time.time < end) yield return null;
            Assert.AreEqual(AgentStatus.Completed, grace.Ai.Status, string.Join("\n", pc.log));
            Assert.IsTrue(pc.log.Any(l => l.Contains("tarefa enviada para Grace")));
            pc.Submit("rm -rf");
            StringAssert.StartsWith("comando desconhecido", pc.log.Last());
        }

        [UnityTest]
        public IEnumerator Mission_InvalidLocation_RetriesOnce_ThenFails()
        {
            var llm = Mock(Delegation("volcano", 2));
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
            var llm = Mock(Delegation("lake", 1), Choice("east_field"));
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
            AgentHub.Llm = Delayed(0.5f, Delegation("lake,north_meadow", 1), Choice("lake"));
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
            AgentHub.Llm = Delayed(10f, Delegation("lake", 1));
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
            AgentHub.Llm = Mock(Delegation("plaza", 1), Choice("plaza"));
            pc.Submit("mission build one house at the plaza");
            StringAssert.Contains("missão iniciada", pc.log.Last());
            yield return WaitMission();
            pc.Submit("mission status");
            StringAssert.Contains("Succeeded", pc.log.Last());
            StringAssert.Contains("plaza", pc.log.Last());
        }

        /// Opt-in: does real Llama refuse something the team cannot do? Logs the outcome either way.
        [UnityTest, Explicit("needs Ollama running with the configured Llama model")]
        public IEnumerator LiveLlama_UnsupportedMission()
        {
            AgentHub.Llm = new OllamaClient();
            Assert.IsTrue(MissionControl.Start("Build a giant stone castle with towers on top of the lake", l => Debug.Log("[live] " + l), out var err), err);
            yield return WaitMission(400f);
            Debug.Log($"[live] RESULT {MissionControl.State.status}: {MissionControl.State.result}");
            Assert.AreEqual("Unsupported", MissionControl.State.status, Log);
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
