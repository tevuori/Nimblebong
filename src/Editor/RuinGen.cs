using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DmMap;

/// <summary>Procedural ruin complexes — broken castles, fallen towers,
/// rotted houses and ancient circles. Everything is generated as real
/// scene objects (wall strokes in collapsed segments, tower/ruin/pillar
/// props, floor foundations, debris) so the user can move, delete or
/// adjust every piece afterwards. One click = one undo commit.</summary>
public static class RuinGen
{
    static double N(int seed, int i) => Rng.Noise(seed * 0.631 + i * 7.113);

    /// <summary>Generate a ruin at `center`. `seed` picks the archetype
    /// and every detail; `scale` scales the footprint (~1 = 200 px).</summary>
    public static void Generate(Scene scene, Vector2 center, int seed, double scale = 1.0)
    {
        var archetype = (int)(N(seed, 0) * 4);
        switch (archetype)
        {
            case 0: Castle(scene, center, seed, scale); break;
            case 1: Tower(scene, center, seed, scale); break;
            case 2: HouseRuin(scene, center, seed, scale); break;
            default: Circle(scene, center, seed, scale); break;
        }
    }

    // ── shared pieces ────────────────────────────────────────────────

    /// <summary>A broken wall along a polyline: the line is chopped
    /// into short standing fragments with collapsed gaps. Each fragment
    /// is its own wall stroke, slightly jittered off the ideal line —
    /// centuries of subsidence.</summary>
    static void BrokenWall(Scene scene, List<Vector2> line, int seed, int s0, double width = 15)
    {
        // Walk the polyline in arc-length, emitting fragments of
        // 30-70 px separated by gaps of 15-45 px.
        var pts = Resample(line, 10);
        if (pts.Count < 3)
            return;
        var i = 0;
        var frag = 0;
        while (i < pts.Count - 2)
        {
            var fragLen = 3 + (int)(N(seed, s0 + frag * 13) * 5); // 3-7 samples = 30-70 px
            if (N(seed, s0 + frag * 13 + 1) > 0.28) // some sections fully collapsed
            {
                var seg = pts.Skip(i).Take(fragLen + 1).ToList();
                if (seg.Count >= 2)
                {
                    // Crumble jitter — bigger towards the ends.
                    var jit = new List<Vector2>();
                    for (var k = 0; k < seg.Count; k++)
                    {
                        var edge = Math.Min(k, seg.Count - 1 - k) / (double)Math.Max(1, seg.Count / 2);
                        var j = (1 - edge * 0.5) * 6;
                        jit.Add(
                            seg[k] + new Vector2(
                                (float)((N(seed, s0 + frag * 31 + k) - 0.5) * j),
                                (float)((N(seed, s0 + frag * 37 + k) - 0.5) * j)
                            )
                        );
                    }
                    scene.Strokes.Add(
                        new Stroke
                        {
                            Id = scene.AllocId(),
                            Kind = Kinds.Wall,
                            Points = jit,
                            Width = width * (0.8 + N(seed, s0 + frag * 41) * 0.4),
                        }
                    );
                }
            }
            else if (fragLen > 2)
            {
                // Collapsed section — a couple of rubble rocks instead.
                var mid = pts[Math.Min(pts.Count - 1, i + fragLen / 2)];
                Debris(scene, mid, seed + frag * 7, 1 + (int)(N(seed, s0 + frag * 43) * 2), 26);
            }
            i += fragLen + 1 + (int)(N(seed, s0 + frag * 13 + 2) * 4);
            frag++;
        }
    }

