using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AgentWorld.Tests
{
    public class AiEditModeTests
    {
        static readonly string[] Locs = { "lake", "plaza", "lab" };

        [Test]
        public void Parse_ValidPlan()
        {
            var plan = PlanValidator.Parse(@"{""agent"":""builder"",""goal"":""casa"",""actions"":[{""type"":""move_to"",""target"":""lake""},{""type"":""build"",""structure"":""house"",""location"":""near_lake""}]}", out var err);
            Assert.IsNull(err);
            Assert.AreEqual(2, plan.actions.Count);
            Assert.AreEqual("build", plan.actions[1].type);
        }

        [Test]
        public void Parse_ToleratesProseAndFences()
        {
            var plan = PlanValidator.Parse("Sure!\n```json\n{\"actions\":[{\"type\":\"speak\",\"message\":\"oi\"}]}\n```", out var err);
            Assert.IsNull(err, err);
            Assert.AreEqual("oi", plan.actions[0].message);
        }

        [TestCase("")]
        [TestCase("I will build a house now.")]
        [TestCase("{\"actions\":[]}")]
        [TestCase("{ broken json ")]
        public void Parse_InvalidResponses_ReturnNull(string raw)
        {
            Assert.IsNull(PlanValidator.Parse(raw, out var err));
            Assert.IsNotEmpty(err);
        }

        [Test]
        public void Validate_AcceptsGoodPlan_AndNormalizesLocations()
        {
            var plan = PlanValidator.Parse(@"{""actions"":[{""type"":""Move_To"",""target"":""Lake""},{""type"":""build"",""structure"":""House"",""location"":""near_lake""},{""type"":""wait"",""seconds"":99}]}", out _);
            CollectionAssert.IsEmpty(PlanValidator.Validate(plan, Locs, "builder"));
            Assert.AreEqual("lake", plan.actions[1].location);
            Assert.AreEqual(10f, plan.actions[2].seconds);
        }

        [Test]
        public void Validate_RejectsUnsafeOrInvalidActions()
        {
            Assert.IsNotEmpty(Errors(@"{""actions"":[{""type"":""shell"",""message"":""rm -rf /""}]}", "builder"), "unknown action");
            Assert.IsNotEmpty(Errors(@"{""actions"":[{""type"":""move_to"",""target"":""volcano""}]}", "builder"), "unknown location");
            Assert.IsNotEmpty(Errors(@"{""actions"":[{""type"":""build"",""structure"":""castle"",""location"":""lake""}]}", "builder"), "unknown structure");
            Assert.IsNotEmpty(Errors(@"{""actions"":[{""type"":""build"",""structure"":""house"",""location"":""lake""}]}", "explorer"), "non-builder building");
            Assert.IsNotEmpty(Errors(@"{""actions"":[{""type"":""speak"",""message"":""""}]}", "builder"), "empty speak");
            var many = "{\"actions\":[" + string.Join(",", System.Linq.Enumerable.Repeat("{\"type\":\"wait\",\"seconds\":1}", 9)) + "]}";
            Assert.IsNotEmpty(Errors(many, "builder"), "too many actions");
        }

        static System.Collections.Generic.List<string> Errors(string json, string role) =>
            PlanValidator.Validate(PlanValidator.Parse(json, out _), Locs, role);

        [Test]
        public void Save_V2_RoundTripsStructuresAndAgents()
        {
            var path = Path.Combine(Path.GetTempPath(), "aw_v2.json");
            var s = new WorldState();
            s.structures.Add(new StructureState { id = "house_built_1", type = "house", builtBy = "Linus", position = new Vector2(-5, -3) });
            s.agents.Add(new AgentState { id = "linus", role = "builder", status = "Completed", goal = "casa", memory = { "Built house_built_1" } });
            SaveSystem.Save(s, path);
            var loaded = SaveSystem.Load(path);
            Assert.AreEqual(3, loaded.version);
            Assert.AreEqual(new Vector2(-5, -3), loaded.structures[0].position);
            Assert.AreEqual("Built house_built_1", loaded.agents[0].memory[0]);
            File.Delete(path);
        }

        [Test]
        public void Save_V1_StillLoads()
        {
            var path = Path.Combine(Path.GetTempPath(), "aw_v1.json");
            File.WriteAllText(path, "{\"version\":1,\"player\":{\"x\":1,\"y\":2},\"npcs\":[{\"id\":\"ada\",\"position\":{\"x\":3,\"y\":4}}],\"computers\":[]}");
            var loaded = SaveSystem.Load(path);
            Assert.AreEqual(new Vector2(1, 2), loaded.player);
            Assert.IsNotNull(loaded.structures);
            Assert.IsEmpty(loaded.structures);
            Assert.IsEmpty(loaded.agents);
            File.Delete(path);
        }

        [Test]
        public void Terminal_KeepsOldCommands_AndAddsAgentCommands()
        {
            var shell = new LocalShellBackend();
            var help = shell.Execute("help", null);
            foreach (var cmd in new[] { "echo", "whoami", "agents", "time", "ide", "clear", "agent builder", "agent stop", "world status", "ai status" })
                StringAssert.Contains(cmd, help);
            Assert.AreEqual(LocalShellBackend.Clear, shell.Execute("clear", null));
            StringAssert.StartsWith("usage:", shell.Execute("world", null));
            StringAssert.StartsWith("unknown agent", shell.Execute("agent dragon voar", null));
        }

        [Test]
        public void BuildSites_AvoidRoadsLabAndBounds()
        {
            var go = new GameObject("sb");
            var sb = go.AddComponent<StructureBuilder>();
            Assert.IsFalse(sb.IsFree(new Vector2(5, -2.5f)), "on the main road");
            Assert.IsFalse(sb.IsFree(new Vector2(0.5f, -8)), "on the south road");
            Assert.IsFalse(sb.IsFree(new Vector2(0, 6)), "inside the lab");
            Assert.IsFalse(sb.IsFree(new Vector2(30, 0)), "outside the map");
            Assert.IsTrue(sb.IsFree(new Vector2(14, -8)), "open east field");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ExpertRouter_ParsesOnlyKnownIntents()
        {
            var r = ExpertRouter.Parse("Sure: {\"intent\":\"Scout\",\"agent\":\"Grace\",\"reason\":\"find land\"}", out var err);
            Assert.IsNull(err, err);
            Assert.AreEqual("scout", r.intent);
            Assert.AreEqual("grace", r.agent);
            Assert.IsNull(ExpertRouter.Parse("{\"intent\":\"delete_world\"}", out err));
            StringAssert.Contains("unknown intent", err);
            Assert.IsNull(ExpertRouter.Parse("no json here", out _));
        }

        [Test]
        public void Terminal_ShortUnknownCommand_StillErrors()
        {
            StringAssert.StartsWith("unknown command", new LocalShellBackend().Execute("rm -rf", null));
            StringAssert.Contains("Find a safe place", new LocalShellBackend().Execute("help", null));
        }

        [Test]
        public void TerminalFont_ScalesWithScreenHeight()
        {
            Assert.AreEqual(18, TerminalUI.FontSize(720));
            Assert.AreEqual(27, TerminalUI.FontSize(1080));
            Assert.AreEqual(14, TerminalUI.FontSize(300)); // floor keeps tiny windows legible
            Assert.AreEqual(48, TerminalUI.FontSize(4320));
        }

        [Test]
        public void MainScene_HasAiWorld()
        {
            EditorSceneManager.OpenScene("Assets/AgentWorld/Scenes/Main.unity");
            Assert.IsNotNull(Object.FindAnyObjectByType<StructureBuilder>().housePrefab);
            Assert.IsNotNull(GameObject.Find("Lake"));
            var ids = System.Linq.Enumerable.Select(Object.FindObjectsByType<Landmark>(FindObjectsSortMode.None), l => l.id);
            CollectionAssert.IsSubsetOf(new[] { "lab", "lake", "plaza", "house_1" }, System.Linq.Enumerable.ToList(ids));
            var roles = System.Linq.Enumerable.Select(Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None), n => n.agentRole);
            CollectionAssert.AreEquivalent(new[] { "builder", "explorer", "assistant" }, System.Linq.Enumerable.ToList(roles));
        }
    }
}
