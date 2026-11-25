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
        // Landscape archetypes — terrain+water only, no props or
        // buildings: the user picks a base landform and builds on it.
        // Each archetype arranges massifs into a coherent structure
        // (valley walls, ridge spines, plateaus) instead of scattered
        // blobs, and routes water along the low lines the terrain
        // implies. Terraces form naturally because same-height shapes
        // merge into one massif while different heights stack.
        var scene = new Scene { Width = size.X, Height = size.Y, Name = $"Mapa {seed}" };
        var W = size.X;
        var H = size.Y;
        var archetype = (int)(N(seed, 0) * 5);

        // ── shared helpers ──────────────────────────────────────────

        // Noise-wandered path across the map — cumulative drift, not a
        // sine wave, so rivers read as terrain-following.
        List<Vector2> Wander(double x0, double y0, double x1, double y1, int segs, double drift, int s0)
        {
            var pts = new List<Vector2>();
            var off = 0.0;
            for (var i = 0; i <= segs; i++)
            {
                var t = i / (double)segs;
                off += (N(seed, s0 + i) - 0.5) * drift;
                off *= 0.86; // pull back so the path does not random-walk off
                pts.Add(
                    new Vector2(
                        (float)(x0 + (x1 - x0) * t + off * (y1 - y0 == 0 ? 1 : 0)),
                        (float)(y0 + (y1 - y0) * t + off)
                    )
                );
            }
            return pts;
        }

        // Wobbly blob outline — shared by hills and ponds.
        List<Vector2> Blob(double cx, double cy, double rx, double ry, int s0, int n = 14)
        {
            var pts = new List<Vector2>();
            var lobes = 2 + (int)(N(seed, s0 + 1) * 3);
            for (var k = 0; k < n; k++)
            {
                var t = k / (double)n * Math.PI * 2;
                var r = 0.78 + 0.34 * N(seed, s0 + 10 + k);
                pts.Add(
                    new Vector2(
                        (float)(cx + Math.Cos(t) * rx * r * (1 + 0.18 * Math.Cos(t * lobes))),
                        (float)(cy + Math.Sin(t) * ry * r)
                    )
                );
            }
            return pts;
        }

        // Elongated capsule-ish polygon along a polyline — the ridge
        // building block. Terrain only adds height, so valleys exist
        // as the low corridors left between ridges.
        List<Vector2> RidgePoly(List<Vector2> spine, double width)
        {
            var left = new List<Vector2>();
            var right = new List<Vector2>();
            for (var i = 0; i < spine.Count; i++)
            {
                var a = spine[Math.Max(0, i - 1)];
                var b = spine[Math.Min(spine.Count - 1, i + 1)];
                var d = (b - a).Normalized();
                var nrm = new Vector2(-d.Y, d.X);
                left.Add(spine[i] + nrm * (float)(width / 2));
                right.Add(spine[i] - nrm * (float)(width / 2));
            }
            right.Reverse();
            left.AddRange(right);
            return left;
        }

        void Terrain(List<Vector2> pts, double height, double size, double rough, int model)
        {
            scene.Shapes.Add(
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Terrain,
                    Points = pts,
                    Height = height,
                    Size = size,
                    Roughness = rough,
                    Model = model,
                }
            );
        }

        void River(List<Vector2> pts, double width)
        {
            scene.Strokes.Add(
                new Stroke { Id = scene.AllocId(), Kind = Kinds.River, Points = pts, Width = width }
            );
        }

        void Pond(List<Vector2> pts)
        {
            scene.Shapes.Add(
                new Shape { Id = scene.AllocId(), Kind = Kinds.Pond, Points = pts, Size = 24 }
            );
        }

        // A terraced ridge: stacked elongated shapes along the same
        // spine — low broad foothill, higher core, optional craggy
        // summit. Heights descend outward so the slope reads as a
        // coherent escarpment, not stacked pancakes: the widest shape
        // is lowest, the narrowest highest.
        void TerracedRidge(List<Vector2> spine, double baseWidth, double topHeight, int s0, bool cliffSide = false)
        {
            var rough = 20 + N(seed, s0 + 2) * 55;
            var mid = topHeight * (0.45 + N(seed, s0 + 3) * 0.15);
            var low = topHeight * (0.2 + N(seed, s0 + 4) * 0.1);
            var m1 = (int)(N(seed, s0 + 5) * 2); // slope texture of the low steps
            // Foothill skirt — wide, low, soft slope.
            Terrain(RidgePoly(spine, baseWidth * 1.7), low, 40 + N(seed, s0 + 6) * 22, rough * 0.5, m1);
            // Mid terrace.
            Terrain(RidgePoly(spine, baseWidth * 1.15), mid, 30 + N(seed, s0 + 7) * 16, rough * 0.7, m1);
            // Summit — rocky ridge texture; a narrow slope band makes
            // the downhill read as a cliff face.
            Terrain(
                RidgePoly(spine, baseWidth * 0.62),
                topHeight,
                cliffSide ? 14 + N(seed, s0 + 8) * 8 : 24 + N(seed, s0 + 8) * 14,
                rough,
                2
            );
        }

        // Small foothill blobs scattered along a line — the transition
        // zone that keeps big massifs from sitting on bare flatland.
        void Foothills(List<Vector2> along, double dist, int count, int s0)
        {
            for (var i = 0; i < count; i++)
            {
                var t = N(seed, s0 + i * 3);
                var idx = Math.Min(along.Count - 1, (int)(t * along.Count));
                var c = along[idx];
                var side = N(seed, s0 + i * 3 + 1) < 0.5 ? -1 : 1;
                var cx = c.X + (N(seed, s0 + i * 3 + 2) - 0.5) * 130;
                var cy = c.Y + side * dist * (0.7 + N(seed, s0 + i * 7) * 0.6);
                Terrain(
                    Blob(cx, cy, 46 + N(seed, s0 + i * 11) * 60, 38 + N(seed, s0 + i * 13) * 44, s0 + i * 40),
                    8 + N(seed, s0 + i * 17) * 14,
                    30 + N(seed, s0 + i * 19) * 20,
                    15 + N(seed, s0 + i * 23) * 30,
                    1
                );
            }
        }

        // Gentle undulation everywhere — a few very low, very broad
        // swells in the corners so even the "flat" land has a pulse.
        for (var i = 0; i < 4; i++)
        {
            var ux = W * (0.1 + N(seed, 900 + i * 3) * 0.8);
            var uy = H * (0.1 + N(seed, 901 + i * 3) * 0.8);
            Terrain(
                Blob(ux, uy, 130 + N(seed, 902 + i * 3) * 120, 100 + N(seed, 903 + i * 3) * 90, 905 + i * 31),
                5 + N(seed, 904 + i * 3) * 8,
                60 + N(seed, 906 + i) * 30,
                10 + N(seed, 907 + i) * 20,
                1
            );
        }

        switch (archetype)
        {
            // ── river valley: terraced walls flanking a wander valley ──
            case 0:
            {
                var valley = Wander(-60, H * (0.3 + N(seed, 700) * 0.4), W + 60, H * (0.3 + N(seed, 701) * 0.4), 9, 130, 710);
                var width = 150 + N(seed, 720) * 100;
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    var off = valley.Select(p => new Vector2(p.X, (float)(p.Y + side * width))).ToList();
                    TerracedRidge(off, 130 + N(seed, 730 + (int)side) * 70, 38 + N(seed, 740) * 30, 750 + (int)(side + 1) * 50, cliffSide: true);
                    Foothills(off, 60, 6, 800 + (int)(side + 1) * 60);
                }
                River(valley, 24 + N(seed, 760) * 14);
                // Tributaries sliding down the valley walls.
                var tribs = 1 + (int)(N(seed, 770) * 2);
                for (var i = 0; i < tribs; i++)
                {
                    var t = 0.25 + N(seed, 780 + i) * 0.5;
                    var idx = (int)(t * (valley.Count - 1));
                    var start = valley[idx];
                    var side = N(seed, 785 + i) < 0.5 ? -1 : 1;
                    var up = new List<Vector2>
                    {
                        new(start.X + (float)(N(seed, 786) - 0.5) * 60, (float)(start.Y + side * (width + 150))),
                        new(start.X + (float)(N(seed, 787) - 0.5) * 40, (float)(start.Y + side * width * 0.6)),
                        start,
                    };
                    River(up, 9 + N(seed, 788 + i) * 6);
                }
                break;
            }

            // ── ridge spine: a diagonal crag with a cliff face ──
            case 1:
            {
                var diag = N(seed, 700) < 0.5;
                var spine = diag
                    ? Wander(-60, H * 0.2, W + 60, H * 0.75, 8, 90, 710)
                    : Wander(-60, H * 0.8, W + 60, H * 0.25, 8, 90, 710);
                TerracedRidge(spine, 150 + N(seed, 720) * 80, 48 + N(seed, 730) * 32, 740, cliffSide: true);
                Foothills(spine, 110, 7, 800);
                // A couple of secondary crags echoing the main spine.
                var echoes = 1 + (int)(N(seed, 750) * 2);
                for (var i = 0; i < echoes; i++)
                {
                    var cx = W * (0.15 + N(seed, 760 + i) * 0.7);
                    var cy = H * (diag ? 0.72 : 0.28) + (N(seed, 765 + i) - 0.5) * H * 0.2;
                    Terrain(Blob(cx, cy, 90 + N(seed, 770 + i) * 70, 70 + N(seed, 775 + i) * 50, 780 + i * 30), 20 + N(seed, 782 + i) * 22, 30 + N(seed, 784) * 14, 30 + N(seed, 786) * 40, 2);
                }
                // Lowland river bending around the spine's end.
                var low = diag ? H * 0.86 : H * 0.14;
                var rv = Wander(-40, low - 40 * N(seed, 790), W + 40, low + 60 * (N(seed, 791) - 0.5), 8, 110, 792);
                River(rv, 22 + N(seed, 793) * 12);
                // Streams off the steep face — cascades where they
                // cross the terrace edges.
                for (var i = 0; i < 2; i++)
                {
                    var idx = 2 + (int)(N(seed, 795 + i) * (spine.Count - 4));
                    var c = spine[idx];
                    var down = diag ? 1 : -1;
                    River(
                        [
                            new(c.X, c.Y),
                            new(c.X + (float)(N(seed, 797 + i) - 0.5) * 90, (float)(c.Y + down * 130)),
                            new(c.X + (float)(N(seed, 798 + i) - 0.5) * 140, (float)(c.Y + down * 260)),
                        ],
                        9 + N(seed, 799 + i) * 5
                    );
                }
                break;
            }

            // ── lake bowl: ring hills around a central water ──
            case 2:
            {
                var cx = W * (0.4 + N(seed, 700) * 0.2);
                var cy = H * (0.42 + N(seed, 701) * 0.16);
                var ringR = 210 + N(seed, 702) * 90;
                var hills = 5 + (int)(N(seed, 703) * 3);
                for (var i = 0; i < hills; i++)
                {
                    var a = i / (double)hills * Math.PI * 2 + N(seed, 710 + i) * 0.5;
                    // Leave a gap in the ring where the outlet stream exits.
                    if (Math.Abs(Math.IEEERemainder(a - 0.4, Math.PI * 2)) < 0.5)
                        continue;
                    var hx = cx + Math.Cos(a) * ringR * (0.85 + N(seed, 720 + i) * 0.4);
                    var hy = cy + Math.Sin(a) * ringR * 0.72 * (0.85 + N(seed, 730 + i) * 0.4);
                    var spine = new List<Vector2>
                    {
                        new((float)(hx - 60), (float)(hy - 30)),
                        new((float)hx, (float)hy),
                        new((float)(hx + 60), (float)(hy + 30)),
                    };
                    TerracedRidge(spine, 90 + N(seed, 740 + i) * 50, 26 + N(seed, 750 + i) * 30, 760 + i * 40);
                }
                Pond(Blob(cx, cy, 110 + N(seed, 770) * 60, 80 + N(seed, 771) * 40, 775, 18));
                // Outlet stream draining the lake to the nearest edge.
                var outlet = Wander(cx + ringR * 0.5, cy, W + 60, cy + (N(seed, 780) - 0.5) * H * 0.5, 5, 90, 782);
                outlet[0] = new Vector2((float)(cx + 60), (float)cy);
                River(outlet, 14 + N(seed, 784) * 8);
                break;
            }

            // ── plateau: high tableland with a cliff edge over lowland ──
            case 3:
            {
                var horizontal = N(seed, 700) < 0.6;
                var hMain = 42 + N(seed, 701) * 26;
                // The plateau body — one huge multi-lobe shape, steep band.
                List<Vector2> body;
                if (horizontal)
                {
                    var edge = H * (0.3 + N(seed, 702) * 0.25);
                    body = Wander(-60, -60, W + 60, -60, 1, 0, 710);
                    // Build the outline manually: top strip down to a
                    // wandering cliffline.
                    var cliff = Wander(-60, edge, W + 60, edge + (N(seed, 703) - 0.5) * 90, 9, 70, 711);
                    body = [new Vector2(-60, -60), new Vector2(W + 60, -60)];
                    for (var i = cliff.Count - 1; i >= 0; i--)
                        body.Add(cliff[i]);
                }
                else
                {
                    // Vertical escarpment — plateau fills the left strip,
                    // its cliff edge wanders down the map.
                    var edge = W * (0.55 + N(seed, 702) * 0.2);
                    var cliff = Wander(edge, -60, edge + (N(seed, 703) - 0.5) * 90, H + 60, 9, 70, 711);
                    body = [new Vector2(-60, -60), new Vector2(-60, H + 60)];
                    for (var i = cliff.Count - 1; i >= 0; i--)
                        body.Add(new Vector2(cliff[i].X, cliff[i].Y));
                }
                Terrain(body, hMain, 16 + N(seed, 704) * 10, 25 + N(seed, 705) * 45, 0);
                // Foothill debris along the cliff base.
                var baseLine = horizontal
                    ? Wander(-60, H * 0.62, W + 60, H * 0.62, 4, 40, 712)
                    : Wander(W * 0.4, -60, W * 0.4, H + 60, 4, 40, 712);
                Foothills(baseLine, 60, 7, 720);
                // Lowland river below the cliff.
                if (horizontal)
                    River(Wander(-40, H * (0.68 + N(seed, 730) * 0.15), W + 40, H * (0.66 + N(seed, 731) * 0.2), 8, 100, 732), 22 + N(seed, 733) * 12);
                else
                    River(Wander(W * (0.62 + N(seed, 730) * 0.2), -40, W * (0.6 + N(seed, 731) * 0.25), H + 40, 8, 100, 732), 22 + N(seed, 733) * 12);
                // A waterfall stream spilling off the plateau edge.
                if (N(seed, 740) < 0.7)
                {
                    var sx = horizontal ? W * (0.3 + N(seed, 741) * 0.4) : W * 0.55;
                    if (horizontal)
                        River(
                            [
                                new((float)sx, (float)(H * 0.28)),
                                new((float)(sx + (N(seed, 742) - 0.5) * 60), (float)(H * 0.45)),
                                new((float)(sx + (N(seed, 743) - 0.5) * 90), (float)(H * 0.66)),
                            ],
                            10 + N(seed, 744) * 5
                        );
                }
                // Occasionally a lone mesa out in the lowland.
                if (N(seed, 750) < 0.6)
                {
                    var mx = horizontal ? W * (0.2 + N(seed, 751) * 0.6) : W * (0.75 + N(seed, 751) * 0.15);
                    var my = horizontal ? H * (0.7 + N(seed, 752) * 0.2) : H * (0.3 + N(seed, 752) * 0.4);
                    Terrain(Blob(mx, my, 70 + N(seed, 753) * 40, 55 + N(seed, 754) * 30, 756), hMain * 0.55, 15 + N(seed, 757) * 8, 40 + N(seed, 758) * 35, 2);
                }
                break;
            }

            // ── rolling hill country: chained ridges + braided valley ──
            default:
            {
                var chains = 3 + (int)(N(seed, 700) * 2);
                for (var c = 0; c < chains; c++)
                {
                    var y0 = H * (0.15 + 0.7 * c / Math.Max(1, chains - 1) + (N(seed, 710 + c) - 0.5) * 0.12);
                    var spine = Wander(-60, y0, W + 60, y0 + (N(seed, 720 + c) - 0.5) * H * 0.3, 7, 90, 730 + c * 20);
                    // Break each chain into 2-3 overlapping ridge
                    // segments so the skyline undulates.
                    var segs = 2 + (int)(N(seed, 740 + c) * 2);
                    for (var g = 0; g < segs; g++)
                    {
                        var i0 = (int)(g / (double)segs * (spine.Count - 2));
                        var i1 = Math.Min(spine.Count - 1, i0 + 2 + (int)(N(seed, 750 + c * 7 + g) * 2));
                        var piece = spine.Skip(i0).Take(i1 - i0 + 1).ToList();
                        if (piece.Count < 2)
                            continue;
                        TerracedRidge(piece, 100 + N(seed, 760 + c * 5 + g) * 60, 24 + N(seed, 770 + c * 5 + g) * 34, 780 + c * 60 + g * 20);
                    }
                    Foothills(spine, 80, 5, 790 + c * 30);
                }
                // River threading the main valley between the chains.
                var vy = H * (0.35 + N(seed, 795) * 0.3);
                River(Wander(-40, vy, W + 40, vy + (N(seed, 796) - 0.5) * H * 0.25, 9, 130, 797), 20 + N(seed, 798) * 14);
                // And often a still pond in a side hollow.
                if (N(seed, 799) < 0.5)
                    Pond(Blob(W * (0.2 + N(seed, 800) * 0.6), H * (0.25 + N(seed, 801) * 0.5), 60 + N(seed, 802) * 40, 44 + N(seed, 803) * 26, 804));
                break;
            }
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
