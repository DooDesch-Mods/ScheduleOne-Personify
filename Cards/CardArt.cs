using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Personify.Cards
{
    /// <summary>What the card picture shows. Pixels are 8-bit RGBA, rows top to bottom.</summary>
    public sealed class CardArtInput
    {
        public string Name;
        /// <summary>customer, dealer, supplier or townsfolk.</summary>
        public string Role;
        public string Code;
        public string Creator;
        /// <summary>Code of the card this one remixes, or null.</summary>
        public string BasedOn;
        /// <summary>Full-body render filling the height chart (<see cref="CardArt.ChartWidth"/> x <see cref="CardArt.ChartHeight"/>,
        /// <see cref="CardArt.ChartMeters"/> tall, feet on the bottom row), transparent around the NPC. Null draws no body.</summary>
        public byte[] Body;
        /// <summary>Square head render for the mugshot, <see cref="HeadSize"/> pixels wide. Null leaves the frame empty.</summary>
        public byte[] Head;
        public int HeadSize;
    }

    /// <summary>
    /// Draws the NPC card picture in the NPC Vault "booking room" look: a cinderblock wall, the height chart with the
    /// NPC standing against it, and a letter-board placard with name, role, creator and mugshot. Pure managed code
    /// (no Unity), so the off-game preview tool draws the same picture; text comes from glyph atlases baked by
    /// tools/card-font.py from Big Shoulders and Archivo (SIL OFL).
    /// </summary>
    public static class CardArt
    {
        public const int Width = 900;
        public const int Height = 1260;

        // Height chart: x 60..840, y 110..1010, 0 to 2.2 m.
        public const int ChartX = 60, ChartY = 110, ChartWidth = 780, ChartHeight = 900;
        public const float ChartMeters = 2.2f;

        private static readonly Rgb Wall = Hex("#6f7d72"), Mortar = Hex("#647168"), Vinyl = Hex("#e9e9e2"),
            Ink = Hex("#1d221f"), Yellow = Hex("#e4c23b"), Paper = Hex("#f1f1ec"), Muted = Hex("#b9c0b9");

        public static string RoleColorHex(string role) => role switch
        {
            "dealer" => "#d0503f",
            "supplier" => "#5fa86f",
            "customer" => "#5b8fc7",
            _ => "#b8a98a",
        };

        /// <summary>Draws the card picture; returns RGBA (<see cref="Width"/> x <see cref="Height"/>).</summary>
        public static byte[] Compose(CardArtInput a)
        {
            var c = new Canvas(Width, Height);
            DrawWall(c);
            DrawChart(c);
            if (a.Body != null && a.Body.Length == ChartWidth * ChartHeight * 4)
                c.Blend(a.Body, ChartWidth, ChartHeight, ChartX, ChartY);

            // Top strip: vault name left, booking number right.
            c.Fill(0, 0, Width, 96, Ink);
            Font display = Font.Load("display"), label = Font.Load("label"), small = Font.Load("small");
            display.Draw(c, "NPC VAULT", 60, 76, Vinyl, 0.5f);
            string code = (a.Code ?? "").ToUpperInvariant();
            float codeW = display.Measure(code, 0.5f);
            display.Draw(c, code, Width - 60 - (int)codeW, 76, Yellow, 0.5f);
            float bookingW = label.Measure("BOOKING NO.", 0.8f, 2);
            label.Draw(c, "BOOKING NO.", Width - 60 - (int)codeW - 14 - (int)bookingW, 70, Muted, 0.8f, 2);

            // Placard.
            int py = 1030, ph = 166;
            c.Fill(ChartX, py, ChartWidth, ph, Ink);
            c.Fill(ChartX, py, 18, ph, Hex(RoleColorHex(a.Role)));
            int mug = 146, mx = ChartX + ChartWidth - mug - 10, my = py + 10;
            c.Fill(mx - 3, my - 3, mug + 6, mug + 6, Vinyl);
            c.Fill(mx, my, mug, mug, Hex("#2a302c"));
            if (a.Head != null && a.HeadSize > 0) c.DrawScaled(a.Head, a.HeadSize, a.HeadSize, mx, my, mug, mug);

            int textX = ChartX + 40, textRight = mx - 24;
            string name = (a.Name ?? "Unknown").ToUpperInvariant();
            float nameScale = Math.Min(0.78f, (textRight - textX) / Math.Max(1f, display.Measure(name, 1f)));
            display.Draw(c, name, textX, py + 92, Paper, nameScale);
            string role = (a.Role ?? "townsfolk").ToUpperInvariant();
            string line2 = string.IsNullOrWhiteSpace(a.Creator) ? role : role + "  ·  BY " + a.Creator.ToUpperInvariant();
            label.Draw(c, Fit(label, line2, textRight - textX, 0.8f, 2), textX, py + 128, Muted, 0.8f, 2);
            if (!string.IsNullOrWhiteSpace(a.BasedOn))
                small.Draw(c, "Remix of " + a.BasedOn.ToUpperInvariant(), textX, py + 154, Muted, 0.8f);

            // Address under the placard: where the card lives if its data is ever lost.
            small.Draw(c, "personify.doomods.com/c/" + (a.Code ?? ""), ChartX, 1238, Vinyl, 1f);
            float tagW = small.Measure("NPC CARD", 0.9f, 2);
            small.Draw(c, "NPC CARD", ChartX + ChartWidth - (int)tagW, 1238, Muted, 0.9f, 2);
            return c.Pixels;
        }

        private static string Fit(Font f, string text, int width, float scale, int tracking)
        {
            if (f.Measure(text, scale, tracking) <= width) return text;
            while (text.Length > 1 && f.Measure(text + "...", scale, tracking) > width) text = text.Substring(0, text.Length - 1);
            return text.TrimEnd() + "...";
        }

        private static void DrawWall(Canvas c)
        {
            c.Fill(0, 0, Width, Height, Wall);
            // Cinderblock courses: 46 px high, blocks 92 px long, every other course shifted by half a block.
            for (int y = 0, row = 0; y < Height; y += 46, row++)
            {
                c.Fill(0, y, Width, 2, Mortar);
                for (int x = (row % 2) * 46; x < Width; x += 92) c.Fill(x, y, 2, 46, Mortar);
            }
        }

        private static void DrawChart(Canvas c)
        {
            c.Fill(ChartX, ChartY, ChartWidth, ChartHeight, Vinyl);
            Font display = Font.Load("display");
            float pxPerMeter = ChartHeight / ChartMeters;
            const float Inch = 0.0254f;
            // Ticks every 3 inches, longer every 6, full rule and label every foot.
            for (int inch = 3; inch * Inch < ChartMeters; inch += 3)
            {
                int y = ChartY + ChartHeight - (int)Math.Round(inch * Inch * pxPerMeter);
                if (inch % 12 == 0)
                {
                    c.Fill(ChartX, y - 2, ChartWidth, 4, Ink);
                    // The label hangs under its rule, so the top mark stays inside the chart.
                    display.Draw(c, (inch / 12) + "'", ChartX + 64, y + 42, Ink, 0.36f);
                }
                else
                {
                    int len = inch % 6 == 0 ? 56 : 28;
                    c.Fill(ChartX, y - 1, len, 2, Ink);
                    c.Fill(ChartX + ChartWidth - len, y - 1, len, 2, Ink);
                }
            }
            c.Fill(ChartX, ChartY + ChartHeight - 4, ChartWidth, 4, Ink);
        }

        private readonly struct Rgb
        {
            public readonly byte R, G, B;
            public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        }

        private static Rgb Hex(string s)
        {
            s = s.TrimStart('#');
            return new Rgb(Convert.ToByte(s.Substring(0, 2), 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16));
        }

        private sealed class Canvas
        {
            public readonly int W, H;
            public readonly byte[] Pixels;

            public Canvas(int w, int h) { W = w; H = h; Pixels = new byte[w * h * 4]; }

            public void Fill(int x, int y, int w, int h, Rgb col)
            {
                int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
                for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++)
                {
                    int i = (yy * W + xx) * 4;
                    Pixels[i] = col.R; Pixels[i + 1] = col.G; Pixels[i + 2] = col.B; Pixels[i + 3] = 255;
                }
            }

            /// <summary>Source-over blend of an RGBA image at (x, y).</summary>
            public void Blend(byte[] src, int sw, int sh, int x, int y)
            {
                for (int yy = 0; yy < sh; yy++)
                {
                    int ty = y + yy;
                    if (ty < 0 || ty >= H) continue;
                    for (int xx = 0; xx < sw; xx++)
                    {
                        int tx = x + xx;
                        if (tx < 0 || tx >= W) continue;
                        int s = (yy * sw + xx) * 4;
                        Mix((ty * W + tx) * 4, src[s], src[s + 1], src[s + 2], src[s + 3]);
                    }
                }
            }

            /// <summary>Draws an RGBA image scaled into a box (bilinear).</summary>
            public void DrawScaled(byte[] src, int sw, int sh, int x, int y, int w, int h)
            {
                for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    float fx = (xx + 0.5f) * sw / w - 0.5f, fy = (yy + 0.5f) * sh / h - 0.5f;
                    int x0 = Clamp((int)Math.Floor(fx), sw), y0 = Clamp((int)Math.Floor(fy), sh);
                    int x1 = Clamp(x0 + 1, sw), y1 = Clamp(y0 + 1, sh);
                    float ax = fx - (float)Math.Floor(fx), ay = fy - (float)Math.Floor(fy);
                    var px = new float[4];
                    for (int k = 0; k < 4; k++)
                    {
                        float top = src[(y0 * sw + x0) * 4 + k] * (1 - ax) + src[(y0 * sw + x1) * 4 + k] * ax;
                        float bot = src[(y1 * sw + x0) * 4 + k] * (1 - ax) + src[(y1 * sw + x1) * 4 + k] * ax;
                        px[k] = top * (1 - ay) + bot * ay;
                    }
                    int tx = x + xx, ty = y + yy;
                    if (tx < 0 || ty < 0 || tx >= W || ty >= H) continue;
                    Mix((ty * W + tx) * 4, (byte)px[0], (byte)px[1], (byte)px[2], (byte)px[3]);
                }
            }

            public void Mix(int i, byte r, byte g, byte b, byte a)
            {
                if (a == 0) return;
                int inv = 255 - a;
                Pixels[i] = (byte)((r * a + Pixels[i] * inv) / 255);
                Pixels[i + 1] = (byte)((g * a + Pixels[i + 1] * inv) / 255);
                Pixels[i + 2] = (byte)((b * a + Pixels[i + 2] * inv) / 255);
                Pixels[i + 3] = 255;
            }

            private static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;
        }

        private sealed class Font
        {
            private static readonly Dictionary<string, Font> Cache = new Dictionary<string, Font>();

            private int _aw;
            private byte[] _atlas;
            private readonly Dictionary<char, (int x, int y, int w, int h, int ox, int oy, float adv)> _glyphs =
                new Dictionary<char, (int, int, int, int, int, int, float)>();

            /// <summary>Loads Personify.Assets.card.&lt;name&gt;.json/.bin from this assembly.</summary>
            public static Font Load(string name)
            {
                lock (Cache)
                {
                    if (Cache.TryGetValue(name, out Font f)) return f;
                    var asm = typeof(CardArt).Assembly;
                    f = new Font();
                    JObject meta = JObject.Parse(new StreamReader(Resource(asm, name + ".json")).ReadToEnd());
                    f._aw = (int)meta["width"];
                    using (var z = new ZLibStream(Resource(asm, name + ".bin"), CompressionMode.Decompress))
                    using (var o = new MemoryStream()) { z.CopyTo(o); f._atlas = o.ToArray(); }
                    foreach (var kv in (JObject)meta["glyphs"])
                    {
                        var g = (JObject)kv.Value;
                        f._glyphs[kv.Key[0]] = ((int)g["x"], (int)g["y"], (int)g["w"], (int)g["h"], (int)g["ox"], (int)g["oy"], (float)g["adv"]);
                    }
                    Cache[name] = f;
                    return f;
                }
            }

            private static Stream Resource(Assembly asm, string file)
            {
                string res = "Personify.Assets.card." + file;
                return asm.GetManifestResourceStream(res) ?? throw new InvalidOperationException($"card font resource {res} is missing");
            }

            private bool TryGlyph(char ch, out (int x, int y, int w, int h, int ox, int oy, float adv) g) =>
                _glyphs.TryGetValue(ch, out g) || _glyphs.TryGetValue('?', out g);

            public float Measure(string text, float scale, int tracking = 0)
            {
                float w = 0;
                foreach (char ch in text) if (TryGlyph(ch, out var g)) w += g.adv * scale + tracking;
                return Math.Max(0, w - tracking);
            }

            /// <summary>Draws text with its baseline at <paramref name="baseline"/>.</summary>
            public void Draw(Canvas c, string text, int x, int baseline, Rgb col, float scale, int tracking = 0)
            {
                float pen = x;
                foreach (char ch in text)
                {
                    if (!TryGlyph(ch, out var g)) continue;
                    int dw = (int)Math.Ceiling(g.w * scale), dh = (int)Math.Ceiling(g.h * scale);
                    float gx = pen + g.ox * scale, gy = baseline + g.oy * scale;
                    for (int yy = 0; yy < dh; yy++)
                    for (int xx = 0; xx < dw; xx++)
                    {
                        // Box filter over the source texels this destination pixel covers keeps small text crisp.
                        float sx0 = xx / scale, sy0 = yy / scale, sx1 = (xx + 1) / scale, sy1 = (yy + 1) / scale;
                        int ix0 = (int)sx0, iy0 = (int)sy0, ix1 = Math.Min(g.w, (int)Math.Ceiling(sx1)), iy1 = Math.Min(g.h, (int)Math.Ceiling(sy1));
                        int sum = 0, n = 0;
                        for (int sy = iy0; sy < iy1; sy++)
                        for (int sx = ix0; sx < ix1; sx++) { sum += _atlas[(g.y + sy) * _aw + g.x + sx]; n++; }
                        if (n == 0 || sum == 0) continue;
                        int tx = (int)Math.Round(gx) + xx, ty = (int)Math.Round(gy) + yy;
                        if (tx < 0 || ty < 0 || tx >= c.W || ty >= c.H) continue;
                        c.Mix((ty * c.W + tx) * 4, col.R, col.G, col.B, (byte)(sum / n));
                    }
                    pen += g.adv * scale + tracking;
                }
            }
        }
    }
}
