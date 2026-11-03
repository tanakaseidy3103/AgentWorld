using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AgentWorld.Tests
{
    public class EditModeTests
    {
        const string ScenePath = "Assets/AgentWorld/Scenes/Main.unity";

        [Test]
        public void SaveSystem_RoundTrip()
        {
            var path = Path.Combine(Path.GetTempPath(), "aw_roundtrip.json");
            var s = new WorldState { player = new Vector2(1.5f, -2f) };
            s.npcs.Add(new EntityState { id = "ada", position = new Vector2(3, 4) });
            s.computers.Add(new ComputerState { id = "lab-pc-1", log = { "> help", "ok" } });

            SaveSystem.Save(s, path);
            SaveSystem.Save(s, path); // second save exercises the replace path
            var loaded = SaveSystem.Load(path);

            Assert.AreEqual(s.player, loaded.player);
            Assert.AreEqual("ada", loaded.npcs[0].id);
            Assert.AreEqual(new Vector2(3, 4), loaded.npcs[0].position);
            CollectionAssert.AreEqual(new[] { "> help", "ok" }, loaded.computers[0].log);
            File.Delete(path);
        }

        [Test]
        public void SaveSystem_MissingOrCorrupt_ReturnsNull()
        {
            var path = Path.Combine(Path.GetTempPath(), "aw_corrupt.json");
            File.Delete(path);
            Assert.IsNull(SaveSystem.Load(path));
            File.WriteAllText(path, "{ not json");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Save invalido"));
            Assert.IsNull(SaveSystem.Load(path));
            File.Delete(path);
        }

        [Test]
        public void LocalShell_Commands()
        {
            var shell = new LocalShellBackend();
            StringAssert.Contains("echo", shell.Execute("help", null));
            Assert.AreEqual("ola mundo", shell.Execute("echo ola mundo", null));
            StringAssert.StartsWith("comando desconhecido", shell.Execute("rm -rf", null));
        }

        [Test]
        public void MainScene_HasPlayableWorld()
        {
            Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.path == ScenePath && s.enabled), "Main scene not in build settings");
            EditorSceneManager.OpenScene(ScenePath);

            Assert.AreEqual(1, Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length);
            Assert.IsNotNull(Object.FindAnyObjectByType<Interactor>());
            Assert.AreEqual(3, Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(3, Object.FindObjectsByType<Computer>(FindObjectsSortMode.None).Select(c => c.id).Distinct().Count());
            Assert.IsNotNull(Object.FindAnyObjectByType<WorldSaveManager>());
            Assert.IsNotNull(Object.FindAnyObjectByType<TerminalUI>());
            Assert.IsNotNull(GameObject.Find("Lab"));
            Assert.AreEqual(3, Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Count(r => r.name.StartsWith("House_")));
            var cam = Camera.main;
            Assert.IsTrue(cam.orthographic);
            Assert.IsNotNull(cam.GetComponent<CameraFollow>().target);
        }

        [Test]
        public void Prefabs_AreValid()
        {
            foreach (var name in new[] { "Player", "Computer", "Tree", "House_0", "Npc_Ada", "Npc_Linus", "Npc_Grace" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/AgentWorld/Prefabs/{name}.prefab");
                Assert.IsNotNull(p, name);
                Assert.IsNotNull(p.GetComponent<SpriteRenderer>().sprite, name + " sprite");
            }
        }
    }
}
