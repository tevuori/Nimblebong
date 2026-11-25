#nullable enable
// Surface painter — ground areas ("surface:<id>" shapes) in the albedo
// viewport, above ponds/fields and under props: a feathered wash, a rim
// and seeded patterns clipped to the outline. Everything visual comes
// from SurfaceDefs (assets/config/surfaces). Deterministic: every mark is
// seeded from its position via Rng.Noise.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class SurfacePainter : Node2D
{
    public ResolvedBiome? Biome;

    readonly List<(SurfaceDef Def, Vector2[] Poly)> _areas = [];

    public void Add(SurfaceDef def, IReadOnlyList<Vector2> points)
    {
        if (points.Count >= 3)
            _areas.Add((def, [.. points]));
    }

    Color C(string spec, double alpha = 1)
    {
        var c = PropDefs.ResolveColor(spec, Biome);
        return c with { A = (float)(c.A * alpha) };
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    public override void _Draw()
    {
        foreach (var (def, raw) in _areas)
        {
            var merged = Geometry2D.MergePolygons(raw, raw);
            foreach (var poly in merged.Where(p => !Geometry2D.IsPolygonClockwise(p) || merged.Count == 1))
                DrawSurface(def, poly);
        }
    }

    void DrawSurface(SurfaceDef def, Vector2[] poly)
    {
        // Feather: a few expanding halos with falling alpha.
        const int steps = 4;
        for (var i = steps; i >= 1; i--)
        {
            var grow = (float)(def.Feather * i / steps);
            foreach (var ring in Geometry2D.OffsetPolygon(poly, grow, Geometry2D.PolyJoinType.Round))
                Fill(ring, C(def.Color, def.Alpha * 0.22 * (1 - (i - 1) / (double)steps)));
        }
        Fill(poly, C(def.Color, def.Alpha));
        if (def.EdgeColor != null && def.EdgeWidth > 0)
            DrawPolyline([.. poly, poly[0]], C(def.EdgeColor, def.EdgeAlpha), (float)def.EdgeWidth, true);

        var (min, max) = Bounds(poly);
        foreach (var pat in def.Patterns)
        {
            var sp = pat.Spacing;
            var row = 0;
            for (var y = min.Y; y <= max.Y; y += (float)sp, row++)
                for (var x = min.X + (row % 2) * (float)sp * 0.5f; x <= max.X; x += (float)sp)
                {
                    var s = x * 0.731 + y * 0.379 + pat.Size * 7.1;
                    var p = new Vector2(
                        x + (float)((Rng.Noise(s + 1) - 0.5) * sp * 0.9),
                        y + (float)((Rng.Noise(s + 2) - 0.5) * sp * 0.9)
                    );
                    if (!Poly.PointInPolygon(p, poly))
                        continue;
                    Mark(pat, p, s, poly);
                }
        }
    }

    void Mark(SurfacePattern pat, Vector2 p, double s, Vector2[] poly)
    {
        var col = C(pat.Color, pat.Alpha * (0.7 + Rng.Noise(s + 3) * 0.3));
        var size = pat.Size * (0.7 + Rng.Noise(s + 4) * 0.6);
        Vector2 Dir(double a) => new((float)Math.Cos(a), (float)Math.Sin(a));
        switch (pat.Type)
        {
            case "dots":
                DrawCircle(p, (float)size, col);
                break;
            case "stones":
            {
                var n = 6;
                var pts = new Vector2[n];
                var rot = Rng.Noise(s + 5) * Math.Tau;
                for (var k = 0; k < n; k++)
                    pts[k] = p + Dir(rot + k / (double)n * Math.Tau) * (float)(size * (0.7 + Rng.Noise(s + 10 + k) * 0.5));
                Fill(pts, col);
                DrawPolyline([.. pts, pts[0]], col.Darkened(0.45f) with { A = col.A }, 0.8f, true);
                break;
            }
            case "strokes":
            {
                var a = pat.Angle + (Rng.Noise(s + 5) - 0.5) * 0.9;
                DrawLine(p, p + Dir(a) * (float)size, col, 1.1f, true);
                break;
            }
            case "hatch":
                DrawLine(p - Dir(pat.Angle) * (float)(size * 0.5), p + Dir(pat.Angle) * (float)(size * 0.5), col, 1f, true);
                break;
            case "ripples":
                DrawArc(p, (float)size, (float)(Math.PI * 1.1), (float)(Math.PI * 1.9), 8, col, 1f, true);
                break;
            case "waves":
            {
                var pts = new Vector2[9];
                var along = Dir(pat.Angle);
                var across = Dir(pat.Angle + Math.PI / 2);
                for (var k = 0; k < pts.Length; k++)
                {
                    var t = k / (double)(pts.Length - 1) - 0.5;
                    pts[k] = p + along * (float)(t * size * 4) + across * (float)(Math.Sin(t * Math.PI * 2) * size * 0.35);
                }
                var inside = pts.Where(q => Poly.PointInPolygon(q, poly)).ToArray();
                if (inside.Length >= 2)
                    DrawPolyline(inside, col, 1.2f, true);
                break;
            }
            case "cracks":
            {
                var pts = new List<Vector2> { p };
                var a = Rng.Noise(s + 5) * Math.Tau;
                for (var k = 0; k < 4; k++)
                {
                    a += (Rng.Noise(s + 20 + k) - 0.5) * 1.4;
                    var next = pts[^1] + Dir(a) * (float)(size * (0.6 + Rng.Noise(s + 30 + k) * 0.6));
                    if (!Poly.PointInPolygon(next, poly))
                        break; // cracks stop at the outline
                    pts.Add(next);
                }
                if (pts.Count < 2)
                    break;
                if (pat.Glow != null)
                    DrawPolyline([.. pts], C(pat.Glow, pat.Alpha * 0.45), 4.5f, true);
                DrawPolyline([.. pts], col, 1.3f, true);
                break;
            }
        }
    }

    static (Vector2 Min, Vector2 Max) Bounds(Vector2[] poly)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var p in poly)
        {
            min = min.Min(p);
            max = max.Max(p);
        }
        return (min, max);
    }

    /// <summary>Props a surface scatters inside itself at render time
    /// (not saved in the scene) — reeds in a marsh, stones on scree.</summary>
    public static IEnumerable<PropPlacement> ScatterProps(Scene scene, string biome)
    {
        foreach (var sh in scene.Shapes)
        {
            if (SurfaceDefs.ForKind(sh.Kind) is not { } def || def.Scatter.Count == 0)
                continue;
            var poly = sh.Points;
            var min = new Vector2(poly.Min(q => q.X), poly.Min(q => q.Y));
            var max = new Vector2(poly.Max(q => q.X), poly.Max(q => q.Y));
            var li = 0;
            foreach (var sc in def.Scatter)
            {
                li++;
                if (PropDefs.TryGet(sc.Kind, out var pd) && !pd.InBiome(biome))
                    continue;
                var size = sc.Size > 0 ? sc.Size : FillRules.Config.SizeOf(sc.Kind);
                var row = 0;
                for (var y = min.Y; y <= max.Y; y += (float)sc.Spacing, row++)
                    for (var x = min.X + (row % 2) * (float)sc.Spacing * 0.5f; x <= max.X; x += (float)sc.Spacing)
                    {
                        var s = x * 0.37 + y * 0.53 + li * 101.3 + sh.Id * 7.7;
                        var p = new Vector2(
                            x + (float)((Rng.Noise(s + 1) - 0.5) * sc.Spacing * 0.9),
                            y + (float)((Rng.Noise(s + 2) - 0.5) * sc.Spacing * 0.9)
                        );
                        if (Rng.Noise(s + 3) > sc.Chance || !Poly.PointInPolygon(p, poly))
                            continue;
                        var scale = size * (sc.ScaleMin + Rng.Noise(s + 4) * (sc.ScaleMax - sc.ScaleMin));
                        yield return new PropPlacement(
                            SceneView.MapProp(sc.Kind), p.X, p.Y, scale / 24.0,
                            Rng.Noise(s + 5) * Math.Tau, s
                        ).WithDef(sc.Kind);
                    }
            }
        }
    }
}
