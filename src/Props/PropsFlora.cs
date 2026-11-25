#nullable enable
// Flora drawer — small top-down plants in the watercolor+ink style.
// One parametric drawer, seven forms; every plant species is a JSON
// prop definition with "base": "flora" and a "flora" block (see
// assets/config/props/README.md). All variation is seeded from the
// prop (Jitter on VarSeed), so a plant redraws identically.
//
// Prop-local frame: 24 px scene scale = 1.0, local radius ~8–12 px.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    static readonly FloraSpec DefaultFlora = new();

    void DrawFlora(PropPlacement p)
    {
        var spec = p.Def?.Flora ?? DefaultFlora;
        var rand = new Jitter(p.VarSeed + 0.5);
        // Leaf color with a per-plant nudge so a meadow isn't one flat tone.
        var leafBase = PropDefs.ResolveColor(spec.Leaf, BiomeCfg);
        var leaf = Palette.Mix(
            leafBase,
            rand.Next() < 0.5 ? Palette.Tint(leafBase, 22) : Palette.Tint(leafBase, -18),
            rand.Next() * 0.5
        );
        var dark = spec.LeafDark != null
            ? PropDefs.ResolveColor(spec.LeafDark, BiomeCfg)
            : Palette.Mix(leaf, Ink, 0.38);
        var accents = spec.Accents.Count > 0
            ? spec.Accents.ConvertAll(a => PropDefs.ResolveColor(a, BiomeCfg))
            : [Palette.Hex("#f2d14b")];
        var ink = Ink with { A = (float)(0.25 + 0.6 * spec.Ink) };
        var count = spec.CountMin + (int)(rand.Next() * (spec.CountMax - spec.CountMin + 1));
        count = Math.Max(1, count);
        var f = new FloraCtx(p, spec, rand, leaf, dark, accents, ink, count);

        switch (spec.Form)
        {
            case "fern": FloraFern(f); break;
            case "flowers": FloraFlowers(f); break;
            case "flowerpatch": FloraFlowerPatch(f); break;
            case "reeds": FloraReeds(f); break;
            case "mushrooms": FloraMushrooms(f); break;
            case "lilypads": FloraLilypads(f); break;
            case "groundcover": FloraGroundcover(f); break;
            default: FloraTuft(f); break;
        }
    }

    sealed record FloraCtx(
        PropPlacement P, FloraSpec S, Jitter Rand, Color Leaf, Color Dark,
        List<Color> Accents, Color Ink, int Count
    )
    {
        // Field, not the positional property — a property getter would
        // hand out copies of the Jitter struct and repeat its sequence.
        public Jitter R = Rand;

        public Color Accent() => Accents[(int)(R.Next() * Accents.Count) % Accents.Count];
        public double Len() => R.Range(S.LengthMin, S.LengthMax);
    }

    float W(FloraCtx f, double w) => (float)Math.Max(0.5, w * f.P.Scale);

    Vector2 Polar(double a, double r) =>
        new((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));

    /// <summary>Soft dark contact patch under a clump — grounds it.</summary>
    void FloraShade(FloraCtx f, double r, double alpha = 0.28) =>
        Fill(Xform(f.P, Poly.Ellipse(0.6, 0.8, r, r * 0.8, 0, 10)), f.Dark with { A = (float)alpha });

    // --- tuft: grass blades radiating from a tight base ----------------------
    void FloraTuft(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.45);
        for (var i = 0; i < f.Count; i++)
        {
            var a = (i + f.R.Next() * 0.8) / f.Count * Math.PI * 2;
            var baseR = f.R.Range(0, f.S.Radius * 0.25);
            var len = f.Len();
            var bend = f.R.Range(-0.45, 0.45);
            var b = Polar(a, baseR);
            var m = b + Polar(a + bend * 0.5, len * 0.55);
            var t = m + Polar(a + bend, len * 0.45);
            var col = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.45, 1));
            DrawPolyline(Xform(f.P, [b, m, t]), col, W(f, f.S.Width), true);
            if (f.S.Accents.Count > 0 && f.R.Next() < f.S.AccentChance * 0.5)
                DrawCircle(Xform(f.P, t), W(f, f.S.AccentSize * 0.6), f.Accent());
        }
    }

    // --- fern: rosette of fronds with alternating leaflets -------------------
    void FloraFern(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.6);
        var spin = f.R.Next() * Math.PI * 2;
        for (var i = 0; i < f.Count; i++)
        {
            var a = spin + (i + f.R.Range(-0.2, 0.2)) / f.Count * Math.PI * 2;
            var len = f.Len();
            var curl = f.R.Range(-0.35, 0.35);
            const int segs = 5;
            var rib = new Vector2[segs + 1];
            for (var k = 0; k <= segs; k++)
            {
                var t = k / (double)segs;
                rib[k] = Polar(a + curl * t * t, len * t);
            }
            var col = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.55, 1));
            for (var k = 1; k < segs; k++)
            {
                var t = k / (double)segs;
                var dir = rib[k + 1] - rib[k - 1];
                var ang = Math.Atan2(dir.Y, dir.X);
                var size = len * 0.27 * (1 - t * 0.65);
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    var c = rib[k] + Polar(ang + side * 1.1, size * 0.8);
                    Fill(Xform(f.P, Poly.Ellipse(c.X, c.Y, size, size * 0.5, ang + side * 0.9, 7)), col);
                }
            }
            DrawPolyline(Xform(f.P, rib), f.Dark, W(f, f.S.Width * 0.7), true);
        }
    }

    // --- flowers: leaf rosette + petal heads ---------------------------------
    void FloraFlowers(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.55, 0.22);
        var leaves = Math.Max(3, f.Count);
        for (var i = 0; i < leaves; i++)
        {
            var a = (i + f.R.Next() * 0.6) / leaves * Math.PI * 2;
            var c = Polar(a, f.S.Radius * f.R.Range(0.25, 0.55));
            var col = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.5, 1));
            Fill(Xform(f.P, Poly.Ellipse(c.X, c.Y, f.S.Radius * 0.42, f.S.Radius * 0.2, a, 7)), col);
        }
        var heads = Math.Max(1, (int)Math.Round(f.Count * f.S.AccentChance));
        for (var i = 0; i < heads; i++)
        {
            var c = Polar(f.R.Next() * Math.PI * 2, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.75);
            FlowerHead(f, c, PickHead(f));
        }
    }

    // --- reeds: stems leaning with the wind, some with cattail heads --------
    void FloraReeds(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.6, 0.22);
        var wind = f.R.Range(-0.6, 0.6) - Math.PI / 2 * 0.3;
        for (var i = 0; i < f.Count; i++)
        {
            var b = Polar(f.R.Next() * Math.PI * 2, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.6);
            var a = wind + f.R.Range(-0.35, 0.35);
            var len = f.Len();
            var t = b + Polar(a, len);
            var col = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.4, 1));
            DrawLine(Xform(f.P, b), Xform(f.P, t), col, W(f, f.S.Width), true);
            if (f.S.Accents.Count > 0 && f.R.Next() < f.S.AccentChance)
            {
                var h = b + Polar(a, len * 0.72);
                var head = Poly.Ellipse(h.X, h.Y, f.S.AccentSize * 1.4, f.S.AccentSize * 0.6, a, 8);
                Fill(Xform(f.P, head), f.Accent());
                Stroke(Xform(f.P, head), f.Ink, W(f, 0.5));
            }
        }
    }

    // --- mushrooms: a cluster of round caps with spots and ink rims ---------
    void FloraMushrooms(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.5, 0.2);
        var caps = new List<(Vector2 C, double R)>();
        for (var i = 0; i < f.Count; i++)
        {
            var c = Polar(f.R.Next() * Math.PI * 2, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.6);
            caps.Add((c, f.S.AccentSize * f.R.Range(0.7, 1.4)));
        }
        caps.Sort((x, y) => x.C.Y.CompareTo(y.C.Y));
        foreach (var (c, r) in caps)
        {
            var cap = f.Accent();
            var poly = Poly.Ellipse(c.X, c.Y, r, r * 0.9, 0, 10);
            Fill(Xform(f.P, poly), cap);
            // Lit crown toward the sun side (north-west), spots on top.
            DrawCircle(Xform(f.P, c + new Vector2((float)(-r * 0.25), (float)(-r * 0.25))),
                W(f, r * 0.5), Palette.Tint(cap, 30) with { A = 0.55f });
            if (f.R.Next() < f.S.AccentChance)
                for (var k = 0; k < 3; k++)
                    DrawCircle(Xform(f.P, c + Polar(f.R.Next() * Math.PI * 2, r * 0.5)),
                        W(f, r * 0.14), Palette.Hex("#f6f1e4"));
            Stroke(Xform(f.P, poly), f.Ink, W(f, 0.6));
        }
    }

    // --- lilypads: notched discs with veins, the odd blossom ----------------
    void FloraLilypads(FloraCtx f)
    {
        for (var i = 0; i < f.Count; i++)
        {
            var c = Polar(f.R.Next() * Math.PI * 2, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.7);
            var r = f.Len() * 0.5;
            var notch = f.R.Next() * Math.PI * 2;
            const int n = 14;
            var pad = new List<Vector2> { c };
            for (var k = 0; k <= n; k++)
            {
                var a = notch + 0.35 + k / (double)n * (Math.PI * 2 - 0.7);
                pad.Add(c + Polar(a, r));
            }
            var poly = pad.ToArray();
            var col = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.55, 1));
            Fill(Xform(f.P, poly), col);
            for (var k = 0; k < 4; k++)
                DrawLine(Xform(f.P, c), Xform(f.P, c + Polar(notch + 0.9 + k * 1.3, r * 0.8)),
                    f.Dark with { A = 0.5f }, W(f, 0.4), true);
            Stroke(Xform(f.P, poly), f.Ink, W(f, 0.5));
            if (f.S.Accents.Count > 0 && f.R.Next() < f.S.AccentChance)
            {
                var petal = f.Accent();
                var turn = f.R.Next() * Math.PI;
                for (var k = 0; k < 6; k++)
                {
                    var pc = c + Polar(turn + k / 6.0 * Math.PI * 2, f.S.AccentSize * 0.7);
                    Fill(Xform(f.P, Poly.Ellipse(pc.X, pc.Y, f.S.AccentSize * 0.7, f.S.AccentSize * 0.35,
                        turn + k / 6.0 * Math.PI * 2, 6)), petal);
                }
                DrawCircle(Xform(f.P, c), W(f, f.S.AccentSize * 0.35), Palette.Hex("#f2c94c"));
            }
        }
    }

    // --- groundcover: low overlapping blobs + tiny blossoms (clover, heather)
    void FloraGroundcover(FloraCtx f)
    {
        for (var i = 0; i < f.Count; i++)
        {
            var c = Polar(f.R.Next() * Math.PI * 2, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.55);
            var r = f.Len() * 0.5;
            var blob = new Vector2[9];
            for (var k = 0; k < blob.Length; k++)
            {
                var a = k / (double)blob.Length * Math.PI * 2;
                blob[k] = c + Polar(a, r * f.R.Range(0.75, 1.15));
            }
            var col = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.5, 1)) with { A = 0.9f };
            Fill(Xform(f.P, blob), col);
        }
        if (f.S.Accents.Count == 0)
            return;
        var dots = (int)Math.Round(f.Count * 2 * f.S.AccentChance);
        for (var i = 0; i < dots; i++)
        {
            var c = Polar(f.R.Next() * Math.PI * 2, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.7);
            DrawCircle(Xform(f.P, c), W(f, f.S.AccentSize * f.R.Range(0.6, 1)), f.Accent());
        }
    }
}
