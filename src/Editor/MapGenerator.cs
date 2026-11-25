// Seeded map generator — builds a whole Scene from one integer:
// a meandering river, terraced massifs, a road that fords the river,
// forest patches and a hamlet. Everything derives from Rng.Noise so
// the same seed always produces the same map.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public static partial class MapGenerator
{
    /// <summary>Deterministic [0,1) value for (seed, index) — Rng.Noise
    /// is hash-based so arbitrary seeds are fine.</summary>
    static double N(int seed, int i) => Rng.Noise(seed * 0.618033 + i * 7.13);

    public static Scene Generate(int seed, Vector2 size) =>
        GenerateLandscape(seed, size);

    public static Scene GenerateLandscape(int seed, Vector2 size)
    {
        var scene = new Scene { Width = size.X, Height = size.Y, Name = $"Mapa {seed}" };
        var cx = size.X / 2;
        var cy = size.Y / 2;

        // ── massifs: 4-7 hills placed away from the map center ──
        var hillCount = 4 + (int)(N(seed, 1) * 3);
        for (var i = 0; i < hillCount; i++)
        {
            var a = N(seed, 10 + i) * Math.PI * 2;
            var d = 0.28 + N(seed, 30 + i) * 0.3;
            var center = new Vector2(
                (float)(cx + Math.Cos(a) * cx * 1.5 * d),
                (float)(cy + Math.Sin(a) * cy * 1.5 * d)
            );
            var radius = 90 + N(seed, 50 + i) * 130;
            var pts = new List<Vector2>();
            var lobes = 2 + (int)(N(seed, 70 + i) * 3);
            for (var k = 0; k < 16; k++)
            {
                var t = k / 16.0 * Math.PI * 2;
                var r = radius * (0.78 + 0.32 * N(seed, 90 + i * 17 + k));
                pts.Add(
                    new Vector2(
                        (float)(center.X + Math.Cos(t) * r * (1 + 0.2 * Math.Cos(t * lobes))),
                        (float)(center.Y + Math.Sin(t) * r)
                    )
                );
            }
            scene.Shapes.Add(
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Terrain,
                    Points = pts,
                    Height = 15 + N(seed, 120 + i) * 55,
                    Size = 34 + N(seed, 140 + i) * 28,
                    Roughness = N(seed, 160 + i) * 85,
                    Model = (int)(N(seed, 180 + i) * 3),
                }
            );
        }

        // ── river: top edge to bottom edge, meandering ──
        var riverPts = new List<Vector2>();
        var rx = size.X * (0.2 + N(seed, 200) * 0.6);
        var phase = N(seed, 201) * Math.PI * 2;
        var amp = 60 + N(seed, 202) * 90;
        var nSeg = 7;
        for (var i = 0; i <= nSeg; i++)
        {
            var y = -40 + (size.Y + 80) * i / nSeg;
            var x = rx + amp * Math.Sin(phase + i * 1.1);
            riverPts.Add(new Vector2((float)x, (float)y));
        }
        scene.Strokes.Add(
            new Stroke
            {
                Id = scene.AllocId(),
                Kind = Kinds.River,
                Points = riverPts,
                Width = 26 + N(seed, 203) * 14,
            }
        );

        // ── road: left edge to right edge through the map ──
        var roadPts = new List<Vector2>();
        var ry = size.Y * (0.3 + N(seed, 210) * 0.4);
        var rphase = N(seed, 211) * Math.PI * 2;
        for (var i = 0; i <= 6; i++)
        {
            var x = -40 + (size.X + 80) * i / 6;
            var y = ry + 50 * Math.Sin(rphase + i * 0.9);
            roadPts.Add(new Vector2((float)x, (float)y));
        }
        scene.Strokes.Add(
            new Stroke
            {
                Id = scene.AllocId(),
                Kind = Kinds.Road,
                Points = roadPts,
                Width = 14 + N(seed, 212) * 8,
            }
        );

        // ── forests: 2-4 elliptical patches on the lowlands ──
        var forests = 2 + (int)(N(seed, 220) * 3);
        for (var i = 0; i < forests; i++)
        {
            var center = new Vector2(
                (float)(size.X * (0.12 + N(seed, 240 + i) * 0.76)),
                (float)(size.Y * (0.15 + N(seed, 260 + i) * 0.7))
            );
            var rx2 = 90 + N(seed, 280 + i) * 120;
            var ry2 = 60 + N(seed, 300 + i) * 90;
            var patch = new List<Vector2>();
            for (var k = 0; k < 14; k++)
            {
                var t = k / 14.0 * Math.PI * 2;
                patch.Add(
                    new Vector2(
                        (float)(center.X + Math.Cos(t) * rx2 * (0.8 + 0.35 * N(seed, 320 + i * 31 + k))),
                        (float)(center.Y + Math.Sin(t) * ry2 * (0.8 + 0.35 * N(seed, 420 + i * 31 + k)))
                    )
                );
            }
            var mix = PropScatter.ScatterMix(
                patch,
                46,
                [(PropKind.Tree, 3.0), (PropKind.Pine, 1.6), (PropKind.Bush, 0.7)],
                density: 0.92,
                seed: seed + i * 101
            );
            foreach (var p in mix)
                scene.Props.Add(FromPlacement(scene, p));
        }

        // ── hamlet: houses clustered at a road point ──
        var hx = roadPts[2 + (int)(N(seed, 500) * 2)].X;
        var hy = roadPts[2 + (int)(N(seed, 501) * 2)].Y + 60;
        var houses = 3 + (int)(N(seed, 502) * 3);
        for (var i = 0; i < houses; i++)
        {
            var a = i / (double)houses * Math.PI * 2 + N(seed, 510 + i);
            var d = 30 + N(seed, 520 + i) * 55;
            scene.Props.Add(
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.House,
                    X = hx + Math.Cos(a) * d,
                    Y = hy + Math.Sin(a) * d * 0.7,
                    Scale = 24 + N(seed, 530 + i) * 8,
                    Rotation = N(seed, 540 + i) * 0.6 - 0.3,
                    Seed = seed * 13 + i,
                }
            );
        }

        // ── sprinkled rocks & bushes ──
        var scatter = 14 + (int)(N(seed, 600) * 10);
        for (var i = 0; i < scatter; i++)
        {
            scene.Props.Add(
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = N(seed, 610 + i) < 0.5 ? Kinds.Rock : Kinds.Bush,
                    X = size.X * N(seed, 620 + i * 3),
                    Y = size.Y * N(seed, 621 + i * 3),
                    Scale = 14 + N(seed, 622 + i * 3) * 12,
                    Seed = seed * 29 + i,
                }
            );
        }
        return scene;
    }

    /// <summary>G1.3 port — BSP partition into rooms joined by L-shaped
    /// floor corridors, doors where corridors pierce walls, archetyped
    /// furniture (hall / bedroom / storage / crypt).</summary>
    public static Scene GenerateDungeon(int seed, Vector2 size)
    {
        var scene = new Scene { Width = size.X, Height = size.Y, Name = $"Dungeon {seed}" };
        var rand = (int i) => N(seed, i);
        double density = 0.6;
        var x0 = size.X * 0.06;
        var y0 = size.Y * 0.06;
        var x1 = size.X * 0.94;
        var y1 = size.Y * 0.94;

        // BSP — recursive split until cells are room-sized.
        var maxDepth = 2 + (int)Math.Round(density * 2);
        var rooms = new List<Rect2>();
        var corridors = new List<(Vector2 A, Vector2 B)>();
        Vector2 Walk((double X0, double Y0, double X1, double Y1) node, int depth, ref int seq)
        {
            var w = node.X1 - node.X0;
            var h = node.Y1 - node.Y0;
            if (depth >= maxDepth || (w < 150 && h < 150))
            {
                var m = 7 + rand(seq++) * 9;
                var room = new Rect2(
                    (float)(node.X0 + m),
                    (float)(node.Y0 + m),
                    (float)(w - 2 * m),
                    (float)(h - 2 * m)
                );
                rooms.Add(room);
                return room.GetCenter();
            }
            if (w > h)
            {
                var cut = node.X0 + w * (0.38 + rand(seq++) * 0.24);
                var a = Walk((node.X0, node.Y0, cut, node.Y1), depth + 1, ref seq);
                var b = Walk((cut, node.Y0, node.X1, node.Y1), depth + 1, ref seq);
                corridors.Add((a, b));
                return a;
            }
            var cut2 = node.Y0 + h * (0.38 + rand(seq++) * 0.24);
            var la = Walk((node.X0, node.Y0, node.X1, cut2), depth + 1, ref seq);
            var rb = Walk((node.X0, cut2, node.X1, node.Y1), depth + 1, ref seq);
            corridors.Add((la, rb));
            return la;
        }
        var seq = 0;
        Walk((x0, y0, x1, y1), 0, ref seq);

        // Corridor floor rects — horizontal leg then vertical leg.
        const double cw = 26;
        foreach (var (a, b) in corridors)
        {
            var r1 = new Rect2(
                (float)(Math.Min(a.X, b.X) - cw / 2),
                (float)(a.Y - cw / 2),
                (float)(Math.Abs(b.X - a.X) + cw),
                (float)cw
            );
            var r2 = new Rect2(
                (float)(b.X - cw / 2),
                (float)(Math.Min(a.Y, b.Y) - cw / 2),
                (float)cw,
                (float)(Math.Abs(b.Y - a.Y) + cw)
            );
            foreach (var r in new[] { r1, r2 })
                scene.Shapes.Add(
                    new Shape
                    {
                        Id = scene.AllocId(),
                        Kind = Kinds.Floor,
                        Points = Rect(r),
                        Size = 22,
                    }
                );
        }
        foreach (var room in rooms)
            scene.Shapes.Add(
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Room,
                    Points = Rect(room),
                    Size = 20,
                    Height = 18,
                }
            );

        // Doors where a corridor pierces a room wall.
        void DoorAt(Rect2 room, double tx, double ty)
        {
            var c = room.GetCenter();
            var dx = tx - c.X;
            var dy = ty - c.Y;
            Vector2 pt =
                Math.Abs(dx) > Math.Abs(dy)
                    ? new Vector2(
                        (float)(dx > 0 ? room.End.X : room.Position.X),
                        (float)Math.Clamp(ty, room.Position.Y + 14, room.End.Y - 14)
                    )
                    : new Vector2(
                        (float)Math.Clamp(tx, room.Position.X + 14, room.End.X - 14),
                        (float)(dy > 0 ? room.End.Y : room.Position.Y)
                    );
            scene.Props.Add(
                new Prop { Id = scene.AllocId(), Kind = "door", X = pt.X, Y = pt.Y, Scale = 24 }
            );
        }
        bool Contains(Rect2 r, Vector2 p) =>
            p.X > r.Position.X && p.X < r.End.X && p.Y > r.Position.Y && p.Y < r.End.Y;
        foreach (var (a, b) in corridors)
        {
            var ra = rooms.Find(r => Contains(r, a));
            var rb = rooms.Find(r => Contains(r, b));
            if (ra.Size.X > 0)
                DoorAt(ra, b.X, a.Y);
            if (rb.Size.X > 0)
                DoorAt(rb, b.X, a.Y);
        }

        // Furniture archetypes — hall / bedroom / storage / crypt.
        for (var ri = 0; ri < rooms.Count; ri++)
        {
            var room = rooms[ri];
            var c = room.GetCenter();
            var w = room.Size.X;
            var h = room.Size.Y;
            if (w < 70 || h < 70)
                continue;
            var arche = (int)(N(seed + ri * 977, 0) * 4);
            void Item(string kind, double px, double py, double sz, int m = 0) =>
                scene.Props.Add(
                    new Prop
                    {
                        Id = scene.AllocId(),
                        Kind = kind,
                        X = px,
                        Y = py,
                        Scale = sz,
                        Seed = m,
                    }
                );
            switch (arche)
            {
                case 0:
                    Item("rug", c.X, c.Y, Math.Min(w, h) * 0.55);
                    Item("table", c.X, c.Y, Math.Min(w * 0.5, 90), 1);
                    Item("lantern", room.Position.X + 16, c.Y, 22, 2);
                    Item("lantern", room.End.X - 16, c.Y, 22, 2);
                    break;
                case 1:
                    Item("bed", room.Position.X + 34, room.Position.Y + 36, 56);
                    Item("rug", c.X, c.Y + h * 0.15, Math.Min(w, h) * 0.4, 1);
                    Item("chest", room.End.X - 24, room.End.Y - 24, 26);
                    Item("lantern", room.End.X - 16, room.Position.Y + 16, 22);
                    break;
                case 2:
                    Item("crate", room.Position.X + 20, room.Position.Y + 20, 26);
                    Item("crate", room.Position.X + 40, room.Position.Y + 24, 22);
                    Item("barrel", room.End.X - 22, room.Position.Y + 20, 22);
                    Item("shelf", c.X, room.Position.Y + 14, Math.Min(w * 0.5, 70));
                    Item("lantern", c.X, c.Y, 22, 2);
                    break;
                default:
                    Item("grave", c.X, c.Y, 30, 2);
                    Item("bones", room.Position.X + 26, room.End.Y - 22, 34);
                    Item("bones", room.End.X - 26, room.Position.Y + 22, 30, 1);
                    Item("lantern", room.Position.X + 16, room.Position.Y + 16, 22, 2);
                    break;
            }
        }
        return scene;
    }

    static List<Vector2> Rect(Rect2 r) =>
        [
            r.Position,
            new Vector2(r.End.X, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
        ];

    static Prop FromPlacement(Scene scene, PropPlacement p) =>
        new()
        {
            Id = scene.AllocId(),
            Kind = p.Kind switch
            {
                PropKind.Pine => "pine",
                PropKind.Rock => Kinds.Rock,
                PropKind.Bush => Kinds.Bush,
                PropKind.House => Kinds.House,
                _ => Kinds.Tree,
            },
            X = p.X,
            Y = p.Y,
            Scale = 22 * p.Scale,
            Rotation = p.Rotation,
            Seed = (int)p.Seed,
        };
}

