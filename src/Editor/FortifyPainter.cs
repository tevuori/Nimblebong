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

    readonly List<(string Kind, Vector2[] Pts, double Width, int Model)> _runs = [];

    public void Add(string kind, IReadOnlyList<Vector2> points, double width, int model)
    {
        if (points.Count < 2)
            return;
        _runs.Add((kind, Spline.Resample(points, 6), width, model));
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    public override void _Draw()
    {
        foreach (var (kind, pts, width, model) in _runs)
            if (kind == Kinds.Fence)
                DrawFence(pts);
            else
                DrawWall(pts, width, model);
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
        var poly = Spline.StrokePolygon(pts, w);
        Fill(poly, Mortar);
        Fill(Spline.StrokePolygon(pts, Math.Max(2, w - 4)), Stone);

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
