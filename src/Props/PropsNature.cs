// Prop painters — nature & wilderness batch. Owns DrawTent,
// DrawCampfire, DrawWell, DrawRuin, DrawGrave, DrawBones, DrawStatue,
// DrawStoneCircle, DrawCrystal, DrawBale, DrawCrate, DrawBarrel.
// Ported from DMmap/src/dm-area/scene/render/scene-props.ts (crate and
// barrel live in scene-renderer.ts, bones in scene-bones.ts).
//
// The TS renderer picks variants via shape.model; PropPlacement has no
// model field, so each kind rolls a seeded variant from Jitter instead —
// the same trick DrawRock uses. Colors follow the woodland palette from
// scene-biomes.appearance() (stone #a4a68e, water #298f99, wood #a37d4c).
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    // Palette constants the TS reads from the biome profile / config.
    static readonly Color WoodT = Palette.Hex("#a37d4c"); // profile.wood
    static readonly Color WaterT = Palette.Hex("#298f99"); // profile.water
    static readonly Color StoneEdge = Palette.Hex("#4a4f46"); // prop ink

    // --- shared local-frame helpers ----------------------------------------
    // (N-prefixed so the sibling batch files can't collide with them.)

    /// <summary>Rotate pts by `a` then translate to (x,y) inside the prop
    /// frame — the TS `ctx.save(); ctx.translate(x,y); ctx.rotate(a)`
    /// pattern used by menhirs, bone pieces and ruin blocks.</summary>
    static Vector2[] NFrame(double x, double y, double a, IEnumerable<Vector2> pts)
    {
        var c = Math.Cos(a);
        var s = Math.Sin(a);
        return pts
            .Select(q => new Vector2(
                (float)(x + q.X * c - q.Y * s),
                (float)(y + q.X * s + q.Y * c)
            ))
            .ToArray();
    }

    /// <summary>Quadratic Bezier sampled to a polyline — TS
    /// `quadraticCurveTo` (bone waists, beast-snout, skull sutures).</summary>
    static Vector2[] NQuad(Vector2 a, Vector2 b, Vector2 c, int seg = 7)
    {
        var pts = new Vector2[seg + 1];
        for (var i = 0; i <= seg; i++)
        {
            var t = i / (double)seg;
            var u = 1 - t;
            pts[i] = new Vector2(
                (float)(u * u * a.X + 2 * u * t * b.X + t * t * c.X),
                (float)(u * u * a.Y + 2 * u * t * b.Y + t * t * c.Y)
            );
        }
        return pts;
    }

    /// <summary>Partial ellipse arc sampled to a polyline (cranial rims,
    /// ruin wall arcs).</summary>
    static Vector2[] NArc(
        double cx,
        double cy,
        double rx,
        double ry,
        double a0,
        double a1,
        int seg = 9
    )
    {
        var pts = new Vector2[seg + 1];
        for (var i = 0; i <= seg; i++)
        {
            var t = a0 + (a1 - a0) * i / seg;
            pts[i] = new Vector2(
                (float)(cx + Math.Cos(t) * rx),
                (float)(cy + Math.Sin(t) * ry)
            );
        }
        return pts;
    }

    /// <summary>Open prop-local polyline stroked with prop-scaled width —
    /// the TS `drawLine` port.</summary>
    void NLine(PropPlacement p, IEnumerable<Vector2> local, Color color, double width) =>
        DrawPolyline(
            Xform(p, local),
            color,
            (float)Math.Max(0.6, width * p.Scale),
            true
        );

    /// <summary>TS prop.model semantics: an explicit p.Model wins
    /// (wrapped into range); Model&lt;0 falls back to the seeded roll.</summary>
    static int PropModel(PropPlacement p, int count, double roll)
    {
        if (p.Model >= 0)
            return ((p.Model % count) + count) % count;
        return Math.Min(count - 1, (int)(roll * count));
    }

    void DrawTent(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        // Models: 0 cream ridge tent, 1 striped ridge tent, 2 striped
        // pavilion — the reference canvas is always cream/white based
        // with ink outlines, never flat brown.
        var model = PropModel(p, 3, rand.Next());
        var r = 14 * rand.Range(0.85, 1.15);
        // Sun-bleached canvas with a per-instance nudge.
        var cloth = Palette.Mix(
            Palette.Hex("#e9dcbd"),
            rand.Next() < 0.5 ? Palette.Hex("#f4e9cd") : Palette.Hex("#d9c69c"),
            rand.Next() * 0.5
        );
        var ink = Palette.Hex("#463b2a");
        var seam = Palette.Mix(cloth, ink, 0.45);
        var pole = Palette.Hex("#6a5636");
        var rope = Palette.Hex("#7a6a48");
        // Stripe dye: madder red or woad blue (seeded per tent).
        var dye = rand.Next() < 0.55 ? Palette.Hex("#b3453a") : Palette.Hex("#3f6d9a");

        ContactShadow(p, r * 1.15, r * 0.9);

        if (model == 2)
        {
            // Pavilion — a conical striped tent read obliquely as a tall
            // triangle: stripes radiate from the apex, inked ribs between
            // them, a pole tip sticking out of the peak.
            var apex = new Vector2(0, (float)(-r * 1.05));
            var bl = new Vector2((float)(-r * 0.95), (float)(r * 0.62));
            var br = new Vector2((float)(r * 0.95), (float)(r * 0.62));
            var body = Xform(p, new[] { apex, br, bl });
            Fill(body, cloth);
            // Six base segments — alternating dyed gores.
            const int gores = 6;
            for (var i = 0; i < gores; i++)
            {
                if (i % 2 == 0)
                    continue;
                var a0 = bl + (br - bl) * (i / (float)gores);
                var a1 = bl + (br - bl) * ((i + 1) / (float)gores);
                foreach (
                    var piece in Geometry2D.IntersectPolygons(
                        body,
                        Xform(p, new[] { apex, a1, a0 })
                    )
                )
                    Fill(piece, dye);
            }
            // Inked ribs along the gore boundaries.
            for (var i = 0; i <= gores; i++)
                NLine(
                    p,
                    new[] { apex, bl + (br - bl) * (i / (float)gores) },
                    seam,
                    1.1
                );
            Stroke(body, ink, (float)Math.Max(0.9, 1.5 * p.Scale));
            // Center pole tip + pennant dot.
            NLine(
                p,
                new[] { apex, new Vector2(0, (float)(-r * 1.3)) },
                pole,
                2
            );
            // Door slit at the base center.
            Fill(
                Xform(
                    p,
                    new[]
                    {
                        new Vector2((float)(-r * 0.16), (float)(r * 0.62)),
                        new Vector2(0, (float)(r * 0.18)),
                        new Vector2((float)(r * 0.16), (float)(r * 0.62)),
                    }
                ),
                Palette.Hex("#3a3226")
            );
            foreach (var side in new[] { -1, 1 })
            {
                // Guy ropes off the base corners, pegged out.
                var corner = side < 0 ? bl : br;
                var peg = new Vector2(corner.X * 1.25f, corner.Y + (float)(r * 0.3));
                NLine(p, new[] { corner, peg }, rope, 1);
                DrawCircle(
                    Xform(p, peg),
                    (float)Math.Max(0.9, 1.1 * p.Scale),
                    ink
                );
            }
            return;
        }

        {
            // Ridge tent — prism footprint: two triangular end faces
            // (north/south apexes) joined by the ridge line.
            var bodyPts = new[]
            {
                new Vector2((float)-r, (float)(-r * 0.6)),
                new Vector2(0, (float)-r),
                new Vector2((float)r, (float)(-r * 0.6)),
                new Vector2((float)r, (float)(r * 0.7)),
                new Vector2(0, (float)r),
                new Vector2((float)-r, (float)(r * 0.7)),
            };
            var body = Xform(p, bodyPts);
            Fill(body, cloth);

            if (model == 1)
                // Striped canvas — dye bands running across the ridge,
                // clipped to the body (same trick as the TS stripes).
                for (var band = 0; band < 3; band++)
                {
                    var y0 = -r * 0.95 + band * r * 0.62;
                    var quad = new[]
                    {
                        new Vector2((float)(-r * 1.15), (float)y0),
                        new Vector2((float)(r * 1.15), (float)y0),
                        new Vector2((float)(r * 1.15), (float)(y0 + r * 0.32)),
                        new Vector2((float)(-r * 1.15), (float)(y0 + r * 0.32)),
                    };
                    foreach (
                        var piece in Geometry2D.IntersectPolygons(body, Xform(p, quad))
                    )
                        Fill(piece, dye);
                }

            // Shaded west slope + lit east slope — the cloth volume.
            Fill(
                Xform(
                    p,
                    new[]
                    {
                        new Vector2((float)-r, (float)(-r * 0.6)),
                        new Vector2(0, (float)-r),
                        new Vector2(0, (float)r),
                        new Vector2((float)-r, (float)(r * 0.7)),
                    }
                ),
                new Color(0f, 0f, 0f, 0.07f)
            );
            Fill(
                Xform(
                    p,
                    new[]
                    {
                        new Vector2(0, (float)-r),
                        new Vector2((float)r, (float)(-r * 0.6)),
                        new Vector2((float)r, (float)(r * 0.7)),
                        new Vector2(0, (float)r),
                    }
                ),
                new Color(1f, 1f, 1f, 0.13f)
            );

            // Panel seams across both slopes, perpendicular to the ridge.
            foreach (var t in new[] { 0.3, 0.55, 0.8 })
            {
                var ry = -r + 2 * r * t;
                foreach (var side in new[] { -1, 1 })
                {
                    var ey = -r * 0.6 + r * 1.3 * t;
                    NLine(
                        p,
                        new[]
                        {
                            new Vector2(0, (float)ry),
                            new Vector2((float)(side * r), (float)ey),
                        },
                        seam,
                        1
                    );
                }
            }
            // Ridge seam.
            NLine(
                p,
                new[] { new Vector2(0, (float)-r), new Vector2(0, (float)r) },
                seam,
                1.6
            );
            Stroke(body, ink, (float)Math.Max(0.9, 1.5 * p.Scale));

            // Door — a dark triangular opening in the south end face
            // with one flap tied back.
            Fill(
                Xform(
                    p,
                    new[]
                    {
                        new Vector2((float)(-r * 0.3), (float)(r * 0.72)),
                        new Vector2(0, (float)(r * 0.1)),
                        new Vector2((float)(r * 0.3), (float)(r * 0.72)),
                    }
                ),
                Palette.Hex("#3a3226")
            );
            NLine(
                p,
                new[]
                {
                    new Vector2(0, (float)(r * 0.1)),
                    new Vector2((float)(r * 0.3), (float)(r * 0.72)),
                },
                ink,
                1.2
            );

            // Ridge poles poking past both apexes.
            foreach (var side in new[] { -1, 1 })
                NLine(
                    p,
                    new[]
                    {
                        new Vector2(0, (float)(side * r)),
                        new Vector2(0, (float)(side * r * 1.16)),
                    },
                    pole,
                    2.2
                );
            // Guy ropes off the eave corners, pegged into the ground.
            foreach (var side in new[] { -1, 1 })
                foreach (var (ey, py) in new[] { (-0.55, -0.85), (0.65, 0.95) })
                {
                    var from = new Vector2((float)(side * r), (float)(r * ey));
                    var peg = new Vector2((float)(side * r * 1.3), (float)(r * py));
                    NLine(p, new[] { from, peg }, rope, 1);
                    DrawCircle(
                        Xform(p, peg),
                        (float)Math.Max(0.9, 1.0 * p.Scale),
                        ink
                    );
                }
        }
    }

    void DrawCampfire(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        // Models: 0 burning star-laid fire, 1 cooking tripod, 2 cold ash.
        var model = PropModel(p, 3, rand.Next());
        var r = 10 * rand.Range(0.85, 1.2);
        var stone = Palette.Mix(Stone, Palette.Hex("#5a5c48"), rand.Next() * 0.3);
        var cold = model == 2;
        var tripod = model == 1;

        ContactShadow(p, r * 1.1, r * 0.95);

        // Warm glow bed first — the stones and logs sit inside the light.
        if (!cold)
        {
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 1.45, r * 1.35, 0, 14)),
                new Color(0.95f, 0.62f, 0.22f, 0.12f)
            );
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.9, r * 0.82, 0, 12)),
                new Color(0.98f, 0.68f, 0.25f, 0.16f)
            );
        }

        // Stone ring — small chunky rocks with ink outlines, kept low-key
        // so the logs and embers carry the read.
        for (var i = 0; i < 9; i++)
        {
            var a = i / 9.0 * Math.PI * 2 + rand.Next() * 0.2;
            var dist = r * (0.88 + rand.Next() * 0.1);
            var w = Xform(
                p,
                Poly.Ellipse(
                    Math.Cos(a) * dist,
                    Math.Sin(a) * dist,
                    r * 0.14,
                    r * 0.12,
                    a,
                    7
                )
            );
            var tone = Palette.Mix(stone, Colors.Black, 0.1 + rand.Next() * 0.2);
            Fill(w, tone);
            Stroke(w, Palette.Hex("#42463c"), (float)Math.Max(0.7, 1.1 * p.Scale));
        }

        // Star-laid logs radiating from the ember bed — the reference
        // campfire read: dark charred inner ends, warm wood outside.
        var logEdge = Palette.Hex("#3b2c1c");
        var logWood = Palette.Hex("#7d5a34");
        var logCount = cold ? 4 : 5;
        for (var i = 0; i < logCount; i++)
        {
            var a = i / (double)logCount * Math.PI * 2 + 0.5 + rand.Next() * 0.3;
            var inner = new Vector2(
                (float)(Math.Cos(a) * r * (cold ? 0.05 : 0.1)),
                (float)(Math.Sin(a) * r * (cold ? 0.05 : 0.1))
            );
            var outer = new Vector2(
                (float)(Math.Cos(a) * r * 0.74),
                (float)(Math.Sin(a) * r * 0.74)
            );
            var pts = new[] { inner, outer };
            NLine(p, pts, logEdge, 6.4);
            NLine(p, pts, cold ? Palette.Hex("#4a3a28") : logWood, 4.2);
            if (!cold)
                // Charred dark tip toward the embers.
                NLine(
                    p,
                    new[] { inner, (inner + (outer - inner) * 0.45f) },
                    Palette.Hex("#2e2318"),
                    3.4
                );
        }

        if (cold)
        {
            // Burned-out fire (TS model 2): ash disc + charred stubs.
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.55, r * 0.5, 0, 12)),
                Palette.Hex("#4a453c")
            );
            for (var i = 0; i < 4; i++)
            {
                var a = i * 1.7 + 0.4;
                NLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(Math.Cos(a) * r * 0.08), (float)(Math.Sin(a) * r * 0.08)),
                        new Vector2((float)(Math.Cos(a) * r * 0.5), (float)(Math.Sin(a) * r * 0.5)),
                    },
                    Palette.Hex("#2e2b26"),
                    3.4
                );
            }
        }
        else
        {
            // Bright ember core — the clear hot spot the references read
            // by: layered orange discs + a white-yellow heart, then a
            // small flame lick on top.
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.5, r * 0.45, 0, 12)),
                Palette.Hex("#c95f26")
            );
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.36, r * 0.32, 0, 11)),
                Palette.Hex("#e99742")
            );
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.2, r * 0.18, 0, 10)),
                Palette.Hex("#ffe4a0")
            );
            // Loose ember sparks around the core.
            for (var i = 0; i < 5; i++)
            {
                var a = i * 1.35 + 0.6;
                DrawCircle(
                    Xform(
                        p,
                        new Vector2(
                            (float)(Math.Cos(a) * r * rand.Range(0.3, 0.55)),
                            (float)(Math.Sin(a) * r * rand.Range(0.3, 0.55))
                        )
                    ),
                    (float)Math.Max(0.7, r * 0.045 * p.Scale),
                    Palette.Hex("#ffcf6e")
                );
            }
            var flame = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.22), (float)(r * 0.1)),
                    new Vector2((float)(-r * 0.1), (float)(-r * 0.3)),
                    new Vector2(0, (float)(-r * 0.6)),
                    new Vector2((float)(r * 0.1), (float)(-r * 0.22)),
                    new Vector2((float)(r * 0.22), (float)(r * 0.1)),
                }
            );
            Fill(flame, new Color(0.95f, 0.66f, 0.28f, 0.85f));
            Stroke(flame, Palette.Hex("#bc6b32"), (float)Math.Max(0.6, 1.0 * p.Scale));
            if (tripod)
            {
                // Cooking tripod (TS model 1): three legs to the apex, a
                // kettle hanging at the center.
                for (var i = 0; i < 3; i++)
                {
                    var a = i / 3.0 * Math.PI * 2 + 0.4;
                    NLine(
                        p,
                        new[]
                        {
                            new Vector2(
                                (float)(Math.Cos(a) * r * 0.85),
                                (float)(Math.Sin(a) * r * 0.85)
                            ),
                            new Vector2(0, (float)(-r * 0.08)),
                        },
                        Palette.Hex("#4a4234"),
                        1.8
                    );
                }
                var pot = Xform(p, Poly.Ellipse(0, -r * 0.02, r * 0.17, r * 0.16, 0, 9));
                Fill(pot, Palette.Hex("#2e2b26"));
                Stroke(pot, Palette.Hex("#1c1a16"), (float)Math.Max(0.6, 0.9 * p.Scale));
            }
        }
    }

    void DrawWell(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 12 * rand.Range(0.9, 1.15);
        var stone = Palette.Mix(Stone, Colors.Black, rand.Next() * 0.12);

        ContactShadow(p, r * 1.1, r * 0.95);

        var ring = Xform(p, Poly.Ellipse(0, 0, r, r, 0, 16));
        Fill(ring, stone);
        Stroke(ring, Palette.Hex("#4f5349"), (float)Math.Max(0.8, 1.2 * p.Scale));
        var water = Xform(p, Poly.Ellipse(0, 0, r * 0.62, r * 0.62, 0, 14));
        Fill(water, WaterT);
        Stroke(water, Palette.Hex("#4b554e"), (float)Math.Max(0.6, 1.0 * p.Scale));
        // Mortar joints radiating across the rim.
        for (var i = 0; i < 10; i++)
        {
            var a = i / 10.0 * Math.PI * 2;
            NLine(
                p,
                new[]
                {
                    new Vector2((float)(Math.Cos(a) * r * 0.7), (float)(Math.Sin(a) * r * 0.7)),
                    new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)),
                },
                Palette.Hex("#626557"),
                1
            );
        }

        var v = rand.Next();
        if (v < 0.45)
        {
            // Windlass beam + crank drum.
            NLine(
                p,
                new[] { new Vector2((float)(-r * 0.9), 0), new Vector2((float)(r * 0.9), 0) },
                WoodT,
                5
            );
            DrawCircle(Xform(p, Vector2.Zero), (float)(r * 0.16 * p.Scale), Palette.Hex("#d2bc8a"));
        }
        else if (v < 0.8)
        {
            // Wooden canopy on two posts (TS model 1).
            foreach (var side in new[] { -1, 1 })
                NLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(side * r * 0.82), (float)(-r * 0.55)),
                        new Vector2((float)(side * r * 0.82), (float)(r * 0.35)),
                    },
                    WoodT,
                    3
                );
            var roof = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.95), (float)(-r * 0.5)),
                    new Vector2(0, (float)(-r * 0.78)),
                    new Vector2((float)(r * 0.95), (float)(-r * 0.5)),
                    new Vector2((float)(r * 0.85), (float)(-r * 0.1)),
                    new Vector2(0, (float)(-r * 0.4)),
                    new Vector2((float)(-r * 0.85), (float)(-r * 0.1)),
                }
            );
            Fill(roof, WoodT);
            Stroke(roof, Palette.Hex("#4d3f2c"), (float)Math.Max(0.8, 1.2 * p.Scale));
            NLine(
                p,
                new[] { new Vector2(0, (float)(-r * 0.78)), new Vector2(0, (float)(-r * 0.4)) },
                Palette.Hex("#4d3f2c"),
                1.4
            );
        }
        // v ≥ 0.8: bare stone ring (TS model 2).
    }

    void DrawRuin(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 18 * rand.Range(0.85, 1.15);
        var color = Palette.Mix(
            Stone,
            rand.Next() < 0.5 ? Colors.White : Palette.Hex("#5a5c48"),
            rand.Next() * 0.18
        );

        ContactShadow(p, r * 1.05, r * 0.9);

        var v = rand.Next();
        if (v < 0.4)
        {
            // Broken wall arc (TS model 0): a crumbled arc of wall, a few
            // displaced blocks inside, scattered chips.
            var arc = NArc(0, 0, r * 0.85, r * 0.85, -Math.PI * 0.9, -Math.PI * 0.9 + Math.PI * 1.15, 10);
            NLine(p, arc, StoneEdge, r * 0.3);
            NLine(p, arc, color, Math.Max(2, r * 0.3 - 4));
            foreach (var t in new[] { 0.15, 0.5, 0.8 })
            {
                var a = -Math.PI * 0.9 + t * Math.PI * 1.15;
                var block = NFrame(
                    Math.Cos(a) * r * 0.6,
                    Math.Sin(a) * r * 0.6,
                    a + rand.Next() * 0.8,
                    new[]
                    {
                        new Vector2((float)(-r * 0.16), (float)(-r * 0.1)),
                        new Vector2((float)(r * 0.16), (float)(-r * 0.1)),
                        new Vector2((float)(r * 0.16), (float)(r * 0.1)),
                        new Vector2((float)(-r * 0.16), (float)(r * 0.1)),
                    }
                );
                var w = Xform(p, block);
                Fill(w, color);
                Stroke(w, StoneEdge, (float)Math.Max(0.6, 1.0 * p.Scale));
            }
            for (var i = 0; i < 4; i++)
            {
                var cx = (rand.Next() - 0.5) * r * 1.1;
                var cy = (rand.Next() - 0.5) * r * 1.1;
                var w = Xform(p, Poly.Ellipse(cx, cy, r * 0.09, r * 0.08, rand.Next(), 7));
                Fill(w, color);
                Stroke(w, StoneEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
            }
        }
        else if (v < 0.75)
        {
            // Collapsed round tower (TS model 1): jagged ring, hollow dark
            // interior, fallen chunks around the foot.
            var pts = new Vector2[16];
            for (var i = 0; i < 16; i++)
            {
                var a = i / 16.0 * Math.PI * 2;
                var jag = i % 3 == 0 ? 0.6 : 0.88 + rand.Next() * 0.1;
                pts[i] = new Vector2((float)(Math.Cos(a) * r * jag), (float)(Math.Sin(a) * r * jag));
            }
            var ringW = Xform(p, pts);
            Fill(ringW, color);
            Stroke(ringW, StoneEdge, (float)Math.Max(0.8, 1.2 * p.Scale));
            var hole = Xform(p, Poly.Ellipse(0, 0, r * 0.5, r * 0.5, 0, 14));
            Fill(hole, Palette.Hex("#3a3630"));
            Stroke(hole, Palette.Hex("#2c2820"), (float)Math.Max(0.6, 1.0 * p.Scale));
            for (var i = 0; i < 5; i++)
            {
                var a = i * 1.9 + 0.7;
                var chunk = new[]
                {
                    new Vector2((float)(Math.Cos(a) * r * 0.75 - 6), (float)(Math.Sin(a) * r * 0.75)),
                    new Vector2((float)(Math.Cos(a) * r * 0.95), (float)(Math.Sin(a) * r * 0.95 - 4)),
                    new Vector2((float)(Math.Cos(a) * r * 0.95 + 7), (float)(Math.Sin(a) * r * 0.95 + 5)),
                };
                var w = Xform(p, chunk);
                Fill(w, color);
                Stroke(w, StoneEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
            }
        }
        else
        {
            // Rubble heap (TS model 2): small jagged stones spilled around.
            for (var i = 0; i < 7; i++)
            {
                var a = i * 2.1 + rand.Next() * 0.8;
                var px = Math.Cos(a) * r * 0.45 * rand.Next();
                var py = Math.Sin(a) * r * 0.4 * rand.Next();
                var sides = 5 + i % 3;
                var pts = new Vector2[sides];
                for (var k = 0; k < sides; k++)
                {
                    var aa = k / (double)sides * Math.PI * 2;
                    var rr = r * (0.12 + rand.Next() * 0.12);
                    pts[k] = new Vector2((float)(px + Math.Cos(aa) * rr), (float)(py + Math.Sin(aa) * rr));
                }
                var w = Xform(p, pts);
                Fill(w, i % 2 == 1 ? color : Palette.Hex("#8a8577"));
                Stroke(w, StoneEdge, (float)Math.Max(0.6, 1.0 * p.Scale));
            }
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.5, r * 0.45, 0, 12)),
                new Color(0f, 0f, 0f, 0.06f)
            );
        }
        // Woodland moss creeping over the stones (TS biome dressing).
        for (var i = 0; i < 4; i++)
            NLine(
                p,
                new[]
                {
                    new Vector2((float)((rand.Next() - 0.5) * r), (float)((rand.Next() - 0.5) * r)),
                    new Vector2((float)((rand.Next() - 0.5) * r), (float)((rand.Next() - 0.5) * r)),
                },
                new Color(0.33f, 0.49f, 0.28f, 0.4f),
                2
            );
    }

    void DrawGrave(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 10 * rand.Range(0.9, 1.2);
        var color = Palette.Mix(Stone, Colors.Black, rand.Next() * 0.15);
        var earth = Palette.Hex("#6b5844");
        var grass = new Color(0.33f, 0.49f, 0.28f, 0.65f);

        ContactShadow(p, r * 1.0, r * 0.8);

        var v = rand.Next();
        if (v < 0.2)
        {
            // Flat slab (TS model 2).
            var slab = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.85), (float)(-r * 0.6)),
                    new Vector2((float)(r * 0.85), (float)(-r * 0.6)),
                    new Vector2((float)(r * 0.85), (float)(r * 0.6)),
                    new Vector2((float)(-r * 0.85), (float)(r * 0.6)),
                }
            );
            Fill(slab, color);
            Stroke(slab, StoneEdge, (float)Math.Max(0.8, 1.2 * p.Scale));
            var inner = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.65), (float)(-r * 0.42)),
                    new Vector2((float)(r * 0.65), (float)(-r * 0.42)),
                    new Vector2((float)(r * 0.65), (float)(r * 0.42)),
                    new Vector2((float)(-r * 0.65), (float)(r * 0.42)),
                }
            );
            Fill(inner, new Color(1f, 1f, 1f, 0.09f));
            Stroke(inner, new Color(0.29f, 0.31f, 0.27f, 0.55f), (float)Math.Max(0.6, 0.9 * p.Scale));
            NLine(
                p,
                new[] { new Vector2(0, (float)(-r * 0.42)), new Vector2(0, (float)(r * 0.42)) },
                new Color(0.29f, 0.31f, 0.27f, 0.65f),
                1.2
            );
        }
        else if (v < 0.55)
        {
            // Stone cross on a mound (TS model 1).
            Fill(Xform(p, Poly.Ellipse(0, r * 0.2, r * 0.8, r * 0.5, 0, 14)), earth);
            var cross = new[]
            {
                new Vector2((float)(-r * 0.12), (float)(-r * 0.9)),
                new Vector2((float)(r * 0.12), (float)(-r * 0.9)),
                new Vector2((float)(r * 0.12), (float)(-r * 0.3)),
                new Vector2((float)(r * 0.42), (float)(-r * 0.3)),
                new Vector2((float)(r * 0.42), (float)(-r * 0.06)),
                new Vector2((float)(r * 0.12), (float)(-r * 0.06)),
                new Vector2((float)(r * 0.12), (float)(r * 0.3)),
                new Vector2((float)(-r * 0.12), (float)(r * 0.3)),
                new Vector2((float)(-r * 0.12), (float)(-r * 0.06)),
                new Vector2((float)(-r * 0.42), (float)(-r * 0.06)),
                new Vector2((float)(-r * 0.42), (float)(-r * 0.3)),
                new Vector2((float)(-r * 0.12), (float)(-r * 0.3)),
            };
            var cw = Xform(p, cross);
            Fill(cw, color);
            Stroke(cw, StoneEdge, (float)Math.Max(0.8, 1.2 * p.Scale));
            NLine(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.35), (float)(r * 0.5)),
                    new Vector2((float)(-r * 0.2), (float)(r * 0.35)),
                    new Vector2(0, (float)(r * 0.55)),
                    new Vector2((float)(r * 0.25), (float)(r * 0.4)),
                },
                grass,
                1.4
            );
        }
        else
        {
            // Headstone on an earth mound (TS model 0).
            Fill(Xform(p, Poly.Ellipse(0, r * 0.25, r * 0.85, r * 0.5, 0, 14)), earth);
            var stone = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.4), (float)(-r * 0.75)),
                    new Vector2((float)(r * 0.4), (float)(-r * 0.75)),
                    new Vector2((float)(r * 0.4), (float)(-r * 0.1)),
                    new Vector2((float)(-r * 0.4), (float)(-r * 0.1)),
                }
            );
            Fill(stone, color);
            Stroke(stone, StoneEdge, (float)Math.Max(0.8, 1.2 * p.Scale));
            var cap = Xform(p, Poly.Ellipse(0, -r * 0.75, r * 0.4, r * 0.4, 0, 12));
            Fill(cap, color);
            Stroke(cap, StoneEdge, (float)Math.Max(0.8, 1.2 * p.Scale));
            // Engraved cross mark.
            var mark = new Color(0.29f, 0.31f, 0.27f, 0.65f);
            NLine(
                p,
                new[] { new Vector2((float)(-r * 0.2), (float)(-r * 0.6)), new Vector2((float)(r * 0.2), (float)(-r * 0.6)) },
                mark,
                1.2
            );
            NLine(
                p,
                new[] { new Vector2(0, (float)(-r * 0.6)), new Vector2(0, (float)(-r * 0.3)) },
                mark,
                1.2
            );
            for (var i = 0; i < 3; i++)
                NLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(-r * 0.5 + i * r * 0.5), (float)(r * 0.45)),
                        new Vector2((float)(-r * 0.45 + i * r * 0.5), (float)(r * 0.3)),
                    },
                    grass,
                    1.4
                );
        }
    }

    void DrawStatue(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 10 * rand.Range(0.9, 1.2);
        var color = Palette.Mix(
            Stone,
            rand.Next() < 0.5 ? Colors.White : Palette.Hex("#5a5c48"),
            rand.Next() * 0.18
        );
        var plinth = Palette.Mix(Stone, Colors.Black, 0.08);

        ContactShadow(p, r * 0.95, r * 0.85);

        var v = rand.Next();
        if (v < 0.3)
        {
            // Menhir (TS model 2): bare upright stone with a carved rune.
            var men = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.3), (float)(-r * 0.85)),
                    new Vector2((float)(r * 0.25), (float)(-r * 0.9)),
                    new Vector2((float)(r * 0.38), (float)(r * 0.55)),
                    new Vector2((float)(r * 0.1), (float)(r * 0.75)),
                    new Vector2((float)(-r * 0.35), (float)(r * 0.6)),
                }
            );
            Fill(men, color);
            Stroke(men, StoneEdge, (float)Math.Max(0.8, 1.2 * p.Scale));
            NLine(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.12), (float)(-r * 0.45)),
                    new Vector2((float)(r * 0.1), (float)(-r * 0.2)),
                    new Vector2((float)(-r * 0.05), (float)(r * 0.1)),
                },
                new Color(0.29f, 0.31f, 0.27f, 0.65f),
                1.6
            );
            NLine(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.25), (float)(r * 0.3)),
                    new Vector2((float)(r * 0.3), (float)(-r * 0.3)),
                },
                new Color(1f, 1f, 1f, 0.13f),
                2
            );
        }
        else
        {
            // Shared square plinth.
            var base_ = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.65), (float)(-r * 0.65)),
                    new Vector2((float)(r * 0.65), (float)(-r * 0.65)),
                    new Vector2((float)(r * 0.65), (float)(r * 0.65)),
                    new Vector2((float)(-r * 0.65), (float)(r * 0.65)),
                }
            );
            Fill(base_, plinth);
            Stroke(base_, Palette.Hex("#3b3226"), (float)Math.Max(0.8, 1.2 * p.Scale));
            if (v < 0.65)
            {
                // Obelisk + pyramid cap (TS model 1).
                var ob = Xform(
                    p,
                    new[]
                    {
                        new Vector2((float)(-r * 0.2), (float)(-r * 0.75)),
                        new Vector2((float)(r * 0.2), (float)(-r * 0.75)),
                        new Vector2((float)(r * 0.34), (float)(r * 0.4)),
                        new Vector2((float)(-r * 0.34), (float)(r * 0.4)),
                    }
                );
                Fill(ob, color);
                Stroke(ob, StoneEdge, (float)Math.Max(0.8, 1.1 * p.Scale));
                var cap = Xform(
                    p,
                    new[]
                    {
                        new Vector2((float)(-r * 0.14), (float)(-r * 0.9)),
                        new Vector2((float)(r * 0.14), (float)(-r * 0.9)),
                        new Vector2(0, (float)(-r * 0.75)),
                    }
                );
                Fill(cap, color);
                Stroke(cap, StoneEdge, (float)Math.Max(0.6, 1.0 * p.Scale));
                // Sun-catching SE arris.
                NLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(r * 0.06), (float)(-r * 0.6)),
                        new Vector2((float)(r * 0.2), (float)(r * 0.25)),
                    },
                    new Color(1f, 1f, 1f, 0.18f),
                    1.6
                );
            }
            else
            {
                // Robed figure with a head (TS model 0).
                var fig = Xform(
                    p,
                    new[]
                    {
                        new Vector2(0, (float)(-r * 0.6)),
                        new Vector2((float)(-r * 0.45), (float)(r * 0.3)),
                        new Vector2((float)(r * 0.45), (float)(r * 0.3)),
                    }
                );
                Fill(fig, color);
                Stroke(fig, StoneEdge, (float)Math.Max(0.8, 1.1 * p.Scale));
                var head = Xform(p, Poly.Ellipse(0, -r * 0.4, r * 0.25, r * 0.25, 0, 10));
                Fill(head, color);
                Stroke(head, Palette.Hex("#52574f"), (float)Math.Max(0.6, 1.0 * p.Scale));
            }
        }
    }

    void DrawStoneCircle(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 20 * rand.Range(0.85, 1.15);
        var stone = Palette.Mix(Stone, Colors.Black, rand.Next() * 0.12);

        ContactShadow(p, r * 1.0, r * 0.9);

        // One standing stone: a leaning quad tangent to the circle + a
        // sun-edge highlight — the TS `menhir()` helper.
        void Menhir(double a, double dist, double s)
        {
            var quad = NFrame(
                Math.Cos(a) * dist,
                Math.Sin(a) * dist,
                a + Math.PI / 2,
                new[]
                {
                    new Vector2((float)(-r * 0.14 * s), (float)(-r * 0.2 * s)),
                    new Vector2((float)(r * 0.12 * s), (float)(-r * 0.24 * s)),
                    new Vector2((float)(r * 0.15 * s), (float)(r * 0.2 * s)),
                    new Vector2((float)(-r * 0.12 * s), (float)(r * 0.18 * s)),
                }
            );
            var w = Xform(p, quad);
            Fill(w, stone);
            Stroke(w, StoneEdge, (float)Math.Max(0.6, 1.0 * p.Scale));
            var lit = NFrame(
                Math.Cos(a) * dist,
                Math.Sin(a) * dist,
                a + Math.PI / 2,
                new[]
                {
                    new Vector2((float)(-r * 0.06 * s), (float)(-r * 0.12 * s)),
                    new Vector2((float)(r * 0.05 * s), (float)(r * 0.1 * s)),
                }
            );
            NLine(p, lit, new Color(1f, 1f, 1f, 0.2f), 1.2);
        }

        var v = rand.Next();
        if (v < 0.45)
        {
            // Ring of seven menhirs (TS model 0).
            for (var i = 0; i < 7; i++)
                Menhir(i / 7.0 * Math.PI * 2 + 0.2, r * 0.82, 1);
            Fill(
                Xform(p, Poly.Ellipse(0, 0, r * 0.35, r * 0.35, 0, 12)),
                new Color(0f, 0f, 0f, 0.07f)
            );
        }
        else if (v < 0.75)
        {
            // Stone spiral coiling into the center (TS model 1).
            for (var i = 0; i < 11; i++)
                Menhir(i * 0.92 + 0.3, r * (0.12 + i * 0.066), 0.55 + i % 3 * 0.12);
            var hub = Xform(p, Poly.Ellipse(0, 0, r * 0.14, r * 0.14, 0, 9));
            Fill(hub, stone);
            Stroke(hub, StoneEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
        }
        else
        {
            // Cairn — piled stones with a cap (TS model 2).
            for (var i = 0; i < 8; i++)
            {
                var a = i * 2.2 + p.Seed;
                var w = Xform(
                    p,
                    Poly.Ellipse(
                        Math.Cos(a) * r * 0.42,
                        Math.Sin(a) * r * 0.38,
                        r * 0.22,
                        r * 0.2,
                        a,
                        8
                    )
                );
                Fill(w, Palette.Mix(stone, Colors.Black, rand.Next() * 0.15));
                Stroke(w, StoneEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
            }
            for (var i = 0; i < 4; i++)
            {
                var a = i * 2.4 + 0.8;
                var w = Xform(
                    p,
                    Poly.Ellipse(
                        Math.Cos(a) * r * 0.2,
                        Math.Sin(a) * r * 0.18,
                        r * 0.18,
                        r * 0.16,
                        a,
                        8
                    )
                );
                Fill(w, Palette.Mix(stone, Colors.White, 0.08));
                Stroke(w, StoneEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
            }
            var cap = Xform(p, Poly.Ellipse(0, -r * 0.05, r * 0.16, r * 0.15, 0, 8));
            Fill(cap, Palette.Mix(stone, Colors.White, 0.12));
            Stroke(cap, StoneEdge, (float)Math.Max(0.6, 0.9 * p.Scale));
        }
    }

    void DrawCrystal(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 11 * rand.Range(0.9, 1.25);
        var color = Palette.Mix(
            Palette.Hex("#a7c3cf"),
            rand.Next() < 0.5 ? Palette.Hex("#83d4cd") : Palette.Hex("#c8b8d8"),
            rand.Next() * 0.4
        );
        var edge = Palette.Hex("#394654");

        ContactShadow(p, r * 0.95, r * 0.8);

        // The TS `crystal()` shard: upright five-sided shard + a pale NE
        // facet. Three clustered shards like the rock/crystal branch.
        void Shard(double cx, double cy, double size)
        {
            var body = Xform(
                p,
                new[]
                {
                    new Vector2((float)(cx - size * 0.35), (float)(cy + size * 0.4)),
                    new Vector2((float)(cx - size * 0.45), (float)(cy - size * 0.2)),
                    new Vector2((float)cx, (float)(cy - size)),
                    new Vector2((float)(cx + size * 0.45), (float)(cy - size * 0.1)),
                    new Vector2((float)(cx + size * 0.3), (float)(cy + size * 0.45)),
                }
            );
            Fill(body, color);
            Stroke(body, edge, (float)Math.Max(0.6, 1.0 * p.Scale));
            var facet = Xform(
                p,
                new[]
                {
                    new Vector2((float)cx, (float)(cy - size)),
                    new Vector2((float)cx, (float)(cy + size * 0.3)),
                    new Vector2((float)(cx + size * 0.45), (float)(cy - size * 0.1)),
                }
            );
            Fill(facet, new Color(0.82f, 0.93f, 0.92f, 0.53f));
            Stroke(facet, Palette.Hex("#7fb7bc"), (float)Math.Max(0.6, 0.9 * p.Scale));
        }

        Shard(-r * 0.35, r * 0.2, r * 0.7);
        Shard(r * 0.35, r * 0.3, r * 0.65);
        Shard(0, -r * 0.15, r);
    }

    void DrawBale(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 10 * rand.Range(0.85, 1.2);
        var straw = Palette.Mix(
            Palette.Hex("#c9a94e"),
            rand.Next() < 0.5 ? Palette.Hex("#b8934a") : Palette.Hex("#dcc06a"),
            rand.Next() * 0.45
        );

        ContactShadow(p, r * 1.0, r * 0.85);

        if (rand.Next() < 0.22)
        {
            // Square bale (TS model 2).
            var sq = Xform(
                p,
                new[]
                {
                    new Vector2((float)(-r * 0.8), (float)(-r * 0.6)),
                    new Vector2((float)(r * 0.8), (float)(-r * 0.6)),
                    new Vector2((float)(r * 0.8), (float)(r * 0.6)),
                    new Vector2((float)(-r * 0.8), (float)(r * 0.6)),
                }
            );
            Fill(sq, straw);
            Stroke(sq, Palette.Hex("#6e5b34"), (float)Math.Max(0.8, 1.2 * p.Scale));
            // Twine bands.
            foreach (var bx in new[] { -0.3, 0.3 })
                NLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(r * bx), (float)(-r * 0.58)),
                        new Vector2((float)(r * bx), (float)(r * 0.58)),
                    },
                    Palette.Hex("#8a7433"),
                    1.2
                );
        }
        else
        {
            // Round bale: spiral-bound roll seen from the top.
            var roll = Xform(p, Poly.Ellipse(0, 0, r, r, 0, 14));
            Fill(roll, straw);
            Stroke(roll, Palette.Hex("#8a7433"), (float)Math.Max(0.8, 1.2 * p.Scale));
            if (rand.Next() < 0.4)
                // Flower-studded meadow roll (TS model 1).
                for (var i = 0; i < 8; i++)
                {
                    var a = i / 8.0 * Math.PI * 2;
                    DrawCircle(
                        Xform(
                            p,
                            new Vector2((float)(Math.Cos(a) * r * 0.62), (float)(Math.Sin(a) * r * 0.62))
                        ),
                        (float)Math.Max(1.0, r * 0.28 * 0.5 * p.Scale),
                        Palette.Hex("#e4ddc8")
                    );
                }
            var coil = new Color(0.66f, 0.53f, 0.24f, 0.6f); // #a8873e99
            foreach (var ring in new[] { 0.65, 0.35 })
                NLine(
                    p,
                    Poly.Ellipse(0, 0, r * ring, r * ring, 0, 24),
                    coil,
                    1.4
                );
        }
    }

    void DrawCrate(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        // Models: 0 X-braced lid, 1 slatted planks, 2 stenciled lid.
        var model = PropModel(p, 3, rand.Next());
        var r = 9 * rand.Range(0.85, 1.25);
        var wood = Palette.Mix(
            Palette.Hex("#b0824e"),
            rand.Next() < 0.5 ? Colors.White : Colors.Black,
            rand.Next() * 0.14
        );
        var woodD = Palette.Mix(wood, Colors.Black, 0.32);
        var edge = Palette.Hex("#46351f");
        var seamC = Palette.Mix(wood, edge, 0.55);

        ContactShadow(p, r * 1.1, r * 1.0);

        // Oblique cube — lid + south wall drop, like the houses.
        var top = RectPtsCrate(r);
        var tw = Xform(p, top);
        var faces = WallDrop(
            tw,
            r * 0.62 * p.Scale,
            woodD,
            edge,
            Math.Max(0.7, 1.2 * p.Scale)
        );
        // Vertical plank seams down the wall face.
        foreach (var (fa, fb) in faces)
        {
            var dir = (fb - fa) / 3f;
            for (var i = 1; i <= 2; i++)
            {
                var at = fa + dir * i;
                DrawLine(
                    at + new Vector2(0, (float)(r * 0.06 * p.Scale)),
                    at + new Vector2(0, (float)(r * 0.56 * p.Scale)),
                    Palette.Mix(woodD, edge, 0.5),
                    (float)Math.Max(0.6, 0.8 * p.Scale)
                );
            }
        }
        Fill(tw, wood);
        Stroke(tw, edge, (float)Math.Max(0.9, 1.7 * p.Scale));
        // Inset lid frame.
        var inset = RectPtsCrate(r * 0.72);
        Stroke(
            Xform(p, inset),
            seamC,
            (float)Math.Max(0.6, 1.0 * p.Scale)
        );
        // NW sun edge on the lid.
        NLine(
            p,
            new[]
            {
                new Vector2((float)(-r + 1.5), (float)(r - 1.5)),
                new Vector2((float)(-r + 1.5), (float)(-r + 1.5)),
                new Vector2((float)(r - 1.5), (float)(-r + 1.5)),
            },
            new Color(1f, 1f, 1f, 0.18f),
            1.2
        );
        if (model == 1)
        {
            // Slatted crate: plank seams across the lid both ways.
            for (var i = -1; i <= 1; i++)
            {
                NLine(
                    p,
                    new[]
                    {
                        new Vector2((float)-r, (float)(i * r * 0.5)),
                        new Vector2((float)r, (float)(i * r * 0.5)),
                    },
                    seamC,
                    1.2
                );
                NLine(
                    p,
                    new[]
                    {
                        new Vector2((float)(i * r * 0.5), (float)-r),
                        new Vector2((float)(i * r * 0.5), (float)r),
                    },
                    seamC,
                    1.2
                );
            }
        }
        else if (model == 2)
        {
            // Stencil mark — a little inked diamond sigil mid-lid.
            var mark = Xform(
                p,
                new[]
                {
                    new Vector2(0, (float)(-r * 0.3)),
                    new Vector2((float)(r * 0.24), 0),
                    new Vector2(0, (float)(r * 0.3)),
                    new Vector2((float)(-r * 0.24), 0),
                }
            );
            Stroke(mark, edge, (float)Math.Max(0.7, 1.2 * p.Scale));
        }
        else
        {
            // Classic X brace across the lid.
            NLine(
                p,
                new[]
                {
                    new Vector2((float)(-r + 3), (float)(-r + 3)),
                    new Vector2((float)(r - 3), (float)(r - 3)),
                },
                edge,
                2.4
            );
            NLine(
                p,
                new[]
                {
                    new Vector2((float)(r - 3), (float)(-r + 3)),
                    new Vector2((float)(-r + 3), (float)(r - 3)),
                },
                edge,
                2.4
            );
        }
        // Corner nails.
        foreach (var (nx, ny) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            DrawCircle(
                Xform(p, new Vector2((float)(nx * r * 0.82), (float)(ny * r * 0.82))),
                (float)Math.Max(0.7, 0.9 * p.Scale),
                edge
            );
    }

    /// <summary>Lid square for the crate — local helper so the inset
    /// frame shares the geometry.</summary>
    static Vector2[] RectPtsCrate(double h) =>
        [
            new Vector2((float)-h, (float)-h),
            new Vector2((float)h, (float)-h),
            new Vector2((float)h, (float)h),
            new Vector2((float)-h, (float)h),
        ];

    void DrawBarrel(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 8 * rand.Range(0.85, 1.25);
        var wood = Palette.Mix(
            Palette.Hex("#a37d4c"),
            rand.Next() < 0.5 ? Colors.White : Colors.Black,
            rand.Next() * 0.14
        );
        var woodD = Palette.Mix(wood, Colors.Black, 0.3);
        var edge = Palette.Hex("#43331f");
        var hoop = Palette.Hex("#3f4237");

        ContactShadow(p, r * 1.1, r * 1.0);

        // Oblique cylinder — lid disc + stave wall dropping south, so
        // the barrel reads as a vessel instead of a flat disc.
        var lid = Poly.Ellipse(0, 0, r, r * 0.92, 0, 14);
        var lw = Xform(p, lid);
        var drop = r * 0.72 * p.Scale;
        var faces = WallDrop(
            lw,
            drop,
            woodD,
            edge,
            Math.Max(0.7, 1.1 * p.Scale)
        );
        // Vertical stave seams + two iron hoops down the wall.
        foreach (var (fa, fb) in faces)
        {
            for (var i = 1; i <= 3; i++)
            {
                var at = fa + (fb - fa) * (i / 4f);
                DrawLine(
                    at + new Vector2(0, (float)(drop * 0.08)),
                    at + new Vector2(0, (float)(drop * 0.92)),
                    Palette.Mix(woodD, edge, 0.5),
                    (float)Math.Max(0.6, 0.7 * p.Scale)
                );
            }
            foreach (var hy in new[] { 0.3, 0.72 })
                DrawLine(
                    fa + new Vector2(0, (float)(drop * hy)),
                    fb + new Vector2(0, (float)(drop * hy)),
                    hoop,
                    (float)Math.Max(0.8, 1.6 * p.Scale)
                );
        }
        Fill(lw, wood);
        Stroke(lw, edge, (float)Math.Max(0.9, 1.5 * p.Scale));
        // Lid inset + stave seams radiating from the center, bung hole.
        var inset = Xform(p, Poly.Ellipse(0, 0, r * 0.78, r * 0.72, 0, 13));
        Fill(inset, Palette.Mix(wood, Colors.Black, 0.1));
        Stroke(inset, Palette.Mix(wood, edge, 0.55), (float)Math.Max(0.6, 0.9 * p.Scale));
        for (var i = 0; i < 5; i++)
        {
            var a = i / 5.0 * Math.PI * 2 + 0.3;
            NLine(
                p,
                new[]
                {
                    new Vector2((float)(Math.Cos(a) * r * 0.12), (float)(Math.Sin(a) * r * 0.11)),
                    new Vector2((float)(Math.Cos(a) * r * 0.74), (float)(Math.Sin(a) * r * 0.68)),
                },
                new Color(0.35f, 0.26f, 0.16f, 0.55f),
                1
            );
        }
        DrawCircle(
            Xform(p, Vector2.Zero),
            (float)Math.Max(0.9, r * 0.1 * p.Scale),
            Palette.Mix(wood, edge, 0.6)
        );
        // NW rim light.
        NLine(
            p,
            NArc(0, 0, r * 0.85, r * 0.78, Math.PI * 0.9, Math.PI * 1.6, 8),
            new Color(1f, 1f, 1f, 0.22f),
            1.2
        );
    }

    // --- bones ---------------------------------------------------------------
    // Port of scene-bones.ts: every piece is a filled polygon or a stroked
    // curve in prop-local space. Three passes — widened dark edge under all
    // pieces, the bone fill, then sockets/cracks — so the scatter reads as
    // one inked silhouette. Woodland dressing: grass through the bones.
    void DrawBones(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 12 * rand.Range(0.9, 1.2);
        var fill = Palette.Mix(
            Palette.Hex("#d8cdb2"),
            rand.Next() < 0.5 ? Palette.Hex("#c9bd9e") : Palette.Hex("#e5dcc4"),
            rand.Next() * 0.5
        );
        var edge = Palette.Mix(fill, Palette.Hex("#231c12"), 0.62);
        var shade = Palette.Mix(fill, Palette.Hex("#5b4a33"), 0.32);
        var hi = Palette.Mix(fill, Colors.White, 0.45);
        var socket = Palette.Mix(fill, Palette.Hex("#1c1712"), 0.82);
        var ew = Math.Max(0.9, r * 0.055);
        var th = Math.Max(2.2, r * 0.14);

        var polys = new List<Vector2[]>(); // filled pieces, prop-local
        var lines = new List<(Vector2[] Pts, double W)>(); // stroked pieces
        var details = new List<Action>();

        ContactShadow(p, r * 1.1, r * 0.9);
        // Dark ground stain under the scatter.
        for (var i = 0; i < 3; i++)
            Fill(
                Xform(
                    p,
                    Poly.Ellipse(
                        (rand.Next() - 0.5) * r * 0.5,
                        (rand.Next() - 0.5) * r * 0.4,
                        r * (0.6 + rand.Next() * 0.2),
                        r * (0.42 + rand.Next() * 0.18),
                        rand.Next() * Math.PI,
                        10
                    )
                ),
                new Color(0.35f, 0.3f, 0.2f, 0.13f)
            );

        // Long bone: waisted shaft + two knuckles at each end, then a
        // shaded belly and a lit edge.
        void Bone(double x, double y, double len, double a, double t)
        {
            var h = len / 2;
            var k = t * 0.55;
            polys.Add(
                NFrame(
                    x,
                    y,
                    a,
                    new[]
                    {
                        new Vector2((float)-h, (float)(-t * 0.42)),
                        new Vector2(0, (float)(-t * 0.3)),
                        new Vector2((float)h, (float)(-t * 0.42)),
                        new Vector2((float)h, (float)(t * 0.42)),
                        new Vector2(0, (float)(t * 0.3)),
                        new Vector2((float)-h, (float)(t * 0.42)),
                    }
                )
            );
            foreach (var (ex, ey) in new[] { (-h, -1.0), (-h, 1.0), (h, -1.0), (h, 1.0) })
                polys.Add(NFrame(x, y, a, Poly.Ellipse(ex, ey * t * 0.4, k, k, 0, 8)));
            details.Add(() =>
            {
                NLine(
                    p,
                    NFrame(
                        x,
                        y,
                        a,
                        NQuad(
                            new Vector2((float)(-h * 0.85), (float)(t * 0.24)),
                            new Vector2(0, (float)(t * 0.14)),
                            new Vector2((float)(h * 0.85), (float)(t * 0.24))
                        )
                    ),
                    shade,
                    t * 0.22
                );
                NLine(
                    p,
                    NFrame(
                        x,
                        y,
                        a,
                        new[]
                        {
                            new Vector2((float)(-h * 0.55), (float)(-t * 0.2)),
                            new Vector2((float)(h * 0.35), (float)(-t * 0.2)),
                        }
                    ),
                    hi,
                    t * 0.16
                );
            });
        }

        void Shard(double x, double y, double s, double a) =>
            polys.Add(
                NFrame(
                    x,
                    y,
                    a,
                    new[]
                    {
                        new Vector2((float)-s, (float)(-s * 0.3)),
                        new Vector2((float)(s * 0.8), (float)(-s * 0.45)),
                        new Vector2((float)s, (float)(s * 0.2)),
                        new Vector2((float)(-s * 0.6), (float)(s * 0.4)),
                    }
                )
            );

        // Human skull from above, facing +x: cranium + face + cheekbones,
        // sockets, nasal notch, teeth, sagittal suture.
        void Skull(double x, double y, double s, double a)
        {
            polys.Add(NFrame(x, y, a, Poly.Ellipse(-s * 0.2, 0, s * 0.56, s * 0.46, 0, 12)));
            polys.Add(NFrame(x, y, a, Poly.Ellipse(s * 0.3, 0, s * 0.36, s * 0.33, 0, 10)));
            foreach (var sy in new[] { -1, 1 })
                polys.Add(NFrame(x, y, a, Poly.Ellipse(s * 0.2, sy * s * 0.34, s * 0.15, s * 0.1, 0, 8)));
            details.Add(() =>
            {
                // Shadowed SE rim of the cranium.
                NLine(
                    p,
                    NFrame(x, y, a, NArc(-s * 0.2, 0, s * 0.48, s * 0.38, 0.3, 1.9)),
                    shade,
                    s * 0.08
                );
                Fill(
                    Xform(p, NFrame(x, y, a, Poly.Ellipse(-s * 0.35, -s * 0.16, s * 0.18, s * 0.1, -0.4, 8))),
                    new Color(hi.R, hi.G, hi.B, 0.7f)
                );
                foreach (var sy in new[] { -1, 1 })
                {
                    Fill(
                        Xform(
                            p,
                            NFrame(x, y, a, Poly.Ellipse(s * 0.34, sy * s * 0.16, s * 0.13, s * 0.11, sy * 0.3, 8))
                        ),
                        socket
                    );
                    Fill(
                        Xform(p, NFrame(x, y, a, Poly.Ellipse(s * 0.3, sy * s * 0.15, s * 0.07, s * 0.05, 0, 7))),
                        shade
                    );
                }
                // Nasal notch + teeth ticks.
                Fill(
                    Xform(
                        p,
                        NFrame(
                            x,
                            y,
                            a,
                            new[]
                            {
                                new Vector2((float)(s * 0.5), (float)(-s * 0.05)),
                                new Vector2((float)(s * 0.62), 0),
                                new Vector2((float)(s * 0.5), (float)(s * 0.05)),
                            }
                        )
                    ),
                    socket
                );
                for (var t = -2; t <= 2; t++)
                    NLine(
                        p,
                        NFrame(
                            x,
                            y,
                            a,
                            new[]
                            {
                                new Vector2((float)(s * 0.62), (float)(t * s * 0.06)),
                                new Vector2((float)(s * 0.67), (float)(t * s * 0.06)),
                            }
                        ),
                        edge,
                        Math.Max(0.6, s * 0.03)
                    );
                // Sagittal suture — a little zigzag crack.
                var suture = new Vector2[7];
                for (var i = 0; i <= 6; i++)
                    suture[i] = new Vector2(
                        (float)(-s * 0.7 + i * s * 0.1),
                        (float)((i % 2 == 1 ? 1 : -1) * s * 0.03)
                    );
                NLine(p, NFrame(x, y, a, suture), shade, Math.Max(0.6, ew * 0.7));
            });
        }

        // Horned beast skull, facing +x: cranium, long tapered snout, two
        // sweeping horns; sockets, nostrils, horn ridges, tooth row.
        void BeastSkull(double x, double y, double s, double a)
        {
            polys.Add(NFrame(x, y, a, Poly.Ellipse(-s * 0.15, 0, s * 0.42, s * 0.36, 0, 10)));
            // Snout: three quadratic segments top → tip → bottom.
            var snout = new List<Vector2>();
            snout.AddRange(
                NQuad(
                    new Vector2(0, (float)(-s * 0.28)),
                    new Vector2((float)(s * 0.6), (float)(-s * 0.22)),
                    new Vector2((float)(s * 1.05), (float)(-s * 0.1)),
                    6
                )
            );
            snout.AddRange(
                NQuad(
                    new Vector2((float)(s * 1.05), (float)(-s * 0.1)),
                    new Vector2((float)(s * 1.15), 0),
                    new Vector2((float)(s * 1.05), (float)(s * 0.1)),
                    4
                )
            );
            snout.AddRange(
                NQuad(
                    new Vector2((float)(s * 1.05), (float)(s * 0.1)),
                    new Vector2((float)(s * 0.6), (float)(s * 0.22)),
                    new Vector2(0, (float)(s * 0.28)),
                    6
                )
            );
            polys.Add(NFrame(x, y, a, snout));
            foreach (var sy in new[] { -1, 1 })
            {
                var horn = new List<Vector2>
                {
                    new((float)(-s * 0.3), (float)(sy * s * 0.2)),
                };
                horn.AddRange(
                    NQuad(
                        new Vector2((float)(-s * 0.3), (float)(sy * s * 0.2)),
                        new Vector2((float)(-s * 0.5), (float)(sy * s * 0.95)),
                        new Vector2((float)(-s * 1.05), (float)(sy * s * 0.8)),
                        6
                    )
                );
                horn.AddRange(
                    NQuad(
                        new Vector2((float)(-s * 1.05), (float)(sy * s * 0.8)),
                        new Vector2((float)(-s * 0.55), (float)(sy * s * 0.72)),
                        new Vector2((float)(-s * 0.48), (float)(sy * s * 0.3)),
                        6
                    )
                );
                polys.Add(NFrame(x, y, a, horn));
            }
            details.Add(() =>
            {
                foreach (var sy in new[] { -1, 1 })
                {
                    Fill(
                        Xform(
                            p,
                            NFrame(x, y, a, Poly.Ellipse(s * 0.08, sy * s * 0.2, s * 0.12, s * 0.08, sy * 0.4, 8))
                        ),
                        socket
                    );
                    Fill(
                        Xform(
                            p,
                            NFrame(x, y, a, Poly.Ellipse(s * 0.95, sy * s * 0.05, s * 0.05, s * 0.03, 0, 7))
                        ),
                        socket
                    );
                    // Horn ridges.
                    foreach (var t in new[] { 0.35, 0.55, 0.75 })
                    {
                        var hx = -s * (0.35 + t * 0.6);
                        var hy = sy * s * (0.3 + t * 0.48);
                        NLine(
                            p,
                            NFrame(
                                x,
                                y,
                                a,
                                new[]
                                {
                                    new Vector2((float)(hx - s * 0.06), (float)(hy - sy * s * 0.07)),
                                    new Vector2((float)(hx + s * 0.06), (float)(hy + sy * s * 0.03)),
                                }
                            ),
                            shade,
                            Math.Max(0.6, s * 0.035)
                        );
                    }
                }
                // Tooth row along the snout.
                for (var i = 0; i < 6; i++)
                    foreach (var sy in new[] { -1, 1 })
                    {
                        var tx = s * (0.35 + i * 0.1);
                        var ty = sy * s * (0.23 - i * 0.022);
                        NLine(
                            p,
                            NFrame(
                                x,
                                y,
                                a,
                                new[]
                                {
                                    new Vector2((float)tx, (float)ty),
                                    new Vector2((float)tx, (float)(ty + sy * s * 0.05)),
                                }
                            ),
                            edge,
                            Math.Max(0.6, s * 0.03)
                        );
                    }
                NLine(
                    p,
                    NFrame(
                        x,
                        y,
                        a,
                        new[]
                        {
                            new Vector2((float)(s * 0.1), 0),
                            new Vector2((float)(s * 0.55), (float)(s * 0.02)),
                            new Vector2((float)(s * 0.85), 0),
                        }
                    ),
                    shade,
                    Math.Max(0.6, ew * 0.7)
                );
            });
        }

        void Vert(double x, double y, double w, double a)
        {
            polys.Add(NFrame(x, y, a, Poly.Ellipse(0, 0, w * 0.5, w * 0.36, 0, 8)));
            polys.Add(
                NFrame(
                    x,
                    y,
                    a,
                    new[]
                    {
                        new Vector2((float)(-w * 0.12), (float)(-w * 0.95)),
                        new Vector2((float)(w * 0.12), (float)(-w * 0.95)),
                        new Vector2((float)(w * 0.12), (float)(w * 0.95)),
                        new Vector2((float)(-w * 0.12), (float)(w * 0.95)),
                    }
                )
            );
            details.Add(() =>
                Fill(Xform(p, NFrame(x, y, a, Poly.Ellipse(0, 0, w * 0.14, w * 0.1, 0, 7))), shade)
            );
        }

        void Curve(Vector2[] pts, double w)
        {
            var line = pts.Length == 3 ? NQuad(pts[0], pts[1], pts[2]) : pts;
            lines.Add((line, w));
        }

        // --- variant layout --------------------------------------------------
        var model = PropModel(p, 4, rand.Next());
        if (model == 3)
        {
            // Bone pile crowned by skulls.
            var count = 9 + (int)(rand.Next() * 5);
            for (var i = 0; i < count; i++)
            {
                var ang = rand.Next() * Math.PI * 2;
                var dist = r * Math.Sqrt(rand.Next()) * 0.55;
                Bone(
                    Math.Cos(ang) * dist,
                    Math.Sin(ang) * dist * 0.8,
                    r * (0.45 + rand.Next() * 0.45),
                    rand.Next() * Math.PI,
                    th * (0.7 + rand.Next() * 0.4)
                );
            }
            for (var i = 0; i < 2; i++)
                Bone(
                    (rand.Next() - 0.5) * r * 0.4,
                    (rand.Next() - 0.5) * r * 0.3,
                    r * (0.6 + rand.Next() * 0.3),
                    rand.Next() * Math.PI,
                    th
                );
            Skull(-r * 0.12, -r * 0.1, r * 0.42, -0.5 + rand.Next());
            if (rand.Next() < 0.75)
                Skull(r * 0.3, r * 0.18, r * 0.32, -0.8 + rand.Next() * 1.6);
            for (var i = 0; i < 7; i++)
            {
                var a = rand.Next() * Math.PI * 2;
                var d = r * (0.7 + rand.Next() * 0.35);
                Shard(Math.Cos(a) * d, Math.Sin(a) * d * 0.8, th * (0.35 + rand.Next() * 0.35), rand.Next() * 6);
            }
        }
        else if (model == 2)
        {
            // Large beast skeleton: curved spine with tapering ribs, a
            // stray leg bone, horned skull at the head end.
            Vector2 Spine(double t) =>
                new(
                    (float)(-r * 0.95 + t * r * 1.3),
                    (float)(r * 0.25 - t * r * 0.35 + Math.Sin(t * Math.PI) * r * 0.18)
                );
            const int n = 12;
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)(n - 1);
                var v0 = Spine(t);
                var v1 = Spine(Math.Min(1, t + 0.01));
                var a = Math.Atan2(v1.Y - v0.Y, v1.X - v0.X);
                if (t > 0.35 && t < 0.85)
                    foreach (var side in new[] { -1, 1 })
                    {
                        if (rand.Next() < 0.15)
                            continue; // missing rib
                        var len = r * (0.35 + (t - 0.35) * 0.9) * (rand.Next() < 0.12 ? 0.45 : 1);
                        var px = -Math.Sin(a) * side;
                        var py = Math.Cos(a) * side;
                        var tip = new Vector2(
                            (float)(v0.X + px * len - Math.Cos(a) * len * 0.45),
                            (float)(v0.Y + py * len - Math.Sin(a) * len * 0.45)
                        );
                        var ctrl = new Vector2(
                            (float)(v0.X + px * len * 0.95 + Math.Cos(a) * len * 0.15),
                            (float)(v0.Y + py * len * 0.95 + Math.Sin(a) * len * 0.15)
                        );
                        Curve(new[] { v0, ctrl, tip }, Math.Max(1.6, r * 0.075 * (1.2 - t * 0.5)));
                    }
                Vert(v0.X, v0.Y, r * (0.06 + t * 0.07), a);
            }
            var head = Spine(1);
            var neck = Spine(0.97);
            BeastSkull(
                head.X + r * 0.18,
                head.Y - r * 0.06,
                r * 0.36,
                Math.Atan2(head.Y - neck.Y, head.X - neck.X)
            );
            Bone(-r * 0.3, r * 0.75, r * 0.6, 0.4 + rand.Next() * 0.4, th * 0.85);
            Shard(r * 0.55, r * 0.55, th * 0.6, rand.Next() * 3);
            Shard(-r * 0.75, -r * 0.3, th * 0.5, rand.Next() * 3);
        }
        else if (model == 1)
        {
            // Skull with a detached jaw and a few long bones.
            var a = -0.4 + rand.Next() * 0.8;
            Bone(-r * 0.25, r * 0.35, r * 0.95, 0.3 + rand.Next() * 0.3, th);
            Bone(-r * 0.1, -r * 0.45, r * 0.75, -0.6 + rand.Next() * 0.3, th * 0.9);
            if (rand.Next() < 0.7)
                Bone(r * 0.55, r * 0.5, r * 0.55, 1.2 + rand.Next(), th * 0.8);
            Skull(r * 0.2, -r * 0.02, r * 0.62, a);
            var jx = r * 0.62;
            var jy = -r * 0.48;
            Curve(
                new[]
                {
                    new Vector2((float)(jx - r * 0.18), (float)(jy - r * 0.12)),
                    new Vector2((float)(jx + r * 0.22), (float)jy),
                    new Vector2((float)(jx - r * 0.18), (float)(jy + r * 0.12)),
                },
                Math.Max(1.6, r * 0.08)
            );
            for (var i = 0; i < 3; i++)
                Shard(
                    (rand.Next() - 0.5) * r * 1.5,
                    (rand.Next() - 0.5) * r * 1.3,
                    th * (0.35 + rand.Next() * 0.3),
                    rand.Next() * 6
                );
        }
        else
        {
            // Scattered long bones + fragments.
            var count = 3 + (int)(rand.Next() * 3);
            for (var i = 0; i < count; i++)
            {
                var ang = i / (double)count * Math.PI * 2 + rand.Next() * 0.8;
                var dist = r * (0.28 + rand.Next() * 0.4);
                Bone(
                    Math.Cos(ang) * dist,
                    Math.Sin(ang) * dist * 0.85,
                    r * (0.5 + rand.Next() * 0.5),
                    rand.Next() * Math.PI,
                    th * (0.75 + rand.Next() * 0.35)
                );
            }
            for (var i = 0; i < 4; i++)
                Shard(
                    (rand.Next() - 0.5) * r * 1.6,
                    (rand.Next() - 0.5) * r * 1.4,
                    th * (0.35 + rand.Next() * 0.35),
                    rand.Next() * 6
                );
        }

        // --- painting: edge pass, fill pass, details --------------------------
        var wscale = (double)p.Scale;
        foreach (var poly in polys)
        {
            var w = Xform(p, poly);
            Fill(w, edge);
            Stroke(w, edge, (float)Math.Max(0.6, ew * 2 * wscale));
        }
        foreach (var (pts, wpt) in lines)
            DrawPolyline(Xform(p, pts), edge, (float)((wpt + ew * 2) * wscale), true);
        foreach (var poly in polys)
            Fill(Xform(p, poly), fill);
        foreach (var (pts, wpt) in lines)
            DrawPolyline(Xform(p, pts), fill, (float)Math.Max(0.8, wpt * wscale), true);
        foreach (var d in details)
            d();

        // Woodland dressing — grass growing through the bones.
        var grassC = Palette.Mix(Palette.Hex("#8aab54"), Palette.Hex("#24391b"), 0.5);
        grassC.A = 0.85f;
        for (var i = 0; i < 7; i++)
        {
            var at = polys[(int)(rand.Next() * polys.Count)];
            var c = Poly.Centroid(at);
            for (var b = -1; b <= 1; b++)
                NLine(
                    p,
                    new[]
                    {
                        c,
                        new Vector2(
                            (float)(c.X + b * th * 0.4 + (rand.Next() - 0.5) * th),
                            (float)(c.Y - th * (0.7 + rand.Next() * 0.4))
                        ),
                    },
                    grassC,
                    Math.Max(0.8, ew * 0.9)
                );
        }
    }

    // Placeholder marker used by the sibling batch files (PropsVillage,
    // PropsInterior) until those kinds get their own painters — keep it
    // so the partial class still compiles.
    void Stub(PropPlacement p) =>
        DrawCircle(p.Pos, 4 * p.Scale, new Color(0.4f, 0.2f, 0.6f, 0.5f));

    // --- ambient critters (smart fill) -------------------------------------

    /// <summary>Butterfly — two bright wings around a dark body. Ambient
    /// critter from the smart fill; no contact shadow (it hovers, the
    /// heightmap throws the shadow south of it instead).</summary>
    void DrawButterfly(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var wing = new[]
        {
            Palette.Hex("#e8a13a"),
            Palette.Hex("#d8648e"),
            Palette.Hex("#e8e0f0"),
            Palette.Hex("#86b7e0"),
            Palette.Hex("#e8d05a"),
        }[(int)(rand.Next() * 5) % 5];
        var a = rand.Next() * Math.PI * 2;
        var ca = Math.Cos(a);
        var sa = Math.Sin(a);
        var spread = rand.Range(0.55, 0.95);
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var c = new Vector2((float)(-sa * side * 3.6), (float)(ca * side * 3.6));
            Fill(Xform(p, Poly.Ellipse(c.X, c.Y, 4.2, 2.3, a + side * spread, 8)), wing);
        }
        DrawLine(
            Xform(p, new Vector2((float)(-ca * 4.4), (float)(-sa * 4.4))),
            Xform(p, new Vector2((float)(ca * 4.4), (float)(sa * 4.4))),
            StoneEdge,
            1.8f
        );
    }

    /// <summary>Soaring bird — a shallow two-arc chevron, ink-dark.
    /// Reads as a distant flyer; the height entry puts its shadow far
    /// south-east like the real thing.</summary>
    void DrawBird(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var s = rand.Range(7, 11);
        var a = rand.Next() * Math.PI * 2;
        var ca = Math.Cos(a);
        var sa = Math.Sin(a);
        Vector2 Dir(double x, double y) =>
            new((float)(x * ca - y * sa), (float)(x * sa + y * ca));
        var ink = Palette.Mix(StoneEdge, Colors.Black, 0.25);
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var mid = Dir(s * 0.32, side * s * 0.52);
            var tip = Dir(s * 0.1, side * s);
            DrawLine(Xform(p, mid), Xform(p, tip), ink, Math.Max(1.2f, (float)(s * 0.15)));
            DrawLine(Xform(p, Dir(-s * 0.06, 0)), Xform(p, mid), ink, Math.Max(1.4f, (float)(s * 0.19)));
        }
    }
}