// ── Region fill (TS generateFill — "encounter filler") ──────────────
public static class MapFill
{
    // Kinds a fill may clone across the region — lines, areas, utility
    // kinds and auto-generated structures are excluded (TS fillable).
    static readonly HashSet<string> Excluded =
        ["marker", "label", "waterfall", "dock", "camp", "portal", "door", "window"];

    static readonly HashSet<string> Solids =
        [Kinds.House, Kinds.Room, Kinds.Pond, Kinds.Field, Kinds.Scatter, Kinds.Floor];

    static bool Fillable(string kind) => !Excluded.Contains(kind) && !Solids.Contains(kind);

    // Companion decor — synthesized extras weighted as a fraction of the
    // source count so a pure forest still gets undergrowth (TS companions).
    static readonly Dictionary<string, (string Kind, double Factor)[]> Companions = new()
    {
        ["tree"] = [("bush", 0.4), ("rock", 0.2), ("crystal", 0.06), ("bones", 0.04)],
        ["bush"] = [("rock", 0.2), ("crystal", 0.05)],
        ["rock"] = [("bush", 0.18), ("crystal", 0.08)],
        ["crystal"] = [("rock", 0.2), ("bush", 0.12)],
        ["bones"] = [("rock", 0.15), ("crystal", 0.08)],
        ["boat"] = [("rock", 0.15)],
        ["tent"] = [("crate", 0.25), ("barrel", 0.2), ("bale", 0.18), ("bones", 0.06)],
        ["campfire"] = [("crate", 0.2), ("bale", 0.18), ("bones", 0.1)],
        ["house"] = [("crate", 0.2), ("barrel", 0.2), ("lantern", 0.12), ("cart", 0.08)],
        ["tower"] = [("barricade", 0.2), ("flag", 0.15), ("lantern", 0.1)],
        ["stall"] = [("crate", 0.25), ("barrel", 0.2), ("bale", 0.18)],
        ["well"] = [("barrel", 0.18), ("crate", 0.15), ("lantern", 0.1)],
        ["cart"] = [("crate", 0.25), ("bale", 0.2), ("barrel", 0.15)],
        ["fountain"] = [("lantern", 0.15), ("statue", 0.08)],
        ["statue"] = [("lantern", 0.12)],
        ["lantern"] = [("crate", 0.12)],
        ["grave"] = [("bones", 0.25), ("rock", 0.15), ("lantern", 0.08)],
        ["ruin"] = [("rock", 0.22), ("bones", 0.15), ("bush", 0.15)],
        ["stonecircle"] = [("bones", 0.2), ("crystal", 0.15), ("rock", 0.15)],
        ["chest"] = [("crate", 0.2), ("barrel", 0.15)],
        ["barricade"] = [("flag", 0.12), ("crate", 0.1)],
        ["flag"] = [("lantern", 0.1)],
    };

