// Context-aware scatter — the "smart" fill mode. Samples the marked
// region on a jittered grid, classifies each point against the map it
// lands on (road verge, shoreline, slope or plateau, village edge,
// open land) and picks props from a weighted palette for that context,
// then sprinkles ambient critters on top. Deterministic per seed —
// the dice button rerolls a variant.
//
// Everything tunable — context thresholds, footprints and the per-layer
// weighted tables (main props, flora understory, critters) — lives in
// assets/config/fill.json (FillRules). Prop definitions outside the
// scene's biome drop out of the tables.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public static class SmartFill
{
    public static MapFill.FillResult Generate(
        IReadOnlyList<Vector2> region,
        Scene scene,
        int seed,
        double density = 0.6
    )
    {
        if (region.Count < 3)
            return new MapFill.FillResult([], []);
        var cfg = FillRules.Config;
        var biome = scene.Biome ?? "woodland";
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
            .Props.Where(p => cfg.Buildings.Contains(PropDefs.BaseKind(p.Kind)))
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

        // Biome filter: definitions outside this biome drop out.
        bool InBiome(string kind) => !PropDefs.TryGet(kind, out var def) || def.InBiome(biome);

        string? Pick(FillTable table)
        {
            var total = 0.0;
            foreach (var e in table.Kinds)
                if (InBiome(e.Kind))
                    total += e.Weight;
            if (total <= 0)
                return null;
            var roll = rand.Next() * total;
            foreach (var e in table.Kinds)
            {
                if (!InBiome(e.Kind))
                    continue;
                roll -= e.Weight;
                if (roll <= 0)
                    return e.Kind;
            }
            return null;
        }

        // Footprint used for spacing — the scene stores the pre-definition
        // scale (SceneView applies def.Scale), so collisions use both.
        double Footprint(string kind, double size) =>
            size * (PropDefs.TryGet(kind, out var def) ? def.Scale : 1);

        // Context of a point — FillConfig.Contexts order; null = on the
        // road surface (kept clear).
        string? Classify(Vector2 p, double wd)
        {
            if (wd < 0)
                return wd < -cfg.DeepWater ? "deepWater" : "shallows";
            var rd = LineDist(p, roads);
            if (rd < cfg.Road)
                return null;
            if (buildings.Any(b => b.DistanceTo(p) < cfg.Village))
                return "village";
            if (rd < cfg.Roadside)
                return "roadside";
            if (wd < cfg.Shore)
                return "shore";
            if (tops.Any(t => Poly.PointInPolygon(p, t))
                || terrain.Any(t => Poly.PointInPolygon(p, t.Points)))
                return "slope";
            return "land";
        }

        var generated = new List<Prop>();
        // Everything already standing (scene + earlier layers), tagged
        // with the layer that placed it ("" = scene).
        var placed = new List<(Vector2 P, double R, string Layer)>(
            existing.Select(e => (e.P, e.R, ""))
        );

        foreach (var layer in cfg.Layers)
        {
            var spacing = layer.Spacing(density);
            var count = 0;

            // Same-layer props block fully; other layers by `crowding`
            // (flora tucks in under tree edges with a low value).
            bool Free(Vector2 p, double size) =>
                placed.All(q =>
                    q.P.DistanceTo(p)
                    >= q.R * (q.Layer == layer.Id ? 1 : layer.Crowding) + size * 0.42
                );

            void Spawn(string kind, Vector2 p)
            {
                var size = cfg.SizeOf(kind) * (0.8 + rand.Next() * 0.5);
                var foot = Footprint(kind, size);
                if (!Free(p, foot))
                    return;
                // Walls and fences are hard blockers — nothing spawns on
                // the barrier line itself.
                foreach (var b in barriers)
                    for (var i = 1; i < b.Points.Count; i++)
                        if (SegD(p, b.Points[i - 1], b.Points[i]) <= b.Width / 2 + foot * 0.5)
                            return;
                placed.Add((p, foot * 0.42, layer.Id));
                count++;
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

            for (var y = (double)minY; y <= maxY && count < layer.Limit; y += spacing)
                for (var x = (double)minX; x <= maxX && count < layer.Limit; x += spacing)
                {
                    var p = new Vector2(
                        (float)(x + (rand.Next() - 0.5) * spacing * 1.1),
                        (float)(y + (rand.Next() - 0.5) * spacing * 1.1)
                    );
                    if (!Poly.PointInPolygon(p, region))
                        continue;
                    if (layer.EdgeFade)
                    {
                        var edge = Edge(p);
                        if (edge < spacing * 0.25)
                            continue;
                        if (edge < spacing && rand.Next() > edge / spacing)
                            continue;
                    }
                    var wd = WaterDist(p);
                    var context = Classify(p, wd);
                    if (context == null || !layer.Contexts.TryGetValue(context, out var table))
                        continue;
                    if (rand.Next() > table.Chance * layer.ChanceIn(biome))
                        continue;
                    // Patchiness — clumps into groves with clearings
                    // instead of a uniform sprinkle.
                    if (layer.Patchy.Contains(context))
                    {
                        var patch =
                            Math.Sin(p.X * 0.023 + seed * 0.7) * Math.Sin(p.Y * 0.019 + seed * 1.3);
                        if (patch < -0.1)
                            continue;
                    }
                    var kind = Pick(table);
                    if (kind == null)
                        continue;
                    if (!Interactions.WaterKind(kind) && wd < Footprint(kind, cfg.SizeOf(kind)) * 0.35)
                        continue; // dry-land kinds keep their feet out of water
                    Spawn(kind, p);
                }
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
