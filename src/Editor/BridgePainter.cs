// Bridge painter — port of the web `bridges()` detector plus the three
// draw models from scene-renderer.ts. Every road×river segment crossing
// (not near-parallel, deduplicated) stamps a bridge oriented along the
// road; the road's Model picks the style: 0 wooden planks, 1 stone arch,
// 2 rope-and-plank.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class BridgePainter : Node2D
{
    public Color Stone = Palette.Hex("#a4a68e");

    struct Bridge
    {
        public Vector2 Point;
        public double Angle;
        public double Length;
        public double Width;
        public int Model;
    }

    readonly List<Bridge> _bridges = [];

    /// <summary>Rebuild the crossing list from scene strokes — call on
    /// every Rebuild (roads/rivers change).</summary>
    public void SetStrokes(IReadOnlyList<Stroke> strokes)
    {
        _bridges.Clear();
        var roads = new List<Stroke>();
        var rivers = new List<Stroke>();
        foreach (var s in strokes)
        {
            if (s.Kind == Kinds.Road)
                roads.Add(s);
            else if (s.Kind == Kinds.River)
                rivers.Add(s);
        }
        foreach (var road in roads)
        {
            var rp = Spline.Resample(road.Points, 14).ToList();
            foreach (var river in rivers)
            {
                var rv = Spline.Resample(river.Points, 14).ToList();
                for (var i = 1; i < rp.Count; i++)
                for (var j = 1; j < rv.Count; j++)
                {
                    var point = SegIntersect(rp[i - 1], rp[i], rv[j - 1], rv[j]);
                    if (point == null)
                        continue;
                    var pt = point.Value;
                    // Skip hits right at either line's endpoints and hits
                    // within a river's width of an existing bridge.
                    if (
                        rp[0].DistanceTo(pt) < 0.001
                        || rp[^1].DistanceTo(pt) < 0.001
                        || rv[0].DistanceTo(pt) < 0.001
                        || rv[^1].DistanceTo(pt) < 0.001
                    )
                        continue;
                    var near = false;
                    foreach (var b in _bridges)
                        if (b.Point.DistanceTo(pt) < river.Width)
                        {
                            near = true;
                            break;
                        }
                    if (near)
                        continue;
                    var angle = Math.Atan2(rp[i].Y - rp[i - 1].Y, rp[i].X - rp[i - 1].X);
                    var crossing = Math.Abs(
                        Math.Sin(
                            angle
                                - Math.Atan2(rv[j].Y - rv[j - 1].Y, rv[j].X - rv[j - 1].X)
                        )
                    );
                    if (crossing < 0.25)
                        continue;
                    _bridges.Add(
                        new Bridge
                        {
                            Point = pt,
                            Angle = angle,
                            Length = (river.Width + 24) / crossing,
                            Width = road.Width + 12,
                            Model = ((road.Model % 3) + 3) % 3,
                        }
                    );
                }
            }
        }
        QueueRedraw();
    }

    static Vector2? SegIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var rx = b.X - a.X;
        var ry = b.Y - a.Y;
        var sx = d.X - c.X;
        var sy = d.Y - c.Y;
        var cross = rx * sy - ry * sx;
        if (Math.Abs(cross) < 1e-9)
            return null;
        var t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / cross;
        var u = ((c.X - a.X) * ry - (c.Y - a.Y) * rx) / cross;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1
            ? new Vector2((float)(a.X + t * rx), (float)(a.Y + t * ry))
            : null;
    }

    public override void _Draw()
    {
        foreach (var b in _bridges)
        {
            DrawSetTransform(b.Point, (float)b.Angle);
            switch (b.Model)
            {
                case 1:
                    DrawStone(b);
                    break;
                case 2:
                    DrawRope(b);
                    break;
                default:
                    DrawWood(b);
                    break;
            }
            DrawSetTransform(Vector2.Zero);
        }
    }

    void Line(Vector2 a, Vector2 b, Color c, float w) => DrawLine(a, b, c, w, true);

    static Vector2[] Rect(double x, double y, double w, double h) =>
        [
            new((float)x, (float)y),
            new((float)(x + w), (float)y),
            new((float)(x + w), (float)(y + h)),
            new((float)x, (float)(y + h)),
        ];

    /// <summary>Model 0 — timber deck: dark under-planks, alternating
    /// plank runs with seam ticks, side rails, end posts.</summary>
    void DrawWood(Bridge b)
    {
        var L = b.Length;
        var W = b.Width;
        // Shadow under the deck (drops south).
        DrawColoredPolygon(
            Rect(-L / 2 - 3, -W / 2 + 8, L + 6, W),
            new Color(0.05f, 0.06f, 0.04f, 0.22f)
        );
        Fill(Rect(-L / 2 - 3, -W / 2, L + 6, W), Palette.Hex("#765139"));
        for (var x = -L / 2; x < L / 2; x += 7)
        {
            Fill(
                Rect(x, -W / 2 + 2, 6, W - 4),
                Palette.Hex((int)x % 3 != 0 ? "#b49a68" : "#c2aa7c")
            );
            Line(
                new((float)x, (float)(-W / 2 + 3)),
                new((float)(x + 1), (float)(W / 2 - 3)),
                Palette.Hex("#684a33"),
                1
            );
        }
        foreach (var side in new[] { -1.0, 1.0 })
        {
            Line(
                new((float)(-L / 2 - 5), (float)(side * W / 2)),
                new((float)(L / 2 + 5), (float)(side * W / 2)),
                Palette.Hex("#5f4a31"),
                4
            );
            foreach (var end in new[] { -1.0, 1.0 })
            {
                var p = new Vector2((float)(end * L / 2), (float)(side * W / 2));
                DrawCircle(p, 3, Palette.Hex("#dbc394"));
                DrawArc(p, 3, 0, Mathf.Tau, 12, Palette.Hex("#51412c"), 1f);
            }
        }
    }

    /// <summary>Model 1 — stone arch: masonry deck, raised parapets,
    /// dark arch mouth under the south face.</summary>
    void DrawStone(Bridge b)
    {
        var L = b.Length;
        var W = b.Width;
        var stone = Stone;
        DrawColoredPolygon(
            Rect(-L / 2 - 5, -W / 2 + 4, L + 10, W + 16),
            new Color(0.05f, 0.06f, 0.04f, 0.22f)
        );
        Fill(Rect(-L / 2 - 5, -W / 2 - 8, L + 10, W + 16), Palette.Hex("#575a4b"));
        Fill(Rect(-L / 2 - 3, -W / 2 - 6, L + 6, W + 12), stone);
        Fill(Rect(-L / 2 - 3, -W / 2 + 1, L + 6, W - 2), Palette.Tint(stone, 14));
        for (var x = -L / 2; x < L / 2; x += 11)
            Line(
                new((float)x, (float)(-W / 2 + 2)),
                new((float)(x + 2), (float)(W / 2 - 2)),
                Palette.Tint(stone, -20),
                1
            );
        // Arch mouth — two nested half-ellipses under the south edge.
        var archR = Math.Min(L * 0.28, 30);
        DrawColoredPolygon(
            Poly.Ellipse(0, W / 2 + 6, archR, 9, 0, 16)[..8],
            Palette.Hex("#2e3b34")
        );
        DrawColoredPolygon(
            Poly.Ellipse(0, W / 2 + 6, archR * 0.68, 5.5, 0, 16)[..8],
            Palette.Hex("#15211c")
        );
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var a = new Vector2((float)(-L / 2 - 3), (float)(side * (W / 2 - 3)));
            var c = new Vector2((float)(L / 2 + 3), (float)(side * (W / 2 - 3)));
            Line(a, c, Palette.Hex("#4c534c"), 6);
            Line(a, c, Palette.Tint(stone, 10), 3.5f);
            foreach (var end in new[] { -1.0, 1.0 })
            {
                var p = new Vector2((float)(end * (L / 2 + 3)), (float)(side * (W / 2 - 3)));
                DrawCircle(p, 3.4f, stone);
                DrawArc(p, 3.4f, 0, Mathf.Tau, 12, Palette.Hex("#4c534c"), 1f);
            }
        }
    }

    /// <summary>Model 2 — rope bridge: sparse planks hung between two
    /// sagging ropes, hanger ties, anchor posts.</summary>
    void DrawRope(Bridge b)
    {
        var L = b.Length;
        var W = b.Width;
        DrawColoredPolygon(
            Rect(-L / 2, -W / 2 + 9, L, W - 6),
            new Color(0.05f, 0.06f, 0.04f, 0.2f)
        );
        for (var x = -L / 2; x < L / 2; x += 9)
        {
            Fill(
                Rect(x, -W / 2 + 3, 6.5, W - 6),
                Palette.Hex((int)x % 3 != 0 ? "#8a6f4c" : "#97795a")
            );
            Line(
                new((float)(x + 6.5), (float)(-W / 2 + 3)),
                new((float)(x + 6.5), (float)(W / 2 - 3)),
                Palette.Hex("#4a3a28"),
                1f
            );
        }
        foreach (var side in new[] { -1.0, 1.0 })
        {
            // Sagging rope — a shallow quadratic bow below the deck edge.
            var y = (float)(side * (W / 2 + 2));
            var sag = (float)(side * (W / 2 + 8));
            var pts = new List<Vector2>();
            for (var i = 0; i <= 12; i++)
            {
                var t = i / 12.0;
                var x = -L / 2 - 6 + (L + 12) * t;
                var qy = (1 - t) * (1 - t) * y + 2 * (1 - t) * t * sag + t * t * y;
                pts.Add(new((float)x, (float)qy));
            }
            DrawPolyline([.. pts], Palette.Hex("#6d5638"), 1.8f, true);
            for (var x = -L / 2; x < L / 2; x += 16)
                Line(
                    new((float)x, (float)(side * (W / 2 - 2))),
                    new((float)(x + 2), (float)(side * (W / 2 + 3))),
                    Palette.Hex("#6d5638"),
                    1
                );
            foreach (var end in new[] { -1.0, 1.0 })
            {
                var p = new Vector2((float)(end * (L / 2 + 5)), (float)(side * (W / 2 + 2)));
                DrawCircle(p, 3, Palette.Hex("#8a6f4c"));
                DrawArc(p, 3, 0, Mathf.Tau, 12, Palette.Hex("#4a3a28"), 1f);
            }
        }
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }
}
