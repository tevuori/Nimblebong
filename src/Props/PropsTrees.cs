// Per-biome tree species — the TS scene-trees.ts species switch.
// The tree prop picks a species from the biome's trees.species list by
// seed; seasonal transforms follow scene-biomes (winter→bare for
// deciduous, autumn palette picks, spring blossoms, conifer snow).
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    /// <summary>Resolved biome — set by SceneView so species, tints and
    /// the active season follow the map's environment.</summary>
    public ResolvedBiome? BiomeCfg;

    static SceneConfig.TreeConfig TreeCfg => SceneConfig.Trees();

    void DrawSpeciesTree(PropPlacement p)
    {
        var speciesList = BiomeCfg?.Species ?? [];
        var species =
            p.Species
            ?? (
                speciesList.Count > 0
                    ? speciesList[(int)(p.Seed * 7919 % speciesList.Count)]
                    : "oak"
            );
        var rand = new Jitter(p.VarSeed);
        var meta = TreeCfg.For(species);
        var look = BiomeCfg?.SeasonLook ?? "summer";
        var winterBare = look == "winter" && meta.Deciduous;
        var color = JitterGreen(Foliage, rand);

        if (look == "autumn" && meta.Deciduous && TreeCfg.AutumnColors.Count > 0)
        {
            var pick = TreeCfg.AutumnColors[(int)(rand.Next() * TreeCfg.AutumnColors.Count)];
            if (pick.To is { } to)
                color = Palette.Mix(color, to, pick.Amount);
        }
        else if (look == "autumn" && meta.Evergreen && TreeCfg.AutumnEvergreen.To != default)
        {
            color = Palette.Mix(color, TreeCfg.AutumnEvergreen.To, TreeCfg.AutumnEvergreen.Amount);
        }

        var r = 24 * rand.Range(0.85, 1.15) * meta.Size;
        if (meta.Shadow > 0)
            ContactShadow(p, r * 0.95 * meta.Shadow, r * 0.8 * meta.Shadow);
        if (winterBare)
            species = "bare";

        switch (species)
        {
            case "oak":
                DrawTree(p);
                Blossoms(p, rand, r, look);
                break;
            case "birch":
                DrawBirch(p, rand, r, color);
                Blossoms(p, rand, r, look);
                break;
            case "acacia":
                DrawAcacia(p, rand, r, color);
                Blossoms(p, rand, r, look);
                break;
            case "mangrove":
                DrawMangrove(p, rand, r, color);
                break;
            case "willow":
                DrawWillow(p, rand, r, color);
                break;
            case "cypress":
                DrawCypress(p, rand, r, color);
                break;
            case "spruce":
            case "snowfir":
            case "pine":
                if (species == "pine")
                    DrawPineTufts(p, rand, r, color, look == "winter");
                else
                    DrawPine(
                        p,
                        species == "snowfir" ? TreeCfg.SnowfirSnow
                        : look == "winter" ? TreeCfg.ConiferSnow
                        : 0,
                        species == "snowfir"
                            ? Palette.Mix(color, Palette.Hex("#2f4d45"), 0.2)
                            : color
                    );
                break;
            case "palm":
                DrawPalm(p, rand, r, color);
                break;
            case "cactus":
                DrawCactus(p, rand, r, color, look);
                break;
            case "bare":
                BareTree(p, rand, r, Palette.Mix(Palette.Hex("#5e5246"), color, 0.15), winterBare || look == "winter" || species == "bare", false);
                break;
            case "charred":
                BareTree(p, rand, r, Palette.Hex("#2b2622"), false, true);
                break;
            case "deadtree":
                BareTree(p, rand, r, Palette.Hex("#8a8072"), false, false);
                break;
            case "stump":
                DrawStump(p, rand, r);
                break;
            case "mushroom":
            {
                var caps = new[]
                {
                    color,
                    Palette.Mix(color, Palette.Hex("#e07a5f"), 0.45),
                    Palette.Mix(color, Palette.Hex("#5fc4b8"), 0.5),
                    Palette.Mix(color, Palette.Hex("#d9b45a"), 0.45),
                };
                MushroomCap(p, rand, 0, 0, r * 0.85, caps[(int)(rand.Next() * caps.Length)]);
                break;
            }
            case "shroomcluster":
            {
                var tone =
                    rand.Next() < 0.5 ? color : Palette.Mix(color, Palette.Hex("#5fc4b8"), 0.45);
                var caps = new List<(double X, double Y, double R)>();
                var n = 3 + (int)(rand.Next() * 4);
                for (var k = 0; k < n; k++)
                {
                    var a = rand.Next() * Math.PI * 2;
                    var d = Math.Sqrt(rand.Next()) * r * 0.5;
                    caps.Add((Math.Cos(a) * d, Math.Sin(a) * d, r * rand.Range(0.22, 0.42)));
                }
                foreach (var c in caps.OrderBy(c => c.Y))
                    MushroomCap(p, rand, c.X, c.Y, c.R, Palette.Mix(tone, Colors.White, rand.Next() * 0.15));
                break;
            }
            case "coral":
            {
                var tone =
                    rand.Next() < 0.5
                        ? Palette.Mix(color, Palette.Hex("#ff7fb0"), 0.4)
                        : Palette.Mix(color, Palette.Hex("#5fe0d0"), 0.45);
                DrawCircle(Xform(p, Vector2.Zero), (float)(r * p.Scale), tone with { A = 0.12f });
                var segs = Branches(rand, 5, r * 1.05, Math.Max(2.4, r * 0.14), 2);
                Strokes(p, segs, Palette.Mix(tone, Colors.Black, 0.5), 2);
                Strokes(p, segs, tone, 0);
                foreach (var t in segs.Where(s => s.Tip))
                    DrawCircle(
                        Xform(p, t.B),
                        (float)Math.Max(1.5, r * 0.07 * p.Scale),
                        Palette.Mix(tone, Colors.White, 0.6)
                    );
                break;
            }
            default:
                DrawTree(p);
                break;
        }
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>Spring blossom dots scattered over the canopy —
    /// TS blossoms(): seeded chance per tree.</summary>
    void Blossoms(PropPlacement p, Jitter rand, double r, string look)
    {
        if (look != "spring" || TreeCfg.BlossomColors.Count == 0)
            return;
        if (rand.Next() > TreeCfg.BlossomChance)
            return;
        var n = (int)(r * 0.9);
        for (var k = 0; k < n; k++)
        {
            var a = rand.Next() * Math.PI * 2;
            var d = Math.Sqrt(rand.Next()) * r * 0.9;
            DrawCircle(
                Xform(p, new Vector2((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d))),
                (float)(rand.Range(0.8, 1.6) * p.Scale),
                TreeCfg.BlossomColors[(int)(rand.Next() * TreeCfg.BlossomColors.Count)]
            );
        }
    }

    /// <summary>Recursive branch skeleton — segments radiate from the
    /// center and split; the TS branches() helper. `Tip` marks the
    /// outermost segment of each arm.</summary>
    static List<(Vector2 A, Vector2 B, double W, bool Tip)> Branches(
        Jitter rand,
        int arms,
        double len,
        double w0,
        int depth
    )
    {
        var segs = new List<(Vector2, Vector2, double, bool)>();
        for (var k = 0; k < arms; k++)
        {
            var a = (k / (double)arms) * Math.PI * 2 + (rand.Next() - 0.5) * 0.8;
            var mid = new Vector2(
                (float)(Math.Cos(a) * len * rand.Range(0.4, 0.6)),
                (float)(Math.Sin(a) * len * rand.Range(0.4, 0.6))
            );
            segs.Add((Vector2.Zero, mid, w0, false));
            var kids = 2 + (int)(rand.Next() * 2);
            for (var j = 0; j < kids; j++)
            {
                var a2 = a + (rand.Next() - 0.5) * 1.4;
                var end = mid + new Vector2(
                    (float)(Math.Cos(a2) * len * rand.Range(0.3, 0.55)),
                    (float)(Math.Sin(a2) * len * rand.Range(0.3, 0.55))
                );
                segs.Add((mid, end, Math.Max(0.8, w0 * 0.55), depth <= 1));
                if (depth > 1)
                {
                    var a3 = a2 + (rand.Next() - 0.5) * 0.9;
                    segs.Add(
                        (
                            end,
                            end + new Vector2(
                                (float)(Math.Cos(a3) * len * 0.3),
                                (float)(Math.Sin(a3) * len * 0.3)
                            ),
                            Math.Max(0.6, w0 * 0.3),
                            true
                        )
                    );
                }
            }
        }
        return segs;
    }

    /// <summary>Stroke a segment list — each seg's W is its width in
    /// prop units; `pad` widens for the dark under-stroke.</summary>
    void Strokes(
        PropPlacement p,
        List<(Vector2 A, Vector2 B, double W, bool Tip)> segs,
        Color color,
        double pad
    )
    {
        foreach (var sgm in segs)
            DrawLine(
                Xform(p, sgm.A),
                Xform(p, sgm.B),
                color,
                (float)Math.Max(0.6, (sgm.W + pad) * p.Scale),
                true
            );
    }

    // --- species ------------------------------------------------------------

    void DrawBirch(PropPlacement p, Jitter rand, double r, Color color)
    {
        var segs = Branches(rand, 4, r * 0.9, Math.Max(1.6, r * 0.08), 2);
        Strokes(p, segs.Select(s => (s.A, s.B, s.W + 1.5, s.Tip)).ToList(), Palette.Hex("#3a3530"), 0);
        Strokes(p, segs, Palette.Hex("#efece2"), 0);
        var light = Palette.Mix(color, Palette.Hex("#c8d86a"), 0.3);
        var clumps = new List<Vector2[]>();
        var n = 6 + (int)(rand.Next() * 4);
        for (var k = 0; k < n; k++)
        {
            var a = rand.Next() * Math.PI * 2;
            var d = Math.Sqrt(rand.Next()) * r * 0.6;
            clumps.Add(Blob(Math.Cos(a) * d, Math.Sin(a) * d, r * rand.Range(0.2, 0.32), rand, 10, 0.2));
        }
        foreach (var c in clumps)
            Stroke(Xform(p, c), Palette.Mix(color, Palette.Hex("#0d140a"), 0.62), (float)(1.8 * p.Scale));
        foreach (var c in clumps)
            Fill(Xform(p, c), Palette.Mix(light, Colors.Black, 0.18));
        var off = new Vector2((float)(-r * 0.04), (float)(-r * 0.05));
        foreach (var c in clumps)
            Fill(Xform(p, c.Select(q => q * 0.9f + off).ToArray()), light);
    }

    void DrawAcacia(PropPlacement p, Jitter rand, double r, Color color)
    {
        var segs = Branches(rand, 5, r * 0.95, Math.Max(1.6, r * 0.09), 2);
        Strokes(p, segs, Palette.Mix(Palette.Hex("#6b5236"), color, 0.15), 0);
        var olive = Palette.Mix(color, Palette.Hex("#9aa04a"), 0.35);
        var edge = Palette.Mix(olive, Palette.Hex("#0d140a"), 0.55);
        var lobes = 5 + (int)(rand.Next() * 3);
        for (var k = 0; k < lobes; k++)
        {
            var a = (k / (double)lobes) * Math.PI * 2 + rand.Next() * 0.4;
            var d = r * 0.55 * rand.Range(0.8, 1.05);
            var lob = Blob(Math.Cos(a) * d, Math.Sin(a) * d, r * rand.Range(0.26, 0.36), rand, 12, 0.16);
            Stroke(Xform(p, lob), edge, (float)(2 * p.Scale));
            Fill(Xform(p, lob), Palette.Mix(olive, rand.Next() < 0.5 ? Colors.Black : Colors.White, rand.Next() * 0.12));
        }
    }

    void DrawMangrove(PropPlacement p, Jitter rand, double r, Color color)
    {
        var roots = new List<(Vector2, Vector2, double, bool)>();
        var n = 9 + (int)(rand.Next() * 5);
        for (var k = 0; k < n; k++)
        {
            var a = rand.Next() * Math.PI * 2;
            var a2 = a + (rand.Next() - 0.5) * 0.6;
            roots.Add(
                (
                    new Vector2((float)(Math.Cos(a) * r * 0.5), (float)(Math.Sin(a) * r * 0.5)),
                    new Vector2(
                        (float)(Math.Cos(a2) * r * rand.Range(0.9, 1.15)),
                        (float)(Math.Sin(a2) * r * rand.Range(0.9, 1.15))
                    ),
                    Math.Max(1.2, r * 0.05),
                    false
                )
            );
        }
        Strokes(p, roots, Palette.Hex("#2e261d"), 1.6);
        Strokes(p, roots, Palette.Hex("#7a6248"), 0);
        var dark = Palette.Mix(color, Colors.Black, 0.12);
        var lobes = 8 + (int)(rand.Next() * 3);
        for (var k = 0; k < lobes; k++)
        {
            var a = (k / (double)lobes) * Math.PI * 2 + rand.Next() * 0.4;
            var d = r * 0.4 * rand.Range(0.8, 1.1);
            var lob = Blob(Math.Cos(a) * d, Math.Sin(a) * d, r * rand.Range(0.36, 0.5) * 0.7, rand);
            Fill(Xform(p, lob), Palette.Mix(dark, rand.Next() < 0.5 ? Colors.Black : Colors.White, rand.Next() * 0.1));
        }
    }

    void DrawWillow(PropPlacement p, Jitter rand, double r, Color color)
    {
        var light = Palette.Mix(color, Palette.Hex("#b5c96a"), 0.3);
        var body = Blob(0, 0, r * 0.9, rand, 18, 0.06);
        Stroke(Xform(p, body), Palette.Mix(light, Palette.Hex("#0d140a"), 0.6), (float)(2 * p.Scale));
        Fill(Xform(p, body), Palette.Mix(light, Colors.Black, 0.3));
        var swirl = (rand.Next() - 0.5) * 0.8;
        foreach (var (tone, len, w) in new (Color, double, double)[]
        {
            (Palette.Mix(light, Colors.Black, 0.12), 1, 1.6),
            (light, 0.8, 1.2),
            (Palette.Mix(light, Colors.White, 0.25), 0.55, 0.9),
        })
        {
            var n = (int)Math.Round(r * 1.3);
            for (var k = 0; k < n; k++)
            {
                var a = rand.Next() * Math.PI * 2;
                var start = r * 0.12 * rand.Next();
                var end = r * len * rand.Range(0.75, 0.98);
                var mid = new Vector2(
                    (float)(Math.Cos(a + swirl) * end * 0.6),
                    (float)(Math.Sin(a + swirl) * end * 0.6)
                );
                var tip = new Vector2(
                    (float)(Math.Cos(a + swirl * 0.4) * end),
                    (float)(Math.Sin(a + swirl * 0.4) * end)
                );
                DrawLine(
                    Xform(p, new Vector2((float)(Math.Cos(a) * start), (float)(Math.Sin(a) * start))),
                    Xform(p, mid),
                    tone,
                    (float)Math.Max(0.5, w * p.Scale),
                    true
                );
                DrawLine(Xform(p, mid), Xform(p, tip), tone, (float)Math.Max(0.5, w * p.Scale), true);
            }
        }
    }

    void DrawCypress(PropPlacement p, Jitter rand, double r, Color color)
    {
        var dark = Palette.Mix(color, Palette.Hex("#1c2a1a"), 0.35);
        var body = Poly.Ellipse(0, 0, r * 0.62, r * 0.62, 0, 14);
        for (var k = 0; k < body.Length; k++)
            body[k] *= (float)(1 + 0.08 * Math.Sin(k * 2.4));
        Stroke(Xform(p, body), Palette.Mix(dark, Palette.Hex("#0d140a"), 0.6), (float)(2 * p.Scale));
        Fill(Xform(p, body), dark);
        // Spiral hatch + a few twig nicks.
        for (var k = 0; k < 26; k++)
        {
            var t = k / 26.0;
            var a = t * Math.PI * 6;
            var d = t * r * 0.55;
            DrawArc(
                Xform(p, new Vector2((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d))),
                (float)(2 * p.Scale),
                0,
                (float)Math.PI,
                5,
                Palette.Mix(dark, Colors.White, 0.18),
                (float)Math.Max(0.5, 0.9 * p.Scale)
            );
        }
    }

    /// <summary>Needle-tuft pine — star clusters around a few branch
    /// arms (TS case "pine").</summary>
    void DrawPineTufts(PropPlacement p, Jitter rand, double r, Color color, bool winter)
    {
        var segs = Branches(rand, 5, r * 1.1, Math.Max(1.4, r * 0.07), 1);
        Strokes(p, segs, Palette.Hex("#5e4630"), 0);
        var pine = Palette.Mix(color, Palette.Hex("#3b5a3a"), 0.2);
        var edge = Palette.Mix(pine, Palette.Hex("#0d140a"), 0.62);
        var centers = new List<Vector2> { Vector2.Zero };
        var tufts = 5 + (int)(rand.Next() * 4);
        for (var k = 0; k < tufts; k++)
        {
            var a = (k / (double)tufts) * Math.PI * 2 + rand.Next() * 0.5;
            var d = r * rand.Range(0.45, 0.62);
            centers.Add(new Vector2((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d)));
        }
        foreach (var c in centers)
        {
            var R = r * rand.Range(0.26, 0.36);
            var star = new Vector2[20];
            for (var k = 0; k < 20; k++)
            {
                var a = (k / 20.0) * Math.PI * 2;
                var rr = R * (k % 2 == 1 ? 0.55 : 1);
                star[k] = c + new Vector2((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
            }
            var w = Xform(p, star);
            Stroke(w, edge, (float)(2 * p.Scale));
            Fill(w, Palette.Mix(pine, rand.Next() < 0.5 ? Colors.Black : Colors.White, rand.Next() * 0.15));
            if (winter)
                Fill(
                    Xform(p, Blob(c.X - R * 0.2, c.Y - R * 0.2, R * 0.45, rand, 10, 0.1)),
                    TreeCfg.SnowColor
                );
        }
    }

    void DrawPalm(PropPlacement p, Jitter rand, double r, Color color)
    {
        var edge = Palette.Mix(color, Palette.Hex("#0d140a"), 0.6);
        var fronds = 6 + (int)(rand.Next() * 5);
        for (var k = 0; k < fronds; k++)
        {
            var a = (k / (double)fronds) * Math.PI * 2 + (rand.Next() - 0.5) * 0.4;
            var len = r * rand.Range(0.8, 1.05);
            var bend = (rand.Next() - 0.5) * 0.5;
            var cos = Math.Cos(a);
            var sin = Math.Sin(a);
            Vector2 Pt(double t, double sgn)
            {
                var w = len * 0.17 * Math.Pow(Math.Sin(Math.PI * t), 0.7);
                var along = t * len;
                var side = Math.Sin(t * Math.PI) * bend * len * 0.3 + sgn * w;
                return new Vector2(
                    (float)(along * cos - side * sin),
                    (float)(along * sin + side * cos)
                );
            }
            var outline = new List<Vector2>();
            for (var s = 0; s <= 12; s++)
                outline.Add(Pt(s / 12.0, s % 2 == 1 ? 0.55 : 1));
            for (var s = 12; s >= 0; s--)
                outline.Add(Pt(s / 12.0, s % 2 == 1 ? -0.55 : -1));
            var w2 = Xform(p, outline);
            Stroke(w2, edge, (float)(1.6 * p.Scale));
            Fill(w2, Palette.Mix(color, k % 2 == 1 ? Colors.Black : Colors.White, rand.Next() * 0.14));
            // Central rib.
            var rib = new Vector2[11];
            for (var s = 0; s <= 10; s++)
            {
                var t = s / 10.0;
                var side = Math.Sin(t * Math.PI) * bend * len * 0.3;
                rib[s] = new Vector2(
                    (float)(t * len * cos - side * sin),
                    (float)(t * len * sin + side * cos)
                );
            }
            DrawPolyline(
                [.. rib.Select(q => Xform(p, q))],
                Palette.Mix(color, Palette.Hex("#e8e0a0"), 0.45),
                (float)Math.Max(0.5, 0.8 * p.Scale),
                true
            );
        }
        // Coconuts.
        DrawCircle(Xform(p, Vector2.Zero), (float)(r * 0.13 * p.Scale), Palette.Hex("#8e7250"));
        for (var k = (int)(rand.Next() * 4); k > 0; k--)
        {
            var a = rand.Next() * Math.PI * 2;
            DrawCircle(
                Xform(p, new Vector2((float)(Math.Cos(a) * r * 0.12), (float)(Math.Sin(a) * r * 0.12))),
                (float)(r * 0.07 * p.Scale),
                Palette.Hex("#5c4127")
            );
        }
    }

    void DrawCactus(PropPlacement p, Jitter rand, double r, Color color, string look)
    {
        var green = Palette.Mix(color, Palette.Hex("#6f8f5a"), 0.4);
        var edge = Palette.Mix(green, Palette.Hex("#0d140a"), 0.6);
        var bodies = new List<(double X, double Y, double R)> { (0, 0, r * 0.36) };
        for (var k = (int)(rand.Next() * 4); k > 0; k--)
        {
            var a = rand.Next() * Math.PI * 2;
            var d = r * rand.Range(0.5, 0.65);
            bodies.Add((Math.Cos(a) * d, Math.Sin(a) * d, r * rand.Range(0.18, 0.26)));
        }
        // Joining arms — dark under-stroke then green.
        foreach (var b in bodies.Skip(1))
        {
            var a = Xform(p, Vector2.Zero);
            var bp = Xform(p, new Vector2((float)b.X, (float)b.Y));
            DrawLine(a, bp, edge, (float)((b.R * 1.1 + 3) * p.Scale), true);
        }
        foreach (var b in bodies.Skip(1))
            DrawLine(
                Xform(p, Vector2.Zero),
                Xform(p, new Vector2((float)b.X, (float)b.Y)),
                green,
                (float)(b.R * 1.1 * p.Scale),
                true
            );
        foreach (var b in bodies)
        {
            var bp = Xform(p, new Vector2((float)b.X, (float)b.Y));
            DrawCircle(bp, (float)((b.R + 1.5) * p.Scale), edge);
            DrawCircle(bp, (float)(b.R * p.Scale), green);
            DrawCircle(
                Xform(p, new Vector2((float)(b.X - b.R * 0.25), (float)(b.Y - b.R * 0.25))),
                (float)(b.R * 0.45 * p.Scale),
                Palette.Mix(green, Colors.White, 0.22)
            );
            // Ribs.
            var ribs = 8 + (int)(rand.Next() * 4);
            for (var k = 0; k < ribs; k++)
            {
                var a = (k / (double)ribs) * Math.PI * 2;
                DrawLine(
                    Xform(p, new Vector2((float)(b.X + Math.Cos(a) * b.R * 0.25), (float)(b.Y + Math.Sin(a) * b.R * 0.25))),
                    Xform(p, new Vector2((float)(b.X + Math.Cos(a) * b.R * 0.95), (float)(b.Y + Math.Sin(a) * b.R * 0.95))),
                    Palette.Mix(green, Colors.Black, 0.3),
                    (float)Math.Max(0.5, 0.8 * p.Scale),
                    true
                );
            }
        }
        if (look == "spring" || rand.Next() < 0.2)
            DrawCircle(
                Xform(p, Vector2.Zero),
                (float)(r * 0.1 * p.Scale),
                rand.Next() < 0.5 ? Palette.Hex("#f07aa0") : Palette.Hex("#f6d04d")
            );
    }

    /// <summary>Leafless branch skeleton — winter-bare deciduous,
    /// charred (embers) and dead trees share it (TS bareTree).</summary>
    void BareTree(PropPlacement p, Jitter rand, double r, Color color, bool snow, bool embers)
    {
        var segs = Branches(rand, 6, r * 1.05, Math.Max(1.8, r * 0.09), 2);
        Strokes(p, segs.Select(s => (s.A, s.B, s.W + 1.2, s.Tip)).ToList(), Palette.Mix(color, Colors.Black, 0.5), 0);
        Strokes(p, segs, color, 0);
        if (snow)
            foreach (var sgm in segs.Where(s => s.Tip))
            {
                var dir = (sgm.B - sgm.A).Normalized();
                var snowEnd = sgm.B - dir * (float)(r * 0.1);
                DrawLine(
                    Xform(p, sgm.A + dir * (float)(r * 0.15)),
                    Xform(p, snowEnd),
                    TreeCfg.SnowColor,
                    (float)Math.Max(0.8, sgm.W * 0.5 * p.Scale),
                    true
                );
            }
        if (embers)
        {
            var n = 3 + (int)(rand.Next() * 4);
            for (var k = 0; k < n; k++)
            {
                var sgm = segs[(int)(rand.Next() * segs.Count)];
                var t = rand.Next();
                var pt = sgm.A + (sgm.B - sgm.A) * (float)t;
                DrawCircle(Xform(p, pt), (float)(rand.Range(0.8, 1.8) * p.Scale), Palette.Hex("#ff7a2a") with { A = 0.8f });
            }
        }
    }

    void DrawStump(PropPlacement p, Jitter rand, double r)
    {
        var R = r * 0.4;
        var n = 3 + (int)(rand.Next() * 3);
        for (var k = 0; k < n; k++)
        {
            var a = rand.Next() * Math.PI * 2;
            DrawLine(
                Xform(p, new Vector2((float)(Math.Cos(a) * R * 0.8), (float)(Math.Sin(a) * R * 0.8))),
                Xform(p, new Vector2((float)(Math.Cos(a) * R * 1.6), (float)(Math.Sin(a) * R * 1.6))),
                Palette.Hex("#3a3029"),
                (float)Math.Max(2, R * 0.3 * p.Scale),
                true
            );
        }
        var top = Blob(0, 0, R, rand, 14, 0.06);
        Stroke(Xform(p, top), Palette.Mix(Palette.Hex("#4a3e34"), Colors.Black, 0.6), (float)(2 * p.Scale));
        Fill(Xform(p, top), Palette.Hex("#4a3e34"));
        // Growth rings.
        for (var k = 1; k <= 3; k++)
            DrawArc(
                Xform(p, Vector2.Zero),
                (float)(R * (k / 4.2) * p.Scale),
                0,
                Mathf.Tau,
                16,
                Palette.Hex("#2a2420"),
                (float)Math.Max(0.5, 0.8 * p.Scale)
            );
        if (rand.Next() < 0.6)
        {
            DrawCircle(
                Xform(p, new Vector2((float)(R * 0.2), (float)(-R * 0.1))),
                (float)(R * 0.45 * p.Scale),
                Palette.Hex("#ff7a2a") with { A = 0.35f }
            );
            DrawCircle(
                Xform(p, new Vector2((float)(R * 0.2), (float)(-R * 0.1))),
                (float)(R * 0.15 * p.Scale),
                Palette.Hex("#ffa050")
            );
        }
    }

    /// <summary>Domed mushroom cap with a stem hint — TS mushroomCap.</summary>
    void MushroomCap(PropPlacement p, Jitter rand, double cx, double cy, double cr, Color color)
    {
        var cap = Poly.Ellipse(cx, cy, cr, cr * 0.72, rand.Next() * 0.4 - 0.2, 12);
        var edge = Palette.Mix(color, Colors.Black, 0.55);
        Stroke(Xform(p, cap), edge, (float)(1.6 * p.Scale));
        Fill(Xform(p, cap), color);
        // Rim shade + highlight spots.
        var rim = cap.Select(q => new Vector2(q.X, q.Y + (float)(cr * 0.18))).ToArray();
        Stroke(Xform(p, rim), Palette.Mix(color, Colors.Black, 0.3), (float)Math.Max(0.5, 0.9 * p.Scale));
        var spots = 2 + (int)(rand.Next() * 4);
        for (var k = 0; k < spots; k++)
        {
            var a = rand.Next() * Math.PI * 2;
            var d = Math.Sqrt(rand.Next()) * cr * 0.55;
            DrawCircle(
                Xform(p, new Vector2((float)(cx + Math.Cos(a) * d), (float)(cy + Math.Sin(a) * d * 0.7))),
                (float)(cr * rand.Range(0.08, 0.18) * p.Scale),
                Palette.Mix(color, Colors.White, 0.55)
            );
        }
    }
}
