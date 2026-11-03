using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AgentWorld.EditorTools
{
    /// Generates pixel-art sprites, prefabs and the Main scene.
    /// Menu: AgentWorld > Rebuild World. Batch: -executeMethod AgentWorld.EditorTools.WorldBuilder.Build
    public static class WorldBuilder
    {
        const string Root = "Assets/AgentWorld";
        const string ArtDir = Root + "/Art", PrefabDir = Root + "/Prefabs", SceneDir = Root + "/Scenes";
        public const string ScenePath = SceneDir + "/Main.unity";
        const int PPU = 16;
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f), Feet = new Vector2(0.5f, 0f);
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        struct NpcDef { public string id, name, role; public Color32 shirt, hair; public Vector2 home; public string[] lines; }

        static readonly NpcDef[] Npcs =
        {
            new NpcDef { id = "ada", name = "Ada", role = "Arquiteta de Agentes", shirt = C(150, 80, 180), hair = C(70, 40, 30), home = new Vector2(-3, 7),
                lines = new[] { "Este laboratório vai abrigar a Living IDE.", "Cada computador será um portal para um agente.", "Use E nos computadores para abrir o terminal." } },
            new NpcDef { id = "linus", name = "Linus", role = "Engenheiro de Sistemas", shirt = C(70, 150, 90), hair = C(220, 190, 90), home = new Vector2(8, -6),
                lines = new[] { "O kernel deste mundo ainda é simples, mas estável.", "Digite 'agents' num terminal para ver todos nós.", "Pressione F5 para salvar o mundo." } },
            new NpcDef { id = "grace", name = "Grace", role = "Depuradora", shirt = C(200, 70, 70), hair = C(30, 30, 35), home = new Vector2(-7, 0),
                lines = new[] { "Encontrei um bug... era uma mariposa!", "Testes primeiro, depois o deploy.", "Logo teremos agentes LLM conversando aqui." } },
        };

        [MenuItem("AgentWorld/Rebuild World")]
        public static void Build()
        {
            foreach (var d in new[] { ArtDir, PrefabDir, SceneDir }) Directory.CreateDirectory(d);
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
            GraphicsSettings.transparencySortAxis = Vector3.up; // lower on screen draws in front

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Sprites -------------------------------------------------------
            var grass = MakeSprite("grass", Tex(16, 16, (x, y) => Noise(x, y, 1) > .85f ? C(70, 140, 60) : Noise(x, y, 2) > .93f ? C(130, 195, 95) : C(86, 160, 72)), Center);
            var path = MakeSprite("path", Tex(16, 16, (x, y) => Noise(x, y, 3) > .88f ? C(170, 140, 100) : C(200, 170, 122)), Center);
            var floor = MakeSprite("lab_floor", Tex(16, 16, (x, y) => x == 0 || y == 0 ? C(150, 160, 175) : C(205, 212, 222)), Center);
            var wall = MakeSprite("lab_wall", Tex(16, 16, (x, y) => y % 4 == 0 || (x + (y / 4 % 2) * 4) % 8 == 0 ? C(60, 64, 80) : C(95, 105, 130)), Center);
            var tree = MakeSprite("tree", TreeTex(), Feet);
            var computer = MakeSprite("computer", FromMap(ComputerMap, new Dictionary<char, Color32>
            {
                ['k'] = C(40, 44, 52), ['g'] = C(30, 90, 60), ['l'] = C(120, 240, 150), ['w'] = C(150, 105, 60), ['d'] = C(105, 70, 40),
            }), Feet);
            var roofs = new[] { C(190, 70, 60), C(60, 110, 180), C(90, 150, 80) };

            // --- Prefabs -------------------------------------------------------
            var playerPrefab = SavePrefab(MakeCharacter("Player", CharSprite("player", C(60, 110, 200), C(110, 70, 40)), true), "Player");

            var go = Spr("Computer", computer, Vector2.zero, null, 0, null, true);
            Box(go, new Vector2(1f, 0.5f), new Vector2(0, 0.25f));
            go.AddComponent<Computer>();
            var computerPrefab = SavePrefab(go, "Computer");

            go = Spr("Tree", tree, Vector2.zero, null, 0, null, true);
            Box(go, new Vector2(0.6f, 0.4f), new Vector2(0, 0.2f));
            var treePrefab = SavePrefab(go, "Tree");

            var housePrefabs = new GameObject[roofs.Length];
            for (var i = 0; i < roofs.Length; i++)
            {
                go = Spr("House", MakeSprite("house_" + i, HouseTex(roofs[i]), Feet), Vector2.zero, null, 0, null, true);
                Box(go, new Vector2(2.8f, 1.4f), new Vector2(0, 0.7f));
                housePrefabs[i] = SavePrefab(go, "House_" + i);
            }

            var npcPrefabs = new GameObject[Npcs.Length];
            for (var i = 0; i < Npcs.Length; i++)
            {
                var n = Npcs[i];
                go = MakeCharacter("Npc_" + n.name, CharSprite("npc_" + n.id, n.shirt, n.hair), false);
                var agent = go.AddComponent<NpcAgent>();
                agent.id = n.id; agent.displayName = n.name; agent.role = n.role; agent.lines = n.lines;
                npcPrefabs[i] = SavePrefab(go, "Npc_" + n.name);
            }

            // --- World ---------------------------------------------------------
            var world = new GameObject("World").transform;
            Spr("Ground", grass, Vector2.zero, world, -100, new Vector2(40, 30));
            Spr("Path_H", path, new Vector2(0, -2), world, -90, new Vector2(40, 2));
            Spr("Path_V", path, new Vector2(0, -5.75f), world, -90, new Vector2(2, 18.5f));

            var bounds = new GameObject("Bounds").transform;
            bounds.SetParent(world);
            Box(Child("N", bounds, new Vector2(0, 15.5f)), new Vector2(42, 1), Vector2.zero);
            Box(Child("S", bounds, new Vector2(0, -15.5f)), new Vector2(42, 1), Vector2.zero);
            Box(Child("E", bounds, new Vector2(20.5f, 0)), new Vector2(1, 32), Vector2.zero);
            Box(Child("W", bounds, new Vector2(-20.5f, 0)), new Vector2(1, 32), Vector2.zero);

            // Lab: floor x[-6,6] y[4,12], walls around it, 2-unit doorway at the bottom.
            var lab = new GameObject("Lab").transform;
            lab.SetParent(world);
            Spr("Floor", floor, new Vector2(0, 8), lab, -80, new Vector2(12, 8));
            Wall("Wall_N", wall, lab, new Vector2(0, 12.5f), new Vector2(14, 1));
            Wall("Wall_W", wall, lab, new Vector2(-6.5f, 8), new Vector2(1, 8));
            Wall("Wall_E", wall, lab, new Vector2(6.5f, 8), new Vector2(1, 8));
            Wall("Wall_SW", wall, lab, new Vector2(-3.75f, 3.5f), new Vector2(5.5f, 1));
            Wall("Wall_SE", wall, lab, new Vector2(3.75f, 3.5f), new Vector2(5.5f, 1));
            for (var i = 0; i < 3; i++)
            {
                var pc = Place(computerPrefab, new Vector2(-4 + 4 * i, 10.5f), lab);
                pc.name = "Computer_" + (i + 1);
                pc.GetComponent<Computer>().id = "lab-pc-" + (i + 1);
            }

            var houses = new[] { new Vector2(-13, 4), new Vector2(13, 4), new Vector2(-12, -10) };
            for (var i = 0; i < houses.Length; i++) Place(housePrefabs[i], houses[i], world).name = "House_" + (i + 1);

            foreach (var t in new[] { new Vector2(-17, 12), new Vector2(-10, 13), new Vector2(10, 13), new Vector2(17, 12), new Vector2(-18, -1),
                         new Vector2(18, -3), new Vector2(-5, -12), new Vector2(6, -10), new Vector2(15, -12), new Vector2(-17, -13), new Vector2(9, 8) })
                Place(treePrefab, t, world);

            for (var i = 0; i < Npcs.Length; i++) Place(npcPrefabs[i], Npcs[i].home, null);

            var player = Place(playerPrefab, new Vector2(0, -4), null);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            var c = cam.AddComponent<Camera>();
            c.orthographic = true;
            c.orthographicSize = 7;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = C(25, 35, 30);
            cam.transform.position = new Vector3(0, -4, -10);
            cam.AddComponent<CameraFollow>().target = player.transform;

            var systems = new GameObject("Systems");
            systems.AddComponent<WorldSaveManager>();
            systems.AddComponent<TerminalUI>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("AgentWorld: world built at " + ScenePath);
        }

        // --- Helpers -------------------------------------------------------------

        static GameObject MakeCharacter(string name, Sprite sprite, bool player)
        {
            var go = Spr(name, sprite, Vector2.zero, null, 0, null, true);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.3f;
            col.offset = new Vector2(0, 0.3f);
            if (player) { go.AddComponent<PlayerController>(); go.AddComponent<Interactor>(); }
            return go;
        }

        static GameObject SavePrefab(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject Place(GameObject prefab, Vector2 pos, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            return go;
        }

        static GameObject Child(string name, Transform parent, Vector2 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            return go;
        }

        static void Wall(string name, Sprite s, Transform parent, Vector2 pos, Vector2 size) =>
            Box(Spr(name, s, pos, parent, 0, size), size, Vector2.zero);

        static GameObject Spr(string name, Sprite s, Vector2 pos, Transform parent, int order, Vector2? tiled = null, bool sortByFeet = false)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = order;
            if (tiled is Vector2 size) { sr.drawMode = SpriteDrawMode.Tiled; sr.size = size; }
            if (sortByFeet) sr.spriteSortPoint = SpriteSortPoint.Pivot;
            return go;
        }

        static void Box(GameObject go, Vector2 size, Vector2 offset)
        {
            var b = go.AddComponent<BoxCollider2D>();
            b.size = size;
            b.offset = offset;
        }

        static Sprite MakeSprite(string name, Texture2D tex, Vector2 pivot)
        {
            var path = $"{ArtDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PPU;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.alphaIsTransparency = true;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect; // required for tiled draw mode
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = pivot;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        static float Noise(int x, int y, int seed)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        static Texture2D Tex(int w, int h, Func<int, int, Color32> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                t.SetPixel(x, y, f(x, y));
            t.Apply();
            return t;
        }

        static Texture2D FromMap(string[] rows, Dictionary<char, Color32> palette)
        {
            var h = rows.Length;
            return Tex(16, h, (x, y) =>
            {
                var row = rows[h - 1 - y].PadRight(16, '.');
                return palette.TryGetValue(row[x], out var col) ? col : Clear;
            });
        }

        static Sprite CharSprite(string name, Color32 shirt, Color32 hair) =>
            MakeSprite(name, FromMap(CharacterMap, new Dictionary<char, Color32>
            {
                ['o'] = C(40, 30, 30), ['h'] = hair, ['s'] = C(240, 200, 160), ['e'] = C(30, 30, 40),
                ['c'] = shirt, ['p'] = C(50, 60, 90), ['b'] = C(60, 40, 30),
            }), Feet);

        static readonly string[] CharacterMap =
        {
            "................",
            ".....oooooo.....",
            "....ohhhhhho....",
            "....ohssssho....",
            "....osesseso....",
            "....osssssso....",
            ".....oooooo.....",
            "....occcccco....",
            "...occccccccco..",
            "...sccccccccs...",
            "....occcccco....",
            "....oppppppo....",
            "....oppoopppo...",
            "....oppoopppo...",
            "....obbo.obbo...",
            "................",
        };

        static readonly string[] ComputerMap =
        {
            "................",
            "..kkkkkkkkkkkk..",
            "..kggggggggggk..",
            "..kgllggggggk...",
            "..kggggllgggggk.",
            "..kglllllggggk..",
            "..kggggggggggk..",
            "..kkkkkkkkkkkk..",
            "......kkkk......",
            ".....kkkkkk.....",
            "wwwwwwwwwwwwwwww",
            "dddddddddddddddd",
            ".dd..........dd.",
            ".dd..........dd.",
            ".dd..........dd.",
            "................",
        };

        static Texture2D TreeTex() => Tex(16, 24, (x, y) =>
        {
            float dx = x - 7.5f, dy = y - 15f;
            if (dx * dx + dy * dy <= 7.5f * 7.5f)
                return Noise(x, y, 7) > .75f || dy < -4 ? C(40, 110, 50) : Noise(x, y, 8) > .9f ? C(110, 190, 90) : C(60, 140, 65);
            if (x >= 6 && x <= 9 && y <= 9) return x == 6 ? C(80, 50, 30) : C(115, 75, 45);
            return Clear;
        });

        static Texture2D HouseTex(Color32 roof) => Tex(48, 40, (x, y) =>
        {
            const int W = 48;
            var dark = new Color32((byte)(roof.r * .6f), (byte)(roof.g * .6f), (byte)(roof.b * .6f), 255);
            if (y >= 22)
            {
                var m = (y - 22) / 2;
                if (x < m || x > W - 1 - m) return Clear;
                if (x == m || x == W - 1 - m || y == 39) return C(50, 35, 30);
                return y % 3 == 0 ? dark : roof;
            }
            if (x < 2 || x > 45) return Clear;
            var inDoor = x >= 20 && x <= 27 && y >= 1 && y <= 13;
            if (inDoor)
            {
                if (x == 26 && y == 7) return C(240, 200, 60);
                return x == 20 || x == 27 || y == 13 ? C(70, 45, 25) : C(120, 80, 45);
            }
            var inWin = ((x >= 7 && x <= 14) || (x >= 33 && x <= 40)) && y >= 9 && y <= 16;
            if (inWin)
            {
                var edge = x == 7 || x == 14 || x == 33 || x == 40 || y == 9 || y == 16 || x == 10 || x == 37 || y == 12;
                return edge ? C(245, 240, 230) : (x + y) % 5 == 0 ? C(200, 230, 250) : C(110, 170, 220);
            }
            if (x == 2 || x == 45 || y == 0) return C(90, 65, 45);
            return y % 4 == 0 ? C(205, 180, 140) : C(230, 210, 170);
        });
    }
}
