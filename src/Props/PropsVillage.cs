// Prop painters — village & structures batch. Owns DrawTower,
// DrawStall, DrawCart, DrawFlag, DrawBarricade, DrawLantern,
// DrawFountain, DrawPillar, DrawMarker.
// Ported from DMmap/src/dm-area/scene/render/scene-props.ts and
// adapted to the oblique house style: raised top face + wall drop
// screen-south for anything with height (tower, fountain, pillar,
// the stall counter/canopy). Stone pieces use the Stone tint with
// darker mortar joints and the shared #4a4f46 ink edge.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    // --- oblique helpers -------------------------------------------------

    /// <summary>Axis-aligned local-space rectangle as a polygon.</summary>
    static Vector2[] RectPts(double x0, double y0, double x1, double y1) =>
        [
            new((float)x0, (float)y0),
            new((float)x1, (float)y0),
            new((float)x1, (float)y1),
            new((float)x0, (float)y1),
        ];

    /// <summary>Wall quads dropping screen-south off every south-facing
    /// edge of the (world-space) top polygon — the house oblique
    /// extrusion. `flare` pushes the ground edge away from the polygon
    /// centroid, widening the silhouette toward the base (obelisks,
    /// column shafts). Returns the dropped edges for follow-up detail
    /// (doors, mortar, runes).</summary>
    List<(Vector2 A, Vector2 B)> WallDrop(
        Vector2[] top,
        double drop,
        Color wall,
        Color edge,
        double edgeW,
        double flare = 0
    )
    {
        var ctr = Poly.Centroid(top);
        var d = (float)drop;
        var faces = new List<(Vector2 A, Vector2 B)>();
        for (var i = 0; i < top.Length; i++)
        {
            var a = top[i];
            var b = top[(i + 1) % top.Length];
            if (((a + b) / 2).Y <= ctr.Y + 0.5f)
                continue;
            var a2 = a + new Vector2((a.X - ctr.X) * (float)flare, d);
            var b2 = b + new Vector2((b.X - ctr.X) * (float)flare, d);
            var quad = new[] { a, b, b2, a2 };
            Fill(quad, wall);
            Stroke(quad, edge, (float)edgeW);
            faces.Add((a, b));
        }
        return faces;
    }

    /// <summary>Southernmost edge of a world-space top polygon — where
    /// doors and face details land (same midpoint test as WallDrop).</summary>
    static (Vector2 A, Vector2 B)? SouthFace(Vector2[] top)
    {
        var ctr = Poly.Centroid(top);
        (Vector2 A, Vector2 B)? best = null;
        var bestY = float.MinValue;
        for (var i = 0; i < top.Length; i++)
        {
            var a = top[i];
            var b = top[(i + 1) % top.Length];
            var my = (a.Y + b.Y) * 0.5f;
            if (my <= ctr.Y + 0.5f || my <= bestY)
                continue;
            bestY = my;
            best = (a, b);
        }
        return best;
    }

    /// <summary>Ink-edged disc in prop-local space — the TS drawDisc.</summary>
    void Disc(
        PropPlacement p,
        double x,
        double y,
        double rad,
        Color fill,
        Color? edge = null,
        double edgeW = 1.0
    )
    {
        var c = Xform(p, new Vector2((float)x, (float)y));
        var rr = (float)Math.Max(0.7, rad * p.Scale);
        DrawCircle(c, rr, fill);
        if (edge is { } e)
            DrawArc(c, rr, 0, Mathf.Tau, 18, e, (float)Math.Max(0.6, edgeW * p.Scale), true);
    }

    /// <summary>Stroke segment in prop-local space — the TS drawLine.
    /// `w` is the unscaled width (scaled by p.Scale inside).</summary>
    void Seg(PropPlacement p, double x0, double y0, double x1, double y1, Color color, double w)
    {
        DrawLine(
            Xform(p, new Vector2((float)x0, (float)y0)),
            Xform(p, new Vector2((float)x1, (float)y1)),
            color,
            (float)Math.Max(0.6, w * p.Scale)
        );
    }

    /// <summary>Stroke polyline in prop-local space.</summary>
    void Seg(PropPlacement p, Vector2[] pts, Color color, double w)
    {
        DrawPolyline(Xform(p, pts), color, (float)Math.Max(0.6, w * p.Scale), true);
    }

    // --- watchtower ---------------------------------------------------------
    // Small castle keep: stone body extruded south, battlement ring on
    // the top face, door in the southernmost wall. Models: round tower,
    // square keep, ruined stump (TS tower models 0/1/2).
    void DrawTower(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 13 * rand.Range(0.9, 1.15);
        var stone = Palette.Mix(
            Stone,
            rand.Next() < 0.5 ? Colors.White : Palette.Hex("#6a6c58"),
            rand.Next() * 0.18
        );
        var edge = Palette.Hex("#4a4f46");
        var mortar = Palette.Mix(stone, edge, 0.6) with { A = 0.5f };
        var ruined = model == 2;
        var wallH = r * (ruined ? rand.Range(0.7, 0.95) : rand.Range(1.5, 1.85));
        var drop = wallH * p.Scale;

        ContactShadow(p, r * 1.25, r * 1.05 + wallH * 0.45);

        var top =
            model == 1
                ? RectPts(-r * 0.82, -r * 0.82, r * 0.82, r * 0.82)
            : ruined ? Blob(0, 0, r, rand, 11, 0.16)
            : Poly.Ellipse(0, 0, r, r, 0, 14);
        var tw = Xform(p, top);

        // Stone body — south wall drop with mortar courses.
        var faces = WallDrop(
            tw,
            drop,
            Palette.Mix(stone, Colors.Black, 0.22),
            edge,
            Math.Max(0.8, 1.3 * p.Scale)
        );
        foreach (var (a, b) in faces)
            DrawLine(
                a + new Vector2(0, (float)(drop * 0.5)),
                b + new Vector2(0, (float)(drop * 0.5)),
                mortar,
                (float)Math.Max(0.6, 0.8 * p.Scale)
            );
        for (var i = 0; i < Math.Min(2, faces.Count); i++)
        {
            var m = (faces[i].A + faces[i].B) / 2;
            DrawLine(
                m + new Vector2(0, (float)(drop * 0.12)),
                m + new Vector2(0, (float)(drop * 0.45)),
                mortar,
                (float)Math.Max(0.6, 0.7 * p.Scale)
            );
            DrawLine(
                m + new Vector2(0, (float)(drop * 0.62)),
                m + new Vector2(0, (float)(drop * 0.88)),
                mortar,
                (float)Math.Max(0.6, 0.7 * p.Scale)
            );
        }
        // Door in the southernmost face, running down to the ground.
        if (!ruined && SouthFace(tw) is { } f)
        {
            var dir = (f.B - f.A).Normalized();
            var len = (f.B - f.A).Length();
            var doorW = Math.Min((float)(r * 0.55 * p.Scale), len * 0.7f);
            var at = (f.A + f.B) / 2 - dir * (doorW * 0.5f);
            var door = new[]
            {
                at + new Vector2(0, (float)(drop * 0.1)),
                at + dir * doorW + new Vector2(0, (float)(drop * 0.1)),
                at + dir * doorW + new Vector2(0, (float)drop),
                at + new Vector2(0, (float)drop),
            };
            Fill(door, Palette.Hex("#3d3428"));
            Stroke(door, edge, (float)Math.Max(0.6, 0.8 * p.Scale));
        }

        // Battlement — a little block straddling the rim, rising
        // screen-north like the wall drop falls south.
        void Merlon(Vector2 b1Local, Vector2 b2Local, double h)
        {
            var a = Xform(p, b1Local);
            var b = Xform(p, b2Local);
            var up = new Vector2(0, (float)(-h * p.Scale));
            var quad = new[] { a, b, b + up, a + up };
            Fill(quad, Palette.Mix(stone, Colors.Black, 0.18));
            Stroke(quad, edge, (float)Math.Max(0.6, 0.9 * p.Scale));
        }

        Fill(tw, stone);
        Stroke(tw, edge, (float)Math.Max(0.8, 1.4 * p.Scale));

        if (model == 1)
        {
            // Square keep — inset court shade + merlons on the north edge.
            var inset = RectPts(-r * 0.55, -r * 0.55, r * 0.55, r * 0.55);
            Fill(Xform(p, inset), new Color(0f, 0f, 0f, 0.09f));
            Stroke(
                Xform(p, inset),
                Palette.Mix(stone, edge, 0.5),
                (float)Math.Max(0.6, 0.8 * p.Scale)
            );
            for (var i = 0; i < 4; i++)
            {
                var x = -r * 0.72 + i * r * 0.46;
                Merlon(new Vector2((float)x, (float)(-r * 0.82)), new Vector2((float)(x + r * 0.3), (float)(-r * 0.82)), r * 0.3);
            }
        }
        else if (ruined)
        {
            // Broken crown — dark hollow, a few surviving merlons.
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.55, r * 0.55, 0, 10)),
                Palette.Mix(stone, Colors.Black, 0.45)
            );
            for (var k = 0; k < 3; k++)
            {
                var a = -Math.PI * rand.Range(0.3, 0.75);
                var d = rand.Range(0.05, 0.11);
                var b1 = new Vector2((float)(Math.Cos(a - d) * r * 0.95), (float)(Math.Sin(a - d) * r * 0.95));
                var b2 = new Vector2((float)(Math.Cos(a + d) * r * 0.95), (float)(Math.Sin(a + d) * r * 0.95));
                Merlon(b1, b2, r * rand.Range(0.2, 0.32));
            }
            // Rubble tumbled at the foot of the south wall.
            if (faces.Count > 0)
            {
                var (fa, fb) = faces[faces.Count / 2];
                var foot = (fa + fb) / 2 + new Vector2(0, (float)drop);
                for (var k = 0; k < 3; k++)
                {
                    var off = new Vector2(
                        (float)rand.Range(-r * 0.8, r * 0.8) * p.Scale,
                        (float)rand.Range(1, 4) * p.Scale
                    );
                    DrawCircle(
                        foot + off,
                        (float)(rand.Range(1.4, 2.4) * p.Scale),
                        Palette.Mix(stone, Colors.Black, 0.2)
                    );
                }
            }
        }
        else
        {
            // Round tower — courtyard shade + crenellation gaps + merlons.
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.62, r * 0.62, 0, 12)),
                new Color(0f, 0f, 0f, 0.09f)
            );
            for (var i = 0; i < 8; i++)
            {
                var a = i / 8.0 * Math.PI * 2;
                Seg(
                    p,
                    Math.Cos(a) * r * 0.62,
                    Math.Sin(a) * r * 0.62,
                    Math.Cos(a) * r * 0.95,
                    Math.Sin(a) * r * 0.95,
                    Palette.Mix(stone, edge, 0.55),
                    1.6
                );
            }
            for (var k = 0; k < 5; k++)
            {
                var a = -Math.PI + 0.4 + k * (Math.PI - 0.8) / 4;
                var d = 0.11;
                var b1 = new Vector2((float)(Math.Cos(a - d) * r), (float)(Math.Sin(a - d) * r));
                var b2 = new Vector2((float)(Math.Cos(a + d) * r), (float)(Math.Sin(a + d) * r));
                Merlon(b1, b2, r * 0.32);
            }
        }
    }

    // --- market stall ---------------------------------------------------------
    // Striped awning over a goods counter. TS stall models 0/1/2:
    // produce, crates, smith's pot — same seeded picks here.
    void DrawStall(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 20 * rand.Range(0.9, 1.1);
        var canopy = Palette.Mix(
            Roof,
            rand.Next() < 0.5 ? Palette.Hex("#8a4f38") : Palette.Hex("#c08a5a"),
            rand.Next() * 0.35
        );
        var cream = Palette.Hex("#e8dcc0");
        var woodL = Palette.Mix(Wood, Colors.White, 0.18);
        var woodD = Palette.Mix(Wood, Colors.Black, 0.3);
        var edge = Palette.Hex("#4d3f2c");
        var canopyEdge = Palette.Hex("#59391f");

        ContactShadow(p, r * 1.05, r * 0.95);

        // Counter box — top face + front drop.
        var counter = RectPts(-r * 0.8, r * 0.1, r * 0.8, r * 0.5);
        var cw = Xform(p, counter);
        WallDrop(cw, r * 0.3 * p.Scale, woodD, edge, Math.Max(0.6, 1.0 * p.Scale));
        Fill(cw, woodL);
        Stroke(cw, edge, (float)Math.Max(0.6, 1.1 * p.Scale));
        for (var i = -1; i <= 1; i++)
            Seg(p, i * r * 0.45, r * 0.14, i * r * 0.45, r * 0.46, Palette.Mix(woodL, edge, 0.4), 0.8);

        // Goods on the counter.
        if (model == 0)
        {
            // Produce — three mounds in market colors (TS stall model 0).
            var shades = new[]
            {
                Palette.Hex("#c94f3d"),
                Palette.Hex("#e0a03a"),
                Palette.Hex("#7a9a45"),
            };
            for (var i = 0; i < 3; i++)
                Disc(p, (i - 1) * r * 0.45, r * 0.3, r * 0.16, shades[i], Palette.Mix(shades[i], Colors.Black, 0.45), 0.9);
        }
        else if (model == 1)
        {
            // Stacked crates (TS stall model 1).
            for (var i = 0; i < 3; i++)
            {
                var cx = -r * 0.6 + i * r * 0.42;
                var crate = Xform(p, RectPts(cx, r * 0.14, cx + r * 0.3, r * 0.44));
                Fill(crate, i % 2 == 0 ? Palette.Hex("#6f5335") : Palette.Hex("#8a6a44"));
                Stroke(crate, edge, (float)Math.Max(0.6, 0.9 * p.Scale));
                Seg(p, cx, r * 0.14, cx + r * 0.3, r * 0.44, Palette.Mix(Wood, Colors.Black, 0.5), 0.7);
            }
        }
        else
        {
            // Smith's stall — pot + a wisp of smoke (TS stall model 2).
            Disc(p, 0, r * 0.3, r * 0.24, Palette.Hex("#4a4a4a"), Palette.Hex("#2e2e2e"), 1.2);
            Seg(
                p,
                [
                    new Vector2(0, (float)(r * 0.1)),
                    new Vector2((float)(r * 0.12), (float)(-r * 0.35)),
                    new Vector2((float)(-r * 0.06), (float)(-r * 0.55)),
                ],
                new Color(0.72f, 0.77f, 0.74f, 0.55f),
                2
            );
        }

        // Striped awning — trapezoid top face, cream stripes, scalloped
        // valance hanging off the front edge, posts down to the counter.
        var canopyTop = new[]
        {
            new Vector2((float)-r, (float)(-r * 0.78)),
            new Vector2((float)r, (float)(-r * 0.78)),
            new Vector2((float)(r * 0.85), (float)(-r * 0.08)),
            new Vector2((float)(-r * 0.85), (float)(-r * 0.08)),
        };
        var kw = Xform(p, canopyTop);
        Fill(kw, canopy);
        for (var i = -2; i <= 2; i++)
            if (i % 2 != 0)
            {
                var stripe = new[]
                {
                    new Vector2((float)(i * r * 0.38), (float)(-r * 0.76)),
                    new Vector2((float)(i * r * 0.38 + r * 0.2), (float)(-r * 0.76)),
                    new Vector2((float)(i * r * 0.36 + r * 0.18), (float)(-r * 0.1)),
                    new Vector2((float)(i * r * 0.36 - r * 0.02), (float)(-r * 0.1)),
                };
                Fill(Xform(p, stripe), cream);
            }
        Stroke(kw, canopyEdge, (float)Math.Max(0.7, 1.2 * p.Scale));
        WallDrop(kw, 3.5 * p.Scale, Palette.Mix(canopy, Colors.Black, 0.3), canopyEdge, Math.Max(0.5, 0.7 * p.Scale));
        foreach (var sx in new[] { -r * 0.82, r * 0.82 })
            Seg(p, sx, -r * 0.1, sx * 0.95, r * 0.16, edge, 1.6);
    }

    // --- cart -----------------------------------------------------------------
    // Flatbed wagon: plank bed, spoked south wheel, shafts east.
    // Models: open bed, canvas-covered, hay load (TS cart 0/1/2).
    void DrawCart(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 15 * rand.Range(0.9, 1.15);
        // Bed in the lighter profile-wood register — raw Wood reads as
        // a dark blob at this size.
        var wood = Palette.Mix(
            Palette.Mix(Wood, Colors.White, 0.3),
            rand.Next() < 0.5 ? Colors.White : Colors.Black,
            rand.Next() * 0.15
        );
        var edge = Palette.Mix(Wood, Colors.Black, 0.65);
        var seam = Palette.Mix(wood, Colors.Black, 0.35);

        ContactShadow(p, r * 1.0, r * 0.9);

        // Far wheel peeking over the bed's north edge (drawn under).
        Disc(p, 0, -r * 0.62, r * 0.3, Palette.Mix(wood, Colors.Black, 0.5), edge, 0.9);
        // Shafts east, drawn under the bed.
        foreach (var side in new[] { -1, 1 })
            Seg(p, r * 0.55, side * r * 0.34, r * 1.3, side * r * 0.3, wood, 3);

        var bed = RectPts(-r * 0.75, -r * 0.55, r * 0.75, r * 0.55);
        var bw = Xform(p, bed);
        WallDrop(bw, r * 0.18 * p.Scale, Palette.Mix(wood, Colors.Black, 0.35), edge, Math.Max(0.6, 1.0 * p.Scale));
        Fill(bw, wood);
        Stroke(bw, edge, (float)Math.Max(0.7, 1.2 * p.Scale));

        if (model == 1)
        {
            // Covered wagon — canvas bulge + hoop ribs (TS cart model 1).
            var canvas = Xform(p, Poly.Ellipse(0, 0, r * 0.68, r * 0.46, 0, 14));
            Fill(canvas, Palette.Hex("#e8dcc0"));
            Stroke(canvas, Palette.Hex("#8a7a5a"), (float)Math.Max(0.6, 1.0 * p.Scale));
            foreach (var px in new[] { -r * 0.34, 0.0, r * 0.34 })
                Seg(p, px, -r * 0.4, px, r * 0.4, Palette.Hex("#8a7a5a"), 1.4);
        }
        else
        {
            // Plank seams (open bed / hay cart).
            for (var i = -2; i <= 2; i++)
                Seg(p, i * r * 0.25, -r * 0.5, i * r * 0.25, r * 0.5, seam, 1);
            if (model == 2)
                // Hay load — mounded straw overhanging the bed (TS cart model 2).
                for (var i = 0; i < 4; i++)
                    Disc(
                        p,
                        -r * 0.45 + i * r * 0.3,
                        (i % 2 == 1 ? -1 : 1) * r * 0.12,
                        r * 0.34,
                        Palette.Hex("#e3c58c"),
                        Palette.Hex("#a8873e"),
                        0.9
                    );
            else
                // A couple of sacks on the open bed.
                foreach (var (sx, sy) in new[] { (-r * 0.3, -r * 0.1), (r * 0.25, r * 0.15) })
                    Fill(
                        Xform(p, Blob(sx, sy, r * 0.2, rand, 8, 0.25)),
                        Palette.Hex("#b8a888")
                    );
        }

        // Near wheel — dark rim, wooden hub, four spokes.
        var wc = Xform(p, new Vector2(0, (float)(r * 0.72)));
        var wr = (float)(r * 0.32 * p.Scale);
        DrawCircle(wc, wr, Palette.Hex("#3a352a"));
        DrawArc(wc, wr, 0, Mathf.Tau, 18, edge, (float)Math.Max(0.7, 1.2 * p.Scale), true);
        DrawCircle(wc, wr * 0.62f, Palette.Mix(wood, Colors.Black, 0.2));
        for (var i = 0; i < 4; i++)
        {
            var a = i / 4.0 * Math.PI * 2 + 0.4;
            DrawLine(
                wc,
                wc + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (wr * 0.85f),
                Palette.Mix(wood, Colors.Black, 0.55),
                (float)Math.Max(0.6, 0.8 * p.Scale)
            );
        }
        DrawCircle(wc, wr * 0.2f, edge);
    }

    // --- flag ------------------------------------------------------------------
    // Pole + banner. Models: pennant, swallowtail, square banner
    // (TS flag 0/1/2). Faction red with a seeded hue nudge.
    void DrawFlag(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 16 * rand.Range(0.9, 1.1);
        var flag = Palette.Mix(
            Palette.Hex("#b3453a"),
            rand.Next() < 0.5 ? Palette.Hex("#8a4f38") : Palette.Hex("#d0a04a"),
            rand.Next() * 0.25
        );
        var edge = Palette.Hex("#6d2f27");
        var pole = Palette.Hex("#5e5038");

        ContactShadow(p, r * 0.5, r * 0.55);

        // Stone mound the pole is driven into.
        var mound = Xform(p, Poly.Ellipse(0, r * 0.8, r * 0.3, r * 0.18, 0, 8));
        Fill(mound, Palette.Mix(Stone, Colors.Black, 0.25));
        Stroke(mound, Palette.Mix(Stone, Colors.Black, 0.55), (float)Math.Max(0.5, 0.7 * p.Scale));
        Seg(p, 0, r * 0.85, 0, -r * 0.8, pole, 2.5);

        Vector2[] banner =
            model == 1
                ? // swallowtail
                [
                    new(0, (float)(-r * 0.8)),
                    new((float)(r * 0.7), (float)(-r * 0.8)),
                    new((float)(r * 0.55), (float)(-r * 0.55)),
                    new((float)(r * 0.7), (float)(-r * 0.3)),
                    new(0, (float)(-r * 0.3)),
                ]
            : model == 2
                ? // square banner
                [
                    new(0, (float)(-r * 0.8)),
                    new((float)(r * 0.6), (float)(-r * 0.8)),
                    new((float)(r * 0.6), (float)(-r * 0.2)),
                    new(0, (float)(-r * 0.2)),
                ]
                : // pennant
                [
                    new(0, (float)(-r * 0.8)),
                    new((float)(r * 0.75), (float)(-r * 0.62)),
                    new(0, (float)(-r * 0.42)),
                ];
        var bw = Xform(p, banner);
        Fill(bw, flag);
        Stroke(bw, edge, (float)Math.Max(0.6, 1.1 * p.Scale));
        // Fly-side shade — a darker sliver along the free edge.
        if (model != 0)
            Seg(p, r * 0.55, -r * 0.78, r * 0.55, -r * 0.32, Palette.Mix(flag, Colors.Black, 0.3), 1.0);

        Disc(p, 0, -r * 0.82, 2.2, Palette.Hex("#d8b25e"));
    }

    // --- barricade ---------------------------------------------------------------
    // Field fortifications. Models: chevaux-de-frise, log breastwork,
    // crates & barrels (TS barricade 0/1/2).
    void DrawBarricade(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 15 * rand.Range(0.9, 1.15);
        var wood = Palette.Mix(
            Palette.Mix(Wood, Colors.White, 0.25),
            rand.Next() < 0.5 ? Colors.White : Colors.Black,
            rand.Next() * 0.15
        );
        var edge = Palette.Hex("#4d3f2c");
        var stake = Palette.Hex("#5e4f3a");

        ContactShadow(p, r, r * 0.8);

        if (model == 1)
        {
            // Log breastwork — three stacked logs, stake ends, spikes (TS model 1).
            for (var i = -1; i <= 1; i++)
                Seg(
                    p,
                    -r * 0.92,
                    i * r * 0.33,
                    r * 0.92,
                    i * r * 0.33,
                    i % 2 == 0 ? Palette.Mix(wood, Colors.Black, 0.15) : wood,
                    r * 0.26
                );
            for (var i = -2; i <= 2; i++)
            {
                Seg(p, i * r * 0.38, -r * 0.42, i * r * 0.38, r * 0.42, stake, 2.4);
                Seg(p, i * r * 0.38, -r * 0.42, i * r * 0.38 + 3, -r * 0.62, stake, 2);
            }
        }
        else if (model == 2)
        {
            // Improvised pile — crossed crates + a barrel (TS model 2).
            foreach (var (cx, cy, s) in new[] { (-0.5, 0.15, 0.5), (0.15, 0.3, 0.42), (-0.1, -0.42, 0.38) })
            {
                var box = Xform(p, RectPts(cx * r - r * s, cy * r - r * s, cx * r + r * s, cy * r + r * s));
                Fill(box, wood);
                Stroke(box, edge, (float)Math.Max(0.6, 1.0 * p.Scale));
                Seg(p, cx * r - r * s, cy * r - r * s, cx * r + r * s, cy * r + r * s, stake, 1.4);
            }
            Disc(p, r * 0.62, -r * 0.3, r * 0.34, wood, edge, 1.2);
            Disc(p, r * 0.62, -r * 0.3, r * 0.2, new Color(0f, 0f, 0f, 0.09f));
        }
        else
        {
            // Chevaux-de-frise — a rail with crossed stakes (TS model 0).
            Seg(p, -r * 0.95, 0, r * 0.95, 0, Palette.Mix(wood, Colors.Black, 0.3), r * 0.14);
            for (var i = -2; i <= 2; i++)
            {
                var x = i * r * 0.42;
                foreach (var tilt in new[] { -0.5, 0.5 })
                    Seg(
                        p,
                        x - tilt * r * 0.5,
                        r * 0.55,
                        x + tilt * r * 0.5,
                        -r * 0.55,
                        i % 2 == 0 ? wood : Palette.Mix(wood, Colors.Black, 0.3),
                        3.4
                    );
            }
            Disc(p, 0, 0, r * 0.1, Palette.Hex("#4a4532"));
        }
    }

    // --- lantern -----------------------------------------------------------------
    // Lights. Models: hooked pole lamp, tripod brazier, torch
    // (TS lantern 0/1/2). Warm flame + a soft glow blob.
    void DrawLantern(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 12 * rand.Range(0.9, 1.15);
        var body = Palette.Hex("#6d5f45");
        var iron = Palette.Hex("#4a4234");
        var flame = Palette.Hex("#ffd27a");

        ContactShadow(p, r * 0.7, r * 0.65);

        if (model == 1)
        {
            // Tripod brazier (TS lantern model 1).
            for (var i = 0; i < 3; i++)
            {
                var a = i / 3.0 * Math.PI * 2 + 0.5;
                Seg(
                    p,
                    Math.Cos(a) * r * 0.5,
                    Math.Sin(a) * r * 0.5,
                    Math.Cos(a) * r * 0.85,
                    Math.Sin(a) * r * 0.85,
                    iron,
                    2.4
                );
            }
            Disc(p, 0, 0, r * 0.72, body, Palette.Hex("#3f3a30"), 1.2);
            Disc(p, 0, 0, r * 0.5, Palette.Hex("#3a342c"));
            Disc(p, 0, -r * 0.06, r * 0.34, flame);
            var wisp = new[]
            {
                new Vector2((float)(-r * 0.2), (float)(r * 0.15)),
                new Vector2(0, (float)(-r * 0.42)),
                new Vector2((float)(r * 0.16), (float)(r * 0.1)),
            };
            Fill(Xform(p, wisp), Palette.Hex("#fff0c0"));
            Stroke(Xform(p, wisp), Palette.Hex("#ffcf7a"), (float)Math.Max(0.5, 0.8 * p.Scale));
        }
        else if (model == 2)
        {
            // Torch on a post (TS lantern model 2).
            Seg(p, 0, r * 0.75, 0, -r * 0.1, Palette.Hex("#5a4a34"), 4);
            var fl = new[]
            {
                new Vector2((float)(-r * 0.22), (float)(-r * 0.05)),
                new Vector2(0, (float)(-r * 0.75)),
                new Vector2((float)(r * 0.22), (float)(-r * 0.05)),
            };
            Fill(Xform(p, Blob(0, -r * 0.38, r * 0.55, rand, 10, 0.2)), Palette.TintAlpha(flame, 30, 0.15));
            Fill(Xform(p, fl), flame);
            Stroke(Xform(p, fl), Palette.Hex("#b06a2e"), (float)Math.Max(0.6, 1.0 * p.Scale));
            Disc(p, 0, -r * 0.12, r * 0.14, Palette.Hex("#fff0c0"));
            Disc(p, 0, r * 0.75, 3, iron);
        }
        else
        {
            // Hooked pole lamp (TS lantern model 0).
            Seg(p, 0, r * 0.7, 0, -r * 0.05, iron, 3.4);
            Seg(p, 0, -r * 0.05, r * 0.3, -r * 0.35, iron, 2.6);
            Fill(Xform(p, Blob(r * 0.3, -r * 0.38, r * 0.55, rand, 10, 0.2)), Palette.TintAlpha(flame, 20, 0.18));
            Disc(p, r * 0.3, -r * 0.38, r * 0.3, Palette.Hex("#3a342c"), Palette.Hex("#2c2820"), 1.2);
            Disc(p, r * 0.3, -r * 0.38, r * 0.17, flame);
            Disc(p, 0, r * 0.7, 4, Palette.Hex("#3a342c"), Palette.Hex("#2c2820"), 0.9);
        }
    }

    // --- fountain -----------------------------------------------------------------
    // Stone basin, oblique-extruded. Models: round basin with spray,
    // square basin, dry mossy basin (TS fountain 0/1/2).
    void DrawFountain(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 16 * rand.Range(0.9, 1.1);
        var stone = Palette.Mix(
            Stone,
            rand.Next() < 0.5 ? Colors.White : Palette.Hex("#6a6c58"),
            rand.Next() * 0.18
        );
        var edge = Palette.Hex("#4a4f46");
        var water = Palette.Hex("#298f99");
        var spray = Palette.Tint(water, 55);
        var waterEdge = Palette.Hex("#38505a");
        var drop = r * rand.Range(0.4, 0.5) * p.Scale;

        ContactShadow(p, r * 1.1, r * 0.95 + drop / p.Scale * 0.4);

        if (model == 1)
        {
            // Square basin (TS fountain model 1).
            var outer = RectPts(-r, -r * 0.85, r, r * 0.85);
            var ow = Xform(p, outer);
            WallDrop(ow, drop, Palette.Mix(stone, Colors.Black, 0.25), edge, Math.Max(0.7, 1.2 * p.Scale));
            Fill(ow, stone);
            Stroke(ow, edge, (float)Math.Max(0.7, 1.3 * p.Scale));
            var pool = Xform(p, RectPts(-r * 0.7, -r * 0.55, r * 0.7, r * 0.55));
            Fill(pool, water);
            Stroke(pool, waterEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
            // Side cheeks + center pillar (TS detail).
            foreach (var side in new[] { -1, 1 })
            {
                var cheek = Xform(
                    p,
                    new[]
                    {
                        new Vector2((float)(side * r * 0.78), (float)(-r * 0.75)),
                        new Vector2((float)(side * r * 0.95), (float)(-r * 0.6)),
                        new Vector2((float)(side * r * 0.95), (float)(r * 0.6)),
                        new Vector2((float)(side * r * 0.78), (float)(r * 0.75)),
                    }
                );
                Fill(cheek, stone);
                Stroke(cheek, edge, (float)Math.Max(0.6, 0.9 * p.Scale));
            }
            Disc(p, 0, 0, r * 0.26, stone, edge, 1.1);
        }
        else if (model == 2)
        {
            // Dry mossy basin (TS fountain model 2).
            var outer = Poly.Ellipse(0, 0, r, r * 0.9, 0, 14);
            var ow = Xform(p, outer);
            WallDrop(ow, drop, Palette.Mix(stone, Colors.Black, 0.28), edge, Math.Max(0.7, 1.2 * p.Scale));
            Fill(ow, stone);
            Stroke(ow, edge, (float)Math.Max(0.7, 1.3 * p.Scale));
            var moss = Xform(p, Poly.Ellipse(0, 0, r * 0.68, r * 0.6, 0, 12));
            Fill(moss, Palette.Hex("#5d5648"));
            Stroke(moss, Palette.Hex("#3f4438"), (float)Math.Max(0.6, 0.9 * p.Scale));
            Seg(
                p,
                [
                    new Vector2((float)(-r * 0.5), (float)(-r * 0.2)),
                    new Vector2(0, (float)(r * 0.1)),
                    new Vector2((float)(r * 0.4), (float)(-r * 0.3)),
                ],
                Palette.Hex("#3f4438"),
                1.4
            );
            Disc(p, 0, 0, r * 0.22, stone, edge, 1.0);
            for (var i = 0; i < 4; i++)
            {
                var a = i * 1.9 + 0.6;
                Disc(p, Math.Cos(a) * r * 0.82, Math.Sin(a) * r * 0.74, r * 0.09, stone, edge, 0.8);
            }
        }
        else
        {
            // Round basin with water (TS fountain model 0).
            var outer = Poly.Ellipse(0, 0, r, r * 0.9, 0, 14);
            var ow = Xform(p, outer);
            WallDrop(ow, drop, Palette.Mix(stone, Colors.Black, 0.25), edge, Math.Max(0.7, 1.2 * p.Scale));
            Fill(ow, stone);
            Stroke(ow, edge, (float)Math.Max(0.7, 1.3 * p.Scale));
            // Rim joints between the outer edge and the waterline.
            for (var i = 0; i < 10; i++)
            {
                var a = i / 10.0 * Math.PI * 2;
                Seg(
                    p,
                    Math.Cos(a) * r * 0.74,
                    Math.Sin(a) * r * 0.74 * 0.9,
                    Math.Cos(a) * r * 0.94,
                    Math.Sin(a) * r * 0.94 * 0.9,
                    Palette.Mix(stone, edge, 0.55),
                    1.6
                );
            }
            var pool = Xform(p, Poly.Ellipse(0, r * 0.06, r * 0.7, r * 0.6, 0, 14));
            Fill(pool, water);
            Stroke(pool, waterEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
            // Center pillar + spray arcs + droplets.
            var cp = Xform(p, Vector2.Zero);
            var shaft = Poly.Ellipse(cp.X, cp.Y, (float)(r * 0.24 * p.Scale), (float)(r * 0.2 * p.Scale), 0, 10);
            WallDrop(shaft, r * 0.28 * p.Scale, Palette.Mix(stone, Colors.Black, 0.2), edge, Math.Max(0.5, 0.7 * p.Scale));
            Fill(shaft, stone);
            Stroke(shaft, edge, (float)Math.Max(0.6, 0.8 * p.Scale));
            Disc(p, 0, 0, r * 0.1, spray);
            foreach (var side in new[] { -1, 1 })
            {
                Seg(
                    p,
                    [
                        new Vector2(0, 0),
                        new Vector2((float)(side * r * 0.42), (float)(-r * 0.3)),
                        new Vector2((float)(side * r * 0.55), (float)(-r * 0.05)),
                    ],
                    new Color(0.84f, 0.94f, 0.87f, 0.66f),
                    1.4
                );
                Disc(p, side * r * 0.55, -r * 0.05, 2, Palette.Hex("#e8f6ec"));
            }
        }
    }

    // --- pillar ---------------------------------------------------------------------
    // Stone columns. Models: intact column (plinth + fluted shaft +
    // capital), broken stump, rune monolith (TS pillar 0/1/2).
    void DrawPillar(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 9 * rand.Range(0.95, 1.2);
        var stone = Palette.Mix(
            Stone,
            rand.Next() < 0.5 ? Colors.White : Palette.Hex("#6a6c58"),
            rand.Next() * 0.18
        );
        var edge = Palette.Hex("#4a4f46");

        if (model == 2)
        {
            // Rune monolith — tapered obelisk, wide at the base.
            var H = r * 2.1;
            var drop = H * p.Scale;
            ContactShadow(p, r * 1.1, r * 0.6 + H * 0.3);
            var top = new[]
            {
                new Vector2(0, (float)(-r * 0.4)),
                new Vector2((float)(r * 0.34), 0),
                new Vector2(0, (float)(r * 0.4)),
                new Vector2((float)(-r * 0.34), 0),
            };
            var tw = Xform(p, top);
            WallDrop(tw, drop, Palette.Mix(stone, Colors.Black, 0.18), edge, Math.Max(0.7, 1.1 * p.Scale), flare: 0.85);
            Fill(tw, Palette.Mix(stone, Colors.White, 0.15));
            Stroke(tw, edge, (float)Math.Max(0.7, 1.1 * p.Scale));
            // Rune zigzag down the south face.
            if (SouthFace(tw) is { } f)
            {
                var m = (f.A + f.B) / 2;
                var rune = new[]
                {
                    m + new Vector2((float)(-r * 0.1 * p.Scale), (float)(drop * 0.15)),
                    m + new Vector2((float)(r * 0.06 * p.Scale), (float)(drop * 0.35)),
                    m + new Vector2((float)(-r * 0.06 * p.Scale), (float)(drop * 0.55)),
                    m + new Vector2((float)(r * 0.04 * p.Scale), (float)(drop * 0.8)),
                };
                DrawPolyline(rune, new Color(0.29f, 0.31f, 0.27f, 0.7f), (float)Math.Max(0.6, 1.0 * p.Scale), true);
            }
        }
        else if (model == 1)
        {
            // Broken stump — jagged crown, short drop, rubble.
            var H = r * rand.Range(0.8, 1.1);
            var drop = H * p.Scale;
            ContactShadow(p, r * 0.9, r * 0.7 + H * 0.3);
            var top = Blob(0, 0, r * 0.6, rand, 9, 0.28);
            var tw = Xform(p, top);
            var faces = WallDrop(tw, drop, Palette.Mix(stone, Colors.Black, 0.22), edge, Math.Max(0.6, 1.0 * p.Scale), flare: 0.3);
            Fill(tw, Palette.Mix(stone, Colors.White, 0.08));
            Stroke(tw, edge, (float)Math.Max(0.6, 1.0 * p.Scale));
            // Crack down the south face.
            if (SouthFace(tw) is { } f)
            {
                var m = (f.A + f.B) / 2;
                DrawPolyline(
                    [
                        m + new Vector2((float)(-r * 0.08 * p.Scale), (float)(drop * 0.1)),
                        m + new Vector2((float)(r * 0.05 * p.Scale), (float)(drop * 0.5)),
                        m + new Vector2((float)(-r * 0.03 * p.Scale), (float)(drop * 0.9)),
                    ],
                    Palette.Mix(stone, edge, 0.7),
                    (float)Math.Max(0.5, 0.8 * p.Scale),
                    true
                );
            }
            // Rubble at the foot.
            if (faces.Count > 0)
            {
                var (fa, fb) = faces[faces.Count / 2];
                var foot = (fa + fb) / 2 + new Vector2(0, (float)drop);
                for (var k = 0; k < 3; k++)
                    DrawCircle(
                        foot + new Vector2((float)rand.Range(-r, r) * p.Scale, (float)rand.Range(1, 4) * p.Scale),
                        (float)(rand.Range(1.2, 2.2) * p.Scale),
                        Palette.Mix(stone, Colors.Black, 0.25)
                    );
            }
        }
        else
        {
            // Intact column — plinth, fluted shaft, capital slab.
            var shaftH = r * 2.0;
            var plinthH = r * 0.4;
            var drop = shaftH * p.Scale;
            ContactShadow(p, r * 1.2, r * 0.7 + shaftH * 0.35);

            var shaft = Poly.Ellipse(0, 0, r * 0.45, r * 0.4, 0, 12);
            var sw = Xform(p, shaft);
            var sfaces = WallDrop(sw, drop, Palette.Mix(stone, Colors.Black, 0.18), edge, Math.Max(0.7, 1.0 * p.Scale), flare: 0.35);

            // Plinth — wider squat band the shaft rises out of.
            var ctr = Poly.Centroid(sw);
            var pc = ctr + new Vector2(0, (float)(drop - plinthH * p.Scale * 0.5));
            var plinth = Poly.Ellipse(pc.X, pc.Y, r * 0.78 * p.Scale, r * 0.42 * p.Scale, 0, 12);
            WallDrop(plinth, plinthH * p.Scale * 0.9, Palette.Mix(stone, Colors.Black, 0.28), edge, Math.Max(0.6, 0.9 * p.Scale));
            Fill(plinth, Palette.Mix(stone, Colors.Black, 0.08));
            Stroke(plinth, edge, (float)Math.Max(0.6, 0.9 * p.Scale));

            // Shaft top + capital slab.
            Fill(sw, Palette.Mix(stone, Colors.White, 0.12));
            Stroke(sw, edge, (float)Math.Max(0.7, 1.0 * p.Scale));
            var cap = Xform(p, RectPts(-r * 0.62, -r * 0.3, r * 0.62, r * 0.22));
            WallDrop(cap, r * 0.16 * p.Scale, Palette.Mix(stone, Colors.Black, 0.15), edge, Math.Max(0.5, 0.8 * p.Scale));
            Fill(cap, Palette.Mix(stone, Colors.White, 0.2));
            Stroke(cap, edge, (float)Math.Max(0.6, 0.9 * p.Scale));

            // Fluting — two vertical grooves on the shaft's south face.
            for (var i = 0; i < sfaces.Count; i++)
            {
                var m = (sfaces[i].A + sfaces[i].B) / 2;
                DrawLine(
                    m + new Vector2(0, (float)(drop * 0.12)),
                    m + new Vector2(0, (float)(drop * 0.85)),
                    Palette.Mix(stone, edge, 0.5) with { A = 0.6f },
                    (float)Math.Max(0.5, 0.7 * p.Scale)
                );
            }
        }
    }

    // --- marker -----------------------------------------------------------------------
    // Map tokens. Models: numbered spawn disc, pennant pin, target
    // (TS marker 0/1/2). TS paints the index as text; here a bold
    // roman-I glyph stands in (no text in the painter).
    void DrawMarker(PropPlacement p)
    {
        var rand = new Jitter(p.Seed);
        var model = (int)(rand.Next() * 3);
        var r = 10 * rand.Range(0.95, 1.15);
        var color = Palette.Hex("#d34f3d");
        var paper = Palette.Hex("#fff3d6");
        var ink = new Color(0.18f, 0.16f, 0.12f, 0.85f);
        var edge = Palette.Hex("#5d4a2f");
        var flagEdge = Palette.Hex("#6d2f27");

        ContactShadow(p, r * 0.9, r * 0.8);

        if (model == 1)
        {
            // Pennant pin (TS marker model 1).
            Disc(p, 0, r * 0.55, r * 0.4, new Color(0f, 0f, 0f, 0.15f));
            Seg(p, 0, r * 0.6, 0, -r * 0.85, paper, Math.Max(2.5, r * 0.14));
            var pennant = new[]
            {
                new Vector2(0, (float)(-r * 0.85)),
                new Vector2((float)(r * 0.8), (float)(-r * 0.62)),
                new Vector2((float)(r * 0.66), (float)(-r * 0.36)),
                new Vector2(0, (float)(-r * 0.36)),
            };
            var pw = Xform(p, pennant);
            Fill(pw, color);
            Stroke(pw, flagEdge, (float)Math.Max(0.6, 1.0 * p.Scale));
            Disc(p, 0, -r * 0.88, Math.Max(2, r * 0.09), paper, edge, 0.9);
            Disc(p, 0, r * 0.6, r * 0.16, paper, edge, 0.9);
        }
        else if (model == 2)
        {
            // Target token (TS marker model 2).
            Disc(p, 0, 0, r * 0.78, paper);
            Disc(p, 0, 0, r * 0.62, color);
            Disc(p, 0, 0, r * 0.3, paper);
            var notch = Math.Max(2, r * 0.15);
            foreach (var (x0, y0, x1, y1) in new[]
            {
                (0.0, -0.98, 0.0, -0.62),
                (0.0, 0.62, 0.0, 0.98),
                (-0.98, 0.0, -0.62, 0.0),
                (0.62, 0.0, 0.98, 0.0),
            })
                Seg(p, x0 * r, y0 * r, x1 * r, y1 * r, paper, notch);
            Disc(p, 0, 0, r * 0.15, color);
        }
        else
        {
            // Spawn token — paper disc, red ring, bold "I" glyph.
            Disc(p, 0, 0, r * 0.8, paper, edge, 0.9);
            Disc(p, 0, 0, r * 0.64, color);
            var glyph = new[]
            {
                new Vector2(0, (float)(-r * 0.3)),
                new Vector2(0, (float)(r * 0.3)),
            };
            var wOut = Math.Max(2.2, r * 0.2);
            // Ink under-stroke then paper core, like TS strokeText+fillText.
            Seg(p, -r * 0.18, -r * 0.3, r * 0.18, -r * 0.3, ink, wOut);
            Seg(p, -r * 0.18, r * 0.3, r * 0.18, r * 0.3, ink, wOut);
            Seg(p, glyph[0].X, glyph[0].Y, glyph[1].X, glyph[1].Y, ink, wOut);
            Seg(p, -r * 0.18, -r * 0.3, r * 0.18, -r * 0.3, paper, wOut * 0.45);
            Seg(p, -r * 0.18, r * 0.3, r * 0.18, r * 0.3, paper, wOut * 0.45);
            Seg(p, glyph[0].X, glyph[0].Y, glyph[1].X, glyph[1].Y, paper, wOut * 0.45);
        }
    }
}