    static readonly Dictionary<string, double> CompanionSize = new()
    {
        ["bush"] = 30, ["rock"] = 22, ["crystal"] = 24, ["bones"] = 32,
        ["crate"] = 24, ["barrel"] = 20, ["bale"] = 30, ["lantern"] = 14,
        ["flag"] = 24, ["cart"] = 42, ["statue"] = 36, ["barricade"] = 52,
    };

    /// <summary>mulberry32 — sequential seeded rand like the TS fill.</summary>
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

    public readonly record struct FillResult(List<Prop> Props, List<int> SourceIds);

    /// <summary>Fill a region with a kind-weighted mix of the props found
    /// inside it plus companion decor, dodging existing props, strokes,
    /// solid shapes and massif tops.</summary>
    public static FillResult Generate(
        IReadOnlyList<Vector2> region,
        Scene scene,
        int seed,
        double density = 0.6
    )
    {
        var sources = scene
            .Props.Where(p => Fillable(p.Kind) && Poly.PointInPolygon(new Vector2((float)p.X, (float)p.Y), region))
            .ToList();
        if (sources.Count == 0 || region.Count < 3)
            return new FillResult([], []);
        var rand = new Mulberry(seed);
        var minX = region.Min(p => p.X);
        var minY = region.Min(p => p.Y);
        var maxX = region.Max(p => p.X);
        var maxY = region.Max(p => p.Y);

        // Kind-weighted palette: pick proportional to source counts.
        var byKind = sources.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.ToList());
        var table = byKind.Select(kv => (Kind: kv.Key, List: kv.Value, Weight: (double)kv.Value.Count)).ToList();
        var extra = new Dictionary<string, double>();
        foreach (var kv in byKind)
            if (Companions.TryGetValue(kv.Key, out var comps))
                foreach (var (companion, factor) in comps)
                    if (!byKind.ContainsKey(companion))
                        extra[companion] = extra.GetValueOrDefault(companion) + kv.Value.Count * factor;
        foreach (var (kind, weight) in extra)
        {
            if (weight < 0.15)
                continue;
            table.Add((kind, [new Prop { Kind = kind, Scale = CompanionSize.GetValueOrDefault(kind, 24) }], weight));
        }
        var total = table.Sum(e => e.Weight);
        Prop Pick()
        {
            var roll = rand.Next() * total;
            foreach (var e in table)
            {
                roll -= e.Weight;
                if (roll <= 0)
                    return e.List[(int)(rand.Next() * e.List.Count)];
            }
            return table[^1].List[0];
        }

