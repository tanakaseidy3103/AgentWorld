using System.Collections.Generic;
using UnityEngine;

namespace AgentWorld
{
    /// Small procedural pixel sprites created at runtime (icons, flags, scaffolding, dust). Cached per name.
    public static class RuntimeArt
    {
        const int PPU = 16;
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        static readonly Dictionary<char, Color32> Palette = new Dictionary<char, Color32>
        {
            ['k'] = new Color32(30, 28, 36, 255),    // outline
            ['w'] = new Color32(250, 248, 240, 255), // white
            ['y'] = new Color32(255, 214, 80, 255),  // yellow
            ['g'] = new Color32(110, 220, 120, 255), // green
            ['r'] = new Color32(235, 80, 80, 255),   // red
            ['b'] = new Color32(120, 190, 255, 255), // light blue glass
            ['m'] = new Color32(150, 105, 60, 255),  // wood
            ['d'] = new Color32(105, 70, 40, 255),   // dark wood
            ['s'] = new Color32(170, 175, 185, 255), // steel
            ['p'] = new Color32(200, 140, 255, 255), // purple (Llama)
        };

        // 10x10 speech-bubble icons drawn above agents.
        static readonly Dictionary<string, string[]> Maps = new Dictionary<string, string[]>
        {
            ["think"] = new[] { ".kkkkkkkk.", "kwwwwwwwwk", "kwwwwwwwwk", "kwpwwpwwpk", "kwwwwwwwwk", "kwwwwwwwwk", ".kkkkkkkk.", "...kk.....", "....k.....", ".........." },
            ["decide"] = new[] { ".kkkkkkkk.", "kyyyykyyyk", "kyyyykyyyk", "kyyyykyyyk", "kyyyyyyyyk", "kyyyykyyyk", ".kkkkkkkk.", "...kk.....", "....k.....", ".........." },
            ["scout"] = new[] { "..kkkk....", ".kbbbbk...", "kbbwbbbk..", "kbwbbbbk..", "kbbbbbbk..", ".kbbbbk...", "..kkkkdk..", "......kdk.", ".......kdk", "........k." },
            ["build"] = new[] { ".kkkkk....", "kssssskk..", "kssssssk..", ".kkkkdk...", "....kdk...", "....kdk...", "....kdk...", "....kdk...", "....kdk...", ".....k...." },
            ["walk"] = new[] { "....kk....", "...kggk...", "..kggggk..", ".kggggggk.", "kkkggggkkk", "...kggk...", "...kggk...", "...kggk...", "...kkkk...", ".........." },
            ["done"] = new[] { ".kkkkkkkk.", "kgggggggwk", "kggggggwgk", "kwgggggwgk", "kgwgggwggk", "kggwgwgggk", "kgggwggggk", ".kkkkkkkk.", "..........", ".........." },
            ["fail"] = new[] { ".kkkkkkkk.", "krrrrrrrrk", "krwrrrrwrk", "krrwrrwrrk", "krrrwwrrrk", "krrwrrwrrk", "krwrrrrwrk", ".kkkkkkkk.", "..........", ".........." },
        };

        public static Sprite Icon(string name) => Get(name, () => FromMap(Maps[name]), new Vector2(0.5f, 0f));

        /// Survey flag Grace plants at inspected places: green = buildable land, red = full.
        public static Sprite Flag(bool good) => Get(good ? "flag_ok" : "flag_full", () =>
        {
            var c = good ? Palette['g'] : Palette['r'];
            return Tex(10, 16, (x, y) =>
            {
                if (x == 1 && y < 15) return Palette['d'];
                if (x >= 2 && x <= 8 && y >= 9 && y <= 14 && x - 2 <= (y - 9) + 4) return x == 8 || y == 14 || y == 9 ? (Color32)Color.Lerp(c, Color.black, 0.3f) : c;
                return new Color32(0, 0, 0, 0);
            });
        }, new Vector2(0.15f, 0f));

        /// Wooden scaffolding shown around a structure while it is being built (48x40 like the house).
        public static Sprite Scaffold() => Get("scaffold", () => Tex(48, 40, (x, y) =>
        {
            var pole = x % 15 <= 1 && y < 38;
            var plank = (y % 10 == 0 || y % 10 == 1) && x > 0 && x < 47;
            var brace = (x + y) % 15 == 0 && y % 10 > 1;
            if (pole) return Palette['d'];
            if (plank) return Palette['m'];
            if (brace) return new Color32(130, 90, 50, 200);
            return new Color32(0, 0, 0, 0);
        }), new Vector2(0.5f, 0f));

        public static Sprite Dust() => Get("dust", () => Tex(6, 6, (x, y) =>
        {
            float dx = x - 2.5f, dy = y - 2.5f;
            return dx * dx + dy * dy <= 7f ? new Color32(225, 210, 180, 220) : new Color32(0, 0, 0, 0);
        }), new Vector2(0.5f, 0.5f));

        static Sprite Get(string name, System.Func<Texture2D> make, Vector2 pivot)
        {
            if (Cache.TryGetValue(name, out var s) && s) return s;
            var tex = make();
            tex.filterMode = FilterMode.Point;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, PPU);
            s.name = name;
            return Cache[name] = s;
        }

        static Texture2D FromMap(string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            return Tex(w, h, (x, y) => Palette.TryGetValue(rows[h - 1 - y][x], out var c) ? c : new Color32(0, 0, 0, 0));
        }

        static Texture2D Tex(int w, int h, System.Func<int, int, Color32> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                t.SetPixel(x, y, f(x, y));
            t.Apply();
            return t;
        }
    }
}
