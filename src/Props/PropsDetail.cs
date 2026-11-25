#nullable enable
// Detail drawer — map dressing and tactical cover in the watercolor+ink
// style: fallen logs, stumps, brambles, rubble, cairns, signposts,
// shrines, tracks, puddles, carcasses, anthills, beehives, stalagmites,
// spider webs. Same parametric spec as flora (FloraSpec): "leaf" is the
// main material color (bark, stone, mud), "leafDark" its shade, "accents"
// the extras (moss, berries, candles, bees). A new variant is JSON only.
//
// Prop-local frame: 24 px scene scale = 1.0.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    void DrawDetail(PropPlacement p)
    {
        var f = MakeFloraCtx(p, p.Def?.Flora ?? DefaultFlora);
        switch (f.S.Form)
        {
            case "log": DetailLog(f); break;
            case "stump": DetailStump(f); break;
            case "bramble": DetailBramble(f); break;
            case "rubble": DetailRubble(f); break;
            case "cairn": DetailCairn(f); break;
            case "signpost": DetailSignpost(f); break;
            case "shrine": DetailShrine(f); break;
            case "tracks": DetailTracks(f); break;
            case "puddle": DetailPuddle(f); break;
            case "carcass": DetailCarcass(f); break;
            case "anthill": DetailAnthill(f); break;
            case "beehive": DetailBeehive(f); break;
            case "stalagmite": DetailStalagmite(f); break;
            case "web": DetailWeb(f); break;
            default: DetailRubble(f); break;
        }
    }

    Vector2[] LocalBlob(FloraCtx f, Vector2 c, double r, double ry, double jitter, int n = 10, double rot = 0)
    {
        var pts = new Vector2[n];
        for (var k = 0; k < n; k++)
        {
            var a = k / (double)n * Math.Tau;
            var rr = 1 - jitter + f.R.Next() * jitter * 2;
            var v = new Vector2((float)(Math.Cos(a) * r * rr), (float)(Math.Sin(a) * ry * rr));
            pts[k] = c + v.Rotated((float)rot);
        }
        return pts;
    }

    void InkFill(FloraCtx f, Vector2[] local, Color col, double ink = 0.7)
    {
        var poly = Xform(f.P, local);
        Fill(poly, col);
        if (f.S.Ink > 0)
            Stroke(poly, f.Ink, W(f, ink));
    }

    // --- fallen log: capsule trunk, bark grain, ring-cut end, moss -------
    void DetailLog(FloraCtx f)
    {
        var len = f.Len();
        var half = f.S.Width * 0.5 * f.R.Range(0.9, 1.1);
        Fill(Xform(f.P, Poly.Ellipse(0.8, 1.2, len * 0.55, half * 1.35, 0, 14)), f.Dark with { A = 0.3f });
        // Capsule: two half circles joined.
        var pts = new List<Vector2>();
        for (var k = 0; k <= 8; k++)
        {
            var a = -Math.PI / 2 + k / 8.0 * Math.PI;
            pts.Add(new Vector2((float)(len / 2 + Math.Cos(a) * half * 0.6), (float)(Math.Sin(a) * half)));
        }
        for (var k = 0; k <= 8; k++)
        {
            var a = Math.PI / 2 + k / 8.0 * Math.PI;
            pts.Add(new Vector2((float)(-len / 2 + Math.Cos(a) * half * 0.6), (float)(Math.Sin(a) * half * f.R.Range(0.95, 1.05))));
        }
        InkFill(f, [.. pts], f.Leaf, 0.9);
        // Lit upper side, bark grain along the length.
        Fill(Xform(f.P, Poly.Ellipse(0, -half * 0.35, len * 0.45, half * 0.3, 0, 10)), Palette.Tint(f.Leaf, 18) with { A = 0.6f });
        for (var k = 0; k < 4; k++)
        {
            var y = (k / 3.0 - 0.5) * half * 1.3;
            var x0 = -len / 2 + f.R.Range(0, len * 0.3);
            var x1 = x0 + f.R.Range(len * 0.3, len * 0.7);
            DrawLine(Xform(f.P, new Vector2((float)x0, (float)y)), Xform(f.P, new Vector2((float)x1, (float)y)),
                f.Dark with { A = 0.7f }, W(f, 0.5), true);
        }
        // Ring-cut end.
        var end = new Vector2((float)(len / 2), 0);
        var cut = Poly.Ellipse(end.X, end.Y, half * 0.55, half * 0.9, 0, 10);
        InkFill(f, cut, Palette.Mix(f.Leaf, Palette.Hex("#d9b98a"), 0.6), 0.6);
        DrawArc(Xform(f.P, end), W(f, half * 0.4), 0, Mathf.Tau, 12, f.Dark with { A = 0.5f }, W(f, 0.35), true);
        // Broken branch stub + moss.
        var sx = f.R.Range(-len * 0.3, len * 0.2);
        DrawLine(Xform(f.P, new Vector2((float)sx, (float)(-half * 0.8))),
            Xform(f.P, new Vector2((float)(sx - half * 0.6), (float)(-half * 1.6))), f.Dark, W(f, half * 0.45), true);
        if (f.S.Accents.Count > 0)
            for (var k = 0; k < 3; k++)
            {
                var mc = new Vector2((float)f.R.Range(-len * 0.4, len * 0.3), (float)f.R.Range(-half * 0.6, half * 0.3));
                Fill(Xform(f.P, LocalBlob(f, mc, half * 0.5, half * 0.35, 0.3, 8)), f.Accent() with { A = 0.85f });
            }
    }

    // --- stump: bark rim, cut face with rings, roots ----------------------
    void DetailStump(FloraCtx f)
    {
        var r = f.S.Radius;
        FloraShade(f, r * 1.1, 0.3);
        for (var k = 0; k < 4; k++)
        {
            var a = f.R.Next() * Math.Tau;
            var root = LocalBlob(f, Polar(a, r * 0.9), r * 0.55, r * 0.25, 0.25, 8, a);
            InkFill(f, root, f.Dark, 0.5);
        }
        InkFill(f, LocalBlob(f, Vector2.Zero, r, r * 0.95, 0.08, 14), f.Dark, 0.9);
        var face = Palette.Mix(f.Leaf, Palette.Hex("#d9b98a"), 0.55);
        Fill(Xform(f.P, LocalBlob(f, Vector2.Zero, r * 0.8, r * 0.76, 0.05, 14)), face);
        for (var k = 1; k <= 3; k++)
            DrawArc(Xform(f.P, Vector2.Zero), W(f, r * 0.2 * k), 0, Mathf.Tau, 16, f.Leaf with { A = 0.55f }, W(f, 0.35), true);
        DrawLine(Xform(f.P, Vector2.Zero), Xform(f.P, Polar(f.R.Next() * Math.Tau, r * 0.75)), f.Dark with { A = 0.6f }, W(f, 0.4), true);
        if (f.S.Accents.Count > 0 && f.R.Next() < f.S.AccentChance)
            Fill(Xform(f.P, LocalBlob(f, Polar(f.R.Next() * Math.Tau, r * 0.8), r * 0.4, r * 0.3, 0.3, 8)), f.Accent());
    }

    // --- bramble: tangle of thorny arcs, leaves, berries -------------------
    void DetailBramble(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.9, 0.3);
        for (var i = 0; i < f.Count; i++)
        {
            var c = Polar(f.R.Next() * Math.Tau, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.6);
            var a0 = f.R.Next() * Math.Tau;
            var r = f.Len() * 0.5;
            var arc = new Vector2[7];
            for (var k = 0; k < arc.Length; k++)
                arc[k] = c + Polar(a0 + k * 0.35, r);
            var col = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.2, 0.8));
            DrawPolyline(Xform(f.P, arc), col, W(f, f.S.Width), true);
            for (var k = 1; k < arc.Length; k += 2)
            {
                var d = (arc[k] - c).Normalized();
                DrawLine(Xform(f.P, arc[k]), Xform(f.P, arc[k] + d * (float)(f.S.Width * 1.4)), col, W(f, f.S.Width * 0.5), true);
            }
            var lc = arc[3];
            Fill(Xform(f.P, Poly.Ellipse(lc.X, lc.Y, f.S.Width * 1.6, f.S.Width * 0.9, a0, 6)), f.Leaf);
        }
        if (f.S.Accents.Count > 0)
            for (var i = 0; i < f.Count * f.S.AccentChance; i++)
                DrawCircle(Xform(f.P, Polar(f.R.Next() * Math.Tau, f.S.Radius * 0.7 * Math.Sqrt(f.R.Next()))),
                    W(f, f.S.AccentSize * 0.5), f.Accent());
    }

    // --- rubble / scree: scattered angular stones ---------------------------
    void DetailRubble(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.7, 0.08);
        var stones = Enumerable.Range(0, f.Count)
            .Select(_ => (C: Polar(f.R.Next() * Math.Tau, f.S.Radius * Math.Pow(f.R.Next(), 0.7)), R: f.Len() * 0.5))
            .OrderBy(s => s.C.Y).ToList();
        foreach (var (c, r) in stones)
        {
            var col = Palette.Mix(f.Leaf, f.Dark, f.R.Range(0, 0.45));
            InkFill(f, LocalBlob(f, c, r, r * 0.75, 0.3, 6, f.R.Next() * Math.Tau), col, 0.5);
            Fill(Xform(f.P, Poly.Ellipse(c.X - r * 0.2, c.Y - r * 0.25, r * 0.45, r * 0.3, 0, 6)), Palette.Tint(col, 22) with { A = 0.6f });
            if (f.S.Accents.Count > 0 && f.R.Next() < f.S.AccentChance)
                DrawCircle(Xform(f.P, c), W(f, r * 0.25), f.Accent());
        }
    }

    // --- cairn: stacked stones narrowing upward (north) --------------------
    void DetailCairn(FloraCtx f)
    {
        var r = f.S.Radius;
        FloraShade(f, r * 1.2, 0.35);
        var levels = Math.Max(3, f.Count);
        for (var k = 0; k < levels; k++)
        {
            var t = k / (double)levels;
            var rr = r * (1 - t * 0.75);
            var c = new Vector2((float)f.R.Range(-r * 0.08, r * 0.08), (float)(-t * r * 0.55));
            var col = Palette.Mix(f.Leaf, f.Dark, f.R.Range(0, 0.35));
            InkFill(f, LocalBlob(f, c, rr, rr * 0.8, 0.15, 9), col, 0.7);
            Fill(Xform(f.P, Poly.Ellipse(c.X - rr * 0.2, c.Y - rr * 0.25, rr * 0.5, rr * 0.3, 0, 7)), Palette.Tint(col, 20) with { A = 0.55f });
        }
    }

    // --- signpost: post + plank arrows --------------------------------------
    void DetailSignpost(FloraCtx f)
    {
        var len = f.Len();
        FloraShade(f, len * 0.35, 0.3);
        var boards = Math.Max(1, f.Count);
        for (var i = 0; i < boards; i++)
        {
            var a = f.R.Next() * Math.Tau;
            var w = f.S.Width * 0.5;
            Vector2 L(double x, double y) => new Vector2((float)x, (float)y).Rotated((float)a);
            var board = new[] { L(0, -w), L(len * 0.8, -w), L(len, 0), L(len * 0.8, w), L(0, w) };
            InkFill(f, board, Palette.Mix(f.Leaf, Palette.Hex("#d9b98a"), 0.25 + i * 0.1), 0.7);
            DrawLine(Xform(f.P, L(len * 0.15, 0)), Xform(f.P, L(len * 0.65, 0)), f.Dark with { A = 0.6f }, W(f, 0.4), true);
        }
        InkFill(f, LocalBlob(f, Vector2.Zero, f.S.Width * 0.6, f.S.Width * 0.6, 0.1, 8), f.Dark, 0.7);
    }

    // --- shrine: stone plinth, idol, candles + offerings ------------------
    void DetailShrine(FloraCtx f)
    {
        var r = f.S.Radius;
        FloraShade(f, r * 1.1, 0.35);
        var baseSq = new[] { new Vector2((float)-r, (float)(-r * 0.8)), new Vector2((float)r, (float)(-r * 0.8)),
            new Vector2((float)r, (float)(r * 0.8)), new Vector2((float)-r, (float)(r * 0.8)) };
        InkFill(f, baseSq, f.Leaf, 0.9);
        var top = baseSq.Select(v => v * 0.68f + new Vector2(0, (float)(-r * 0.12))).ToArray();
        InkFill(f, top, Palette.Tint(f.Leaf, 16), 0.6);
        InkFill(f, LocalBlob(f, new Vector2(0, (float)(-r * 0.15)), r * 0.32, r * 0.32, 0.05, 10), f.Dark, 0.7);
        if (f.S.Accents.Count > 0)
            for (var k = 0; k < f.Count; k++)
            {
                var c = Polar(k / (double)f.Count * Math.Tau + 0.4, r * 0.62);
                var col = f.Accent();
                DrawCircle(Xform(f.P, c), W(f, f.S.AccentSize * 0.9), col with { A = 0.35f });
                DrawCircle(Xform(f.P, c), W(f, f.S.AccentSize * 0.4), col);
            }
    }

    // --- tracks: paired prints along a meandering trail --------------------
    void DetailTracks(FloraCtx f)
    {
        var len = f.Len();
        var steps = Math.Max(3, f.Count);
        var bend = f.R.Range(-0.6, 0.6);
        for (var i = 0; i < steps; i++)
        {
            var t = i / (double)(steps - 1);
            var x = (t - 0.5) * len;
            var y = Math.Sin(t * Math.PI) * bend * len * 0.25;
            var side = i % 2 == 0 ? -1 : 1;
            var c = new Vector2((float)x, (float)(y + side * f.S.Width * 0.5));
            var col = f.Dark with { A = 0.65f };
            Fill(Xform(f.P, Poly.Ellipse(c.X, c.Y, f.S.AccentSize * 0.9, f.S.AccentSize * 0.6, 0, 7)), col);
            for (var k = -1; k <= 1; k++)
                DrawCircle(Xform(f.P, c + new Vector2((float)(f.S.AccentSize * 1.1), (float)(k * f.S.AccentSize * 0.5))),
                    W(f, f.S.AccentSize * 0.28), col);
        }
    }

    // --- puddle: mud ring, water, sky glint --------------------------------
    void DetailPuddle(FloraCtx f)
    {
        var r = f.S.Radius;
        var rot = f.R.Next() * Math.PI;
        Fill(Xform(f.P, LocalBlob(f, Vector2.Zero, r * 1.15, r * 0.8, 0.08, 18, rot)), f.Dark with { A = 0.6f });
        var water = f.S.Accents.Count > 0 ? f.Accent() : Palette.Hex("#5f8c96");
        var pool = LocalBlob(f, Vector2.Zero, r, r * 0.65, 0.1, 18, rot);
        InkFill(f, pool, water, 0.4);
        Fill(Xform(f.P, Poly.Ellipse(-r * 0.25, -r * 0.18, r * 0.4, r * 0.12, rot, 8)), new Color(1, 1, 1, 0.35f));
    }

    // --- carcass: stain, spine, ribs, skull --------------------------------
    void DetailCarcass(FloraCtx f)
    {
        var len = f.Len();
        Fill(Xform(f.P, Poly.Ellipse(0, 0, len * 0.6, len * 0.3, 0, 12)), f.Dark with { A = 0.35f });
        var bone = f.Leaf;
        DrawLine(Xform(f.P, new Vector2((float)(-len * 0.45), 0)), Xform(f.P, new Vector2((float)(len * 0.35), 0)),
            bone, W(f, f.S.Width * 0.6), true);
        for (var k = 0; k < 5; k++)
        {
            var x = -len * 0.25 + k * len * 0.11;
            foreach (var side in new[] { -1, 1 })
            {
                var rib = new[]
                {
                    new Vector2((float)x, 0),
                    new Vector2((float)(x + len * 0.04), (float)(side * len * 0.15)),
                    new Vector2((float)(x - len * 0.02), (float)(side * len * 0.24)),
                };
                DrawPolyline(Xform(f.P, rib), bone, W(f, f.S.Width * 0.4), true);
            }
        }
        InkFill(f, LocalBlob(f, new Vector2((float)(len * 0.45), 0), len * 0.13, len * 0.09, 0.1, 9), bone, 0.5);
        DrawCircle(Xform(f.P, new Vector2((float)(len * 0.48), (float)(-len * 0.03))), W(f, len * 0.025), f.Ink);
    }

    // --- anthill: dirt mound rings + ants ---------------------------------
    void DetailAnthill(FloraCtx f)
    {
        var r = f.S.Radius;
        FloraShade(f, r * 1.05, 0.25);
        for (var k = 0; k < 3; k++)
        {
            var rr = r * (1 - k * 0.28);
            var col = Palette.Mix(f.Dark, f.Leaf, k / 2.0);
            InkFill(f, LocalBlob(f, new Vector2(0, (float)(-k * r * 0.08)), rr, rr * 0.85, 0.12, 12), col, k == 0 ? 0.6 : 0.3);
        }
        DrawCircle(Xform(f.P, new Vector2(0, (float)(-r * 0.2))), W(f, r * 0.1), f.Ink);
        for (var k = 0; k < f.Count; k++)
            DrawCircle(Xform(f.P, Polar(f.R.Next() * Math.Tau, r * f.R.Range(0.6, 1.4))), W(f, 0.35), f.Ink);
    }

    // --- beehive: straw skep rings on a plank, bees ------------------------
    void DetailBeehive(FloraCtx f)
    {
        var r = f.S.Radius;
        FloraShade(f, r * 1.1, 0.3);
        var plank = new[] { new Vector2((float)(-r * 1.2), (float)(r * 0.2)), new Vector2((float)(r * 1.2), (float)(r * 0.2)),
            new Vector2((float)(r * 1.2), (float)(r * 0.6)), new Vector2((float)(-r * 1.2), (float)(r * 0.6)) };
        InkFill(f, plank, f.Dark, 0.5);
        for (var k = 0; k < 4; k++)
        {
            var rr = r * (1 - k * 0.22);
            var col = k % 2 == 0 ? f.Leaf : Palette.Tint(f.Leaf, 22);
            InkFill(f, LocalBlob(f, new Vector2(0, (float)(-k * r * 0.1)), rr, rr * 0.9, 0.04, 14), col, k == 0 ? 0.8 : 0.3);
        }
        DrawCircle(Xform(f.P, new Vector2(0, (float)(r * 0.55))), W(f, r * 0.14), f.Ink);
        if (f.S.Accents.Count > 0)
            for (var k = 0; k < f.Count; k++)
                DrawCircle(Xform(f.P, Polar(f.R.Next() * Math.Tau, r * f.R.Range(1.1, 1.8))), W(f, 0.45), f.Accent());
    }

    // --- stalagmites: clustered cones seen from above ---------------------
    void DetailStalagmite(FloraCtx f)
    {
        FloraShade(f, f.S.Radius * 0.9, 0.3);
        var cones = Enumerable.Range(0, f.Count)
            .Select(_ => (C: Polar(f.R.Next() * Math.Tau, f.S.Radius * Math.Sqrt(f.R.Next()) * 0.6), R: f.Len() * 0.5))
            .OrderByDescending(c => c.R).ToList();
        foreach (var (c, r) in cones)
        {
            InkFill(f, LocalBlob(f, c, r, r * 0.92, 0.12, 10), f.Dark, 0.6);
            Fill(Xform(f.P, LocalBlob(f, c - new Vector2((float)(r * 0.15), (float)(r * 0.15)), r * 0.65, r * 0.6, 0.1, 9)), f.Leaf);
            var tip = f.S.Accents.Count > 0 ? f.Accent() : Palette.Tint(f.Leaf, 40);
            DrawCircle(Xform(f.P, c - new Vector2((float)(r * 0.25), (float)(r * 0.25))), W(f, r * 0.22), tip);
        }
    }

    // --- spider web: radial threads + spiral --------------------------------
    void DetailWeb(FloraCtx f)
    {
        var r = f.S.Radius;
        var col = f.Leaf with { A = 0.75f };
        var spokes = Math.Max(5, f.Count);
        var turn = f.R.Next() * Math.Tau;
        for (var k = 0; k < spokes; k++)
            DrawLine(Xform(f.P, Vector2.Zero), Xform(f.P, Polar(turn + k / (double)spokes * Math.Tau, r * f.R.Range(0.85, 1.05))),
                col, W(f, 0.3), true);
        var spiral = new List<Vector2>();
        for (var k = 0; k < spokes * 5; k++)
        {
            var a = turn + k / (double)spokes * Math.Tau;
            spiral.Add(Polar(a, r * (0.12 + 0.85 * k / (spokes * 5.0))));
        }
        DrawPolyline(Xform(f.P, [.. spiral]), col, W(f, 0.25), true);
        if (f.S.Accents.Count > 0 && f.R.Next() < f.S.AccentChance)
            DrawCircle(Xform(f.P, Polar(turn + 0.5, r * 0.3)), W(f, f.S.AccentSize * 0.5), f.Accent());
    }
}
