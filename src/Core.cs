// Shared deterministic helpers — ported from the TS `scene-shared`
// module so the Godot renderer agrees with the web renderer on every
// seeded number.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

/// <summary>Hash of a number to 0..1: same input, same output.</summary>
public static class Rng
{
    public static double Noise(double n)
    {
        var v = Math.Sin(n * 127.1 + 311.7) * 43758.5453;
        return v - Math.Floor(v);
    }
}

public static class Palette
{
    public static Color Hex(string hex) => new(hex);

    /// <summary>Brightens (+) or darkens (−) each channel, like TS `tint`.</summary>
    public static Color Tint(Color c, double amount) =>
        new(
            Mathf.Clamp(c.R + (float)(amount / 255), 0f, 1f),
            Mathf.Clamp(c.G + (float)(amount / 255), 0f, 1f),
            Mathf.Clamp(c.B + (float)(amount / 255), 0f, 1f),
            c.A
        );

    public static Color TintAlpha(Color c, double amount, double alpha) =>
        Tint(c, amount) with { A = (float)alpha };

    public static Color Mix(Color a, Color b, double t) =>
        new(
            a.R + (b.R - a.R) * (float)t,
            a.G + (b.G - a.G) * (float)t,
            a.B + (b.B - a.B) * (float)t
        );

    /// <summary>Pull a color toward its own luminance — TS `desaturate`.</summary>
    public static Color Desaturate(Color c, double amount)
    {
        var lum = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        return Mix(c, new Color(lum, lum, lum, c.A), amount);
    }
}

public static class Poly
{
    /// <summary>Resample a (closed) polyline at `step` px, returning each
    /// sample point + tangent angle + index — the `sampleLine` port.</summary>
    public static IEnumerable<(Vector2 Point, double Angle, int Index)> SampleLine(
        IReadOnlyList<Vector2> poly,
        double step,
        bool closed = true
    )
    {
        var n = poly.Count;
        var segCount = closed ? n : n - 1;
        var index = 0;
        for (var i = 0; i < segCount; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            var len = (b - a).Length();
            if (len < 1e-4) continue;
            var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
            for (var d = 0.0; d < len; d += step)
                yield return (a + (b - a) * (float)(d / len), angle, index++);
        }
    }

    public static bool PointInPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (a, b) = (poly[i], poly[j]);
            if (
                a.Y > p.Y != b.Y > p.Y
                && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X
            )
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Contiguous runs of a closed contour matching a predicate —
    /// used to shade only the south (leeward) arc darker.</summary>
    public static List<List<Vector2>> Runs(
        IReadOnlyList<Vector2> poly,
        Func<Vector2, bool> keep
    )
    {
        var result = new List<List<Vector2>>();
        var cur = new List<Vector2>();
        for (var i = 0; i <= poly.Count; i++)
        {
            var p = poly[i % poly.Count];
            if (keep(p))
                cur.Add(p);
            else if (cur.Count > 0)
            {
                result.Add(cur);
                cur = new List<Vector2>();
            }
        }
        if (cur.Count > 0)
            result.Add(cur);
        return result;
    }

    /// <summary>An ellipse as an 8-gon — DrawColoredPolygon/Line target
    /// where a rotated ellipse is needed.</summary>
    public static Vector2[] Ellipse(
        double x,
        double y,
        double rx,
        double ry,
        double rotation = 0,
        int segments = 10
    )
    {
        var pts = new Vector2[segments];
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        for (var i = 0; i < segments; i++)
        {
            var a = (i / (double)segments) * Math.PI * 2;
            var ex = Math.Cos(a) * rx;
            var ey = Math.Sin(a) * ry;
            pts[i] = new Vector2(
                (float)(x + ex * cos - ey * sin),
                (float)(y + ex * sin + ey * cos)
            );
        }
        return pts;
    }

    public static Vector2 Centroid(IReadOnlyList<Vector2> poly)
    {
        var sum = Vector2.Zero;
        foreach (var p in poly)
            sum += p;
        return sum / poly.Count;
    }
}
