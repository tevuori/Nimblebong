#nullable enable
// Interiors — house / room / floor area shapes painted into the albedo
// viewport. Port of the web `building`/`floorArea`/`roomDecor` plus the
// whole `scene-roofs` pass: a south-extruded wall under the footprint,
// a roof laid out in a rotated frame (ridge along the longer side,
// gable / hip / thatch faces, tiles and shingles in jittered tones,
// chimney + moss + biome extras), a hatched room floor with procedural
// furniture, and the wall ring — 9px wall runs along each edge, carved
// by door/window props anchored within 16px of the edge.
// Node2D has no canvas clip: every interior detail is buffered and
// emitted through `SegRuns`/`IntersectPolygons` against the footprint.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class InteriorPainter : Node2D
{
    /// <summary>Palette + biome id (roofs.* config, swamp stilts).</summary>
    public ResolvedBiome? Biome;
    /// <summary>Inked stroke multiplier — the web `inkMul`.</summary>
    public double Ink = 1;

    /// <summary>Ids of door/window props that anchored to a room edge —
    /// the scene painter should skip their standalone stamps.</summary>
    public readonly HashSet<int> AnchoredPropIds = [];

    static readonly HashSet<string> FurnitureKinds =
        ["table", "bed", "shelf", "rug", "fireplace"];

    readonly List<(Shape S, List<Prop> Props)> _items = [];

    public void Add(Shape s, IReadOnlyList<Prop>? props = null)
    {
        if (s.Points.Count < 3 || s.Kind is not (Kinds.House or Kinds.Room or Kinds.Floor))
            return;
        var relevant = new List<Prop>();
        if (s.Kind == Kinds.Room && props != null)
        {
            var (x0, x1, y0, y1) = Bounds(s.Points);
            foreach (var p in props)
            {
                var at = new Vector2((float)p.X, (float)p.Y);
                if (p.Kind is "door" or "window")
                {
                    if (at.X < x0 - 28 || at.X > x1 + 28 || at.Y < y0 - 28 || at.Y > y1 + 28)
                        continue;
                    relevant.Add(p);
                    if (NearEdge(s.Points, at, Math.Max(14, p.Scale * 0.5)))
                        AnchoredPropIds.Add(p.Id);
                }
                else if (FurnitureKinds.Contains(p.Kind) && Poly.PointInPolygon(at, s.Points))
                    relevant.Add(p);
            }
        }
        _items.Add((s, relevant));
    }

    static (double X0, double X1, double Y0, double Y1) Bounds(IReadOnlyList<Vector2> pts)
    {
        double x0 = float.MaxValue,
            x1 = float.MinValue,
            y0 = float.MaxValue,
            y1 = float.MinValue;
        foreach (var p in pts)
        {
            x0 = Math.Min(x0, p.X);
            x1 = Math.Max(x1, p.X);
            y0 = Math.Min(y0, p.Y);
            y1 = Math.Max(y1, p.Y);
        }
        return (x0, x1, y0, y1);
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    void Stroke(Vector2[] poly, Color color, float width)
    {
        if (poly.Length < 2)
            return;
        var closed = new Vector2[poly.Length + 1];
        Array.Copy(poly, closed, poly.Length);
        closed[^1] = poly[0];
        DrawPolyline(closed, color, width, true);
    }

    static double DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var d = b - a;
        var t = Math.Clamp((p - a).Dot(d) / (d.LengthSquared() == 0 ? 1 : d.LengthSquared()), 0f, 1f);
        return (p - a - d * (float)t).Length();
    }

    static bool NearEdge(IReadOnlyList<Vector2> pts, Vector2 p, double reach)
    {
        for (var i = 0; i < pts.Count; i++)
            if (DistanceToSegment(p, pts[i], pts[(i + 1) % pts.Count]) < reach)
                return true;
        return false;
    }

    /// <summary>Contiguous inside-the-polygon runs of segment a→b —
    /// param intervals (t in 0..1) found by collecting all boundary
    /// crossings and midpoint-testing each slice.</summary>
    static List<(double T0, double T1)> SegRuns(Vector2 a, Vector2 b, IReadOnlyList<Vector2> poly)
    {
        var ts = new List<double> { 0.0, 1.0 };
        var d = b - a;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var e = poly[i] - poly[j];
            var den = d.X * e.Y - d.Y * e.X;
            if (Math.Abs(den) < 1e-9)
                continue;
            var t = ((poly[j].X - a.X) * e.Y - (poly[j].Y - a.Y) * e.X) / den;
            var s = ((poly[j].X - a.X) * d.Y - (poly[j].Y - a.Y) * d.X) / den;
            if (t is > 1e-6f and < 0.999999f && s is >= -1e-9f and <= 1.0000001f)
                ts.Add(t);
        }
        ts.Sort();
        var runs = new List<(double, double)>();
        for (var k = 0; k + 1 < ts.Count; k++)
        {
            var mid = a + d * (float)((ts[k] + ts[k + 1]) * 0.5);
            if (ts[k + 1] - ts[k] > 1e-6 && Poly.PointInPolygon(mid, poly))
                runs.Add((ts[k], ts[k + 1]));
        }
        return runs;
    }

    // Deferred clipped drawing — details are queued, then each stroke is
    // split into inside-the-footprint runs and each fill intersected by
    // the clipper, preserving the TS paint order.
    readonly record struct ClipPrim(Vector2[]? Poly, Vector2 A, Vector2 B, Color C, float W);

    readonly List<ClipPrim> _clip = [];

    void ClipLine(Vector2 a, Vector2 b, Color c, float w) => _clip.Add(new(null, a, b, c, w));

    void ClipFill(Vector2[] poly, Color c) => _clip.Add(new(poly, default, default, c, 0));

    void FlushClipped(Vector2[] poly)
    {
        foreach (var prim in _clip)
        {
            if (prim.Poly != null)
                foreach (var piece in Geometry2D.IntersectPolygons(prim.Poly, poly))
                    Fill(piece, prim.C);
            else
                foreach (var (t0, t1) in SegRuns(prim.A, prim.B, poly))
                    DrawLine(
                        prim.A + (prim.B - prim.A) * (float)t0,
                        prim.A + (prim.B - prim.A) * (float)t1,
                        prim.C,
                        prim.W
                    );
        }
        _clip.Clear();
    }

    public override void _Draw()
    {
        foreach (var (s, props) in _items)
        {
            if (s.Kind == Kinds.Floor)
                DrawFloor(s);
            else
                DrawBuilding(s, props);
        }
    }

    // --- the web `building` — extruded wall + footprint + interior ------

    void DrawBuilding(Shape s, List<Prop> props)
    {
        var pts = s.Points.ToArray();
        var room = s.Kind == Kinds.Room;
        var height = s.Height > 0 ? s.Height : 18.0;
        var color = s.Color != null
            ? Palette.Hex(s.Color)
            : Palette.Hex(room ? "#b8ad86" : "#ac6948");
        // South-extruded wall — the footprint dropped by `height`.
        Fill(
            [.. pts.Select(p => p + new Vector2(0, (float)height))],
            Palette.Tint(Palette.Hex("#706549"), -Math.Round(Math.Min(16, height * 0.22)))
        );
        Fill(pts, color);
        Stroke(pts, Palette.Hex("#4c4935"), room ? 9 : 3);
        var (minX, maxX, minY, maxY) = Bounds(pts);
        if (!room)
        {
            DrawRoof(pts, s.Angle, s.Model, color);
        }
        else
        {
            for (var y = minY; y < maxY; y += 18)
                ClipLine(
                    new Vector2((float)minX, (float)y),
                    new Vector2((float)maxX, (float)y),
                    Palette.Tint(color, -26),
                    1
                );
            for (var x = minX; x < maxX; x += 32)
                ClipLine(
                    new Vector2((float)x, (float)minY),
                    new Vector2((float)x, (float)maxY),
                    Palette.Tint(color, -18),
                    1
                );
            var furnished = props.Any(p =>
                FurnitureKinds.Contains(p.Kind)
                && Poly.PointInPolygon(new Vector2((float)p.X, (float)p.Y), pts)
            );
            if (!furnished)
                RoomDecor(pts, s.Model);
            FlushClipped(pts);
        }
        if (!room && Biome?.Id == "swamp")
            SwampStilts(minX, maxX, minY, maxY, height);
        if (room)
            WallRing(pts, props, minX, maxX, maxY);
    }

    // Rozkročená dvojice kůlů s příčnou vazbou — čte se jako pažíží,
    // ne jako rovná palisáda.
    void SwampStilts(double minX, double maxX, double minY, double maxY, double height)
    {
        for (var x = minX + 6; x < maxX - 4; x += 16)
        {
            var seed = Math.Abs(Math.Floor(x * 7.3 + minY));
            var splay = 3 + Rng.Noise(seed) * 3;
            var foot = maxY + height + 4;
            var at = new Vector2((float)x, (float)maxY);
            DrawLine(
                new Vector2((float)(x - splay), (float)foot),
                at,
                Palette.Hex("#514f37"),
                3
            );
            DrawLine(
                new Vector2((float)(x + splay), (float)foot),
                at + new Vector2(1, 0),
                Palette.Hex("#514f37"),
                3
            );
            DrawLine(
                new Vector2((float)(x - splay * 0.6), (float)(maxY + height * 0.55)),
                new Vector2((float)(x + splay * 0.6), (float)(maxY + height * 0.55)),
                Palette.Hex("#5c5940"),
                1.6f
            );
        }
    }

    // --- wall ring — runs skipping door/window gaps (web `building`) ----

    readonly record struct WallOpening(Vector2 Point, string Kind, int Model, double Size);

    void WallRing(Vector2[] pts, List<Prop> props, double minX, double maxX, double maxY)
    {
        var ocx = pts.Average(p => p.X);
        var ocy = pts.Average(p => p.Y);
        var center = new Vector2(ocx, ocy);
        var openings = props
            .Where(p => p.Kind is "door" or "window")
            .Select(p => new WallOpening(
                new Vector2((float)p.X, (float)p.Y),
                p.Kind,
                Math.Abs(p.Seed) % 3,
                p.Scale
            ))
            .ToList();
        var hasDoor = openings.Any(o => o.Kind == "door");
        var ink = Palette.Hex("#4c4935");
        for (var e = 0; e < pts.Length; e++)
        {
            var a = pts[e];
            var b = pts[(e + 1) % pts.Length];
            var dir = b - a;
            var len = dir.Length();
            if (len < 2)
                continue;
            var u = dir / len;
            // Project each opening onto this edge; keep only those that
            // truly sit on it (a prop near a corner anchors to the
            // closest edge).
            var anchored = openings
                .Select(o =>
                {
                    var t = Math.Clamp((o.Point - a).Dot(u), 0, len);
                    return (o, t, d: (o.Point - a - u * t).Length());
                })
                .Where(x => x.d < 16)
                .OrderBy(x => x.t)
                .ToList();
            var runs = new List<(double S0, double S1)>();
            var start = 0.0;
            foreach (var (o, t, _) in anchored)
            {
                var half = o.Size / 2 * (o.Kind == "window" ? 1.15 : 0.9) + 1.5;
                if (t - half > start + 1)
                    runs.Add((start, t - half));
                start = Math.Max(start, t + half);
            }
            if (start < len - 1)
                runs.Add((start, len));
            foreach (var (s0, s1) in runs)
            {
                var p0 = a + u * (float)s0;
                var p1 = a + u * (float)s1;
                DrawLine(p0, p1, Palette.Hex("#635e48"), 8);
                foreach (var (point, angle, _) in Poly.SampleLine([p0, p1], 14, closed: false))
                {
                    var n = new Vector2((float)Math.Sin(angle), (float)-Math.Cos(angle));
                    DrawLine(point - n * 3, point + n * 3, Palette.Hex("#c6c2a5"), 2);
                }
            }
            foreach (var (o, t, _) in anchored)
            {
                var p = a + u * (float)t;
                var n = new Vector2(-u.Y, u.X);
                if ((center - p).Dot(n) < 0)
                    n = -n;
                var half = (float)(o.Size / 2 * (o.Kind == "window" ? 1.15 : 0.9) + 1.5);
                if (o.Kind == "door")
                {
                    // jambs at both gap ends
                    foreach (var sgn in new[] { -1f, 1f })
                    {
                        var j = p + u * sgn * half;
                        DrawLine(j - n * 6, j + n * 6, ink, 2.6f);
                    }
                    void Leaf(Vector2 jx, float d, float span)
                    {
                        var la = Math.Atan2(n.Y, n.X) - d * 0.85;
                        DrawLine(
                            jx,
                            jx + new Vector2((float)(Math.Cos(la) * span), (float)(Math.Sin(la) * span)),
                            Palette.Hex("#6d4e30"),
                            4
                        );
                    }
                    if (o.Model == 1)
                    {
                        Leaf(p - u * half, 1, half * 0.95f);
                        Leaf(p + u * half, -1, half * 0.95f);
                    }
                    else
                        Leaf(p - u * half, 1, half * 1.8f);
                    if (o.Model == 2)
                    {
                        var a0 = Math.Atan2(-n.Y, -n.X) + Math.PI * 0.85;
                        DrawArc(p, half * 0.95f, (float)a0, (float)(a0 + Math.PI * 1.3), 12, Palette.Hex("#795438"), 3);
                    }
                }
                else
                {
                    // window: bright glass band in the gap + frame
                    // mullions + a soft sun streak thrown onto the floor.
                    DrawLine(p - u * half, p + u * half, Palette.Hex("#efe9cc"), 5.5f);
                    foreach (var sgn in new[] { -1f, 0f, 1f })
                    {
                        var j = p + u * sgn * half;
                        DrawLine(j - n * 5, j + n * 5, ink, 1.6f);
                    }
                    var s = (n + new Vector2(0.55f, 0.85f)).Normalized();
                    var reach = (float)(24 + o.Size * 0.5);
                    ClipFill(
                        [
                            p - u * half * 0.8f,
                            p + u * half * 0.8f,
                            p + u * half * 0.5f + s * reach,
                            p - u * half * 0.5f + s * reach,
                        ],
                        new Color(1f, 0.933f, 0.745f, 0.12f)
                    );
                    FlushClipped(pts);
                }
            }
        }
        // Legacy default: a door on the bottom edge when none was placed.
        if (!hasDoor)
        {
            var mx = (float)((minX + maxX) / 2);
            DrawLine(
                new Vector2(mx - 10, (float)maxY),
                new Vector2(mx + 10, (float)maxY),
                Palette.Hex("#795438"),
                9
            );
        }
    }

    // --- roomDecor — procedural furniture, clipped to the footprint ----

    void RoomDecor(Vector2[] pts, int model)
    {
        var (minX, maxX, minY, maxY) = Bounds(pts);
        var w = maxX - minX;
        var h = maxY - minY;
        double Fx(double x) => model == 1 ? maxX - (x - minX) : x;
        var wood = Palette.Hex("#8a6a44");
        var dark = Palette.Hex("#503a28");
        var cloth = Palette.Hex(model == 2 ? "#5a6d8c" : "#7d4a36");
        void Box(double x, double y, double bw, double bh, Color fill)
        {
            var rx = Fx(x);
            var quad = new[]
            {
                new Vector2((float)(rx - bw), (float)y),
                new Vector2((float)rx, (float)y),
                new Vector2((float)rx, (float)(y + bh)),
                new Vector2((float)(rx - bw), (float)(y + bh)),
            };
            ClipFill(quad, fill);
            for (var i = 0; i < 4; i++)
                ClipLine(quad[i], quad[(i + 1) % 4], dark, 1.5f);
        }
        void EllipseStroke(double cx, double cy, double rx, double ry, Color c, float wd)
        {
            var ring = Poly.Ellipse(cx, cy, rx, ry, 0, 14);
            for (var i = 0; i < ring.Length; i++)
                ClipLine(ring[i], ring[(i + 1) % ring.Length], c, wd);
        }
        void Rug(double x, double y, double rx, double ry)
        {
            var c = Fx(x) - rx;
            ClipFill(Poly.Ellipse(c, y, rx, ry, 0, 16), cloth);
            EllipseStroke(c, y, rx * 0.7, ry * 0.7, Palette.Tint(cloth, 30), 2);
        }
        void Bed(double x, double y, double bw, double bh)
        {
            Box(x + bw, y, bw, bh, wood);
            var rx = Fx(x + bw);
            ClipFill(
                [
                    new Vector2((float)(rx - bw + 3), (float)(y + 3)),
                    new Vector2((float)(rx - 3), (float)(y + 3)),
                    new Vector2((float)(rx - 3), (float)(y + bh - 3)),
                    new Vector2((float)(rx - bw + 3), (float)(y + bh - 3)),
                ],
                Palette.Hex("#c9b795")
            );
            ClipFill(
                [
                    new Vector2((float)(rx - bw + 5), (float)(y + 4)),
                    new Vector2((float)(rx - 5), (float)(y + 4)),
                    new Vector2((float)(rx - 5), (float)(y + 4 + bh * 0.28)),
                    new Vector2((float)(rx - bw + 5), (float)(y + 4 + bh * 0.28)),
                ],
                Palette.Hex("#e8ddc5")
            );
        }
        void Chair(double x, double y, double s)
        {
            Box(x, y - s / 2, s, s, wood);
            var rx = Fx(x);
            ClipFill(
                [
                    new Vector2((float)(rx - s + 2), (float)(y - s / 2 + 2)),
                    new Vector2((float)(rx - 2), (float)(y - s / 2 + 2)),
                    new Vector2((float)(rx - 2), (float)(y + s / 2 - 2)),
                    new Vector2((float)(rx - s + 2), (float)(y + s / 2 - 2)),
                ],
                Palette.Tint(wood, 20)
            );
        }
        void Table(double x, double y, double tw, double th)
        {
            ClipFill(Poly.Ellipse(Fx(x + tw / 2), y + th / 2, tw / 2, th / 2, 0, 16), Palette.Hex("#9c7750"));
            EllipseStroke(Fx(x + tw / 2), y + th / 2, tw / 2, th / 2, dark, 2);
        }
        void Torch(double x, double y)
        {
            ClipFill(Poly.Ellipse(x, y, 8, 8, 0, 12), new Color(1f, 0.745f, 0.353f, 0.35f));
            ClipFill(Poly.Ellipse(x, y, 3, 3, 0, 10), Palette.Hex("#ffb347"));
        }
        var area = w * h;
        if (area < 16000)
        {
            for (double x = minX + 26; x < maxX - 20 && x < minX + 26 + 8 * 30; x += 30)
            {
                var i = (int)((x - minX - 26) / 30);
                Box(x, minY + 10, 22, 20, i % 2 == 1 ? wood : Palette.Tint(wood, -14));
            }
            Box(Fx(minX + w * 0.72), maxY - 30, w * 0.2, 20, wood);
            Rug((minX + maxX) / 2, (minY + maxY) / 2, w * 0.18, h * 0.14);
        }
        else if (area < 45000)
        {
            Bed(minX + 14, minY + 12, Math.Min(46, w * 0.28), Math.Min(60, h * 0.42));
            if (w > 150)
                Bed(maxX - 16 - Math.Min(46, w * 0.28), minY + 12, Math.Min(46, w * 0.28), Math.Min(60, h * 0.42));
            Rug((minX + maxX) / 2, (minY + maxY) / 2 + 6, w * 0.22, h * 0.16);
            Table(maxX / 2 - 30, maxY - 56, 56, 30);
            Chair(maxX / 2 - 44, maxY - 41, 12);
            Chair(maxX / 2 + 40, maxY - 41, 12);
            Torch(minX + 12, minY + h / 2);
            Torch(maxX - 12, minY + h / 2);
        }
        else
        {
            Rug((minX + maxX) / 2, (minY + maxY) / 2, w * 0.3, h * 0.22);
            var tw = Math.Min(w * 0.5, 220);
            var th = Math.Min(h * 0.24, 52);
            Table((minX + maxX) / 2 - tw / 2, (minY + maxY) / 2 - th / 2, tw, th);
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var y = (minY + maxY) / 2 + side * (th / 2 + 12) - 4;
                var x = (minX + maxX) / 2 - tw / 2 + 8;
                ClipLine(new Vector2((float)x, (float)y), new Vector2((float)(x + tw - 16), (float)y), dark, 1.5f);
                ClipLine(new Vector2((float)x, (float)(y + 8)), new Vector2((float)(x + tw - 16), (float)(y + 8)), dark, 1.5f);
                ClipLine(new Vector2((float)x, (float)y), new Vector2((float)x, (float)(y + 8)), dark, 1.5f);
                ClipLine(new Vector2((float)(x + tw - 16), (float)y), new Vector2((float)(x + tw - 16), (float)(y + 8)), dark, 1.5f);
            }
            for (var i = 0; i < 4; i++)
                Torch(minX + 12, minY + 16 + i * (h - 32) / 3);
        }
    }

    // --- floorArea — deck / tile ground fill -----------------------------

    void DrawFloor(Shape s)
    {
        var pts = s.Points.ToArray();
        var height = s.Height;
        var tiles = s.Model == 2; // variant "stone"
        var color = s.Color != null
            ? Palette.Hex(s.Color)
            : (tiles ? Biome?.Stone ?? Palette.Hex("#a4a68e") : Biome?.Wood ?? Palette.Hex("#a37d4c"));
        var (minX, maxX, minY, maxY) = Bounds(pts);
        if (height > 0)
        {
            // Cast shadow plus a dark underside lip selling the elevation.
            var lip = Math.Min(16, 4 + height * 0.12);
            Fill(
                [.. pts.Select(p => p + new Vector2(0, (float)lip))],
                Palette.Tint(color, -56)
            );
        }
        Fill(pts, color);
        ClipFill([.. pts], Palette.Tint(color, 8));
        if (tiles)
        {
            var step = Math.Max(20, s.Size * 0.8);
            for (var y = minY + step * 0.5; y < maxY; y += step)
                ClipLine(new Vector2((float)minX, (float)y), new Vector2((float)maxX, (float)y), Palette.Tint(color, -30), 1.4f);
            for (var x = minX + step * 0.5; x < maxX; x += step)
                ClipLine(new Vector2((float)x, (float)minY), new Vector2((float)x, (float)maxY), Palette.Tint(color, -24), 1.2f);
            for (var i = 0; i < (maxX - minX) * (maxY - minY) / 3000; i++)
            {
                var seed = i * 7.3 + minX * 0.11;
                var x = minX + Rng.Noise(seed) * (maxX - minX);
                var y = minY + Rng.Noise(seed + 1) * (maxY - minY);
                var c = Palette.Tint(color, -36);
                ClipLine(new Vector2((float)(x - 3), (float)y), new Vector2((float)(x + 2), (float)(y + 2)), c, 0.9f);
                ClipLine(new Vector2((float)(x + 2), (float)(y + 2)), new Vector2((float)(x + 5), (float)(y - 1)), c, 0.9f);
            }
        }
        else
        {
            var step = Math.Max(10, Math.Min(18, s.Size * 0.5));
            var row = 0;
            for (var y = minY + step; y < maxY; y += step, row++)
            {
                ClipLine(new Vector2((float)minX, (float)y), new Vector2((float)maxX, (float)y), Palette.Tint(color, -34), 1.6f);
                for (
                    var x = minX + 24 + Rng.Noise(row * 7.3 + minY * 0.13) * 40;
                    x < maxX - 8;
                    x += 54 + Rng.Noise(row + x * 0.07) * 26
                )
                    ClipLine(new Vector2((float)x, (float)(y - step)), new Vector2((float)x, (float)y), Palette.Tint(color, -30), 1.2f);
            }
            for (var i = 0; i < (maxX - minX) * (maxY - minY) / 2600; i++)
            {
                var seed = i * 3.1 + minX * 0.07 + minY * 0.19;
                var x = minX + Rng.Noise(seed) * (maxX - minX);
                var y = minY + Rng.Noise(seed + 1) * (maxY - minY);
                ClipLine(new Vector2((float)(x - 4), (float)y), new Vector2((float)(x + 4), (float)(y + 1)), Palette.Tint(color, -18), 0.8f);
            }
        }
        FlushClipped(pts);
        // The deck's edge path — a bright lip on the rim of raised platforms.
        Stroke(pts, height > 0 ? Palette.Tint(color, -48) : Palette.Hex("#57492f"), height > 0 ? 4 : 3);
        if (height > 0)
            Stroke(pts, Palette.Tint(color, 34), 1.2f);
    }

    // --- roofs — port of scene-roofs.ts ----------------------------------

    /// <summary>Roof look from the biome's `roofs` block (material,
    /// chimney flag, moss density, extras). Parsed straight from the
    /// biome JSON — ResolvedBiome only carries colors.</summary>
    sealed class RoofCfg
    {
        public string Material = "tile";
        public bool Chimney = true;
        public double Moss = 0.35;
        public string Extra = "none";
        public bool WinterSnow = true;

        public static RoofCfg For(string biomeId)
        {
            var cfg = new RoofCfg();
            using var file = FileAccess.Open(
                $"res://assets/config/biomes/{biomeId}.json",
                FileAccess.ModeFlags.Read
            );
            if (file == null)
                return cfg;
            var json = Json.ParseString(file.GetAsText());
            if (
                json.VariantType != Variant.Type.Dictionary
                || !json.AsGodotDictionary().TryGetValue("roofs", out var r)
                || r.VariantType != Variant.Type.Dictionary
            )
                return cfg;
            var d = r.AsGodotDictionary();
            if (d.TryGetValue("material", out var m))
                cfg.Material = m.AsString();
            if (d.TryGetValue("chimney", out var c))
                cfg.Chimney = c.AsBool();
            if (d.TryGetValue("moss", out var mo))
                cfg.Moss = mo.AsDouble();
            if (d.TryGetValue("extra", out var e))
                cfg.Extra = e.AsString();
            if (d.TryGetValue("winterSnow", out var ws))
                cfg.WinterSnow = ws.AsBool();
            return cfg;
        }
    }

    /// <summary>frontend.json `roofMaterials` — tint + tile metrics.</summary>
    readonly record struct RoofLook(string? TintTo, double TintAmt, double RowH, double TileW);

    static readonly Dictionary<string, RoofLook> RoofLooks = new()
    {
        ["tile"] = new(null, 0, 7, 8.5),
        ["shingle"] = new("#7a6048", 0.4, 5.5, 8),
        ["slate"] = new("#4f4b52", 0.45, 6, 10),
        ["thatch"] = new("#c9a55a", 0.6, 6, 0),
    };

    struct RoofFrame
    {
        public Vector2 C;
        public double Rot,
            X0,
            X1,
            Y0,
            Y1;
    }

    readonly record struct RoofFace(Vector2[] Poly, double Dx, double Dy);

    /// <summary>Frame of the roof: the footprint's axes-aligned bbox in
    /// the rotated frame, quarter-turned so the ridge runs along the
    /// longer side.</summary>
    static RoofFrame FrameOf(IReadOnlyList<Vector2> points, double angle)
    {
        var c = Poly.Centroid(points);
        var cos = Math.Cos(-angle);
        var sin = Math.Sin(-angle);
        double x0 = double.MaxValue,
            x1 = double.MinValue,
            y0 = double.MaxValue,
            y1 = double.MinValue;
        foreach (var p in points)
        {
            var dx = p.X - c.X;
            var dy = p.Y - c.Y;
            x0 = Math.Min(x0, dx * cos - dy * sin);
            x1 = Math.Max(x1, dx * cos - dy * sin);
            y0 = Math.Min(y0, dx * sin + dy * cos);
            y1 = Math.Max(y1, dx * sin + dy * cos);
        }
        var rot = angle;
        if (y1 - y0 > x1 - x0)
        {
            (x0, x1, y0, y1) = (y0, y1, -x1, -x0);
            rot += Math.PI / 2;
        }
        return new RoofFrame { C = c, Rot = rot, X0 = x0, X1 = x1, Y0 = y0, Y1 = y1 };
    }

    Vector2 ToWorld(RoofFrame f, double u, double v) =>
        new(
            (float)(f.C.X + u * Math.Cos(f.Rot) - v * Math.Sin(f.Rot)),
            (float)(f.C.Y + u * Math.Sin(f.Rot) + v * Math.Cos(f.Rot))
        );

    /// <summary>noiseSequence(seed, 17.17) — the roof's rand() stream.</summary>
    static Func<double> Seeded(double seed)
    {
        var i = 0;
        return () => Rng.Noise(seed + i++ * 17.17);
    }

    // Current-roof context: footprint for clipping + the frame.
    Vector2[] _fp = null!;
    RoofFrame _fr;

    Vector2 Rw(double u, double v) => ToWorld(_fr, u, v);

    Vector2[] RwPoly(IEnumerable<Vector2> framePts) => [.. framePts.Select(p => Rw(p.X, p.Y))];

    /// <summary>Frame-coords polygon → world, clipped to the footprint.</summary>
    void RoofFill(Vector2[] framePoly, Color c)
    {
        foreach (var piece in Geometry2D.IntersectPolygons(RwPoly(framePoly), _fp))
            Fill(piece, c);
    }

    /// <summary>Frame-coords segment → world, clipped to the footprint.</summary>
    void RoofLine(double u0, double v0, double u1, double v1, Color c, float w)
    {
        var a = Rw(u0, v0);
        var b = Rw(u1, v1);
        if (a == b)
            return;
        foreach (var (t0, t1) in SegRuns(a, b, _fp))
            DrawLine(a + (b - a) * (float)t0, a + (b - a) * (float)t1, c, w);
    }

    /// <summary>Face-local (u across the slope, v down) → world.</summary>
    Vector2 FaceW(RoofFace f, double u, double v) => Rw(u * f.Dy + v * f.Dx, -u * f.Dx + v * f.Dy);

    void FaceFill(List<Vector2[]> pieces, Vector2[] worldPoly, Color c)
    {
        foreach (var pc in pieces)
        foreach (var part in Geometry2D.IntersectPolygons(worldPoly, pc))
            Fill(part, c);
    }

    void FaceLine(List<Vector2[]> pieces, Vector2 a, Vector2 b, Color c, float w)
    {
        if (a == b)
            return;
        foreach (var pc in pieces)
        foreach (var (t0, t1) in SegRuns(a, b, pc))
            DrawLine(a + (b - a) * (float)t0, a + (b - a) * (float)t1, c, w);
    }

    static double RoofSeed(RoofFrame f) => f.C.X * 0.37 + f.C.Y * 0.91;

    /// <summary>Brightness of a face from its downslope direction vs.
    /// the fixed top-left sun.</summary>
    double RoofLight(double dx, double dy)
    {
        var wx = dx * Math.Cos(_fr.Rot) - dy * Math.Sin(_fr.Rot);
        var wy = dx * Math.Sin(_fr.Rot) + dy * Math.Cos(_fr.Rot);
        return (-wx - wy) / Math.Sqrt(2);
    }

    static Color ShadeColor(Color baseColor, double b) =>
        b >= 0
            ? Palette.Mix(baseColor, Palette.Hex("#fff4dc"), b * 0.3)
            : Palette.Mix(baseColor, Palette.Hex("#140e08"), -b * 0.42);

    /// <summary>Chimney position (frame coords) — the web `chimneyOf`
    /// with sceneConfig.chimneys {chance .75, thatch .45, size 7..14}.</summary>
    (double U, double V, double Size)? ChimneyOf(RoofFrame f, int model, RoofCfg cfg)
    {
        if (!cfg.Chimney || cfg.Material == "flat")
            return null;
        var rand = Seeded(RoofSeed(f) + 3.3);
        if (rand() > (model == 2 ? 0.45 : 0.75))
            return null;
        var W = f.X1 - f.X0;
        var H = f.Y1 - f.Y0;
        var size = Math.Max(7, Math.Min(14, Math.Min(W, H) * 0.15));
        var inset = model == 0 ? size : H / 2 + size;
        var u = f.X0 + inset + rand() * Math.Max(0, W - inset * 2);
        var v = (f.Y0 + f.Y1) / 2 + (rand() < 0.5 ? -1 : 1) * H * 0.18;
        return (u, v, size);
    }

    static Vector2[] FrameEllipse(double cx, double cy, double rx, double ry, double rot, int n = 12)
    {
        var pts = new Vector2[n];
        var cos = Math.Cos(rot);
        var sin = Math.Sin(rot);
        for (var i = 0; i < n; i++)
        {
            var a = i / (double)n * Math.PI * 2;
            var ex = Math.Cos(a) * rx;
            var ey = Math.Sin(a) * ry;
            pts[i] = new Vector2((float)(cx + ex * cos - ey * sin), (float)(cy + ex * sin + ey * cos));
        }
        return pts;
    }

    static Vector2[] FrameRect(double x0, double y0, double w, double h) =>
        [
            new Vector2((float)x0, (float)y0),
            new Vector2((float)(x0 + w), (float)y0),
            new Vector2((float)(x0 + w), (float)(y0 + h)),
            new Vector2((float)x0, (float)(y0 + h)),
        ];

    void DrawRoof(Vector2[] pts, double angle, int model, Color wall)
    {
        _fp = pts;
        _fr = FrameOf(pts, angle);
        var rand = Seeded(RoofSeed(_fr));
        var ink = (float)Ink;
        var (x0, x1, y0, y1) = (_fr.X0, _fr.X1, _fr.Y0, _fr.Y1);
        var W = x1 - x0;
        var H = y1 - y0;
        var ym = (y0 + y1) / 2;
        var cfg = RoofCfg.For(Biome?.Id ?? "woodland");
        var snowy = cfg.Material != "flat" && cfg.Extra == "snow";

        // --- flat adobe roofs (desert) -----------------------------------
        if (cfg.Material == "flat")
        {
            var fbase = Palette.Mix(wall, Palette.Hex("#d9b98a"), 0.3);
            RoofFill(FrameRect(x0, y0, W, H), fbase);
            var p = Math.Max(4, Math.Min(W, H) * 0.08);
            var bandPolys = new (Vector2[] Poly, double Ox, double Oy)[]
            {
                ([new((float)x0, (float)y0), new((float)x1, (float)y0), new((float)(x1 - p), (float)(y0 + p)), new((float)(x0 + p), (float)(y0 + p))], 0, -1),
                ([new((float)x1, (float)y0), new((float)x1, (float)y1), new((float)(x1 - p), (float)(y1 - p)), new((float)(x1 - p), (float)(y0 + p))], 1, 0),
                ([new((float)x1, (float)y1), new((float)x0, (float)y1), new((float)(x0 + p), (float)(y1 - p)), new((float)(x1 - p), (float)(y1 - p))], 0, 1),
                ([new((float)x0, (float)y1), new((float)x0, (float)y0), new((float)(x0 + p), (float)(y0 + p)), new((float)(x0 + p), (float)(y1 - p))], -1, 0),
            };
            foreach (var (poly, ox, oy) in bandPolys)
                RoofFill(poly, ShadeColor(Palette.Mix(fbase, Colors.White, 0.12), RoofLight(-ox, -oy) * 0.9));
            RoofFill(FrameRect(x0 + p, y0 + p, W - p * 2, H - p * 2), Palette.Mix(fbase, Colors.Black, 0.08));
            // Wandered hairline cracks in the plaster.
            var crack = Palette.Mix(fbase, Colors.Black, 0.3);
            for (var k = 1 + Math.Floor(rand() * 3); k > 0; k--)
            {
                var px = x0 + p + rand() * (W - p * 2);
                var py = y0 + p + rand() * (H - p * 2);
                var a = Rw(px, py);
                for (var st = 0; st < 4; st++)
                {
                    var nx = px + (rand() - 0.5) * 12;
                    var ny = py + (rand() - 0.5) * 12;
                    var b = Rw(nx, ny);
                    foreach (var (t0, t1) in SegRuns(a, b, _fp))
                        DrawLine(a + (b - a) * (float)t0, a + (b - a) * (float)t1, crack, 0.8f);
                    (px, py, a) = (nx, ny, b);
                }
            }
            var s = Math.Min(W, H);
            void Pot(double px, double py)
            {
                var c = Rw(px, py);
                DrawCircle(c, (float)(s * 0.06), Palette.Hex("#8a4f2e"));
                DrawCircle(c, (float)(s * 0.032), Palette.Hex("#3a2416"));
            }
            if (model == 1)
            {
                // Dome — a crescent of shade wrapped around the drum,
                // aligned to world north-west regardless of the frame.
                var R = s * 0.3;
                var cx = x0 + W * (0.35 + rand() * 0.3);
                var dc = Rw(cx, ym);
                DrawCircle(dc, (float)(R + 1.5 * ink), Palette.Mix(fbase, Colors.Black, 0.25));
                DrawCircle(dc, (float)R, Palette.Mix(fbase, Colors.White, 0.08));
                var crescent = new List<Vector2>();
                for (var i = 0; i <= 14; i++)
                {
                    var a = -0.3 + (Math.PI * 0.8 + 0.3) * i / 14;
                    crescent.Add(dc + new Vector2((float)(Math.Cos(a) * R), (float)(Math.Sin(a) * R)));
                }
                var ic = dc - new Vector2((float)(R * 0.15), (float)(R * 0.15));
                for (var i = 14; i >= 0; i--)
                {
                    var a = -0.3 + (Math.PI * 0.8 + 0.3) * i / 14;
                    crescent.Add(ic + new Vector2((float)(Math.Cos(a) * R * 0.85), (float)(Math.Sin(a) * R * 0.85)));
                }
                Fill([.. crescent], Palette.Mix(fbase, Colors.Black, 0.2));
                DrawCircle(dc - new Vector2((float)(R * 0.35), (float)(R * 0.35)), (float)(R * 0.3), Palette.Mix(fbase, Palette.Hex("#fff4dc"), 0.35));
            }
            else if (model == 2)
            {
                var rw = W * 0.4;
                var rh = H * 0.35;
                var rx = x0 + p + rand() * (W - p * 2 - rw);
                var ry = y0 + p + rand() * (H - p * 2 - rh);
                for (var k = 0; k < 6; k++)
                {
                    var c = k % 2 == 1
                        ? Palette.Hex("#e9dcc0")
                        : rand() < 0.5 ? Palette.Hex("#b0442e") : Palette.Hex("#2f6b7a");
                    RoofFill(FrameRect(rx + k * rw / 6, ry, rw / 6, rh), c);
                }
                Pot(x1 - p - s * 0.1, y1 - p - s * 0.1);
            }
            else
            {
                var hx = x0 + p + s * 0.08 + rand() * (W * 0.3);
                var hs = s * 0.2;
                RoofFill(FrameRect(hx, ym - hs / 2, hs, hs), Palette.Hex("#3a2a1c"));
                for (var k = 1; k < 4; k++)
                    RoofLine(hx + hs * 0.25, ym - hs / 2 + k * hs / 4, hx + hs * 0.75, ym - hs / 2 + k * hs / 4, Palette.Hex("#8a6a44"), 1.2f);
                Pot(x1 - p - s * 0.12, y0 + p + s * 0.12);
                if (rand() < 0.6)
                    Pot(x1 - p - s * 0.3, y0 + p + s * 0.12);
            }
            return;
        }

        // --- pitched roofs -------------------------------------------------
        var thatch = model == 2;
        var material = cfg.Material;
        var look = material is "shingle" or "slate" ? RoofLooks[material] : RoofLooks["tile"];
        var lookUsed = thatch ? RoofLooks["thatch"] : look;
        var baseColor = lookUsed.TintTo != null
            ? Palette.Mix(wall, Palette.Hex(lookUsed.TintTo), lookUsed.TintAmt)
            : wall;
        var inset = Math.Min(H / 2, W / 2);
        var faces = new List<RoofFace>();
        if (model == 0)
        {
            faces.Add(new([new((float)x0, (float)y0), new((float)x1, (float)y0), new((float)x1, (float)ym), new((float)x0, (float)ym)], 0, -1));
            faces.Add(new([new((float)x0, (float)ym), new((float)x1, (float)ym), new((float)x1, (float)y1), new((float)x0, (float)y1)], 0, 1));
        }
        else
        {
            faces.Add(new([new((float)x0, (float)y0), new((float)x1, (float)y0), new((float)(x1 - inset), (float)ym), new((float)(x0 + inset), (float)ym)], 0, -1));
            faces.Add(new([new((float)(x0 + inset), (float)ym), new((float)(x1 - inset), (float)ym), new((float)x1, (float)y1), new((float)x0, (float)y1)], 0, 1));
            faces.Add(new([new((float)x0, (float)y0), new((float)(x0 + inset), (float)ym), new((float)x0, (float)y1)], -1, 0));
            faces.Add(new([new((float)x1, (float)y0), new((float)x1, (float)y1), new((float)(x1 - inset), (float)ym)], 1, 0));
        }
        var rowH = lookUsed.RowH;
        var tileW = look.TileW;
        var mossy = cfg.Moss;

        foreach (var face in faces)
        {
            var b = RoofLight(face.Dx, face.Dy);
            var faceColor = ShadeColor(baseColor, b);
            var pieces = Geometry2D.IntersectPolygons(RwPoly(face.Poly), _fp).ToList();
            foreach (var pc in pieces)
                Fill(pc, Palette.Mix(faceColor, Colors.Black, 0.18));
            // Face-local bounds: u across the slope, v down toward the eave.
            double u0 = double.MaxValue,
                u1 = double.MinValue,
                v0 = double.MaxValue,
                v1 = double.MinValue;
            foreach (var q in face.Poly)
            {
                u0 = Math.Min(u0, q.X * face.Dy - q.Y * face.Dx);
                u1 = Math.Max(u1, q.X * face.Dy - q.Y * face.Dx);
                v0 = Math.Min(v0, q.X * face.Dx + q.Y * face.Dy);
                v1 = Math.Max(v1, q.X * face.Dx + q.Y * face.Dy);
            }
            if (thatch)
            {
                // Straw: dense short strokes down the slope in three
                // tones, scalloped course lines, a heavy rounded eave.
                var tones = new[]
                {
                    Palette.Mix(faceColor, Colors.Black, 0.2),
                    faceColor,
                    Palette.Mix(faceColor, Palette.Hex("#fff2c8"), 0.25),
                };
                for (var t = 0; t < 3; t++)
                {
                    var n = (u1 - u0) * (v1 - v0) / (t == 0 ? 10 : 14);
                    for (var k = 0; k < n; k++)
                    {
                        var u = u0 + rand() * (u1 - u0);
                        var v = v0 + rand() * (v1 - v0);
                        var l = 3 + rand() * 4;
                        var du = (rand() - 0.5) * 1.2;
                        FaceLine(pieces, FaceW(face, u, v), FaceW(face, u + du, v + l), tones[t], t == 2 ? 0.8f : 1.1f);
                    }
                }
                var scallop = Palette.Mix(faceColor, Colors.Black, 0.35);
                for (var v = v0 + rowH * 1.6; v < v1; v += rowH * 1.6)
                for (var u = u0; u < u1; u += 6)
                {
                    FaceLine(pieces, FaceW(face, u, v), FaceW(face, u + 3, v + 1.1), scallop, 1);
                    FaceLine(pieces, FaceW(face, u + 3, v + 1.1), FaceW(face, u + 6, v), scallop, 1);
                }
                FaceLine(pieces, FaceW(face, u0, v1 - 1.5), FaceW(face, u1, v1 - 1.5), Palette.Mix(faceColor, Colors.Black, 0.32), 4);
                continue;
            }
            // Tiles: rows from the ridge (v0) to the eave (v1), staggered
            // joints, four jittered tones, a few odd tiles.
            var buckets = new List<(double U, double V, double W)>[4];
            for (var i = 0; i < 4; i++)
                buckets[i] = [];
            var odd = new List<(double U, double V, double W, bool Dark)>();
            var row = 0;
            for (var v = v0; v < v1; v += rowH, row++)
            {
                var tw = tileW * (material == "slate" ? 0.8 + rand() * 0.4 : 1);
                for (var u = u0 - row % 2 * tw * 0.5; u < u1; u += tw)
                {
                    var roll = rand();
                    if (roll < 0.015)
                        odd.Add((u, v, tw, true));
                    else if (roll < 0.03 * (1 + mossy))
                        odd.Add((u, v, tw, false));
                    else
                        buckets[(int)Math.Floor(rand() * 4)].Add((u, v, tw));
                }
            }
            var toneAmt = new[] { -0.1, -0.03, 0.04, 0.1 };
            for (var k = 0; k < 4; k++)
            {
                var c = Palette.Mix(faceColor, toneAmt[k] < 0 ? Colors.Black : Colors.White, Math.Abs(toneAmt[k]));
                foreach (var (u, v, tw) in buckets[k])
                    FaceFill(
                        pieces,
                        [FaceW(face, u + 0.4, v), FaceW(face, u + tw - 0.4, v), FaceW(face, u + tw - 0.4, v + rowH), FaceW(face, u + 0.4, v + rowH)],
                        c
                    );
            }
            foreach (var (u, v, tw, darkTile) in odd)
                FaceFill(
                    pieces,
                    [FaceW(face, u + 0.4, v), FaceW(face, u + tw - 0.4, v), FaceW(face, u + tw - 0.4, v + rowH), FaceW(face, u + 0.4, v + rowH)],
                    darkTile ? Palette.Mix(faceColor, Colors.Black, 0.5) : Palette.Mix(faceColor, Palette.Hex("#5d7a3c"), 0.45)
                );
            if (material == "tile")
            {
                // Rounded barrel tiles: a light stripe on each tile.
                var stripe = Palette.Mix(faceColor, Palette.Hex("#fff2dc"), 0.22);
                row = 0;
                for (var v = v0; v < v1; v += rowH, row++)
                for (var u = u0 - row % 2 * tileW * 0.5; u < u1; u += tileW)
                    FaceLine(pieces, FaceW(face, u + tileW * 0.32, v + 1), FaceW(face, u + tileW * 0.32, v + rowH - 1.5), stripe, 1.2f);
            }
            // Course shadows on the eave side of each row + joints.
            var course = Palette.Mix(faceColor, Colors.Black, 0.45);
            for (var v = v0 + rowH; v < v1; v += rowH)
                FaceLine(pieces, FaceW(face, u0, v), FaceW(face, u1, v), course, 1.1f);
            var joint = Palette.Mix(faceColor, Colors.Black, 0.3);
            row = 0;
            for (var v = v0; v < v1; v += rowH, row++)
            for (var u = u0 - row % 2 * tileW * 0.5; u < u1; u += tileW)
                FaceLine(pieces, FaceW(face, u, v), FaceW(face, u, v + rowH), joint, 0.6f);
        }

        // Moss creeping in from the eaves.
        if (!snowy)
        for (var k = (int)Math.Floor(rand() * 4 * mossy * 2); k > 0; k--)
        {
            var along = rand() < 0.5;
            var px = along ? x0 + rand() * W : (rand() < 0.5 ? x0 + 3 : x1 - 3);
            var py = along ? (rand() < 0.5 ? y0 + 3 : y1 - 3) : y0 + rand() * H;
            RoofFill(
                FrameEllipse(px, py, 4 + rand() * 7, 2.5 + rand() * 4, rand() * 3, 10),
                Palette.Hex("#5d7a3c") with { A = 0.35f }
            );
        }

        // Ridge and hips: a dark bed under a lighter half-round cap.
        var ridge = model == 0
            ? new[] { new[] { (x0, ym), (x1, ym) } }
            : new[]
            {
                new[] { (x0 + inset, ym), (x1 - inset, ym) },
                new[] { (x0, y0), (x0 + inset, ym) },
                new[] { (x0, y1), (x0 + inset, ym) },
                new[] { (x1, y0), (x1 - inset, ym) },
                new[] { (x1, y1), (x1 - inset, ym) },
            };
        var capBase = Palette.Mix(baseColor, Colors.Black, thatch ? 0.1 : 0.15);
        for (var k = 0; k < ridge.Length; k++)
        {
            var seg = ridge[k];
            var main = k == 0;
            var w = (float)(main ? (thatch ? 7 : 5) : (thatch ? 3.6 : 3.2));
            RoofLine(seg[0].Item1, seg[0].Item2, seg[1].Item1, seg[1].Item2, Palette.Mix(capBase, Colors.Black, 0.45), w + 2 * ink);
            RoofLine(seg[0].Item1, seg[0].Item2, seg[1].Item1, seg[1].Item2, capBase, w);
            RoofLine(seg[0].Item1, seg[0].Item2 - w * 0.18, seg[1].Item1, seg[1].Item2 - w * 0.18, Palette.Mix(capBase, Palette.Hex("#fff2dc"), 0.3), w * 0.3f);
            if (main)
            {
                // Ridge tile joints / thatch spar pattern.
                var jc = Palette.Mix(capBase, Colors.Black, 0.4);
                var len = seg[1].Item1 - seg[0].Item1;
                var step = thatch ? 5 : 7;
                for (var u = 0.0; u < len; u += step)
                {
                    var px = seg[0].Item1 + u;
                    if (thatch)
                    {
                        RoofLine(px, ym - w / 2, px + step / 2, ym + w / 2, jc, 0.8f);
                        RoofLine(px + step / 2, ym + w / 2, px + step, ym - w / 2, jc, 0.8f);
                    }
                    else
                        RoofLine(px, ym - w / 2, px, ym + w / 2, jc, 0.8f);
                }
            }
        }
        if (model == 0)
        {
            // Barge boards along the gable ends.
            var board = Palette.Mix(baseColor, Palette.Hex("#1a120a"), 0.6);
            RoofLine(x0 + 1, y0, x0 + 1, y1, board, 2.6f * ink);
            RoofLine(x1 - 1, y0, x1 - 1, y1, board, 2.6f * ink);
        }

        // Snow: covers each face from the ridge down with a wavy lower
        // edge so the eaves still show.
        if (snowy)
            foreach (var face in faces)
            {
                var b = RoofLight(face.Dx, face.Dy);
                var pieces = Geometry2D.IntersectPolygons(RwPoly(face.Poly), _fp).ToList();
                double u0 = double.MaxValue,
                    u1 = double.MinValue,
                    v0 = double.MaxValue,
                    v1 = double.MinValue;
                foreach (var q in face.Poly)
                {
                    u0 = Math.Min(u0, q.X * face.Dy - q.Y * face.Dx);
                    u1 = Math.Max(u1, q.X * face.Dy - q.Y * face.Dx);
                    v0 = Math.Min(v0, q.X * face.Dx + q.Y * face.Dy);
                    v1 = Math.Max(v1, q.X * face.Dx + q.Y * face.Dy);
                }
                var reach = v0 + (v1 - v0) * (0.38 + rand() * 0.25);
                var cap = new List<Vector2>
                {
                    FaceW(face, u0 - 2, v0 - 2),
                    FaceW(face, u1 + 2, v0 - 2),
                };
                for (var u = u1 + 2; u >= u0 - 2; u -= 5)
                    cap.Add(FaceW(face, u, reach + (rand() - 0.5) * 5));
                FaceFill(pieces, [.. cap], ShadeColor(Palette.Hex("#e9eff1"), b * 0.6) with { A = 0.93f });
                // Tile courses still faintly showing through the snow.
                var faint = ShadeColor(Palette.Hex("#d5dfe3"), b * 0.6);
                for (var v = v0 + rowH; v < reach - 3; v += rowH)
                {
                    var a = FaceW(face, u0, v);
                    var bb = FaceW(face, u1, v);
                    FaceLine(pieces, a, bb, faint, 0.7f);
                    FaceLine(pieces, a, bb, Palette.Hex("#b7c4cc"), 0.8f);
                }
            }

        // Chimney / vent / crystal lamps.
        if (ChimneyOf(_fr, model, cfg) is { } ch)
        {
            var s = ch.Size;
            // Ambient halo under the flue + a drop shadow south-east.
            RoofFill(FrameEllipse(ch.U, ch.V, s * 1.1, s * 0.9, 0, 12), new Color(0.078f, 0.055f, 0.031f, 0.25f));
            RoofFill(FrameRect(ch.U - s / 2 - ink + 3, ch.V - s / 2 - ink + 4, s + 2 * ink, s + 2 * ink), new Color(0.055f, 0.04f, 0.024f, 0.45f));
            RoofFill(FrameRect(ch.U - s / 2 - ink, ch.V - s / 2 - ink, s + 2 * ink, s + 2 * ink), Palette.Hex("#3d342c"));
            RoofFill(FrameRect(ch.U - s / 2, ch.V - s / 2, s, s), Palette.Hex("#7d6f60"));
            for (var k = 1; k < 3; k++)
                RoofLine(ch.U - s / 2, ch.V - s / 2 + k * s / 3, ch.U + s / 2, ch.V - s / 2 + k * s / 3, Palette.Hex("#5a4e42"), 0.7f);
            RoofFill(FrameRect(ch.U - s * 0.28, ch.V - s * 0.28, s * 0.56, s * 0.56), Palette.Hex("#1e1814"));
            if (snowy)
                RoofFill(FrameRect(ch.U - s / 2, ch.V - s / 2, s, s * 0.22), Palette.Hex("#eef3f4"));
        }
        if (cfg.Extra == "ember-vent")
        {
            var u = x0 + W * (0.3 + rand() * 0.4);
            var c = Rw(u, ym);
            DrawCircle(c, 9, new Color(1f, 0.478f, 0.165f, 0.25f));
            var q = FrameRect(u - 4.5, ym - 4.5, 9, 9);
            RoofFill(q, Palette.Hex("#2c241e"));
            RoofFill(FrameRect(u - 2.8, ym - 2.8, 5.6, 5.6), Palette.Hex("#ff8a3d"));
        }
        if (cfg.Extra == "crystal-lamps")
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var u = (x0 + x1) / 2 + side * W * 0.3;
                var c = Rw(u, ym);
                DrawCircle(c, 10, new Color(0.631f, 0.851f, 0.831f, 0.25f));
                DrawCircle(c, 5.5f, Palette.Hex("#46606b"));
                RoofFill(
                    [new((float)u, (float)(ym - 5)), new((float)(u + 3), (float)ym), new((float)u, (float)(ym + 4)), new((float)(u - 3), (float)ym)],
                    Palette.Hex("#a1d9d4")
                );
            }
    }
}
