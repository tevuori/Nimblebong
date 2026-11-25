// Fortifications — wall and fence strokes. Port of the web renderer:
// a fence is two offset rails with posts every ~26px; a wall is a
// dark base band with a lighter stone core, model 1 adds crenellation
// merlons, model 2 draws a double wall (two parapet lines + ticks).
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class FortifyPainter : Node2D
{
    public Color Stone = Palette.Hex("#b7b69a");
    public Color Wood = Palette.Hex("#6e563c");
    public Color Mortar = Palette.Hex("#595c48");
    public Color Ink = Palette.Hex("#3a3527");

    // A stroke drawn as one or more gap-separated segments; Posts hold
    // gate-pillar positions where a crossing carved an opening.
    readonly List<(string Kind, List<Vector2[]> Segs, List<Vector2> Posts, double Width, int Model)> _runs =
        [];

    /// <param name="crossings">Road strokes (pts, width) — the wall or
    /// fence opens a gated gap wherever it crosses one.</param>
    public void Add(
        string kind,
        IReadOnlyList<Vector2> points,
        double width,
        int model,
        IReadOnlyList<(IReadOnlyList<Vector2> Pts, double W)>? crossings = null
    )
    {
        if (points.Count < 2)
            return;
        var pts = Spline.Resample(points, 6);
        var (segs, posts) =
            crossings is { Count: > 0 }
                ? CutAtCrossings(pts, width, crossings)
                : ([pts], new List<Vector2>());
        _runs.Add((kind, segs, posts, width, model));
    }

    /// <summary>Split the run where it lies inside a road ribbon —
    /// each cut pair becomes a gap flanked by gate posts.</summary>
    static (List<Vector2[]>, List<Vector2>) CutAtCrossings(
        Vector2[] pts,
        double width,
        IReadOnlyList<(IReadOnlyList<Vector2> Pts, double W)> crossings
    )
    {
        var hit = new bool[pts.Length];
        for (var i = 0; i < pts.Length; i++)
            foreach (var (cp, cw) in crossings)
            {
                var lim = cw / 2 + width / 2 + 8;
                for (var k = 1; k < cp.Count && !hit[i]; k++)
                    if (SegDist(pts[i], cp[k - 1], cp[k]) <= lim)
                        hit[i] = true;
            }
        var segs = new List<Vector2[]>();
        var posts = new List<Vector2>();
        var cur = new List<Vector2>();
        for (var i = 0; i < pts.Length; i++)
        {
            if (hit[i])
            {
                if (cur.Count >= 2)
                    segs.Add([.. cur]);
                if (cur.Count > 0)
                    posts.Add(cur[^1]);
                cur.Clear();
                continue;
            }
            if (cur.Count == 0 && i > 0)
                posts.Add(pts[i]);
            cur.Add(pts[i]);
        }
        if (cur.Count >= 2)
            segs.Add([.. cur]);
        return (segs, posts);
    }

    static double SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 < 1e-6 ? 0 : Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
        return (a + ab * (float)t - p).Length();
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    public override void _Draw()
    {
        foreach (var (kind, segs, posts, width, model) in _runs)
        {
            foreach (var seg in segs)
                if (kind == Kinds.Fence)
                    DrawFence(seg);
                else
                    DrawWall(seg, width, model);
            foreach (var p in posts)
                DrawGatePost(p, kind, width);
        }
    }

    /// <summary>Pillar capping a wall/fence end at a road gate — stone
    /// for walls, a taller wooden post for fences.</summary>
    void DrawGatePost(Vector2 p, string kind, double w)
    {
        if (kind == Kinds.Fence)
        {
            DrawCircle(p, 4.2f, Ink);
            DrawCircle(p, 3.2f, Wood);
            return;
        }
        var r = (float)Math.Max(4, w * 0.5 + 2);
        DrawCircle(p, r + 2, Mortar);
        DrawCircle(p, r, Stone);
        DrawCircle(p, r * 0.5f, Palette.Tint(Stone, 14));
    }

    /// <summary>Two wooden rails riding the segment offsets + posts.</summary>
    void DrawFence(Vector2[] pts)
    {
        foreach (var off in new[] { -3.0, 2.0 })
        {
            var rail = Spline.OffsetLine(pts, off);
            for (var i = 0; i + 1 < rail.Length; i++)
                DrawLine(rail[i], rail[i + 1], Wood, 2.2f);
        }
        // Posts on a ~26px walk along the polyline.
        var step = 26.0;
        var acc = 0.0;
        for (var i = 0; i + 1 < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var len = a.DistanceTo(b);
            var d = step - acc;
            while (d <= len)
            {
                var p = a + (b - a) * (float)(d / len);
                DrawCircle(p, 3.2f, Ink);
                DrawCircle(p, 2.2f, Wood);
                d += step;
            }
            acc = (acc + len) % step;
        }
    }

    /// <summary>Stone wall — base band + lighter core; crenellation or
    /// a double parapet depending on the stroke model.</summary>
    void DrawWall(Vector2[] pts, double w, int model)
    {
        // Per-segment bands — StrokePolygon self-intersects on closed
        // loops (paddock rings), per-segment quads survive them.
        void Band(Vector2[] line, double ww, Color c)
        {
            for (var i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                var d = b - a;
                if (d.Length() < 0.5f)
                    continue;
                var n = new Vector2(-d.Y, d.X).Normalized() * (float)(ww / 2);
                Fill([a + n, b + n, b - n, a - n], c);
            }
        }
        Band(pts, w, Mortar);
        Band(pts, Math.Max(2, w - 4), Stone);

        if (model == 2)
        {
            // Double wall: two bright parapet lines along both rims.
            var (left, right) = Spline.StrokeSides(pts, w * 0.8);
            foreach (var edge in new[] { left, right })
            {
                for (var i = 0; i + 1 < edge.Length; i++)
                {
                    DrawLine(edge[i], edge[i + 1], Palette.Hex("#4c503c"), (float)(w * 0.28));
                    DrawLine(edge[i], edge[i + 1], Stone, (float)Math.Max(2, w * 0.28 - 3));
                }
            }
        }
        else if (model == 1)
        {
            // Crenellation: merlon squares on both rims every ~14px.
            var (left, right) = Spline.StrokeSides(pts, w * 0.5);
            foreach (var edge in new[] { left, right })
            {
                var acc = 0.0;
                for (var i = 0; i + 1 < edge.Length; i++)
                {
                    var a = edge[i];
                    var b = edge[i + 1];
                    var len = a.DistanceTo(b);
                    var d = 14.0 - acc;
                    while (d <= len)
                    {
                        var p = a + (b - a) * (float)(d / len);
                        var dir = (b - a).Normalized();
                        var n = new Vector2(-dir.Y, dir.X);
                        var q = new[]
                        {
                            p - dir * 2.5f - n * 2.5f,
                            p + dir * 2.5f - n * 2.5f,
                            p + dir * 2.5f + n * 2.5f,
                            p - dir * 2.5f + n * 2.5f,
                        };
                        Fill(q, Palette.Tint(Stone, 14));
                        d += 14.0;
                    }
                    acc = (acc + len) % 14.0;
                }
            }
        }
        else
        {
            // Mortar ticks across the wall every ~16px.
            for (var i = 0; i + 1 < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var n = (b - a).Normalized();
                n = new Vector2(-n.Y, n.X);
                var len = a.DistanceTo(b);
                for (var d = 0.0; d <= len; d += 16)
                {
                    var p = a + (b - a) * (float)(d / len);
                    DrawLine(p - n * (float)(w * 0.32), p + n * (float)(w * 0.32), Palette.Hex("#686b56"), 1.5f);
                }
            }
        }
    }
}
