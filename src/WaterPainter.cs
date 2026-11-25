// Water + road albedo painter — Godot port of `waterGroup` / `roadGroup`
// from the web renderer. Rivers are stacked ribbon fills (verge → bank
// edge → deep rim → shallow → center) built with Spline.StrokePolygon,
// plus a hand-inked shoreline seam, south-bank shading, downstream
// ripple dashes and foam specks. Roads are packed-dirt ribbons with a
// trodden halo, verge, inked edge and wheel ruts.
// Canvas has no clip state: every band is a pre-built polygon pushed
// through the Fill gate (clipper slivers rejected by triangulation).
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class WaterPainter : Node2D
{
    // Woodland palette (biome defaults from src/config).
    public Color Water = Palette.Hex("#298f99");
    public Color BankVerge = Palette.Hex("#647750");
    public Color BankEdge = Palette.Hex("#b4c985");
    public Color RoadDirt = Palette.Hex("#d1b87c");
    public Color RoadVerge = Palette.Hex("#a7aa64");
    public Color RoadHalo = Palette.Hex("#e6dda0") with { A = 0.35f };

    readonly List<(Vector2[] Run, double Width)> _rivers = [];
    readonly List<(Vector2[] Run, double Width)> _roads = [];

    /// <summary>River from spline control points — resampled dense and
    /// ink-jittered once, so every derived band shares the wobble.</summary>
    public void AddRiver(IReadOnlyList<Vector2> control, double width)
    {
        _rivers.Add((Spline.Ink(Spline.Resample(control)), width));
        QueueRedraw();
    }

    public void AddRoad(IReadOnlyList<Vector2> control, double width)
    {
        _roads.Add((Spline.Ink(Spline.Resample(control)), width));
        QueueRedraw();
    }

    /// <summary>Fill a polygon only when it triangulates — clipper and
    /// stroke geometry can yield degenerate slivers the canvas renderer
    /// rejects.</summary>
    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    /// <summary>Ribbon fill that survives self-intersections at tight
    /// bends — unioning the polygon with itself repairs the winding and
    /// splits bow-ties into clean pieces (ARCHITECTURE.md rule).</summary>
    void FillRibbon(Vector2[] poly, Color color)
    {
        foreach (var piece in Geometry2D.MergePolygons(poly, poly))
            Fill(piece, color);
    }

    /// <summary>Closed inked outline around a polygon (the TS `outline`
    /// close=true — DrawPolyline doesn't join last→first).</summary>
    void InkOutline(Vector2[] poly, Color color, float width)
    {
        var pts = Spline.Ink(poly);
        var closed = new Vector2[pts.Length + 1];
        pts.CopyTo(closed, 0);
        closed[^1] = pts[0];
        DrawPolyline(closed, color, width, true);
    }

    public override void _Draw()
    {
        DrawRiverGroup();
        DrawRoadGroup();
    }

    /// <summary>Union a list of stroke ribbons into clean surface pieces —
    /// crossing strokes merge into one body, disjoint strokes stay separate
    /// (TS unionCached). Self-merge repairs bow-ties at tight bends.</summary>
    static List<Vector2[]> UnionAll(IReadOnlyList<(Vector2[] Run, double Width)> strokes, double delta)
    {
        var polys = new List<Vector2[]>();
        foreach (var (run, w) in strokes)
        {
            var poly = Spline.StrokePolygon(run, Math.Max(3, w + delta));
            polys.AddRange(Geometry2D.MergePolygons(poly, poly));
        }
        return TerrainGeometry.Union([.. polys]);
    }

    void DrawRiverGroup()
    {
        var deep = Palette.Tint(Water, -22);
        var shallow = Palette.Tint(Water, 58);
        // Bank stack per stroke — wide grassy verge, pale trodden edge,
        // deep rim, inked shoreline. Drawn before the unioned fill so the
        // merged water covers the seams inside a confluence (TS order).
        foreach (var (run, w) in _rivers)
        {
            FillRibbon(Spline.StrokePolygon(run, w + 22), BankVerge);
            FillRibbon(Spline.StrokePolygon(run, w + 18), BankEdge);
            FillRibbon(Spline.StrokePolygon(run, w + 3), deep);
            InkOutline(
                Spline.StrokePolygon(run, w + 5),
                Palette.Hex("#1d2b1c") with { A = 0.5f },
                1.4f
            );
        }
        // Unioned water surface — shallow rim, then center, then a darker
        // core — so confluences read as one body of water.
        foreach (var piece in UnionAll(_rivers, 0))
            Fill(piece, shallow);
        foreach (var piece in UnionAll(_rivers, -10))
            Fill(piece, Water);
        foreach (var piece in UnionAll(_rivers, -18))
            Fill(piece, Palette.Tint(Water, -14));
        foreach (var (run, w) in _rivers)
            DrawRiverDetails(run, w);
    }

    void DrawRiverDetails(Vector2[] run, double w)
    {
        var waterSeed = run[0].X * 0.13 + run[0].Y * 0.31;

        // Watercolor mottle — sparse low-alpha blobs drifting inside the
        // channel so the fill reads washed, not flat.
        foreach (var (p, angle, index) in Poly.SampleLine(run, Math.Max(40, w * 1.6), closed: false))
        {
            var seed = p.X * 0.19 + p.Y * 0.43 + index * 5.1 + waterSeed;
            if (Rng.Noise(seed) < 0.35)
                continue;
            var blob = Poly.Ellipse(
                p.X,
                p.Y,
                Math.Max(6, (w - 18) * (0.45 + Rng.Noise(seed + 1) * 0.5)),
                Math.Max(4, (w - 18) * (0.3 + Rng.Noise(seed + 2) * 0.3)),
                angle
            );
            Fill(
                blob,
                Rng.Noise(seed + 3) > 0.5
                    ? Palette.TintAlpha(Water, 34, 0.12)
                    : Palette.TintAlpha(Water, -26, 0.12)
            );
        }

        // Bank shading hugging the water edge: the south bank falls into
        // shadow, the north rim catches a pale foam line.
        var (left, right) = Spline.StrokeSides(run, w);
        var normals = Spline.Normals(run);
        BankShading(left, normals, +1);
        BankShading(right, normals, -1);
        FoamSpecks(left, normals, +1, waterSeed);
        FoamSpecks(right, normals, -1, waterSeed);
        BankDetails(run, w, left, normals, +1, waterSeed);
        BankDetails(run, w, right, normals, -1, waterSeed);

        // Downstream ripple dashes — sparse, seeded, bowed mid-dash.
        var rippleInk = Palette.TintAlpha(Water, 95, 0.4);
        var rippleDark = Palette.TintAlpha(Water, -34, 0.3);
        foreach (var (p, angle, index) in Poly.SampleLine(run, 16, closed: false))
        {
            var seed = p.X * 0.31 + p.Y * 0.47 + index * 3.7 + waterSeed;
            if (Rng.Noise(seed) < 0.25)
                continue;
            var lateral = (Rng.Noise(seed + 1) - 0.5) * Math.Max(1, w - 18);
            var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            var side = new Vector2(-dir.Y, dir.X);
            var a = p + side * (float)lateral;
            var len = 6 + Rng.Noise(seed + 2) * 9;
            var bow = (float)((Rng.Noise(seed + 3) - 0.5) * 3);
            var mid = a + dir * (float)(len * 0.5) + side * bow;
            var b = a + dir * (float)len;
            DrawPolyline(
                [a, mid, b],
                Rng.Noise(seed + 4) > 0.75 ? rippleDark : rippleInk,
                1.3f,
                true
            );
        }
    }

    /// <summary>Walk a water edge; where the outward normal leans south
    /// (toward the viewer) trace a shadowed bank line, where it leans
    /// north a pale foam rim just inside the water.</summary>
    void BankShading(Vector2[] side, Vector2[] normals, int sign)
    {
        var shadow = Palette.TintAlpha(Water, -46, 0.4);
        var rim = Palette.Hex("#eaf8f2") with { A = 0.3f };
        var runPts = new List<Vector2>();
        var dark = false;
        void Flush()
        {
            if (runPts.Count > 1)
                DrawPolyline([.. runPts], dark ? shadow : rim, dark ? 3.2f : 1.8f, true);
            runPts.Clear();
        }
        for (var i = 0; i < side.Length; i++)
        {
            // outward.y: left side normal points away from the centerline;
            // for the right side flip the sign.
            var outwardY = normals[i].Y * sign;
            var wantDark = outwardY > 0.35;
            var wantLight = outwardY < -0.35;
            if (wantDark || wantLight)
            {
                if (runPts.Count > 0 && dark != wantDark)
                    Flush();
                dark = wantDark;
                runPts.Add(side[i]);
            }
            else
                Flush();
        }
        Flush();
    }

    /// <summary>Scattered bright foam specks just inland of the
    /// shoreline (paintFoamEdge pass 3).</summary>
    void FoamSpecks(Vector2[] side, Vector2[] normals, int sign, double waterSeed)
    {
        for (var i = 0; i < side.Length; i += 3)
        {
            var s = Rng.Noise(i * 11.3 + waterSeed);
            if (Rng.Noise(i * 7.9 + waterSeed) < 0.5)
                continue;
            var inward = normals[i] * -sign;
            var off = 2.5f + (float)s * 3.5f;
            var p =
                side[i]
                + inward * off
                + new Vector2(
                    (float)(Rng.Noise(i * 5.7 + waterSeed) - 0.5) * 2.5f,
                    (float)(Rng.Noise(i * 4.3 + waterSeed) - 0.5) * 2.5f
                );
            DrawCircle(p, 0.8f + (float)s * 1.5f, new Color(1, 1, 1, 0.45f));
        }
    }

    /// <summary>Grass-bank dressing (TS waterDetails): reed strokes and
    /// turf blobs just outside the shoreline, lily pads drifting inside
    /// on wide stretches, loose pebbles at the water edge.</summary>
    void BankDetails(
        Vector2[] run,
        double w,
        Vector2[] side,
        Vector2[] normals,
        int sign,
        double waterSeed
    )
    {
        var reeds = Palette.Hex("#5e7a45");
        var turf = Palette.Hex("#76a64e");
        var turfInk = Palette.Hex("#3e6841");
        for (var i = 4; i < side.Length; i += 3)
        {
            var outward = normals[i] * sign;
            if (outward.Y > 0.9f) // skip the shadowed south rim — reads cluttered
                continue;
            var p = side[i] + outward * 7f;
            var seed = i * 6.1 + waterSeed;
            // Reeds — two bent strokes + a grass mound.
            if (i % 6 == 1)
            {
                DrawPolyline(
                    [p + new Vector2(-3, 3), p + new Vector2(-4, -6), p + new Vector2(0, 1), p + new Vector2(5, -4)],
                    reeds,
                    1.3f,
                    true
                );
                DrawCircle(p + outward * 2f, 3.5f, turf);
                DrawArc(p + outward * 2f, 3.5f, 0, Mathf.Tau, 10, turfInk, 0.9f);
            }
            // Pebbles at the water's edge.
            if (i % 5 == 2)
                for (var k = 0; k < 3; k++)
                {
                    var ps = seed + k * 7;
                    var pp =
                        p
                        - outward * 9f
                        + new Vector2(
                            (float)(Rng.Noise(ps) - 0.5) * 10,
                            (float)(Rng.Noise(ps + 1) - 0.5) * 10
                        );
                    DrawCircle(
                        pp,
                        (float)(1.3 + Rng.Noise(ps + 2) * 1.7),
                        Palette.Hex(Rng.Noise(ps + 3) > 0.5 ? "#8a8872" : "#6d6c5a")
                    );
                }
            // Lily pads on wide calm stretches.
            if (w >= 24 && i % 13 == 5)
            {
                var inward = outward * (float)(-w * 0.14);
                for (var pad = 0; pad < 3; pad++)
                {
                    var ps = i * 11 + pad * 5 + waterSeed;
                    var pp =
                        side[i]
                        + inward
                        + new Vector2(
                            (float)(Rng.Noise(ps) - 0.5) * 14,
                            (float)(Rng.Noise(ps + 1) - 0.5) * 10
                        );
                    Fill(
                        Poly.Ellipse(
                            pp.X,
                            pp.Y,
                            4 + Rng.Noise(ps + 2) * 3.5,
                            3 + Rng.Noise(ps + 3) * 2,
                            Rng.Noise(ps + 4) * 3
                        ),
                        Palette.Hex(pad % 2 == 1 ? "#4d7d41" : "#5c8f49")
                    );
                }
                if (Rng.Noise(i * 3.7 + waterSeed) > 0.72)
                {
                    var fp = side[i] + inward * 1.4f;
                    DrawCircle(fp, 2.2f, Palette.Hex("#e08bb0"));
                    DrawArc(fp, 2.2f, 0, Mathf.Tau, 10, Palette.Hex("#b35d82"), 0.9f);
                }
            }
        }
    }

    void DrawRoadGroup()
    {
        // Worn halo + verge + inked verge outline per stroke; the surface
        // fills once over the union so junctions read as crossroads.
        foreach (var (run, w) in _roads)
        {
            FillRibbon(Spline.StrokePolygon(run, w + 34), RoadHalo);
            FillRibbon(Spline.StrokePolygon(run, w + 13), RoadVerge);
            InkOutline(
                Spline.StrokePolygon(run, w + 13),
                Palette.Hex("#4a3a1c") with { A = 0.35f },
                1.2f
            );
        }
        foreach (var piece in UnionAll(_roads, 0))
            Fill(piece, RoadDirt);
        foreach (var (run, w) in _roads)
            DrawRoadDetails(run, w);
    }

    void DrawRoadDetails(Vector2[] run, double w)
    {
        // Wheel ruts — two faint grooves at ±0.26w (TS offsetRun).
        var rut = Palette.TintAlpha(RoadDirt, -34, 0.35);
        DrawPolyline(Spline.Ink(Spline.OffsetLine(run, w * 0.26)), rut, 2f, true);
        DrawPolyline(Spline.Ink(Spline.OffsetLine(run, -w * 0.26)), rut, 2f, true);

        // Grit speckle on the surface.
        foreach (var (p, angle, index) in Poly.SampleLine(run, 12, closed: false))
        {
            var seed = index * 7.3 + run[0].X * 0.11;
            var offset = (Rng.Noise(seed) - 0.5) * w * 0.85;
            var dot = new Vector2(
                p.X - (float)(Math.Sin(angle) * offset),
                p.Y + (float)(Math.Cos(angle) * offset)
            );
            DrawCircle(
                dot,
                (float)(1 + Rng.Noise(seed + 1) * 2),
                Palette.Hex("#8e8257") with { A = 0.3f }
            );
        }
    }
}