        // Blockers.
        const double margin = 120;
        var props = scene
            .Props.Where(p =>
                Fillable(p.Kind)
                && p.X > minX - margin && p.X < maxX + margin
                && p.Y > minY - margin && p.Y < maxY + margin
            )
            .ToList();
        var lines = scene
            .Strokes.Where(s => s.Kind is Kinds.River or Kinds.Road or Kinds.Wall or Kinds.Fence)
            .ToList();
        var solids = scene
            .Shapes.Where(s => Solids.Contains(s.Kind))
            .Select(s => s.Points)
            .ToList();
        var massifs = TerrainGeometry.BuildMassifs(
            scene
                .Shapes.Where(s => s.Kind == Kinds.Terrain)
                .Select(s => new TerrainShape
                {
                    Model = s.Model,
                    Height = s.Height,
                    Size = s.Size,
                    Roughness = s.Roughness,
                    Outline = [.. s.Points],
                })
        );
        var tops = massifs.SelectMany(m => m.Tops).ToList();
        var spacing = Math.Max(26, 95 - density * 65);
        double Edge(Vector2 p) =>
            region.Select((a, i) => DistToSeg(p, a, region[(i + 1) % region.Count])).Min();

        var generated = new List<Prop>();
        for (var y = (double)minY; y <= maxY && generated.Count < 400; y += spacing)
            for (var x = (double)minX; x <= maxX && generated.Count < 400; x += spacing)
            {
                var point = new Vector2(
                    (float)(x + (rand.Next() - 0.5) * spacing * 1.15),
                    (float)(y + (rand.Next() - 0.5) * spacing * 1.15)
                );
                if (!Poly.PointInPolygon(point, region))
                    continue;
                var edge = Edge(point);
                if (edge < spacing * 0.25)
                    continue;
                if (edge < spacing && rand.Next() > edge / spacing)
                    continue;
                var sample = Pick();
                var size = Math.Max(8, sample.Scale * (0.8 + rand.Next() * 0.45));
                if (props.Any(p => new Vector2((float)p.X, (float)p.Y).DistanceTo(point) < (p.Scale + size) * 0.42))
                    continue;
                var blocked = false;
                foreach (var line in lines)
                {
                    for (var i = 1; i < line.Points.Count && !blocked; i++)
                        if (DistToSeg(point, line.Points[i - 1], line.Points[i]) <= line.Width / 2 + size * 0.5)
                            blocked = true;
                    if (blocked)
                        break;
                }
                if (blocked)
                    continue;
                if (
                    !Interactions.WaterKind(sample.Kind)
                    && Interactions.InWater(scene, point, size * 0.35)
                )
                    continue;
                if (solids.Any(poly => Poly.PointInPolygon(point, poly)))
                    continue;
                if (tops.Any(top => Poly.PointInPolygon(point, top)))
                    continue;
                generated.Add(
                    new Prop
                    {
                        Kind = sample.Kind,
                        X = point.X,
                        Y = point.Y,
                        Scale = size,
                        Rotation = rand.Next() * Math.PI * 2,
                        Seed = (int)(point.X * 0.37 + point.Y * 0.53),
                    }
                );
            }
        return new FillResult(generated, sources.Select(s => s.Id).ToList());
    }

    static double DistToSeg(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 < 1e-6 ? 0 : Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
        return (a + ab * (float)t - p).Length();
    }
}

