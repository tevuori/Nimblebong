// Area fills — pond and field shapes painted into the albedo
// viewport. Ports of the web `pondArea`/`fieldArea`: a darker bank
// ring hugging the outline, wobbled water surface with speckles and
// a foam edge, and furrowed field rows with stalk ticks.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class AreaPainter : Node2D
{
    public Color Water = Palette.Hex("#3d93a3");
    public Color Ground = Palette.Hex("#8aad54");
    public Color Field = Palette.Hex("#b9a54f");

    readonly List<(string Kind, Vector2[] Poly)> _areas = [];

    public void Add(string kind, IReadOnlyList<Vector2> points)
    {
        if (points.Count >= 3)
            _areas.Add((kind, points is Vector2[] a ? a : [.. points]));
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    public override void _Draw()
    {
        foreach (var (kind, poly) in _areas)
            if (kind == Kinds.Pond)
                DrawPond(poly);
            else if (kind == Kinds.Field)
                DrawField(poly);
    }

    void DrawPond(Vector2[] pts)
    {
        var cx = 0f;
        var cy = 0f;
        foreach (var p in pts)
        {
            cx += p.X;
            cy += p.Y;
        }
        cx /= pts.Length;
        cy /= pts.Length;
        var ctr = new Vector2(cx, cy);
        var seed = cx * 0.13 + cy * 0.31;

        // Bank ring — slightly darker than the ground.
        Fill(pts, Palette.Tint(Ground, -34));
        // Pebbles hugging the rim.
        for (var i = 0; i < 9; i++)
        {
            var p = pts[(int)(Rng.Noise(i * 4.7 + 1) * pts.Length)];
            var dir = (p - ctr).Normalized();
            var at = p - dir * 4;
            DrawCircle(at, (float)(2 + Rng.Noise(i + 2) * 2.5), Palette.Tint(Ground, -48));
        }
        // Water surface — the outline pulled a whisper toward the
        // centroid so the bank ring shows all around.
        var water = new Vector2[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            var wobble = 0.93 + Rng.Noise(i * 3.1 + pts.Length) * 0.05;
            water[i] = ctr + (pts[i] - ctr) * (float)wobble;
        }
        Fill(water, Water);
        // Shallow rim inside the water + darkened far edge.
        var shallow = new Vector2[pts.Length];
        for (var i = 0; i < pts.Length; i++)
            shallow[i] = ctr + (water[i] - ctr) * 0.88f;
        Fill(shallow, Palette.Mix(Water, Palette.Hex("#0b3a44"), 0.25));
        // Speckles + foam edge.
        for (var i = 0; i < 26; i++)
        {
            var p = water[(int)(Rng.Noise(i * 5.3 + 2) * water.Length)];
            DrawCircle(
                p,
                (float)(1.6 + Rng.Noise(i) * 2),
                new Color(1f, 1f, 1f, 0.19f)
            );
        }
        // Broken foam arcs along the shore.
        for (var i = 0; i < water.Length; i += 3)
        {
            var a = water[i];
            var b = water[(i + 2) % water.Length];
            DrawPolyline(
                [a, (a + b) / 2, b],
                new Color(0.9f, 0.97f, 0.95f, 0.4f),
                1.4f
            );
        }
    }

    void DrawField(Vector2[] pts)
    {
        float x0 = float.MaxValue,
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
        // Dark border furrow + fill.
        Fill(pts, Palette.Tint(Field, -30));
        foreach (var inner in TerrainGeometry.Offset(pts, -5))
            Fill(inner, Field);
        // Furrow rows — scan x and emit only the inside-poly runs
        // (Node2D has no clip; cheap run-length clipping per row).
        for (var y = y0 + 7; y < y1; y += 14)
        {
            float? run = null;
            for (var x = x0; x <= x1; x += 4)
            {
                var inside = Poly.PointInPolygon(new Vector2(x, y), pts);
                if (inside && run == null)
                    run = x;
                else if (!inside && run is { } a)
                {
                    DrawLine(new Vector2(a, y), new Vector2(x, y), Palette.Tint(Field, -24), 3);
                    run = null;
                }
            }
            if (run is { } r)
                DrawLine(new Vector2(r, y), new Vector2(x1, y), Palette.Tint(Field, -24), 3);
        }
        // Stalk ticks on a jittered grid — inside-only.
        for (var y = y0 + 7; y < y1; y += 14)
        for (var x = x0 + 7; x < x1; x += 26)
        {
            var s = x * 0.13 + y * 0.31;
            if (Rng.Noise(s) <= 0.35)
                continue;
            var at = new Vector2((float)(x + Rng.Noise(s) * 8), y + 2);
            if (!Poly.PointInPolygon(at, pts))
                continue;
            DrawLine(at, at + new Vector2(2, -6), Palette.Tint(Field, 30), 2);
        }
    }
}
