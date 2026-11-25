// Crossings — where a road meets a plateau rim it becomes stairs;
// where a river drops over one it becomes a waterfall. Detected as
// inside/outside transitions of the resampled stroke against each
// massif's Tops, so they appear automatically wherever the user
// routes a path across terrain.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class CrossingPainter : Node2D
{
    public Color Ink = new(0.22f, 0.19f, 0.12f);
    public Color WaterLight = new(0.72f, 0.88f, 0.9f);
    public Color Foam = new(0.94f, 0.97f, 0.96f);

    readonly List<(string Kind, List<Vector2> Pts, double Width)> _strokes = [];
    List<Massif> _massifs = [];

    public void SetMassifs(List<Massif> massifs) => _massifs = massifs;

    public void AddStroke(string kind, IReadOnlyList<Vector2> points, double width)
    {
        if (points.Count < 2)
            return;
        _strokes.Add((kind, [.. Spline.Resample(points)], width));
    }

    /// <summary>Points where the stroke crosses a plateau rim — the
    /// inside flag flips. Returns (point, direction).</summary>
    static List<(Vector2 P, Vector2 Dir)> RimCrossings(List<Vector2> pts, List<Massif> massifs)
    {
        var hits = new List<(Vector2, Vector2)>();
        var inside = false;
        for (var i = 0; i < pts.Count; i++)
        {
            var now = false;
            foreach (var m in massifs)
            {
                foreach (var top in m.Tops)
                    if (Poly.PointInPolygon(pts[i], top))
                    {
                        now = true;
                        break;
                    }
                if (now)
                    break;
            }
            if (now != inside && i > 0)
            {
                var dir = (pts[i] - pts[i - 1]);
                if (dir.LengthSquared() > 1e-6)
                    hits.Add(((pts[i] + pts[i - 1]) / 2, dir.Normalized()));
            }
            inside = now;
        }
        return hits;
    }

    public override void _Draw()
    {
        foreach (var (kind, pts, width) in _strokes)
            foreach (var (p, dir) in RimCrossings(pts, _massifs))
            {
                var n = new Vector2(-dir.Y, dir.X);
                if (kind == Kinds.River)
                    DrawWaterfall(p, dir, n, (float)width);
                else
                    DrawStairs(p, dir, n, (float)width);
            }
    }

    /// <summary>Treaded staircase climbing the rim — filled plank rungs
    /// between inked stringers, sitting on the slope band just downhill
    /// of the top edge so it reads as cut into the face, not floating
    /// on the lawn.</summary>
    void DrawStairs(Vector2 p, Vector2 dir, Vector2 n, float w)
    {
        // Which side is downhill — probe both ways for the point that
        // falls outside every top.
        var downhill = 1.0f;
        foreach (var s in new[] { 1.0f, -1.0f })
        {
            var probe = p + dir * s * 26;
            var inside = _massifs.Any(m => m.Tops.Any(t => Poly.PointInPolygon(probe, t)));
            if (!inside)
            {
                downhill = s;
                break;
            }
        }
        var c0 = p + dir * downhill * 14; // sit on the band, not the rim line
        var half = w * 0.5f;
        var step = w * 0.34f;
        var tread = Palette.Hex("#c2aa7c");
        var treadDark = Palette.Hex("#a98c5e");
        var rail = Palette.Hex("#51412c");

        // Shadow under the staircase.
        for (var i = -2; i <= 2; i++)
            Fill(
                Rung(c0 + dir * (i * step + 4), n, half, 4),
                new Color(0.05f, 0.06f, 0.04f, 0.18f)
            );
        // Stringers — chunky ink rails a touch wider than the treads.
        foreach (var s in new[] { -1.0f, 1.0f })
        {
            var a = c0 + n * s * (half + 1);
            DrawLine(a - dir * step * 2.6f, a + dir * step * 2.6f, rail, 2.4f);
        }
        // Rungs — alternating plank tones like the bridge deck.
        for (var i = -2; i <= 2; i++)
        {
            var c = c0 + dir * (i * step);
            var poly = Rung(c, n, half, 5);
            Fill(poly, (i % 2 == 0) ? tread : treadDark);
            DrawPolyline([.. poly, poly[0]], rail, 1.4f, true);
            // End grain — a pale tick at each tread end.
            foreach (var s in new[] { -1.0f, 1.0f })
                DrawLine(
                    c + n * s * (half - 2) - dir * 2,
                    c + n * s * (half - 2) + dir * 2,
                    Palette.Tint(tread, 18),
                    1.4f
                );
        }
        // Landing shadow at the downhill foot.
        DrawCircle(c0 + dir * downhill * step * 3.1f, half * 0.5f, new Color(0.05f, 0.06f, 0.04f, 0.15f));
    }

    static Vector2[] Rung(Vector2 c, Vector2 n, float half, float thick)
    {
        var d = new Vector2(-n.Y, n.X) * (thick / 2); // along the stair
        return [c + n * half - d, c + n * half + d, c - n * half + d, c - n * half - d];
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    /// <summary>Bright lip across the river + foam arcs just below it
    /// (the fall lands south/downstream of the crossing point).</summary>
    void DrawWaterfall(Vector2 p, Vector2 dir, Vector2 n, float w)
    {
        var half = w * 0.55f;
        var lip = p + dir * w * 0.18f;
        DrawLine(lip + n * half, lip - n * half, WaterLight, w * 0.34f);
        DrawLine(lip + n * half, lip - n * half, Foam, w * 0.12f);
        // Falling streaks — long white runnels streaming off the lip,
        // the signature top-down waterfall read.
        for (var k = -1; k <= 1; k++)
        {
            var off = lip + n * k * half * 0.55f;
            var len = w * (1.15f - Math.Abs(k) * 0.3f);
            DrawLine(
                off,
                off + dir * len,
                Foam with { A = 0.55f },
                w * (0.14f - Math.Abs(k) * 0.04f)
            );
        }
        // foam: two broken arcs on the downstream side of the lip
        var rng = new Random((int)(p.X * 31 + p.Y * 17));
        for (var i = 0; i < 3; i++)
        {
            var c = p + dir * (w * (0.5f + i * 0.3f));
            var r = w * (0.42f - i * 0.1f);
            var a0 = (float)(rng.NextDouble() * Math.PI * 2);
            DrawArc(c, r, a0, a0 + 1.6f, 6, Foam, 2.2f);
        }
        // splash ticks beside the lip
        for (var i = -1; i <= 1; i += 2)
        {
            var t = p + n * (i * half * 0.7f) + dir * w * 0.4f;
            DrawLine(t, t + dir * 6 + n * i * 2, WaterLight, 1.8f);
        }
    }
}
