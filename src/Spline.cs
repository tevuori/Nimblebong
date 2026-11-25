// Spline + stroke geometry for rivers and roads — Catmull-Rom
// resampling of a few control points into a dense polyline, and the
// strokePolygon port that turns a polyline into a closed ribbon by
// offsetting ±halfwidth along smoothed per-vertex normals.
// Deterministic: no RNG state, all jitter is position-keyed Rng.Noise.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public static class Spline
{
    /// <summary>Catmull-Rom through the control points — `subdivisions`
    /// samples per segment, capped so spacing never drops below ~6 px;
    /// dense freehand input stays wiggle-free instead of exploding into
    /// thousands of sub-pixel samples.</summary>
    public static Vector2[] Resample(IReadOnlyList<Vector2> control, int subdivisions = 10)
    {
        if (control.Count < 2)
            return [.. control];
        var pts = new List<Vector2>(control.Count * subdivisions);
        for (var i = 0; i < control.Count - 1; i++)
        {
            var p0 = control[Math.Max(0, i - 1)];
            var p1 = control[i];
            var p2 = control[i + 1];
            var p3 = control[Math.Min(control.Count - 1, i + 2)];
            var seg = Math.Max(
                2,
                Math.Min(subdivisions, (int)Math.Round(p1.DistanceTo(p2) / 6f))
            );
            for (var s = 0; s < seg; s++)
            {
                var t = s / (double)seg;
                var t2 = t * t;
                var t3 = t2 * t;
                pts.Add(
                    new Vector2(
                        (float)(
                            0.5
                            * (
                                2 * p1.X
                                + (-p0.X + p2.X) * t
                                + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2
                                + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3
                            )
                        ),
                        (float)(
                            0.5
                            * (
                                2 * p1.Y
                                + (-p0.Y + p2.Y) * t
                                + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2
                                + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3
                            )
                        )
                    )
                );
            }
        }
        pts.Add(control[^1]);
        return [.. pts];
    }

    /// <summary>Position-keyed ink jitter — the same coordinates always
    /// wobble the same way, so stacked bands wobble in unison (the TS
    /// renderer's `line`/`outline` trick).</summary>
    public static Vector2[] Ink(IReadOnlyList<Vector2> points, double amp = 1.3)
    {
        var outp = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            outp[i] = new Vector2(
                p.X + (float)(Rng.Noise(p.X * 0.773 + p.Y * 1.291) - 0.5f) * (float)amp,
                p.Y + (float)(Rng.Noise(p.X * 1.339 + p.Y * 0.719) - 0.5f) * (float)amp
            );
        }
        return outp;
    }

    /// <summary>Smoothed "left" unit normals of an open polyline —
    /// (−dy, dx) of the chord through the vertex neighbors, matching
    /// the TS strokePolygon convention (screen coords, y down).</summary>
    public static Vector2[] Normals(IReadOnlyList<Vector2> points)
    {
        var n = points.Count;
        var normals = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var prev = points[Math.Max(0, i - 1)];
            var next = points[Math.Min(n - 1, i + 1)];
            var d = next - prev;
            var len = Math.Max(d.Length(), 1e-4f);
            normals[i] = new Vector2(-d.Y / len, d.X / len);
        }
        return normals;
    }

    /// <summary>The polyline offset by `lateral` px along its normals —
    /// used for road wheel ruts (TS offsetRun).</summary>
    public static Vector2[] OffsetLine(IReadOnlyList<Vector2> points, double lateral)
    {
        var normals = Normals(points);
        var outp = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
            outp[i] = points[i] + normals[i] * (float)lateral;
        return outp;
    }

    /// <summary>Both banks of a stroked run: Left = +normal side,
    /// Right = −normal side (same order as the centerline).</summary>
    public static (Vector2[] Left, Vector2[] Right) StrokeSides(
        IReadOnlyList<Vector2> points,
        double width
    )
    {
        var half = (float)(width / 2);
        var normals = Normals(points);
        var left = new Vector2[points.Count];
        var right = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            left[i] = points[i] + normals[i] * half;
            right[i] = points[i] - normals[i] * half;
        }
        return (left, right);
    }

    /// <summary>Closed ribbon polygon: left edge forward + right edge
    /// reversed — a 1:1 port of the TS `strokePolygon`.</summary>
    public static Vector2[] StrokePolygon(IReadOnlyList<Vector2> points, double width)
    {
        var (left, right) = StrokeSides(points, width);
        var poly = new Vector2[left.Length + right.Length];
        left.CopyTo(poly, 0);
        for (var i = 0; i < right.Length; i++)
            poly[left.Length + i] = right[right.Length - 1 - i];
        return poly;
    }
}