public static partial class MapGenerator
{
    /// <summary>Generate a landscape/dungeon inside an arbitrary map
    /// region — builds at the region size then offsets into place
    /// (the web fill tool's "map"/"dungeon" modes).</summary>
    public static Scene GenerateIn(int seed, Rect2 region, bool dungeon)
    {
        var s = dungeon
            ? GenerateDungeon(seed, region.Size)
            : GenerateLandscape(seed, region.Size);
        Offset(s, region.Position);
        return s;
    }

    static void Offset(Scene s, Vector2 off)
    {
        foreach (var sh in s.Shapes)
            for (var i = 0; i < sh.Points.Count; i++)
                sh.Points[i] += off;
        foreach (var st in s.Strokes)
            for (var i = 0; i < st.Points.Count; i++)
                st.Points[i] += off;
        foreach (var p in s.Props)
        {
            p.X += off.X;
            p.Y += off.Y;
        }
    }

    /// <summary>Append a generated scene's elements into `dst`,
    /// re-allocating ids so nothing collides (one commit = one undo).</summary>
    public static void MergeInto(Scene dst, Scene gen)
    {
        foreach (var sh in gen.Shapes)
        {
            sh.Id = dst.AllocId();
            dst.Shapes.Add(sh);
        }
        foreach (var st in gen.Strokes)
        {
            st.Id = dst.AllocId();
            dst.Strokes.Add(st);
        }
        foreach (var p in gen.Props)
        {
            p.Id = dst.AllocId();
            dst.Props.Add(p);
        }
    }
}