    /// <summary>Rubble — ruin mounds and rocks tumbled together.</summary>
    static void Debris(Scene scene, Vector2 at, int seed, int count, double spread)
    {
        for (var i = 0; i < count; i++)
        {
            var a = N(seed, 50 + i * 3) * Math.PI * 2;
            var d = N(seed, 51 + i * 3) * spread;
            var ruin = N(seed, 52 + i * 3) < 0.45;
            scene.Props.Add(
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = ruin ? "ruin" : Kinds.Rock,
                    X = at.X + Math.Cos(a) * d,
                    Y = at.Y + Math.Sin(a) * d,
                    Scale = ruin ? 15 + N(seed, 53 + i * 3) * 14 : 9 + N(seed, 53 + i * 3) * 9,
                    Rotation = N(seed, 54 + i * 3) * Math.PI * 2,
                    Seed = seed * 17 + i,
                }
            );
        }
    }

    static List<Vector2> Resample(List<Vector2> pts, double step)
    {
        var outp = new List<Vector2> { pts[0] };
        for (var i = 1; i < pts.Count; i++)
        {
            var a = outp[^1];
            var b = pts[i];
            var d = a.DistanceTo(b);
            var n = Math.Max(1, (int)(d / step));
            for (var k = 1; k <= n; k++)
                outp.Add(a.Lerp(b, k / (float)n));
        }
        return outp;
    }

    static List<Vector2> Arc(Vector2 c, double r, double a0, double a1, int n, double squashY = 1.0)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= n; i++)
        {
            var a = a0 + (a1 - a0) * i / n;
            pts.Add(c + new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r * squashY)));
        }
        return pts;
    }

    static void PropAt(Scene scene, string kind, Vector2 at, double scale, int seed, int model = 0, double rot = 0)
    {
        scene.Props.Add(
            new Prop
            {
                Id = scene.AllocId(),
                Kind = kind,
                X = at.X,
                Y = at.Y,
                Scale = scale,
                Rotation = rot,
                Seed = seed,
                Model = model,
            }
        );
    }

    // ── archetypes ───────────────────────────────────────────────────

    /// <summary>Ruined castle — a broken ring wall with a gate gap,
    /// corner towers in varied states of collapse, rubble inside.</summary>
    static void Castle(Scene scene, Vector2 c, int seed, double scale)
    {
        var r = (95 + N(seed, 1) * 55) * scale;
        var squash = 0.75 + N(seed, 2) * 0.25;
        var towers = 3 + (int)(N(seed, 3) * 2);
        var gateA = N(seed, 4) * Math.PI * 2;

        // Ring wall in arcs between tower positions, skipping the gate.
        var towerAngles = new List<double>();
        for (var i = 0; i < towers; i++)
            towerAngles.Add(gateA + Math.PI * 0.35 + i / (double)towers * Math.PI * 1.65 + (N(seed, 10 + i) - 0.5) * 0.3);

        // Wall ring: full circle chopped by BrokenWall, but leave the
        // gate arc out entirely.
        var ringPts = new List<Vector2>();
        var segs = 64;
        for (var i = 0; i <= segs; i++)
        {
            var a = i / (double)segs * Math.PI * 2;
            var rel = Math.Abs(Math.IEEERemainder(a - gateA, Math.PI * 2));
            if (rel < 0.42) // gate opening
                continue;
            ringPts.Add(c + new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r * squash)));
        }
        // Split the ring where the gate gap interrupts continuity.
        var runs = new List<List<Vector2>> { new() };
        foreach (var p in ringPts)
        {
            if (runs[^1].Count > 0 && runs[^1][^1].DistanceTo(p) > r * 0.35)
                runs.Add(new List<Vector2>());
            runs[^1].Add(p);
        }
        var wi = 0;
        foreach (var run in runs)
            if (run.Count > 4)
                BrokenWall(scene, run, seed, 100 + wi++ * 61);

        // Towers at the ring — intact keeps, ruined stumps, or just a
        // rubble heap where the tower fully collapsed.
        for (var i = 0; i < towerAngles.Count; i++)
        {
            var tp = c + new Vector2((float)(Math.Cos(towerAngles[i]) * r), (float)(Math.Sin(towerAngles[i]) * r * squash));
            var state = N(seed, 200 + i);
            if (state < 0.35)
                PropAt(scene, "tower", tp, 20 + N(seed, 210 + i) * 8, seed * 31 + i, model: 0);
            else if (state < 0.75)
                PropAt(scene, "tower", tp, 20 + N(seed, 210 + i) * 8, seed * 31 + i, model: 2);
            else
                Debris(scene, tp, seed + i * 91, 3, 30);
        }

        // Courtyard — foundation slabs of the great hall, interior
        // rubble, a few standing pillars of the collapsed hall.
        var hallW = r * (0.5 + N(seed, 300) * 0.2);
        var hallH = r * 0.32 * squash;
        var hx = c.X + (float)((N(seed, 301) - 0.5) * r * 0.3);
        var hy = c.Y + (float)((N(seed, 302) - 0.5) * r * 0.3 * squash);
        var rot = N(seed, 303) * 0.8 - 0.4;
        var hw = (float)(hallW / 2);
        var hh = (float)hallH;
        var cos = (float)Math.Cos(rot);
        var sin = (float)Math.Sin(rot);
        Vector2 Corner(double dx, double dy) =>
            new(hx + (float)(dx * cos - dy * sin), hy + (float)(dx * sin + dy * cos));
        scene.Shapes.Add(
            new Shape
            {
                Id = scene.AllocId(),
                Kind = Kinds.Floor,
                Points = [Corner(-hw, -hh), Corner(hw, -hh), Corner(hw, hh), Corner(-hw, hh)],
                Size = 18,
            }
        );
        // Hall wall fragments along the slab edges.
        var edgeRuns = new List<List<Vector2>>
        {
            new() { Corner(-hw, -hh), Corner(hw, -hh) },
            new() { Corner(hw, -hh), Corner(hw, hh) },
            new() { Corner(hw, hh), Corner(-hw, hh) },
            new() { Corner(-hw, hh), Corner(-hw, -hh) },
        };
        var ei = 0;
        foreach (var e in edgeRuns)
        {
            if (N(seed, 310 + ei) > 0.25) // some edges fully gone
                BrokenWall(scene, e, seed, 320 + ei * 53, width: 12);
            ei++;
        }
        // Standing pillars where the hall roof once rested.
        var pillars = 2 + (int)(N(seed, 330) * 3);
        for (var i = 0; i < pillars; i++)
            PropAt(
                scene,
                "pillar",
                new Vector2(hx + (float)((N(seed, 340 + i) - 0.5) * hallW * 0.7), hy + (float)((N(seed, 350 + i) - 0.5) * hallH)),
                13 + N(seed, 360 + i) * 7,
                seed * 41 + i
            );
        Debris(scene, new Vector2(hx, hy), seed + 777, 2 + (int)(N(seed, 370) * 3), r * 0.4);
        // Occasionally the inhabitants never left.
        if (N(seed, 380) < 0.4)
            PropAt(scene, Kinds.Bones, new Vector2(hx + 10, hy + 6), 14 + N(seed, 381) * 6, seed * 53);
    }

    /// <summary>Fallen watchtower — one standing stump (or a rare
    /// intact tower), radiating wall fragments, a rubble skirt.</summary>
    static void Tower(Scene scene, Vector2 c, int seed, double scale)
    {
        var intact = N(seed, 1) < 0.3;
        PropAt(scene, "tower", c, (26 + N(seed, 2) * 8) * scale, seed * 7, model: intact ? 0 : 2);
        // Rubble skirt around the base.
        Debris(scene, c, seed + 100, 3 + (int)(N(seed, 3) * 3), 42 * scale);
        // Radiating curtain-wall fragments — the tower was part of
        // something larger once.
        var walls = 1 + (int)(N(seed, 4) * 3);
        for (var i = 0; i < walls; i++)
        {
            var a = N(seed, 10 + i) * Math.PI * 2;
            var len = (60 + N(seed, 20 + i) * 80) * scale;
            var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
            var line = new List<Vector2>
            {
                c + dir * (float)(28 * scale),
                c + dir * (float)(len * 0.6) + new Vector2((float)((N(seed, 30 + i) - 0.5) * 20), (float)((N(seed, 31 + i) - 0.5) * 20)),
                c + dir * (float)len,
            };
            BrokenWall(scene, line, seed, 200 + i * 71, width: 13);
        }
        // A fallen pillar or two, scattered.
        if (N(seed, 40) < 0.6)
            PropAt(
                scene,
                "pillar",
                c + new Vector2((float)((N(seed, 41) - 0.5) * 90 * scale), (float)((N(seed, 42) - 0.5) * 90 * scale)),
                12 + N(seed, 43) * 6,
                seed * 13,
                rot: N(seed, 44) * Math.PI * 2
            );
    }

    /// <summary>Rotted homestead — rectangular foundation with broken
    /// walls, debris of the life that was here.</summary>
    static void HouseRuin(Scene scene, Vector2 c, int seed, double scale)
    {
        var w = (70 + N(seed, 1) * 40) * scale;
        var h = (45 + N(seed, 2) * 25) * scale;
        var rot = N(seed, 3) * Math.PI;
        var cos = (float)Math.Cos(rot);
        var sin = (float)Math.Sin(rot);
        Vector2 Corner(double dx, double dy) =>
            new(c.X + (float)(dx * cos - dy * sin), c.Y + (float)(dx * sin + dy * cos));
        var hw = w / 2;
        var hh = h / 2;
        scene.Shapes.Add(
            new Shape
            {
                Id = scene.AllocId(),
                Kind = Kinds.Floor,
                Points = [Corner(-hw, -hh), Corner(hw, -hh), Corner(hw, hh), Corner(-hw, hh)],
                Size = 16,
            }
        );
        var edges = new List<List<Vector2>>
        {
            new() { Corner(-hw, -hh), Corner(hw, -hh) },
            new() { Corner(hw, -hh), Corner(hw, hh) },
            new() { Corner(hw, hh), Corner(-hw, hh) },
            new() { Corner(-hw, hh), Corner(-hw, -hh) },
        };
        for (var i = 0; i < 4; i++)
            if (N(seed, 10 + i) > 0.2)
                BrokenWall(scene, edges[i], seed, 100 + i * 47, width: 11);
        // Interior debris — collapsed furniture, rubble, sometimes a
        // chest that survived the centuries.
        Debris(scene, c, seed + 300, 1 + (int)(N(seed, 20) * 2), w * 0.3);
        if (N(seed, 21) < 0.5)
            PropAt(scene, "chest", Corner((N(seed, 22) - 0.5) * w * 0.5, (N(seed, 23) - 0.5) * h * 0.5), 13 + N(seed, 24) * 5, seed * 19, rot: N(seed, 25) * Math.PI * 2);
        if (N(seed, 26) < 0.45)
            PropAt(scene, Kinds.Bones, Corner((N(seed, 27) - 0.5) * w * 0.5, (N(seed, 28) - 0.5) * h * 0.5), 12 + N(seed, 29) * 5, seed * 23);
        // Sometimes a second smaller outbuilding footprint nearby.
        if (N(seed, 30) < 0.5)
        {
            var ox = c.X + (float)((N(seed, 31) - 0.5) * 2.2 * w);
            var oy = c.Y + (float)((N(seed, 32) - 0.5) * 2.2 * h + h);
            var sw = w * 0.45;
            var sh = h * 0.5;
            scene.Shapes.Add(
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Floor,
                    Points =
                    [
                        new Vector2((float)(ox - sw / 2), (float)(oy - sh / 2)),
                        new Vector2((float)(ox + sw / 2), (float)(oy - sh / 2)),
                        new Vector2((float)(ox + sw / 2), (float)(oy + sh / 2)),
                        new Vector2((float)(ox - sw / 2), (float)(oy + sh / 2)),
                    ],
                    Size = 14,
                }
            );
            Debris(scene, new Vector2((float)ox, (float)oy), seed + 500, 1 + (int)(N(seed, 33) * 2), sw * 0.5);
        }
    }

    /// <summary>Ancient circle — weathered standing stones around a
    /// center, half of them fallen, older than any kingdom.</summary>
    static void Circle(Scene scene, Vector2 c, int seed, double scale)
    {
        var r = (55 + N(seed, 1) * 35) * scale;
        PropAt(scene, "stonecircle", c, 24 * scale, seed * 3);
        var stones = 5 + (int)(N(seed, 2) * 4);
        for (var i = 0; i < stones; i++)
        {
            var a = i / (double)stones * Math.PI * 2 + N(seed, 10 + i) * 0.4;
            var p = c + new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r * 0.85));
            var fallen = N(seed, 20 + i) < 0.4;
            if (fallen)
                // Fallen — a rock slab lying where the stone tipped.
                PropAt(scene, Kinds.Rock, p + new Vector2((float)((N(seed, 30 + i) - 0.5) * 20), (float)((N(seed, 31 + i) - 0.5) * 20)), 10 + N(seed, 32 + i) * 7, seed * 29 + i, rot: N(seed, 33 + i) * Math.PI * 2);
            else
                PropAt(scene, "pillar", p, 13 + N(seed, 34 + i) * 8, seed * 29 + i);
        }
        if (N(seed, 40) < 0.5)
            PropAt(scene, Kinds.Bones, c + new Vector2((float)((N(seed, 41) - 0.5) * r), (float)((N(seed, 42) - 0.5) * r)), 12 + N(seed, 43) * 5, seed * 37);
    }
}
