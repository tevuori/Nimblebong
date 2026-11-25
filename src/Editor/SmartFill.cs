// Context-aware scatter — the "smart" fill mode. Samples the marked
// region on a jittered grid, classifies each point against the map it
// lands on (road verge, shoreline, slope or plateau, village edge,
// open land) and picks props from a weighted palette for that context,
// then sprinkles ambient critters on top. Deterministic per seed —
// the dice button rerolls a variant.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public static class SmartFill
{
    // Weighted palettes per context — kind string + pick weight. Water
    // entries only ever spawn on the water surface itself.
    static readonly (string K, double W)[] Land =
    [
        ("tree", 1.5), ("pine", 1.0), ("bush", 1.7), ("rock", 1.1),
        ("crystal", 0.1), ("bones", 0.07), ("tent", 0.05),
        ("campfire", 0.05), ("ruin", 0.04), ("stonecircle", 0.02),
        ("statue", 0.02),
    ];
    static readonly (string K, double W)[] Roadside =
    [
        ("bush", 2.4), ("rock", 1.9), ("tree", 1.1), ("lantern", 0.5),
        ("cart", 0.3), ("marker", 0.16), ("well", 0.12), ("flag", 0.1),
        ("bones", 0.08), ("barricade", 0.07),
    ];
    static readonly (string K, double W)[] Shore =
    [
        ("rock", 2.6), ("bush", 1.7), ("crystal", 0.25),
        ("bones", 0.1), ("dock", 0.06), ("boat", 0.1),
    ];
    static readonly (string K, double W)[] Slope =
    [
        ("rock", 3.0), ("pine", 1.5), ("bush", 0.9),
        ("crystal", 0.5), ("statue", 0.05),
    ];
    static readonly (string K, double W)[] Village =
    [
        ("crate", 2.0), ("barrel", 1.8), ("bale", 1.2), ("lantern", 0.9),
        ("cart", 0.6), ("flag", 0.35), ("well", 0.15), ("marker", 0.1),
    ];
    static readonly (string K, double W)[] Open =
    [
        ("boat", 1.0), // open water — nothing else belongs there
    ];

    // Base footprint per kind — prop Scale is absolute px (24 ≈ one
    // "standard" prop), jittered ±25 % at spawn.
    static readonly Dictionary<string, double> Base = new()
    {
        ["tree"] = 46, ["pine"] = 44, ["bush"] = 26, ["rock"] = 24,
        ["crystal"] = 24, ["bones"] = 30, ["tent"] = 36,
        ["campfire"] = 20, ["ruin"] = 38, ["stonecircle"] = 44,
        ["statue"] = 34, ["lantern"] = 16, ["cart"] = 40, ["well"] = 24,
        ["marker"] = 20, ["flag"] = 22, ["barricade"] = 48,
        ["crate"] = 22, ["barrel"] = 20, ["bale"] = 28, ["dock"] = 30,
        ["boat"] = 26, ["butterfly"] = 10, ["bird"] = 22,
    };

    // Prop kinds that read as buildings for the village context.
    static readonly HashSet<string> Building =
        [Kinds.House, "tower", "stall", "ruin", "fountain"];

    public static MapFill.FillResult Generate(
        IReadOnlyList<Vector2> region,
        Scene scene,
        int seed,
        double density = 0.6
    )
    {
        if (region.Count < 3)
            return new MapFill.FillResult([], []);
        var rand = new Mulberry(seed);
        var minX = region.Min(p => p.X);
        var minY = region.Min(p => p.Y);
        var maxX = region.Max(p => p.X);
        var maxY = region.Max(p => p.Y);

        // ── context fields ──────────────────────────────────────────
        var roads = scene.Strokes.Where(s => s.Kind == Kinds.Road).ToList();
        var rivers = scene.Strokes.Where(s => s.Kind == Kinds.River).ToList();
        var barriers = scene
            .Strokes.Where(s => s.Kind is Kinds.Wall or Kinds.Fence)
            .ToList();
        var ponds = scene
            .Shapes.Where(s => s.Kind == Kinds.Pond)
            .Select(s => s.Points)
            .ToList();
        var terrain = scene.Shapes.Where(s => s.Kind == Kinds.Terrain).ToList();
        var massifs = TerrainGeometry.BuildMassifs(
            terrain.Select(s => new TerrainShape
            {
                Model = s.Model,
                Height = s.Height,
                Size = s.Size,
                Roughness = s.Roughness,
                Outline = [.. s.Points],
            })
        );
        var tops = massifs.SelectMany(m => m.Tops).ToList();
        var buildings = scene
            .Props.Where(p => Building.Contains(p.Kind))
            .Select(p => new Vector2((float)p.X, (float)p.Y))
            .ToList();
        var existing = scene
            .Props.Where(p =>
                p.X > minX - 80 && p.X < maxX + 80 && p.Y > minY - 80 && p.Y < maxY + 80
            )
            .Select(p => (P: new Vector2((float)p.X, (float)p.Y), R: p.Scale * 0.42))
            .ToList();

        double SegD(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len2 = ab.LengthSquared();
            var t = len2 < 1e-6 ? 0 : Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
            return (a + ab * (float)t - p).Length();
        }
        double LineDist(Vector2 p, List<Stroke> lines)
        {
            var d = double.MaxValue;
            foreach (var l in lines)
                for (var i = 1; i < l.Points.Count; i++)
                    d = Math.Min(d, SegD(p, l.Points[i - 1], l.Points[i]) - l.Width / 2);
            return d;
        }
        // Signed water distance — negative inside a pond polygon.
        double WaterDist(Vector2 p)
        {
            var d = LineDist(p, rivers);
            foreach (var pond in ponds)
            {
                var e = double.MaxValue;
                for (var i = 0; i < pond.Count; i++)
                    e = Math.Min(e, SegD(p, pond[i], pond[(i + 1) % pond.Count]));
                d = Math.Min(d, Poly.PointInPolygon(p, pond) ? -e : e);
            }
            return d;
        }
        double Edge(Vector2 p) =>
            region.Select((a, i) => SegD(p, a, region[(i + 1) % region.Count])).Min();

        string Pick((string K, double W)[] table)
        {
            var total = 0.0;
            foreach (var e in table)
                total += e.W;
            var roll = rand.Next() * total;
            foreach (var e in table)
            {
                roll -= e.W;
                if (roll <= 0)
                    return e.K;
            }
            return table[^1].K;
        }

        var spacing = Math.Max(34, 72 - density * 34);
        var generated = new List<Prop>();
        var placed = new List<(Vector2 P, double R)>(existing);

        bool Free(Vector2 p, double size) =>
            placed.All(q => q.P.DistanceTo(p) >= q.R + size * 0.42);

        void Spawn(string kind, Vector2 p)
        {
            var size = Base.GetValueOrDefault(kind, 24) * (0.8 + rand.Next() * 0.5);
            if (!Free(p, size))
                return;
            // Walls and fences are hard blockers — nothing spawns on
            // the barrier line itself.
            var blocked = false;
            foreach (var b in barriers)
                for (var i = 1; i < b.Points.Count && !blocked; i++)
                    if (SegD(p, b.Points[i - 1], b.Points[i]) <= b.Width / 2 + size * 0.5)
                        blocked = true;
            if (blocked)
                return;
            placed.Add((p, size * 0.42));
            generated.Add(
                new Prop
                {
                    Kind = kind,
                    X = p.X,
                    Y = p.Y,
                    Scale = size,
                    Rotation = rand.Next() * Math.PI * 2,
                    Seed = (int)(p.X * 0.37 + p.Y * 0.53 + seed),
                }
            );
        }

        for (var y = (double)minY; y <= maxY && generated.Count < 420; y += spacing)
            for (var x = (double)minX; x <= maxX && generated.Count < 420; x += spacing)
            {
                var p = new Vector2(
                    (float)(x + (rand.Next() - 0.5) * spacing * 1.1),
                    (float)(y + (rand.Next() - 0.5) * spacing * 1.1)
                );
                if (!Poly.PointInPolygon(p, region))
                    continue;
                var edge = Edge(p);
                if (edge < spacing * 0.25)
                    continue;
                if (edge < spacing && rand.Next() > edge / spacing)
                    continue;

                var wd = WaterDist(p);
                var rd = LineDist(p, roads);
                if (wd < 0)
                {
                    // Open water — a rare boat, nothing else.
                    if (wd < -14 && rand.Next() < 0.06)
                        Spawn(Pick(Open), p);
                    continue;
                }
                var onTop = tops.Any(t => Poly.PointInPolygon(p, t));
                var onSlope =
                    !onTop && terrain.Any(t => Poly.PointInPolygon(p, t.Points));
                var nearBuilding = buildings.Any(b => b.DistanceTo(p) < 100);

                (string K, double W)[] table;
                if (rd < 10)
                    continue; // on the road surface itself — keep it clear
                else if (nearBuilding)
                    table = Village;
                else if (rd < 46)
                    table = Roadside;
                else if (wd < 44)
                    table = Shore;
                else if (onSlope || onTop)
                    table = Slope;
                else
                    table = Land;

                // Patchiness — open land and slopes clump into groves
                // with clearings instead of a uniform sprinkle. Feature
                // strips (road verge, shore, village) ignore the mask.
                var patch =
                    Math.Sin(p.X * 0.023 + seed * 0.7) * Math.Sin(p.Y * 0.019 + seed * 1.3);
                if ((table == Land || table == Slope) && patch < -0.1)
                    continue;

                var kind = Pick(table);
                var size = Base.GetValueOrDefault(kind, 24);
                if (!Interactions.WaterKind(kind) && wd < size * 0.35)
                    continue; // dry-land kinds keep their feet out of water
                Spawn(kind, p);
            }

        // ── critters — sparse ambient pass over the whole region ─────
        for (var y = (double)minY; y <= maxY; y += spacing)
            for (var x = (double)minX; x <= maxX; x += spacing)
            {
                var p = new Vector2(
                    (float)(x + (rand.Next() - 0.5) * spacing),
                    (float)(y + (rand.Next() - 0.5) * spacing)
                );
                if (!Poly.PointInPolygon(p, region))
                    continue;
                var wd = WaterDist(p);
                var roll = rand.Next();
                // Butterflies love shores and open land; birds go anywhere.
                if (wd < 40 && wd > -30 && roll < 0.09)
                    Spawn("butterfly", p);
                else if (roll > 0.965)
                    Spawn("bird", p);
            }

        return new MapFill.FillResult(generated, []);
    }

    /// <summary>mulberry32 — same generator MapFill uses, kept local so
    /// both fills share the reroll feel.</summary>
    class Mulberry
    {
        int _t;
        public Mulberry(int seed) => _t = seed;
        public double Next()
        {
            unchecked
            {
                _t += 0x6D2B79F5;
                var t = _t;
                t = (t ^ (int)((uint)t >> 15)) * (t | 1);
                t ^= t + ((t ^ (int)((uint)t >> 7)) * (t | 61));
                return (double)(uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
            }
        }
    }
}
