// Prop painters — water / treasure / interior batch. Owns DrawDock,
// DrawBoat, DrawChest, DrawPortal, DrawDoor, DrawWindow, DrawTable,
// DrawBed, DrawShelf, DrawRug, DrawFireplace.
// Ported from DMmap/src/dm-area/scene/render/scene-props.ts.
// Interior props are flat top-down room furniture drawn in the prop's
// local frame so p.Rotation orients them inside a room.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    // `p.wood` in the web palette (#a37d4c) is a lighter timber than the
    // trunk-dark Wood field — props need the contrast.
    Color Timber => Palette.Mix(Wood, Palette.Hex("#d8964e"), 0.5);

    // --- In* drawing helpers (prefixed so sibling batches can't collide) --

    static Vector2[] InRect(double x0, double y0, double x1, double y1) =>
        [
            new((float)x0, (float)y0),
            new((float)x1, (float)y0),
            new((float)x1, (float)y1),
            new((float)x0, (float)y1),
        ];

    /// <summary>Rotate local points around a local pivot — the TS
    /// ctx.translate + ctx.rotate idiom (door leaf, T-dock head).</summary>
    static Vector2[] InRot(IEnumerable<Vector2> pts, double angle, double ox = 0, double oy = 0)
    {
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        return pts
            .Select(q => new Vector2(
                (float)(ox + q.X * c - q.Y * s),
                (float)(oy + q.X * s + q.Y * c)
            ))
            .ToArray();
    }

    /// <summary>Filled polygon + inked edge — the TS drawPoly.</summary>
    void InPoly(PropPlacement p, IEnumerable<Vector2> pts, Color fill, Color? edge = null, double edgeW = 1.2)
    {
        var w = Xform(p, pts);
        Fill(w, fill);
        if (edge is { } ec)
            Stroke(w, ec, (float)Math.Max(0.6, edgeW * p.Scale));
    }

    /// <summary>Open polyline in local coords — the TS drawLine.</summary>
    void InLine(PropPlacement p, IEnumerable<Vector2> pts, Color color, double width)
    {
        var w = Xform(p, pts);
        if (w.Length >= 2)
            DrawPolyline(w, color, (float)Math.Max(0.6, width * p.Scale), true);
    }

    /// <summary>Ink-only closed contour (rugs: trim border has no fill).</summary>
    void InStroke(PropPlacement p, IEnumerable<Vector2> pts, Color color, double width)
    {
        var w = Xform(p, pts);
        if (w.Length >= 3)
            Stroke(w, color, (float)Math.Max(0.6, width * p.Scale));
    }

    /// <summary>Filled circle + optional ink edge — the TS drawDisc.</summary>
    void InDisc(PropPlacement p, double x, double y, double r, Color fill, Color? edge = null)
    {
        var w = Xform(p, Poly.Ellipse(x, y, r, r, 0, 12));
        Fill(w, fill);
        if (edge is { } ec)
            Stroke(w, ec, (float)Math.Max(0.6, 1.0 * p.Scale));
    }

    /// <summary>Arc sampled as a polyline; `dashed` skips every other
    /// segment (the TS ctx.setLineDash swing arc).</summary>
    void InArc(
        PropPlacement p,
        double cx,
        double cy,
        double rad,
        double a0,
        double a1,
        Color color,
        double width,
        bool dashed = false,
        int segs = 20
    )
    {
        for (var i = 0; i < segs; i++)
        {
            if (dashed && i % 2 == 1)
                continue;
            var aa = a0 + (a1 - a0) * i / segs;
            var ab = a0 + (a1 - a0) * (i + 1) / segs;
            InLine(
                p,
                new[]
                {
                    new Vector2((float)(cx + Math.Cos(aa) * rad), (float)(cy + Math.Sin(aa) * rad)),
                    new Vector2((float)(cx + Math.Cos(ab) * rad), (float)(cy + Math.Sin(ab) * rad)),
                },
                color,
                width
            );
        }
    }

    // --- dock ------------------------------------------------------------
    // Wooden pier: jittered plank deck along +X (points into the water),
    // piles poking past the edges, plank seams; variants = straight pier,
    // T-head pier, log raft. (TS `dock`; the soaked pile tips need a water
    // context we don't have, so they're skipped.)
    void DrawDock(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 16 * rand.Range(0.9, 1.15);
        var roll = rand.Next();
        var model = p.Model >= 0 ? ((p.Model % 3) + 3) % 3 : (roll < 0.22 ? 2 : roll < 0.55 ? 1 : 0);
        var edge = Palette.Hex("#4d3f2c");
        var post = Palette.Hex("#5e4f3a");
        var deck = Timber;

        ContactShadow(p, r * 1.2, r * 0.8);

        double J(double i, double span = 2.6) => (rand.Next() - 0.5) * span;

        // Deck band x0..x1, half-width w — skewed corners, side beams and
        // cross seams between planks. ox/oy/oa place it in a rotated
        // sub-frame (the T-head turns the same deck 90°).
        void Planks(double x0, double x1, double w, int key, double ox = 0, double oy = 0, double oa = 0)
        {
            Vector2[] Map(IEnumerable<Vector2> pts) => InRot(pts, oa, ox, oy);
            InPoly(
                p,
                Map(
                    new[]
                    {
                        new Vector2((float)(x0 + J(key)), (float)(-w + J(key + 1))),
                        new Vector2((float)(x1 + J(key + 2)), (float)(-w + J(key + 3))),
                        new Vector2((float)(x1 + J(key + 4)), (float)(w + J(key + 5))),
                        new Vector2((float)(x0 + J(key + 6)), (float)(w + J(key + 7))),
                    }
                ),
                deck,
                edge
            );
            foreach (var side in new[] { -1.0, 1.0 })
                InLine(
                    p,
                    Map(
                        new[]
                        {
                            new Vector2((float)(x0 + 1), (float)(side * (w - 1.4))),
                            new Vector2((float)(x1 - 1), (float)(side * (w - 1.4))),
                        }
                    ),
                    Palette.Hex("#6b5138"),
                    2
                );
            var n = Math.Max(3, (int)Math.Round((x1 - x0) / (r * 0.45)));
            for (var i = 1; i < n; i++)
            {
                var px = x0 + (x1 - x0) * i / n + J(key + 8 + i, 1.6);
                InLine(
                    p,
                    Map(
                        new[]
                        {
                            new Vector2((float)px, (float)(-w + 1.5)),
                            new Vector2((float)(px + J(key + 20 + i, 1.2)), (float)(w - 1.5)),
                        }
                    ),
                    edge,
                    0.9
                );
            }
        }

        // A pair of piles jutting past both deck edges at each x.
        void Piles(double[] xs, double w)
        {
            var i = 0;
            foreach (var px in xs)
            {
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    var tip = new Vector2(
                        (float)(px + J(i * 3 + side + 30)),
                        (float)(side * (w + r * 0.34))
                    );
                    InLine(
                        p,
                        new[] { new Vector2((float)px, (float)(side * (w - 1))), tip },
                        post,
                        3
                    );
                }
                i++;
            }
        }

        if (model == 2)
        {
            // Raft — square log deck with rope lashings in the corners,
            // a barrel and a mooring pole.
            var w = r * 0.8;
            Planks(-w, w, w, 50);
            var rope = Palette.Hex("#d8c9a0");
            foreach (var sx in new[] { -1.0, 1.0 })
                foreach (var sy in new[] { -1.0, 1.0 })
                {
                    InLine(
                        p,
                        new[]
                        {
                            new Vector2((float)(sx * w * 0.6), (float)(sy * w * 0.8)),
                            new Vector2((float)(sx * w * 0.8), (float)(sy * w * 0.6)),
                        },
                        rope,
                        1.4
                    );
                    InLine(
                        p,
                        new[]
                        {
                            new Vector2((float)(sx * w * 0.6), (float)(sy * w * 0.6)),
                            new Vector2((float)(sx * w * 0.8), (float)(sy * w * 0.8)),
                        },
                        rope,
                        1.4
                    );
                }
            InDisc(p, r * 0.3, -r * 0.28, r * 0.2, Palette.Hex("#8a6a44"), edge);
            InLine(
                p,
                new[]
                {
                    new Vector2((float)(r * 0.16), (float)(-r * 0.28)),
                    new Vector2((float)(r * 0.44), (float)(-r * 0.28)),
                },
                edge,
                1.2
            );
            InLine(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.7), (float)(-r * 0.85)),
                    new Vector2((float)(-r * 0.7), (float)(-r * 1.15)),
                },
                post,
                4
            );
        }
        else
        {
            var stemEnd = model == 1 ? r * 1.75 : r * 1.9;
            var w = model == 1 ? r * 0.4 : r * 0.45;
            Planks(-r * 0.15, stemEnd, w, 3);
            Piles(
                model == 1
                    ? new[] { r * 0.35, r * 0.85, r * 1.35 }
                    : new[] { r * 0.3, r * 0.8, r * 1.3, r * 1.75 },
                w
            );
            if (model == 1)
            {
                // T-head — a second deck rotated 90° at the stem's end.
                Planks(-r * 0.95, r * 0.95, r * 0.38, 13, ox: r * 1.72, oa: Math.PI / 2);
                foreach (var px in new[] { r * 1.42, r * 2.02 })
                    foreach (var side in new[] { -1.0, 1.0 })
                        InLine(
                            p,
                            new[]
                            {
                                new Vector2((float)px, (float)(side * r * 0.82)),
                                new Vector2((float)(px + J(px + side)), (float)(side * r * 1.08)),
                            },
                            post,
                            3
                        );
                InDisc(p, r * 1.68, -r * 0.52, r * 0.17, Palette.Hex("#8a6a44"), edge);
                InLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(r * 1.56), (float)(-r * 0.52)),
                        new Vector2((float)(r * 1.8), (float)(-r * 0.52)),
                    },
                    edge,
                    1.2
                );
            }
            else
            {
                // Mooring bollard past the pier's end.
                InLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(r * 1.8), (float)(-r * 0.2)),
                        new Vector2((float)(r * 1.8), (float)(-r * 0.52)),
                    },
                    post,
                    4.5
                );
                InDisc(p, r * 1.8, -r * 0.52, 2.4, Palette.Hex("#8a6a44"), edge);
            }
        }
    }

    // --- boat --------------------------------------------------------------
    // Small top-down hull (pointed oval) with inner planking; variants =
    // rowboat with two benches + a shipped oar, punt with oars out, and a
    // sailboat with mast + triangle sail. (TS `boat`.)
    void DrawBoat(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 13 * rand.Range(0.9, 1.15);
        var roll = rand.Next();
        var model = p.Model >= 0 ? ((p.Model % 3) + 3) % 3 : (roll < 0.55 ? 0 : roll < 0.8 ? 1 : 2);
        var edge = Palette.Hex("#3b3226");

        ContactShadow(p, r * 1.1, r * 0.55);

        InPoly(
            p,
            new[]
            {
                new Vector2((float)-r, 0),
                new Vector2((float)(-r * 0.65), (float)(-r * 0.4)),
                new Vector2((float)(r * 0.65), (float)(-r * 0.4)),
                new Vector2((float)r, 0),
                new Vector2((float)(r * 0.65), (float)(r * 0.4)),
                new Vector2((float)(-r * 0.65), (float)(r * 0.4)),
            },
            Timber,
            edge
        );
        InPoly(
            p,
            new[]
            {
                new Vector2((float)(-r * 0.7), 0),
                new Vector2((float)(-r * 0.5), (float)(-r * 0.25)),
                new Vector2((float)(r * 0.5), (float)(-r * 0.25)),
                new Vector2((float)(r * 0.7), 0),
                new Vector2((float)(r * 0.5), (float)(r * 0.25)),
                new Vector2((float)(-r * 0.5), (float)(r * 0.25)),
            },
            Palette.Hex("#66513e"),
            edge
        );

        if (model == 1)
        {
            // Punt — a single thwart and two oars trailing out.
            InLine(
                p,
                new[] { new Vector2(0, (float)(-r * 0.35)), new Vector2(0, (float)(r * 0.35)) },
                Palette.Hex("#c6aa75"),
                3
            );
            foreach (var side in new[] { -1.0, 1.0 })
                InLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(-r * 0.15), (float)(side * r * 0.15)),
                        new Vector2((float)(r * 0.45), (float)(side * r * 0.85)),
                    },
                    Palette.Hex("#bea479"),
                    2.4
                );
        }
        else if (model == 2)
        {
            // Sailboat — mast, triangle sail and the boom.
            InLine(
                p,
                new[] { new Vector2(0, (float)(r * 0.35)), new Vector2(0, (float)(-r * 0.55)) },
                Palette.Hex("#5e4f3a"),
                2.6
            );
            InPoly(
                p,
                new[]
                {
                    new Vector2((float)(r * 0.03), (float)(-r * 0.55)),
                    new Vector2((float)(r * 0.48), (float)(-r * 0.08)),
                    new Vector2((float)(r * 0.03), (float)(-r * 0.08)),
                },
                Palette.Hex("#e8dcc0"),
                Palette.Hex("#8a7a5a")
            );
            InLine(
                p,
                new[] { new Vector2(0, (float)(-r * 0.08)), new Vector2((float)(r * 0.5), (float)(-r * 0.08)) },
                Palette.Hex("#5e4f3a"),
                1.6
            );
        }
        else
        {
            // Rowboat — two thwarts across and one oar shipped inside.
            foreach (var px in new[] { -r * 0.35, r * 0.35 })
                InLine(
                    p,
                    new[]
                    {
                        new Vector2((float)px, (float)(-r * 0.35)),
                        new Vector2((float)px, (float)(r * 0.35)),
                    },
                    Palette.Hex("#c6aa75"),
                    3
                );
            InLine(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.2), (float)(-r * 0.7)),
                    new Vector2((float)(r * 0.35), (float)(r * 0.7)),
                },
                Palette.Hex("#bea479"),
                2
            );
        }
    }

    // --- chest ---------------------------------------------------------------
    // Treasure chest: box + lid band + straps + lock plate; a rarer open
    // variant spills coin discs. (TS `chest` — model 1 gets an iron lid.)
    void DrawChest(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 10 * rand.Range(0.9, 1.15);
        // The TS rotates chests by a small seed-derived tilt.
        var q = new PropPlacement(p.Kind, p.X, p.Y, p.Scale, p.Rotation + rand.Range(-0.2, 0.2), p.Seed);
        var lid = rand.Next() < 0.3 ? Palette.Hex("#5a5a5e") : Palette.Hex("#6b4f32");
        var edge = Palette.Hex("#3f3222");
        var open = rand.Next() < 0.22;

        ContactShadow(q, r, r * 0.8);

        if (open)
        {
            // Lid hinged up behind the box, gold inside.
            InPoly(q, InRect(-r * 0.8, -r * 0.95, r * 0.8, -r * 0.55), lid, edge);
            InPoly(q, InRect(-r * 0.8, -r * 0.5, r * 0.8, r * 0.6), Timber, edge);
            for (var i = 0; i < 6; i++)
                InDisc(
                    q,
                    -r * 0.5 + i % 3 * r * 0.5,
                    -r * 0.2 + i / 3 * r * 0.4,
                    r * 0.12,
                    Palette.Hex("#e8c25a"),
                    Palette.Hex("#a8873e")
                );
            InDisc(q, 0, 0, r * 0.12, Palette.Hex("#4a4034"), Palette.Hex("#2c2820"));
        }
        else
        {
            InPoly(q, InRect(-r * 0.85, -r * 0.55, r * 0.85, r * 0.55), Timber, edge);
            InPoly(q, InRect(-r * 0.85, -r * 0.55, r * 0.85, -r * 0.1), lid, edge);
            foreach (var side in new[] { -1.0, 1.0 })
                InLine(
                    q,
                    new[]
                    {
                        new Vector2((float)(side * r * 0.45), (float)(-r * 0.55)),
                        new Vector2((float)(side * r * 0.45), (float)(r * 0.55)),
                    },
                    edge,
                    3
                );
            InPoly(
                q,
                InRect(-r * 0.09, -r * 0.15, r * 0.09, r * 0.15),
                Palette.Hex("#d8b25e"),
                Palette.Hex("#6d5a2e")
            );
        }
    }

    // --- portal ----------------------------------------------------------------
    // Magic portal: model 0 = glowing ring on five stones, model 1 = two
    // leaning monoliths with a rune crack and a glow between them,
    // model 2 = an eight-stone circle. (TS `portal`; the glow color is
    // biome-dependent, woodland default here.)
    void DrawPortal(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 14 * rand.Range(0.9, 1.15);
        var roll = rand.Next();
        var model = p.Model >= 0 ? ((p.Model % 3) + 3) % 3 : (roll < 0.5 ? 0 : roll < 0.8 ? 1 : 2);
        var glow = Palette.Hex("#c4aef0");
        var stone = Palette.Hex("#9a86c8");
        var edge = Palette.Hex("#4a4f46");
        var glowSoft = glow with { A = 0x66 / 255f };

        ContactShadow(p, r, r * 0.85);

        if (model == 1)
        {
            foreach (var side in new[] { -1.0, 1.0 })
            {
                // Slightly irregular slab, translated out and leaned inward.
                var slab = InRot(
                    new[]
                    {
                        new Vector2((float)(-r * 0.22), (float)(-r * 0.6)),
                        new Vector2((float)(r * 0.22), (float)(-r * 0.55)),
                        new Vector2((float)(r * 0.26), (float)(r * 0.55)),
                        new Vector2((float)(-r * 0.26), (float)(r * 0.6)),
                    },
                    side * 0.1,
                    side * r * 0.7,
                    0
                );
                InPoly(p, slab, stone, edge);
                InLine(
                    p,
                    InRot(
                        new[]
                        {
                            new Vector2((float)(-r * 0.1), (float)(-r * 0.4)),
                            new Vector2((float)(r * 0.08), (float)(r * 0.3)),
                        },
                        side * 0.1,
                        side * r * 0.7,
                        0
                    ),
                    glow with { A = 0xaa / 255f },
                    1.4
                );
            }
            InLine(
                p,
                new[] { new Vector2((float)(-r * 0.45), 0), new Vector2((float)(r * 0.45), 0) },
                glowSoft,
                2
            );
            InDisc(p, 0, 0, r * 0.12, glow);
        }
        else if (model == 2)
        {
            for (var i = 0; i < 8; i++)
            {
                var a = i / 8.0 * Math.PI * 2 + 0.3;
                InDisc(p, Math.Cos(a) * r * 0.75, Math.Sin(a) * r * 0.75, r * 0.14, stone, edge);
            }
            InDisc(p, 0, 0, r * 0.3, new Color(0, 0, 0, 0x18 / 255f));
            InLine(
                p,
                new[] { new Vector2((float)(-r * 0.15), 0), new Vector2((float)(r * 0.15), 0) },
                glowSoft,
                1.4
            );
            InLine(
                p,
                new[] { new Vector2(0, (float)(-r * 0.15)), new Vector2(0, (float)(r * 0.15)) },
                glowSoft,
                1.4
            );
        }
        else
        {
            InDisc(p, 0, 0, r * 0.5, new Color(0, 0, 0, 0x28 / 255f));
            DrawArc(
                Xform(p, Vector2.Zero),
                (float)(r * 0.8 * p.Scale),
                0,
                Mathf.Tau,
                28,
                stone,
                (float)(r * 0.18 * p.Scale),
                true
            );
            InDisc(p, 0, 0, r * 0.5, glow with { A = 0x33 / 255f });
            DrawArc(
                Xform(p, Vector2.Zero),
                (float)(r * 0.66 * p.Scale),
                0,
                Mathf.Tau,
                24,
                glow with { A = 0.8f },
                (float)Math.Max(0.6, 2 * p.Scale),
                true
            );
            var off = rand.Next() * Math.PI * 2;
            for (var i = 0; i < 5; i++)
            {
                var a = i / 5.0 * Math.PI * 2 + off;
                InDisc(p, Math.Cos(a) * r * 0.8, Math.Sin(a) * r * 0.8, r * 0.1, stone, edge);
            }
            InDisc(p, 0, 0, r * 0.12, new Color(1, 1, 1, 0xcc / 255f));
        }
    }

    // --- door -------------------------------------------------------------------
    // Doorway stamp: dark gap, a leaf swung open 45° with plank seams,
    // a dashed swing arc from the hinge and a knob. (TS `door`; the gap
    // itself is added here so the prop reads standalone, not only on a
    // room wall.)
    void DrawDoor(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 11 * rand.Range(0.95, 1.15);
        var w = r;
        var edge = Palette.Hex("#3e3021");
        var openArc = rand.Next() < 0.75;

        ContactShadow(p, r * 0.8, r * 0.6);

        // The doorway gap under the leaf — a worn threshold either side
        // so the opening reads as part of a wall, not a floating slab.
        InPoly(p, InRect(-w * 0.55, -w * 0.3, w * 0.55, w * 0.3), Palette.Hex("#2e241c"), edge);
        foreach (var side in new[] { -1.0, 1.0 })
            InLine(
                p,
                new[]
                {
                    new Vector2((float)(side * w * 0.55), (float)(-w * 0.3)),
                    new Vector2((float)(side * w * 0.75), (float)(-w * 0.3)),
                },
                edge,
                2.4
            );

        // Leaf at 45° — rect + plank seams in the rotated frame.
        var leaf = InRot(InRect(-w * 0.5, -w * 0.28, w * 0.5, w * 0.28), Math.PI / 4);
        InPoly(p, leaf, Timber, edge);
        foreach (var px in new[] { -w * 0.22, 0.0, w * 0.22 })
            InLine(
                p,
                InRot(
                    new[]
                    {
                        new Vector2((float)px, (float)(-w * 0.26)),
                        new Vector2((float)px, (float)(w * 0.26)),
                    },
                    Math.PI / 4
                ),
                Palette.Hex("#5d422a"),
                1
            );

        if (openArc)
        {
            // Swing arc from the hinge at the leaf's heel — dashed.
            InArc(
                p,
                -r * 0.5,
                r * 0.5,
                r * 0.9,
                -Math.PI / 2,
                0,
                edge with { A = 0x66 / 255f },
                1.1,
                dashed: true
            );
        }
        else
        {
            // Closed variant — the top arc marks the doorway lintel.
            InArc(p, 0, 0, r * 0.62, -Math.PI * 1.15, Math.PI * 0.15, edge, 1.6);
        }
        InDisc(p, r * 0.28, -r * 0.02, 1.6, Palette.Hex("#e8d9a8"));
    }

    // --- window -----------------------------------------------------------------
    // Sill + frame + glass with mullions and a glint — reads as a wall
    // opening from above. (TS `window`.)
    void DrawWindow(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 9 * rand.Range(0.95, 1.15);
        var glass = rand.Next() < 0.3 ? Palette.Hex("#a9c4c9") : Palette.Hex("#cfe3de");
        var frameEdge = Palette.Hex("#3e3629");
        var mullion = Palette.Hex("#5c5038");

        ContactShadow(p, r * 1.0, r * 0.5);

        InPoly(p, InRect(-r, -r * 0.4, r, r * 0.4), Palette.Hex("#efe6c4"), frameEdge);
        InPoly(p, InRect(-r * 0.8, -r * 0.26, r * 0.8, r * 0.26), glass, mullion);
        if (rand.Next() < 0.75)
            InLine(
                p,
                new[] { new Vector2(0, (float)(-r * 0.26)), new Vector2(0, (float)(r * 0.26)) },
                mullion,
                1.4
            );
        InLine(
            p,
            new[] { new Vector2((float)(-r * 0.8), 0), new Vector2((float)(r * 0.8), 0) },
            mullion,
            1.4
        );
        // Sun glint on the NW pane.
        InLine(
            p,
            new[] { new Vector2((float)(-r * 0.6), (float)(-r * 0.1)), new Vector2((float)(-r * 0.2), (float)(-r * 0.1)) },
            new Color(1, 1, 1, 0x99 / 255f),
            1.6
        );
    }

    // --- table --------------------------------------------------------------------
    // Round table, long dining table or square table with an inset top
    // panel. (TS `table`.)
    void DrawTable(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 13 * rand.Range(0.9, 1.15);
        var roll = rand.Next();
        var model = p.Model >= 0 ? ((p.Model % 3) + 3) % 3 : (roll < 0.4 ? 0 : roll < 0.75 ? 1 : 2);
        var edge = Palette.Hex("#4d3a26");

        ContactShadow(p, r, r * 0.7);

        if (model == 1)
        {
            InPoly(p, InRect(-r, -r * 0.42, r, r * 0.42), Timber, edge);
            InLine(
                p,
                new[] { new Vector2((float)(-r * 0.9), 0), new Vector2((float)(r * 0.9), 0) },
                Palette.Hex("#7a5a38"),
                1
            );
            foreach (var sx in new[] { -0.75, 0.75 })
                foreach (var sy in new[] { -0.28, 0.28 })
                    InDisc(p, r * sx, r * sy, 1.8, Palette.Hex("#5d4326"));
        }
        else if (model == 2)
        {
            InPoly(p, InRect(-r * 0.62, -r * 0.62, r * 0.62, r * 0.62), Timber, edge);
            InPoly(
                p,
                InRect(-r * 0.42, -r * 0.42, r * 0.42, r * 0.42),
                Palette.Hex("#8a653f"),
                Palette.Hex("#5d4326")
            );
        }
        else
        {
            InDisc(p, 0, 0, r * 0.82, Timber, edge);
            InDisc(p, 0, 0, r * 0.6, Palette.Hex("#8a653f"), Palette.Hex("#5d4326"));
            InDisc(p, 0, 0, r * 0.14, Palette.Hex("#5d4326"));
        }
    }

    // --- bed -------------------------------------------------------------------------
    // Frame + sheet + blanket folded down from the head + pillow(s).
    // Model 0 single, model 1 double (two pillows), model 2 bedroll.
    // (TS `bed`; the blanket color is the biome "bed" scene color.)
    void DrawBed(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 16 * rand.Range(0.9, 1.15);
        var roll = rand.Next();
        var model = p.Model >= 0 ? ((p.Model % 3) + 3) % 3 : (roll < 0.5 ? 0 : roll < 0.8 ? 1 : 2);
        var frame = Palette.Hex("#8a6a44");
        var blanket = Palette.Mix(Palette.Hex("#8a4a3c"), FoliageCool, rand.Next() * 0.35);
        var sheet = Palette.Hex("#e8ddc5");
        var edge = Palette.Hex("#503a28");
        var pillowEdge = Palette.Hex("#b3a17e");
        var bw = model == 1 ? r : r * 0.62;
        var bh = r * (model == 2 ? 0.6 : 0.95);

        ContactShadow(p, bw * 1.05, bh * 1.05);

        InPoly(p, InRect(-bw, -bh, bw, bh), frame, edge);

        if (model == 2)
        {
            // Bedroll — blanket pad + a rolled pillow at the head.
            InPoly(
                p,
                InRect(-bw + 3, -bh + 3, bw - 3, bh - 3),
                blanket,
                Palette.Hex("#4d3a26")
            );
            InDisc(p, 0, -bh + 5, Math.Max(2, bw - 7), sheet, pillowEdge);
        }
        else
        {
            InPoly(p, InRect(-bw + 4, -bh + 4, bw - 4, bh - 4), sheet);
            InPoly(p, InRect(-bw + 4, -bh * 0.4, bw - 4, bh - 8), blanket);
            // The blanket's folded edge.
            InLine(
                p,
                new[] { new Vector2((float)(-bw + 4), (float)(-bh * 0.4)), new Vector2((float)(bw - 4), (float)(-bh * 0.4)) },
                Palette.Hex("#4d3a26"),
                1.4
            );
            // Pillow(s) at the head (north end of the frame).
            var pw = model == 1 ? bw * 0.42 : bw * 0.62;
            foreach (var px in model == 1 ? new[] { -bw * 0.5, bw * 0.5 } : new[] { 0.0 })
                InPoly(p, InRect(px - pw, -bh + 6, px + pw, -bh + 6 + bh * 0.3), sheet, pillowEdge, 1.0);
        }
    }

    // --- shelf -----------------------------------------------------------------------
    // Wall plank: a bookcase with two rows of colored spines (with gaps
    // where volumes are out), or a two-drawer dresser. (TS `shelf`.)
    void DrawShelf(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 12 * rand.Range(0.95, 1.15);
        var edge = Palette.Hex("#4d3a26");
        var dresser = rand.Next() < 0.25;

        ContactShadow(p, r, r * 0.55);

        InPoly(p, InRect(-r, -r * 0.42, r, r * 0.42), Palette.Hex("#6d5233"), edge);

        if (dresser)
        {
            InPoly(p, InRect(-r + 3, -r * 0.42 + 3, r - 3, r * 0.42 - 3), Timber);
            InLine(
                p,
                new[] { new Vector2((float)(-r + 3), 0), new Vector2((float)(r - 3), 0) },
                edge,
                1.4
            );
            foreach (var py in new[] { -r * 0.21, r * 0.21 })
                InDisc(p, 0, py, 1.8, Palette.Hex("#3e2f1e"));
        }
        else
        {
            var spineColors = new[]
            {
                Palette.Hex("#8a4a3c"),
                Palette.Hex("#5a6d8c"),
                Palette.Hex("#7d6d43"),
                Palette.Hex("#4a6b4f"),
                Palette.Hex("#9c7750"),
            };
            foreach (var row in new[] { -1.0, 1.0 })
            {
                var y = row * r * 0.2;
                var x = -r + 4;
                var i = 0;
                while (x < r - 8)
                {
                    var bw2 = 3 + rand.Next() * 5;
                    if (rand.Next() > 0.22)
                        InPoly(
                            p,
                            InRect(x, y - r * 0.16, x + bw2, y + r * 0.16),
                            spineColors[i % 5],
                            edge,
                            0.7
                        );
                    x += bw2 + 1.5;
                    i++;
                }
                InLine(
                    p,
                    new[] { new Vector2((float)(-r + 2), (float)(y + r * 0.2)), new Vector2((float)(r - 2), (float)(y + r * 0.2)) },
                    edge,
                    1.6
                );
            }
        }
    }

    // --- rug ---------------------------------------------------------------------------
    // Rectangular rug with a trim border, weave stripes and tassels on
    // the short ends; or a round rug with concentric trim and dots.
    // (TS `rug` — rugs deliberately cast no shadow.)
    void DrawRug(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 15 * rand.Range(0.9, 1.15);
        var roll = rand.Next();
        var model = p.Model >= 0 ? ((p.Model % 3) + 3) % 3 : (roll < 0.45 ? 1 : roll < 0.7 ? 2 : 0);
        var cloth = Palette.Mix(Palette.Hex("#8a4a3c"), Palette.Hex("#5a6d8c"), rand.Next() * 0.4);
        var trim = Palette.Hex("#d8c9a0");

        if (model != 0)
        {
            var hw = model == 2 ? r * 0.42 : r;
            InPoly(p, InRect(-hw, -r * 0.6, hw, r * 0.6), cloth, Palette.Hex("#4d3a26"));
            InStroke(p, InRect(-hw + 5, -r * 0.6 + 5, hw - 5, r * 0.6 - 5), trim, 1.2);
            for (var y = -r * 0.6 + 12; y < r * 0.6 - 8; y += 12)
                InLine(
                    p,
                    new[] { new Vector2((float)(-hw + 7), (float)y), new Vector2((float)(hw - 7), (float)y) },
                    trim with { A = 0x55 / 255f },
                    1
                );
            // Tassels off both short ends.
            foreach (var sy in new[] { -1.0, 1.0 })
                for (var i = -2; i <= 2; i++)
                    InLine(
                        p,
                        new[]
                        {
                            new Vector2((float)(i * r * 0.18), (float)(sy * r * 0.6)),
                            new Vector2(
                                (float)(i * r * 0.18 + (i % 2 != 0 ? 1.5 : -1.5)),
                                (float)(sy * (r * 0.6 + 4))
                            ),
                        },
                        trim,
                        1
                    );
        }
        else
        {
            InDisc(p, 0, 0, r * 0.9, cloth, Palette.Hex("#4d3a26"));
            DrawArc(
                Xform(p, Vector2.Zero),
                (float)(r * 0.66 * p.Scale),
                0,
                Mathf.Tau,
                24,
                trim,
                (float)Math.Max(0.6, 2.2 * p.Scale),
                true
            );
            DrawArc(
                Xform(p, Vector2.Zero),
                (float)(r * 0.3 * p.Scale),
                0,
                Mathf.Tau,
                16,
                trim with { A = 0x88 / 255f },
                (float)Math.Max(0.6, 1.4 * p.Scale),
                true
            );
            var off = rand.Next() * Math.PI * 2;
            for (var i = 0; i < 8; i++)
            {
                var a = i / 8.0 * Math.PI * 2 + off;
                DrawCircle(
                    Xform(p, new Vector2((float)(Math.Cos(a) * r * 0.48), (float)(Math.Sin(a) * r * 0.48))),
                    (float)Math.Max(0.8, 1.6 * p.Scale),
                    trim with { A = 0xaa / 255f }
                );
            }
        }
    }

    // --- fireplace ------------------------------------------------------------------------
    // Stone hearth (wider mantel top, sloped shoulders) + dark firebox,
    // crossed logs and flames — or a cold hearth with a cauldron.
    // (TS `fireplace`.)
    
    // --- chair -----------------------------------------------------------------------
    // Top-down seat: model 0 square chair with backrest shadow-line,
    // model 1 round stool, model 2 long bench. (No TS counterpart —
    // the web baked chairs into room decor; here they are real props.)
    void DrawChair(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 7.5 * rand.Range(0.9, 1.1);
        var model = p.Model >= 0 ? ((p.Model % 3) + 3) % 3 : (rand.Next() < 0.6 ? 0 : 1);
        var edge = Palette.Hex("#4d3a26");
        ContactShadow(p, r, r * 0.8);
        if (model == 1)
        {
            InDisc(p, 0, 0, r * 0.8, Timber, edge);
            InDisc(p, 0, 0, r * 0.5, Palette.Tint(Timber, 18));
        }
        else if (model == 2)
        {
            InPoly(p, InRect(-r * 2.2, -r * 0.55, r * 2.2, r * 0.55), Timber, edge);
            InLine(
                p,
                new[] { new Vector2((float)(-r * 2), 0), new Vector2((float)(r * 2), 0) },
                Palette.Hex("#7a5a38"),
                0.8
            );
        }
        else
        {
            // Backrest bar along local -Y, seat below it.
            InPoly(p, InRect(-r, -r * 0.75, r, r * 0.75), Timber, edge);
            InPoly(p, InRect(-r, -r * 1.05, r, -r * 0.55), Palette.Hex("#6d4e2e"), edge);
            InPoly(
                p,
                InRect(-r * 0.7, -r * 0.45, r * 0.7, r * 0.45),
                Palette.Tint(Timber, 16)
            );
        }
    }

    // --- candle ----------------------------------------------------------------------
    // Tiny warm light: glow halo + brass base disc + flame core. Sits on
    // tables, shelves and wall nooks; the glow pass picks it up through
    // the same emissive accent windows use.
    void DrawCandle(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 3.2 * rand.Range(0.9, 1.2);
        InDisc(p, 0, 0, r * 3.2, new Color(1f, 0.72f, 0.32f, 0.22f));
        InDisc(p, 0, 0, r * 1.7, new Color(1f, 0.76f, 0.38f, 0.35f));
        InDisc(p, 0, 0, r, Palette.Hex("#c8a35c"), Palette.Hex("#6b5324"));
        InDisc(p, 0, (float)(-r * 0.3), r * 0.45, Palette.Hex("#ffe9b0"));
    }

    void DrawFireplace(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 13 * rand.Range(0.9, 1.15);
        var cold = rand.Next() < 0.2;
        var stone = Palette.Mix(Stone, Colors.Black, rand.Next() * 0.15);
        var edge = Palette.Hex("#3e3629");

        ContactShadow(p, r * 1.05, r * 0.75);
        // Warm halo before the masonry.
        InDisc(p, 0, 0, r * 1.15, new Color(1f, 0.69f, 0.31f, 0x22 / 255f));

        InPoly(
            p,
            new[]
            {
                new Vector2((float)-r, (float)(-r * 0.7)),
                new Vector2((float)r, (float)(-r * 0.7)),
                new Vector2((float)r, (float)(r * 0.35)),
                new Vector2((float)(r * 0.6), (float)(r * 0.7)),
                new Vector2((float)(-r * 0.6), (float)(r * 0.7)),
                new Vector2((float)-r, (float)(r * 0.35)),
            },
            stone,
            edge
        );
        InPoly(
            p,
            InRect(-r * 0.6, -r * 0.4, r * 0.6, r * 0.3),
            Palette.Hex("#2e241c"),
            Palette.Hex("#1c1610")
        );
        // Crossed logs.
        var log = Palette.Hex("#66503c");
        InLine(
            p,
            new[] { new Vector2((float)(-r * 0.4), (float)(r * 0.15)), new Vector2((float)(r * 0.4), (float)(-r * 0.1)) },
            log,
            4
        );
        InLine(
            p,
            new[] { new Vector2((float)(-r * 0.4), (float)(-r * 0.1)), new Vector2((float)(r * 0.4), (float)(r * 0.15)) },
            log,
            4
        );
        if (!cold)
        {
            InPoly(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.28), (float)(r * 0.05)),
                    new Vector2((float)(-r * 0.12), (float)(-r * 0.32)),
                    new Vector2(0, (float)(-r * 0.52)),
                    new Vector2((float)(r * 0.14), (float)(-r * 0.2)),
                    new Vector2((float)(r * 0.28), (float)(r * 0.05)),
                },
                Palette.Hex("#e99742"),
                Palette.Hex("#bc6b32")
            );
            InPoly(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.1), (float)(r * 0.05)),
                    new Vector2(0, (float)(-r * 0.24)),
                    new Vector2((float)(r * 0.12), (float)(r * 0.05)),
                },
                Palette.Hex("#ffe4a0"),
                Palette.Hex("#ffe4a0")
            );
        }
        else
        {
            InDisc(p, 0, -r * 0.05, r * 0.3, Palette.Hex("#4a453c"));
        }
        // Mantel seam + the two hearth stones at the shoulders.
        InLine(
            p,
            new[] { new Vector2((float)(-r + 3), (float)(-r * 0.62)), new Vector2((float)(r - 3), (float)(-r * 0.62)) },
            edge,
            1.6
        );
        foreach (var sx in new[] { -0.75, 0.75 })
            InDisc(p, sx * r, r * 0.5, r * 0.16, stone, edge);
    }
}
