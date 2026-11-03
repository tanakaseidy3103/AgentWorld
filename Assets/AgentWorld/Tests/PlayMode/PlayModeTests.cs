using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AgentWorld.Tests
{
    public class PlayModeTests
    {
        PlayerController player;

        [UnitySetUp]
        public IEnumerator LoadWorld()
        {
            WorldSaveManager.PathOverride = Path.Combine(Path.GetTempPath(), "aw_playmode.json");
            File.Delete(WorldSaveManager.PathOverride);
            SceneManager.LoadScene("Main");
            yield return null;
            yield return new WaitForFixedUpdate();
            player = Object.FindAnyObjectByType<PlayerController>();
            player.useKeyboard = false;
        }

        [TearDown]
        public void Cleanup()
        {
            TerminalUI.Close();
            File.Delete(WorldSaveManager.PathOverride);
            WorldSaveManager.PathOverride = null;
        }

        [UnityTest]
        public IEnumerator Player_MovesAndCameraFollows()
        {
            var start = player.transform.position;
            player.MoveInput = Vector2.right;
            yield return new WaitForSeconds(1f);
            player.MoveInput = Vector2.zero;
            Assert.Greater(player.transform.position.x - start.x, 2f);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(player.transform.position.x, Camera.main.transform.position.x, 0.2f);
        }

        [UnityTest]
        public IEnumerator Player_IsBlockedByLabWall()
        {
            player.Teleport(new Vector2(-4, 2.5f)); // just below the lab's south-west wall
            yield return new WaitForFixedUpdate();
            player.MoveInput = Vector2.up;
            yield return new WaitForSeconds(1f);
            Assert.Less(player.transform.position.y, 3f);
        }

        [UnityTest]
        public IEnumerator Interact_Computer_OpensTerminal()
        {
            var pc = Object.FindObjectsByType<Computer>(FindObjectsSortMode.None).First(c => c.id == "lab-pc-2");
            player.Teleport((Vector2)pc.transform.position + Vector2.down * 0.8f);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(player.GetComponent<Interactor>().TryInteract());
            Assert.AreSame(pc, TerminalUI.Current);
            pc.Submit("echo oi agente");
            Assert.AreEqual("oi agente", pc.log.Last());
            pc.Submit("agents");
            StringAssert.Contains("Ada", pc.log.Last());
        }

        [UnityTest]
        public IEnumerator Npcs_Wander()
        {
            var npcs = Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None);
            var before = npcs.Select(n => (Vector2)n.transform.position).ToArray();
            yield return new WaitForSeconds(3f);
            Assert.IsTrue(npcs.Where((n, i) => Vector2.Distance(before[i], n.transform.position) > 0.2f).Any());
        }

        [UnityTest]
        public IEnumerator Save_Load_RestoresWorld()
        {
            var mgr = Object.FindAnyObjectByType<WorldSaveManager>();
            var pc = Object.FindAnyObjectByType<Computer>();
            player.Teleport(new Vector2(5, -3));
            pc.Submit("echo persistido");
            yield return new WaitForFixedUpdate();
            mgr.Save();

            player.Teleport(new Vector2(-10, -10));
            pc.log.Clear();
            Assert.IsTrue(mgr.Load());
            yield return new WaitForFixedUpdate();

            Assert.Less(Vector2.Distance(new Vector2(5, -3), player.transform.position), 0.1f);
            Assert.Contains("persistido", pc.log);
        }
    }
}
