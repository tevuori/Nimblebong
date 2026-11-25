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
        var rivers = _strokes.Where(s => s.Kind == Kinds.River).ToList();
        foreach (var (kind, pts, width) in _strokes)
        {
            // Low burial/garden terraces are walked over without a
            // carved fan; stairs are reserved for real cliff massifs.
            var hits = RimCrossings(
                pts,
                kind == Kinds.Road
                    ? _massifs.Where(m => m.Height >= 30).ToList()
                    : _massifs
            );
            if (kind == Kinds.Road)
                // A road entering the gorge exactly at a river crossing is
                // carried by the bridge deck; drawing a second staircase
                // there crowds the bridge with duplicate fans. Keep stair
                // crossings away from the water, where they read as the
                // reference's deliberate cliff access.
                hits = hits.Where(h =>
                        !rivers.Any(r => r.Pts.Any(q => q.DistanceSquaredTo(h.P) < 145 * 145))
                    )
                    .ToList();
            foreach (var (p, dir) in hits)
            {
                var n = new Vector2(-dir.Y, dir.X);
                if (kind == Kinds.River)
                    DrawWaterfall(p, dir, n, (float)width);
                else
                    DrawStairs(p, dir, n, (float)width);
            }
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
        foreach (var sgn in new[] { 1.0f, -1.0f })
        {
            var probe = p + dir * sgn * 26;
            var inside = _massifs.Any(m => m.Tops.Any(t => Poly.PointInPolygon(probe, t)));
            if (!inside)
            {
                downhill = sgn;
                break;
            }
        }

        // Fan staircase: concentric shallow stone sectors opening uphill
        // from a downhill pivot. This is the signature Nimblehold stair —
        // radial joints make it read as carved fan steps, not a ladder.
        var up = -dir * downhill;
        var center = p + dir * downhill * w * 1.15f;
        var baseAngle = Mathf.Atan2(up.Y, up.X);
        var stone = Palette.Hex("#d8d2ba");
        var stoneDark = Palette.Hex("#c0b795");
        var ink = Palette.Hex("#4b422f");
        const int rings = 7;
        var rInner = w * 0.48f;
        var rStep = w * 0.23f;
        var halfSpan = 1.02f;

        Vector2 Polar(float r, float a) => center + new Vector2((float)System.Math.Cos(a), (float)System.Math.Sin(a)) * r;

        // Soft fan shadow, offset downhill.
        var shadowPts = new System.Collections.Generic.List<Vector2>();
        var rOut = rInner + rStep * rings;
        for (var k = 0; k <= 12; k++)
        {
            var a = baseAngle - halfSpan + halfSpan * 2 * k / 12f;
            shadowPts.Add(Polar(rOut + 4, a) + new Vector2(4, 7));
        }
        shadowPts.Add(center + new Vector2(4, 7));
        Fill([.. shadowPts], new Color(0.05f, 0.06f, 0.04f, 0.1f));

        for (var ring = 0; ring < rings; ring++)
        {
            var r0 = rInner + rStep * ring;
            var r1 = r0 + rStep * 0.9f;
            var pts = new System.Collections.Generic.List<Vector2>();
            const int seg = 8;
            for (var k = 0; k <= seg; k++)
            {
                var a = baseAngle - halfSpan + halfSpan * 2 * k / (float)seg;
                pts.Add(Polar(r1, a));
            }
            for (var k = seg; k >= 0; k--)
            {
                var a = baseAngle - halfSpan + halfSpan * 2 * k / (float)seg;
                pts.Add(Polar(r0, a));
            }
            Fill([.. pts], ring % 2 == 0 ? stone : stoneDark);
            // Inked outer nose of every tread.
            var nose = new System.Collections.Generic.List<Vector2>();
            for (var k = 0; k <= seg; k++)
            {
                var a = baseAngle - halfSpan + halfSpan * 2 * k / (float)seg;
                nose.Add(Polar(r1, a));
            }
            DrawPolyline([.. nose], ink with { A = 0.84f }, 1.35f, true);
        }
        // Radial joints dividing the fan into wedge treads.
        for (var j = -2; j <= 2; j++)
        {
            var a = baseAngle + j * halfSpan / 2.35f;
            DrawLine(
                Polar(rInner * 0.8f, a),
                Polar(rOut, a),
                ink with { A = 0.72f },
                1.1f,
                true
            );
        }
        // Outer lip and downhill landing shade.
        DrawArc(center, rOut, baseAngle - halfSpan, baseAngle + halfSpan, 16, ink with { A = 0.86f }, 1.8f);
        DrawCircle(center + dir * downhill * w * 0.2f, w * 0.34f, new Color(0.05f, 0.06f, 0.04f, 0.1f));
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
        DrawLine(lip + n * half, lip - n * half, WaterLight, w * 0.13f);
        DrawLine(lip + n * half, lip - n * half, Foam, w * 0.055f);
        // Falling streaks — long white runnels streaming off the lip,
        // the signature top-down waterfall read.
        for (var k = -1; k <= 1; k++)
        {
            var off = lip + n * k * half * 0.55f;
            var len = w * (0.82f - Math.Abs(k) * 0.18f);
            DrawLine(
                off,
                off + dir * len,
                Foam with { A = 0.55f },
                w * (0.065f - Math.Abs(k) * 0.015f)
            );
        }
        // foam: two broken arcs on the downstream side of the lip
        var rng = new Random((int)(p.X * 31 + p.Y * 17));
        for (var i = 0; i < 3; i++)
        {
            var c = p + dir * (w * (0.5f + i * 0.3f));
            var r = w * (0.3f - i * 0.07f);
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
