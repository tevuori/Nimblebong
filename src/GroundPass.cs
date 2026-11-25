// Ground pass — port of the web `planGround`/`paintGround` (scene-ground.ts).
// A low-res fbm field texture blends base/lush/dry tones into large
// watercolor patches (the mottled Nimblehold turf), then a seeded plan of
// features (dirt patches, grass tufts, pebbles, flowers, leaves, twigs,
// puddles, cracks, snow) is drawn on top. Runs beneath massifs/water —
// exactly like the web, so terrain, strokes and props occlude detail.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public static class GroundPass
{
    // --- seeded randomness (port of scene-ground.ts hash2/valueNoise) ----

    static double Hash2(int x, int y, int s)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263 + s;
            h = (h ^ (int)((uint)h >> 13)) * 1274126177;
            h ^= (int)((uint)h >> 16);
            return (double)((uint)h) / 4294967296.0;
        }
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    static double ValueNoise(double x, double y, int s)
    {
        var xi = (int)Math.Floor(x);
        var yi = (int)Math.Floor(y);
        var fx = Smooth(x - xi);
        var fy = Smooth(y - yi);
        var a = Hash2(xi, yi, s);
        var b = Hash2(xi + 1, yi, s);
        var c = Hash2(xi, yi + 1, s);
        var d = Hash2(xi + 1, yi + 1, s);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    static double Fbm(double x, double y, int s)
    {
        double sum = 0, amp = 1, norm = 0;
        var f = 1.0;
        for (var o = 0; o < 4; o++)
        {
            sum += ValueNoise(x * f, y * f, s + o * 101) * amp;
            norm += amp;
            amp *= 0.5;
            f *= 2.03;
        }
        return sum / norm;
    }

    static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// <summary>Large-scale fields in [0,1]: lush / dry / light.</summary>
    sealed class Fields
    {
        readonly int _seed;
        public Fields(int seed) => _seed = seed;
        public double Lush(double x, double y) => Fbm(x / 260, y / 260, _seed + 11);
        public double Dry(double x, double y) => Fbm(x / 340 + 7.3, y / 340 + 2.1, _seed + 23);
        public double Light(double x, double y) => Fbm(x / 120, y / 120, _seed + 37);
    }

    /// <summary>Deterministic rand stream — same contract as the web's
    /// seededRandom: a counter hashed into [0,1).</summary>
    sealed class Rand
    {
        int _i;
        readonly int _s;
        public Rand(int seed) => _s = seed;
        public double Next() => Hash2(_i++, 0x5eED, _s);
        public double Range(double a, double b) => a + Next() * (b - a);
        public int Cluster(double a, double b) =>
            (int)a + (int)Math.Floor(Next() * (b - a + 1));
    }

    // --- palette ------------------------------------------------------------

    public class Pal
    {
        public Color Base, Lush, Dry, Dirt, DirtDark;
        public Color GrassDark, GrassLight;
        public Color Stone, StoneDark, StoneLight;
        public Color Snow, Puddle, Crack, Twig, Ember;
        public List<Color> Flowers = [];
        public List<Color> Leaves = [];
    }

    /// <summary>Season-graded accent — identity for classic/summer, the
    /// same mixes the web applies otherwise.</summary>
    static Color Grade(Color c, string pack, string look)
    {
        if (pack == "storybook")
            c = Palette.Mix(Palette.Desaturate(c, 0.85), Palette.Hex("#f4e8d2"), 0.22);
        if (pack == "ink")
            c = Palette.Mix(Palette.Desaturate(c, 0.6), Palette.Hex("#8c8478"), 0.15);
        if (look == "winter")
            c = Palette.Mix(Palette.Desaturate(c, 0.75), Palette.Hex("#b8c6cc"), 0.15);
        else if (look == "autumn")
            c = Palette.Mix(c, Palette.Hex("#c9963e"), 0.12);
        return c;
    }

    public static Pal BuildPalette(ResolvedBiome b, string look)
    {
        Color G(string hex) => Grade(Palette.Hex(hex), "", look);
        var dirt = Palette.Mix(b.Ground, G(b.At("ground.dirt").ToHtml()), 0.7);
        var grass = Palette.Mix(b.Ground, b.Foliage, 0.55);
        if (b.Named.TryGetValue("ground.dryGrass", out var dg))
            grass = Palette.Mix(grass, G(dg.ToHtml()), 0.55);
        var pal = new Pal
        {
            Base = b.Ground,
            Lush = Palette.Mix(b.Ground, b.Foliage, 0.55),
            Dry = Palette.Mix(b.Ground, dirt, 0.55),
            Dirt = dirt,
            DirtDark = Palette.Mix(dirt, Colors.Black, 0.25),
            GrassDark = Palette.Mix(grass, Colors.Black, 0.14),
            GrassLight = Palette.Mix(Palette.Mix(b.Ground, grass, 0.5), Colors.White, 0.16),
            Stone = b.Stone,
            StoneDark = Palette.Mix(b.Stone, Colors.Black, 0.32),
            StoneLight = Palette.Mix(b.Stone, Colors.White, 0.3),
            Snow = G("#f4f7f8"),
            Puddle = Palette.Mix(b.Water, b.Ground, 0.3),
            Crack = Palette.Mix(b.Ground, Colors.Black, 0.42),
            Ember = G("#ff7a2a"),
            Twig = Palette.Mix(b.Wood, Colors.Black, 0.2),
        };
        foreach (var c in b.GroundCfg.FlowerColors)
            pal.Flowers.Add(Grade(c, "", look));
        foreach (var hex in new[] { "#c8642a", "#d9a13b", "#a8432a", "#8a5a2b" })
            pal.Leaves.Add(G(hex));
        return pal;
    }

    // --- field texture ------------------------------------------------------

    /// <summary>Low-res fbm field image — full-resolution equivalent of
    /// the web's cell-upscaled canvas: base blended toward lush/dry with
    /// a light wash, so the turf reads as watercolor blotches.</summary>
    public static ImageTexture FieldTexture(
        Vector2 mapSize,
        Pal pal,
        int seed,
        double wash = 0.85,
        int cell = 8
    )
    {
        var fields = new Fields(seed);
        var w = (int)Math.Ceiling(mapSize.X / cell) + 1;
        var h = (int)Math.Ceiling(mapSize.Y / cell) + 1;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        var b = pal.Base;
        var l = pal.Lush;
        var d = pal.Dry;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var wx = x * cell;
            var wy = y * cell;
            var wl = Clamp01((fields.Lush(wx, wy) - 0.4) * 3.2);
            var wd = Clamp01((fields.Dry(wx, wy) - 0.46) * 3.0);
            var lt = (fields.Light(wx, wy) - 0.5) * 46 * wash / 255.0;
            img.SetPixel(
                x,
                y,
                new Color(
                    (float)(b.R + (l.R - b.R) * wl + (d.R - b.R) * wd + lt),
                    (float)(b.G + (l.G - b.G) * wl + (d.G - b.G) * wd + lt),
                    (float)(b.B + (l.B - b.B) * wl + (d.B - b.B) * wd + lt)
                )
            );
        }
        return ImageTexture.CreateFromImage(img);
    }

    // --- features -----------------------------------------------------------

    static Vector2[] Blob(Rand rand, double x, double y, double r, double squash = 0.7)
    {
        var n = 10 + (int)(rand.Next() * 6);
        var rot = rand.Next() * Math.PI;
        var pts = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = i / (double)n * Math.PI * 2;
            var rr = r * (0.72 + rand.Next() * 0.42);
            var lx = Math.Cos(a) * rr;
            var ly = Math.Sin(a) * rr * squash;
            pts[i] = new Vector2(
                (float)(x + lx * Math.Cos(rot) - ly * Math.Sin(rot)),
                (float)(y + lx * Math.Sin(rot) + ly * Math.Cos(rot))
            );
        }
        return pts;
    }

    static Vector2[] Jagged(Rand rand, double x, double y, double len)
    {
        var a = rand.Next() * Math.PI * 2;
        var segs = 4 + (int)(rand.Next() * 4);
        var pts = new List<Vector2> { new((float)x, (float)y) };
        for (var i = 0; i < segs; i++)
        {
            a += (rand.Next() - 0.5) * 1.2;
            var last = pts[^1];
            pts.Add(
                last + new Vector2(
                    (float)(Math.Cos(a) * len / segs),
                    (float)(Math.Sin(a) * len / segs)
                )
            );
        }
        return [.. pts];
    }

    /// <summary>Draw the planned feature scatter — call inside _Draw after
    /// the base rect + field texture, before massifs.</summary>
    public static void Features(
        Node2D n,
        Vector2 mapSize,
        ResolvedBiome biome,
        Pal pal,
        int seed = 7
    )
    {
        var fields = new Fields(seed);
        var rand = new Rand(seed * 7919 + biome.Id.Length * 131 + 17);
        var bt = biome.GroundCfg;
        var st = ResolvedBiome.SeasonScatter.TryGetValue(biome.SeasonLook, out var s)
            ? s
            : ResolvedBiome.SeasonScatter["summer"];
        var area = (double)mapSize.X * mapSize.Y;
        var w = mapSize.X;
        var h = mapSize.Y;

        double Count(double per) => area / per;
        Vector2? Place(Func<double, double, double> weight)
        {
            for (var i = 0; i < 8; i++)
            {
                var x = rand.Next() * w;
                var y = rand.Next() * h;
                if (rand.Next() < weight(x, y))
                    return new Vector2((float)x, (float)y);
            }
            return null;
        }
        void Fill(Vector2[] pts, Color c)
        {
            if (pts.Length >= 3 && Geometry2D.TriangulatePolygon(pts).Length > 0)
                n.DrawColoredPolygon(pts, c);
        }
        void FeatherPatch(Vector2[] pts, Color color, double alpha)
        {
            // Shrinking copies around the centroid — soft edge like the
            // web's 4-pass alpha stack.
            var c = Poly.Centroid(pts);
            foreach (var k in new[] { 1.12, 1.0, 0.86, 0.7 })
            {
                var scaled = new Vector2[pts.Length];
                for (var i = 0; i < pts.Length; i++)
                    scaled[i] = c + (pts[i] - c) * (float)k;
                Fill(scaled, new Color(color.R, color.G, color.B, (float)(alpha / 3)));
            }
        }
        void Pebble(Vector2[] pts)
        {
            var c = Poly.Centroid(pts);
            var r = (float)(pts[0] - c).Length();
            var shifted = new Vector2[pts.Length];
            for (var i = 0; i < pts.Length; i++)
                shifted[i] = pts[i] + new Vector2(1.2f, 1.4f);
            Fill(shifted, new Color(0, 0, 0, 0.35f));
            Fill(pts, pal.Stone);
            n.DrawPolyline(
                pts,
                new Color(pal.StoneDark.R, pal.StoneDark.G, pal.StoneDark.B, 0.5f),
                0.7f,
                true
            );
            n.DrawCircle(
                c - new Vector2(r * 0.3f, r * 0.3f),
                Math.Max(0.6f, r * 0.3f),
                new Color(pal.StoneLight.R, pal.StoneLight.G, pal.StoneLight.B, 0.6f)
            );
        }

        // Dirt patches, favouring the dry field.
        var patchEvery = 38000.0 / Math.Max(bt.Patches, 1e-6);
        for (var i = (int)Count(patchEvery); i > 0; i--)
        {
            var p = Place((x, y) => Clamp01((fields.Dry(x, y) - 0.38) * 3));
            if (p is not { } pp)
                continue;
            var r = rand.Range(18, 64);
            FeatherPatch(Blob(rand, pp.X, pp.Y, r), pal.Dirt, 0.22);
            FeatherPatch(Blob(rand, pp.X, pp.Y, r * 0.72), pal.Dirt, 0.3);
            FeatherPatch(
                Blob(rand, pp.X + rand.Range(-4, 4), pp.Y + rand.Range(-4, 4), r * 0.42),
                pal.DirtDark,
                0.18
            );
            for (var s2 = (int)(r * 0.5); s2 > 0; s2--)
            {
                var a = rand.Next() * Math.PI * 2;
                var dd = Math.Sqrt(rand.Next()) * r * 0.8;
                n.DrawCircle(
                    new Vector2(
                        (float)(pp.X + Math.Cos(a) * dd),
                        (float)(pp.Y + Math.Sin(a) * dd * 0.7)
                    ),
                    (float)rand.Range(0.5, 1.4),
                    new Color(
                        rand.Next() < 0.5 ? pal.DirtDark : Palette.Mix(pal.Dirt, Colors.White, 0.2),
                        0.55f
                    )
                );
            }
            for (var s2 = (int)(rand.Next() * 4); s2 > 0; s2--)
            {
                var a = rand.Next() * Math.PI * 2;
                Pebble(
                    Blob(
                        rand,
                        pp.X + Math.Cos(a) * r * 0.85,
                        pp.Y + Math.Sin(a) * r * 0.6,
                        rand.Range(1.5, 3.2)
                    )
                );
            }
        }

        // Extras that sit flat on the ground.
        if (bt.Extra == "ripples")
        {
            var wind = rand.Range(-0.5, -0.2);
            for (var i = (int)Count(6500); i > 0; i--)
            {
                var x = rand.Next() * w;
                var y = rand.Next() * h;
                var len = rand.Range(18, 52);
                var lines = 2 + (int)(rand.Next() * 3);
                for (var k = 0; k < lines; k++)
                {
                    var ox = x - Math.Sin(wind) * k * 5;
                    var oy = y + Math.Cos(wind) * k * 5;
                    var pts = new Vector2[7];
                    for (var s2 = 0; s2 <= 6; s2++)
                    {
                        var u = (s2 / 6.0 - 0.5) * len * (1 - k * 0.15);
                        var bow = Math.Sin(s2 / 6.0 * Math.PI) * 2.5;
                        pts[s2] = new Vector2(
                            (float)(ox + Math.Cos(wind) * u - Math.Sin(wind) * bow),
                            (float)(oy + Math.Sin(wind) * u + Math.Cos(wind) * bow)
                        );
                    }
                    n.DrawPolyline(
                        pts,
                        k % 2 == 1 ? Palette.Mix(pal.Base, Colors.White, 0.25) : pal.Dirt,
                        0.9f,
                        true
                    );
                }
            }
        }
        if (bt.Extra is "cracks" or "crystals")
        {
            var every = bt.Extra == "cracks" ? 16000 : 30000;
            for (var i = (int)Count(every); i > 0; i--)
            {
                var x = rand.Next() * w;
                var y = rand.Next() * h;
                var main = Jagged(rand, x, y, rand.Range(14, 44));
                var glow = bt.Extra == "cracks" && rand.Next() < 0.25 ? (Color?)pal.Ember : null;
                n.DrawPolyline(main, pal.Crack, 1.2f, true);
                if (glow is { } gl)
                    n.DrawPolyline(main, new Color(gl, 0.4f), 2.6f, true);
                var mid = main[main.Length / 2];
                n.DrawPolyline(Jagged(rand, mid.X, mid.Y, rand.Range(6, 16)), pal.Crack, 0.8f, true);
            }
        }
        if (bt.Extra == "twigs")
            for (var i = (int)Count(28000); i > 0; i--)
            {
                var x = rand.Next() * w;
                var y = rand.Next() * h;
                var a = rand.Next() * Math.PI;
                var len = rand.Range(6, 14);
                var p0 = new Vector2((float)x, (float)y);
                var p1 = p0 + new Vector2((float)(Math.Cos(a) * len), (float)(Math.Sin(a) * len));
                var m = p0 + (p1 - p0) * 0.5f;
                n.DrawLine(p0, p1, pal.Twig, 1.1f, true);
                n.DrawLine(
                    m,
                    m + new Vector2((float)(Math.Cos(a - 0.7) * 4), (float)(Math.Sin(a - 0.7) * 4)),
                    pal.Twig,
                    0.8f,
                    true
                );
            }

        // Snow drifts.
        var snowRate = (bt.Extra == "snow" ? 0.6 : 0) + st.Snow * bt.Snow;
        if (snowRate > 0)
            for (var i = (int)Count(70000 / snowRate); i > 0; i--)
            {
                var p = Place((x, y) => Clamp01((fields.Light(x, y) - 0.3) * 2.5));
                if (p is not { } pp)
                    continue;
                var r = rand.Range(20, 70);
                FeatherPatch(Blob(rand, pp.X, pp.Y, r), pal.Snow, 0.35);
                FeatherPatch(Blob(rand, pp.X, pp.Y, r * 0.65), pal.Snow, 0.5);
            }

        // Grass tufts, densest in the lush field.
        var grassRate = bt.Grass * st.Grass;
        if (grassRate > 0)
            for (var i = (int)Count(650 / grassRate); i > 0; i--)
            {
                var p = Place((x, y) => Clamp01((fields.Lush(x, y) - 0.25) * 2.2));
                if (p is not { } pp)
                    continue;
                var blades = rand.Cluster(3, 7);
                var len = rand.Range(4, 9) * (bt.Extra == "puddles" ? 1.5 : 1);
                var col = rand.Next() < 0.65 ? pal.GrassDark : pal.GrassLight;
                col.A = 0.75f;
                for (var bIdx = 0; bIdx < blades; bIdx++)
                {
                    var a =
                        -Math.PI / 2 + (bIdx / (double)Math.Max(1, blades - 1) - 0.5) * 1.5
                        + rand.Range(-0.2, 0.2);
                    var bx = Math.Cos(a) * len * rand.Range(0.6, 1);
                    var by = Math.Sin(a) * len * rand.Range(0.7, 1.1);
                    // Quadratic blade — three-point bent stroke.
                    n.DrawPolyline(
                        [
                            pp,
                            pp + new Vector2((float)(bx * 0.2), (float)(by * 0.6)),
                            pp + new Vector2((float)bx, (float)by),
                        ],
                        col,
                        1.1f,
                        true
                    );
                }
            }

        // Pebble clusters with the occasional boulder.
        var stoneEvery = 9000.0 / Math.Max(bt.Stones, 1e-6);
        for (var i = (int)Count(stoneEvery); i > 0; i--)
        {
            var x = rand.Next() * w;
            var y = rand.Next() * h;
            if (rand.Next() < 0.08)
            {
                Pebble(Blob(rand, x, y, rand.Range(6, 10)));
                continue;
            }
            for (var k = rand.Cluster(1, 4); k > 0; k--)
                Pebble(
                    Blob(
                        rand,
                        x + rand.Range(-7, 7),
                        y + rand.Range(-5, 5),
                        rand.Range(1.4, 4)
                    )
                );
        }

        // Crystals extra (cavern).
        if (bt.Extra == "crystals")
            for (var i = (int)Count(26000); i > 0; i--)
            {
                var x = rand.Next() * w;
                var y = rand.Next() * h;
                for (var k = rand.Cluster(2, 4); k > 0; k--)
                {
                    var cx = x + rand.Range(-5, 5);
                    var cy = y + rand.Range(-3, 3);
                    var sz = rand.Range(3, 7);
                    var ang = rand.Range(-0.6, 0.6);
                    var cos = Math.Cos(ang);
                    var sin = Math.Sin(ang);
                    n.DrawPolyline(
                        [
                            new Vector2((float)(cx - cos * sz), (float)(cy - sin * sz)),
                            new Vector2((float)cx, (float)(cy - sz * 1.6)),
                            new Vector2((float)(cx + cos * sz), (float)(cy + sin * sz)),
                            new Vector2((float)cx, (float)(cy + sz * 0.5)),
                        ],
                        pal.GrassLight,
                        1.4f,
                        true
                    );
                }
            }

        // Flower meadows — one dominant color per cluster, in lush
        // non-dry spots.
        var flowerRate = bt.Flowers * st.Flowers;
        if (flowerRate > 0 && pal.Flowers.Count > 0)
            for (var i = (int)Count(16000 / flowerRate); i > 0; i--)
            {
                var p = Place((x, y) =>
                    Clamp01((fields.Lush(x, y) - 0.4) * 3)
                    * Clamp01((0.62 - fields.Dry(x, y)) * 4)
                );
                if (p is not { } pp)
                    continue;
                var main = pal.Flowers[(int)(rand.Next() * pal.Flowers.Count)];
                var radius = rand.Range(5, 20);
                for (var k = rand.Cluster(4, 14); k > 0; k--)
                {
                    var a = rand.Next() * Math.PI * 2;
                    var dd = Math.Sqrt(rand.Next()) * radius;
                    n.DrawCircle(
                        new Vector2(
                            (float)(pp.X + Math.Cos(a) * dd),
                            (float)(pp.Y + Math.Sin(a) * dd * 0.75)
                        ),
                        (float)rand.Range(1, 2),
                        rand.Next() < 0.85
                            ? main
                            : pal.Flowers[(int)(rand.Next() * pal.Flowers.Count)]
                    );
                }
            }

        // Fallen leaves (autumn).
        var leafRate = bt.Leaves * st.Leaves;
        if (leafRate > 0 && pal.Leaves.Count > 0)
            for (var i = (int)Count(2600 / leafRate); i > 0; i--)
            {
                var x = rand.Next() * w;
                var y = rand.Next() * h;
                for (var k = rand.Cluster(1, 6); k > 0; k--)
                {
                    var lx = x + rand.Range(-9, 9);
                    var ly = y + rand.Range(-7, 7);
                    var a = rand.Next() * Math.PI;
                    var sz = rand.Range(1.6, 3);
                    n.DrawPolyline(
                        [
                            new Vector2((float)(lx - Math.Cos(a) * sz), (float)(ly - Math.Sin(a) * sz)),
                            new Vector2((float)(lx + Math.Cos(a) * sz), (float)(ly + Math.Sin(a) * sz)),
                        ],
                        pal.Leaves[(int)(rand.Next() * pal.Leaves.Count)],
                        1.4f,
                        true
                    );
                }
            }
    }
}
