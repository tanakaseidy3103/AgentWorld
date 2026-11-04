using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AgentWorld.Tests
{
    /// Deterministic stand-in for Ollama: replays canned replies (last one repeats).
    public class MockLlm : ILlmClient
    {
        public readonly Queue<string> Replies = new Queue<string>();
        public float Delay;
        /// JSON calls (plans/decisions). Free-text calls are counted in TextCalls when TextReply is set.
        public int Calls, TextCalls;
        /// When set, free-text calls (agent dialogue) return this instead of consuming Replies.
        public string TextReply;
        public bool FailText;
        public readonly List<string> Prompts = new List<string>();
        public string Model => "mock-llama";

        public MockLlm(params string[] replies) { foreach (var r in replies) Replies.Enqueue(r); }

        public async Task<string> Chat(string system, string user, bool json, CancellationToken ct)
        {
            Prompts.Add(user);
            if (!json && (TextReply != null || FailText))
            {
                TextCalls++;
                if (Delay > 0) await Awaitable.WaitForSecondsAsync(Delay, ct);
                if (FailText) throw new System.Exception("mock text failure");
                return TextReply;
            }
            Calls++;
            if (Delay > 0) await Awaitable.WaitForSecondsAsync(Delay, ct);
            return Replies.Count > 1 ? Replies.Dequeue() : Replies.Peek();
        }

        public Task<string[]> ListModels(CancellationToken ct) => Task.FromResult(new[] { Model });
    }

    public class AiPlayModeTests
    {
        const string BuildPlan = @"{""agent"":""builder"",""goal"":""casa no lago"",""actions"":[
            {""type"":""move_to"",""target"":""lake""},
            {""type"":""build"",""structure"":""house"",""location"":""near_lake""},
            {""type"":""speak"",""message"":""A casa está pronta!""}]}";

        NpcAgent builder;
        StructureBuilder structures;

        [UnitySetUp]
        public IEnumerator LoadWorld()
        {
            WorldSaveManager.PathOverride = Path.Combine(Path.GetTempPath(), "aw_ai_playmode.json");
            File.Delete(WorldSaveManager.PathOverride);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return new WaitForFixedUpdate();
            builder = AgentHub.Find("builder");
            builder.speed = 8f;
            structures = StructureBuilder.Instance;
            structures.buildSeconds = 0.5f;
        }

        [TearDown]
        public void Cleanup()
        {
            TerminalUI.Close();
            AgentHub.Llm = null;
            AgentHub.LlmTimeoutSeconds = 120f;
            File.Delete(WorldSaveManager.PathOverride);
            WorldSaveManager.PathOverride = null;
        }

        IEnumerator WaitUntilDone(LlamaAgentBrain ai, List<AgentStatus> seen, float timeout = 30f)
        {
            var end = Time.time + timeout;
            while (Time.time < end && (ai.Busy || ai.Status == AgentStatus.Idle && seen.Count == 0))
            {
                if (seen.Count == 0 || seen.Last() != ai.Status) seen.Add(ai.Status);
                yield return null;
            }
            seen.Add(ai.Status);
        }

        [UnityTest]
        public IEnumerator Builder_BuildsHouseNearLake_AndItPersists()
        {
            AgentHub.Llm = new MockLlm(BuildPlan) { Delay = 0.2f }; // real models take time; lets us observe Thinking
            var output = new List<string>();
            Assert.IsTrue(builder.Ai.Assign("construa uma casa perto do lago", output.Add, out var err), err);

            var seen = new List<AgentStatus>();
            yield return WaitUntilDone(builder.Ai, seen, 90f); // walking alone may take up to MoveTimeoutSeconds (40 s), plus building

            Assert.AreEqual(AgentStatus.Completed, builder.Ai.Status, string.Join("\n", output));
            foreach (var s in new[] { AgentStatus.Thinking, AgentStatus.Moving, AgentStatus.Acting, AgentStatus.Completed })
                CollectionAssert.Contains(seen, s);
            var house = structures.Structures.Single();
            Assert.Less(Vector2.Distance(house.transform.position, Landmark.Find("lake").transform.position), 10f);
            Assert.IsTrue(house.GetComponent<Collider2D>().enabled);
            Assert.IsTrue(output.Any(l => l.StartsWith("[Llama] plan")));
            Assert.IsTrue(output.Any(l => l.StartsWith("[Unity]")));

            var mgr = Object.FindAnyObjectByType<WorldSaveManager>();
            var pos = (Vector2)house.transform.position;
            mgr.Save();
            structures.Restore(null); // wipe agent buildings
            yield return null;
            Assert.IsEmpty(structures.Structures);
            Assert.IsTrue(mgr.Load());
            yield return null;
            var restored = structures.Structures.Single();
            Assert.AreEqual(pos, (Vector2)restored.transform.position);
            Assert.AreEqual(AgentStatus.Completed, builder.Ai.Status);
            Assert.IsTrue(builder.Ai.Memory.Any(m => m.Contains("house_built_1")));
        }

        /// Real inference against local Ollama. Opt-in: -testFilter AgentWorld.Tests.AiPlayModeTests.LiveLlama_BuilderBuildsHouse
        [UnityTest, Explicit("needs Ollama running with the configured Llama model")]
        public IEnumerator LiveLlama_BuilderBuildsHouse()
        {
            AgentHub.Llm = new OllamaClient();
            var output = new List<string>();
            Assert.IsTrue(builder.Ai.Assign("build a house near the lake", l => { output.Add(l); Debug.Log("[live] " + l); }, out var err), err);
            yield return WaitUntilDone(builder.Ai, new List<AgentStatus>(), 180f);
            Assert.AreEqual(AgentStatus.Completed, builder.Ai.Status, string.Join("\n", output));
            Assert.AreEqual(1, structures.Structures.Count());
        }

        /// Regression: recompiling scripts during Play mode reloads the domain, which nulls the non-serialized
        /// Brain without re-running Awake; FixedUpdate then threw NullReferenceException every physics step.
        [UnityTest]
        public IEnumerator Npc_LostBrainAfterDomainReload_IsRecreated_NoException()
        {
            builder.Brain = null; // what the domain reload did
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsInstanceOf<LlamaAgentBrain>(builder.Brain);
            Assert.AreEqual("builder", builder.Ai.Role);
            // Any NullReferenceException logged during these frames fails the test (Unity Test Framework).
        }

        /// Regression: the builder's stand point for lake/house sites was inside the lake or a house collider,
        /// so Linus could only arrive by luck (intermittent Builder_BuildsHouseNearLake failures).
        [UnityTest]
        public IEnumerator EveryBuildSite_HasAReachableStandPoint()
        {
            yield return null;
            foreach (var lm in Landmark.All.ToList())
            {
                var site = structures.FindSite(lm.transform.position);
                if (site == null) continue;
                var stand = structures.StandPoint(site.Value);
                var blocker = Physics2D.OverlapCircleAll(stand, 0.35f).FirstOrDefault(h => !h.isTrigger && h.attachedRigidbody == null);
                Assert.IsNull(blocker, $"{lm.id}: stand point {stand} is inside {(blocker ? blocker.name : "")}");
            }
        }

        [UnityTest]
        public IEnumerator InvalidLlmResponse_RetriesOnce_ThenFails()
        {
            var llm = new MockLlm("I don't speak JSON, sorry.");
            AgentHub.Llm = llm;
            Assert.IsTrue(builder.Ai.Assign("faça algo", null, out _));
            yield return WaitUntilDone(builder.Ai, new List<AgentStatus>());
            Assert.AreEqual(AgentStatus.Failed, builder.Ai.Status);
            Assert.AreEqual(2, llm.Calls);
            Assert.IsEmpty(structures.Structures);
        }

        [UnityTest]
        public IEnumerator UnsafeAction_IsRejected_NothingExecutes()
        {
            AgentHub.Llm = new MockLlm(@"{""actions"":[{""type"":""shell"",""message"":""del C:\\*""}]}");
            Assert.IsTrue(builder.Ai.Assign("apague tudo", null, out _));
            yield return WaitUntilDone(builder.Ai, new List<AgentStatus>());
            Assert.AreEqual(AgentStatus.Failed, builder.Ai.Status);
            StringAssert.Contains("unknown action type", builder.Ai.LastResult);
        }

        [UnityTest]
        public IEnumerator OllamaUnavailable_FailsWithClearError()
        {
            AgentHub.Llm = new OllamaClient("http://127.0.0.1:1", "llama3.2");
            Assert.IsTrue(builder.Ai.Assign("construa uma casa", null, out _));
            yield return WaitUntilDone(builder.Ai, new List<AgentStatus>());
            Assert.AreEqual(AgentStatus.Failed, builder.Ai.Status);
            StringAssert.Contains("unavailable", builder.Ai.LastResult);
        }

        [UnityTest]
        public IEnumerator Stop_CancelsThinkingAgent()
        {
            AgentHub.Llm = new MockLlm(BuildPlan) { Delay = 10f };
            Assert.IsTrue(builder.Ai.Assign("construa", null, out _));
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(builder.Ai.Assign("outra coisa", null, out _), "busy agent must refuse a second task");
            builder.Ai.Stop();
            yield return WaitUntilDone(builder.Ai, new List<AgentStatus>(), 3f);
            Assert.AreEqual(AgentStatus.Cancelled, builder.Ai.Status);
            Assert.IsEmpty(structures.Structures);
        }

        [UnityTest]
        public IEnumerator SlowLlm_TimesOut()
        {
            AgentHub.LlmTimeoutSeconds = 0.5f;
            AgentHub.Llm = new MockLlm(BuildPlan) { Delay = 10f };
            Assert.IsTrue(builder.Ai.Assign("construa", null, out _));
            yield return WaitUntilDone(builder.Ai, new List<AgentStatus>(), 5f);
            Assert.AreEqual(AgentStatus.Failed, builder.Ai.Status);
            StringAssert.Contains("did not answer", builder.Ai.LastResult);
        }

        [UnityTest]
        public IEnumerator Terminal_AgentCommand_RunsAsync()
        {
            AgentHub.Llm = new MockLlm(BuildPlan);
            var pc = Object.FindObjectsByType<Computer>(FindObjectsSortMode.None).First(c => c.id == "lab-pc-1");
            pc.Submit("agent builder construa uma casa perto do lago");
            StringAssert.Contains("task sent", pc.log.Last()); // returned immediately, game not blocked
            yield return WaitUntilDone(builder.Ai, new List<AgentStatus>());
            Assert.IsTrue(pc.log.Any(l => l.Contains("Task completed")));
            pc.Submit("world structures");
            StringAssert.Contains("house_built_1", pc.log.Last());
            pc.Submit("echo ainda funciona");
            Assert.AreEqual("ainda funciona", pc.log.Last());
        }

        [UnityTest]
        public IEnumerator Interact_AiNpc_OpensChat_AndReplies()
        {
            AgentHub.Llm = new MockLlm("Olá! Eu construo casas.");
            var player = Object.FindAnyObjectByType<PlayerController>();
            player.useKeyboard = false;
            builder.speed = 0f;
            player.Teleport((Vector2)builder.transform.position + Vector2.left * 0.8f);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(player.GetComponent<Interactor>().TryInteract());
            var chat = TerminalUI.Current;
            Assert.IsNotNull(chat);
            chat.Submit("oi, quem é você?");
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(chat.log.Any(l => l.Contains("Eu construo casas")), string.Join("\n", chat.log));
            Assert.AreEqual(2, builder.Ai.ChatHistory.Count);
        }
    }
}
