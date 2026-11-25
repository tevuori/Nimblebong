#nullable enable
// Procedural room furnishing — seeds a room shape with real, movable
// furniture props (tables, chairs, beds, shelves, crates…) in one
// commit step. Everything is deterministic: the layout depends only on
// the room id + footprint, so undo/redo and save/load reproduce the
// same arrangement. Placement is rejection sampling against the
// footprint polygon with per-kind collision radii; wall furniture
// (beds, shelves, fireplaces) hugs the longest edges and faces inward,
// tables sit on a central rug with chairs pulled up, and storage props
// fill the corners.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public static class RoomFurnish
{
    // Collision radius (px) per kind — how much floor space the piece
    // claims. Rugs are flat and excluded from collision so furniture
    // can stand on them.
    static readonly Dictionary<string, double> Radii = new()
    {
        ["table"] = 30,
        ["bed"] = 34,
        ["shelf"] = 24,
        ["fireplace"] = 22,
        ["chair"] = 11,
        ["crate"] = 15,
        ["barrel"] = 13,
        ["chest"] = 17,
        ["candle"] = 5,
        ["lantern"] = 9,
    };

    struct Rand
    {
        int _i;
        readonly double _s;
        public Rand(double seed) => (_s, _i) = (seed, 0);
        public double Next() => Rng.Noise(_s + _i++ * 17.31);
        public double Range(double a, double b) => a + Next() * (b - a);
        public int Pick(int n) => (int)Math.Floor(Next() * n) % n;
        public bool Chance(double p) => Next() < p;
    }

    /// <summary>Add furniture props for `room` into `scene` (mutating a
    /// commit clone). No-op for tiny closets under ~70 px across.</summary>
    public static void Furnish(Scene scene, Shape room)
    {
        var pts = room.Points;
        if (pts.Count < 3)
            return;
        double x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var q in pts)
        {
            x0 = Math.Min(x0, q.X);
            x1 = Math.Max(x1, q.X);
            y0 = Math.Min(y0, q.Y);
            y1 = Math.Max(y1, q.Y);
        }
        var w = x1 - x0;
        var h = y1 - y0;
        if (w < 70 || h < 70)
            return;
        var area = w * h;
        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;
        var rand = new Rand(room.Id * 3.7 + 11);

        // Door positions — keep a swing radius clear in front of them.
        var doors = scene
            .Props.Where(p =>
                p.Kind == "door"
                && p.X > x0 - 30 && p.X < x1 + 30 && p.Y > y0 - 30 && p.Y < y1 + 30
            )
            .Select(p => new Vector2((float)p.X, (float)p.Y))
            .ToList();

        var placed = new List<(Vector2 Pos, double R)>();

        bool Clear(Vector2 pos, double r)
        {
            if (!Inside(pos, r * 0.7))
                return false;
            foreach (var d in doors)
                if ((pos - d).Length() < r + 40)
                    return false;
            foreach (var (pp, pr) in placed)
                if ((pos - pp).Length() < r + pr + 4)
                    return false;
            return true;
        }

        bool Inside(Vector2 pos, double inset)
        {
            if (!Poly.PointInPolygon(pos, pts))
                return false;
            if (inset <= 0)
                return true;
            // Cheap inset test: nudge in 4 directions — good enough for
            // furniture that must not poke through walls.
            foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
                if (!Poly.PointInPolygon(pos + new Vector2((float)(dx * inset), (float)(dy * inset)), pts))
                    return false;
            return true;
        }

        Prop Put(string kind, Vector2 pos, double rot, double scale, int model = -1)
        {
            var prop = new Prop
            {
                Id = scene.AllocId(),
                Kind = kind,
                X = pos.X,
                Y = pos.Y,
                Scale = scale,
                Rotation = rot,
                Seed = (int)(pos.X * 3 + pos.Y * 7),
                Model = model,
            };
            scene.Props.Add(prop);
            if (Radii.TryGetValue(kind, out var r))
                placed.Add((pos, r * scale / 24));
            return prop;
        }

        bool TryPut(string kind, Vector2 pos, double rot, double scale)
        {
            var r = Radii.GetValueOrDefault(kind, 10) * scale / 24;
            if (!Clear(pos, r))
                return false;
            Put(kind, pos, rot, scale);
            return true;
        }

        // Wall placement: walk the longest edges, sit `inset` px inside
        // the wall, face the room interior (perpendicular).
        IEnumerable<(Vector2 Pos, double Rot)> WallSpots(double inset)
        {
            var edges = new List<(Vector2 A, Vector2 B, double Len)>();
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                edges.Add((a, b, (b - a).Length()));
            }
            foreach (var (a, b, len) in edges.OrderByDescending(e => e.Len))
            {
                if (len < inset * 3)
                    continue;
                var dir = (b - a) / (float)len;
                var centroid = Poly.Centroid(pts);
                // Normal pointing toward the interior — test both sides.
                var n1 = new Vector2(-dir.Y, dir.X);
                var mid = (a + b) / 2;
                var inward = (mid + n1 - centroid).Length() < (mid - n1 - centroid).Length() ? n1 : -n1;
                var rot = Math.Atan2(inward.Y, inward.X) + Math.PI / 2;
                for (var t = 0.25; t < 0.95; t += rand.Range(0.18, 0.3))
                {
                    var pos = a + (b - a) * (float)t + inward * (float)inset;
                    yield return (pos, rot);
                }
            }
        }

        bool TryWall(string kind, double inset, double scale, int tries = 8)
        {
            foreach (var (pos, rot) in WallSpots(inset).Take(tries))
                if (TryPut(kind, pos, rot, scale))
                    return true;
            return false;
        }

        bool TryNear(string kind, Vector2 center, double radius, double scale, int tries = 10, double rotJitter = 0.4, bool faceCenter = false)
        {
            for (var i = 0; i < tries; i++)
            {
                var a = rand.Range(0, Math.PI * 2);
                var d = rand.Range(0, radius);
                var pos = center + new Vector2((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d));
                var rot = faceCenter
                    ? Math.Atan2(center.Y - pos.Y, center.X - pos.X) - Math.PI / 2 + rand.Range(-0.2, 0.2)
                    : rand.Range(-rotJitter, rotJitter);
                if (TryPut(kind, pos, rot, scale))
                    return true;
            }
            return false;
        }

        var center = new Vector2((float)cx, (float)cy);

        // ── layout by floor area ─────────────────────────────────────
        if (area < 12000)
        {
            // Cramped study / cell — rug, small table, chair, candle.
            Put("rug", center, 0, Math.Clamp(Math.Min(w, h) * 0.22, 26, 90), model: 0);
            if (TryNear("table", center, Math.Min(w, h) * 0.15, 24))
            {
                TryNear("chair", center, 34, 23, faceCenter: true);
                TryNear("candle", center, 18, 12, rotJitter: 0);
            }
        }
        else if (area < 40000)
        {
            // Bedroom / common room — rug + table + chairs in the
            // middle, bed and shelf on the walls, crate in a corner.
            Put("rug", center, 0, Math.Clamp(Math.Min(w, h) * 0.25, 28, 100), model: 0);
            TryNear("table", center, Math.Min(w, h) * 0.12, 30);
            var chairs = 2 + rand.Pick(2);
            for (var i = 0; i < chairs; i++)
                TryNear("chair", center, 44, 23, faceCenter: true);
            TryWall("bed", 28, 29);
            if (rand.Chance(0.8))
                TryWall("shelf", 19, 26);
            if (rand.Chance(0.6))
                TryWall("fireplace", 21, 26);
            else
                TryWall("lantern", 15, 20);
            if (rand.Chance(0.7))
                TryWall(rand.Chance(0.5) ? "crate" : "barrel", 17, 22);
            if (rand.Chance(0.5))
                TryNear("candle", center, 16, 12, rotJitter: 0);
        }
        else
        {
            // Hall / large chamber — big central table with chairs
            // around, beds and shelves along walls, storage corners.
            Put("rug", center, 0, Math.Clamp(Math.Min(w, h) * 0.28, 30, 110), model: 0);
            TryNear("table", center, Math.Min(w, h) * 0.1, 36);
            var chairs = 3 + rand.Pick(3);
            for (var i = 0; i < chairs; i++)
                TryNear("chair", center, 52, 23, faceCenter: true);
            TryWall("bed", 28, 29);
            if (w * h > 70000)
                TryWall("bed", 28, 29);
            TryWall("shelf", 19, 26);
            if (rand.Chance(0.8))
                TryWall("shelf", 19, 26);
            TryWall("fireplace", 21, 26);
            TryWall("chest", 19, 24);
            TryWall("crate", 17, 22);
            TryWall("barrel", 16, 22);
            if (rand.Chance(0.7))
                TryWall("lantern", 15, 20);
            TryNear("candle", center, 20, 12, rotJitter: 0);
        }
    }
}
