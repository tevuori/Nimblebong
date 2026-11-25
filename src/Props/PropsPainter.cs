// Props albedo painter — hand-drawn map objects in the watercolor+ink
// Nimblehold style, ported from the web renderer's scene-trees /
// scene-props / scene-roofs. Every prop is a small polygon/arc
// composition drawn in the prop's local frame; per-instance jitter in
// hue, scale and rotation comes from Rng.Noise seeded by position.
// Props draw sorted by Y so southern objects overlap northern ones.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class PropsPainter : Node2D
{
    // Muted earth palette in the AlbedoPainter register.
    public Color Foliage = Palette.Hex("#6d8f46");
    public Color FoliageWarm = Palette.Hex("#8a9a4a");
    public Color FoliageCool = Palette.Hex("#527a52");
    public Color PineGreen = Palette.Hex("#4f6f48");
    public Color Stone = Palette.Hex("#a4a68e");
    public Color Wood = Palette.Hex("#6e563c");
    public Color Wall = Palette.Hex("#cbb98e");
    public Color Roof = Palette.Hex("#ac6948");
    public Color Ink = Palette.Hex("#3a3527");

    List<PropPlacement> _props = [];

    public void SetProps(IEnumerable<PropPlacement> props)
    {
        // Painter's order — northern props first so canopies and roofs
        // overlap whatever stands north of them.
        _props = [.. props.OrderBy(p => p.Y)];
        QueueRedraw();
    }

    // --- deterministic per-prop rand stream (the TS `rand()` port) ----
    struct Jitter
    {
        double _seed;
        int _i;
        public Jitter(double seed) => (_seed, _i) = (seed, 0);
        public double Next() => Rng.Noise(_seed + _i++ * 13.37);
        public double Range(double a, double b) => a + Next() * (b - a);
    }

    /// <summary>Fill a polygon only when it triangulates — the same
    /// gate as AlbedoPainter.Fill.</summary>
    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    /// <summary>Closed ink stroke — appends the first point because
    /// DrawPolyline does not close contours.</summary>
    void Stroke(Vector2[] poly, Color color, float width)
    {
        if (poly.Length < 2)
            return;
        var closed = new Vector2[poly.Length + 1];
        Array.Copy(poly, closed, poly.Length);
        closed[^1] = poly[0];
        DrawPolyline(closed, color, width, true);
    }

    // --- prop-local frame ------------------------------------------------
    static Vector2 Xform(PropPlacement p, Vector2 local)
    {
        var c = Math.Cos(p.Rotation);
        var s = Math.Sin(p.Rotation);
        return new Vector2(
            p.X + (float)((local.X * c - local.Y * s) * p.Scale),
            p.Y + (float)((local.X * s + local.Y * c) * p.Scale)
        );
    }

    static Vector2[] Xform(PropPlacement p, IEnumerable<Vector2> pts) =>
        pts.Select(q => Xform(p, q)).ToArray();

    /// <summary>Translate + scale only — for parts that must keep a
    /// screen-south direction (trunks, eaves) no matter how the prop
    /// is rotated.</summary>
    static Vector2 XformU(PropPlacement p, Vector2 local) =>
        new(p.X + local.X * p.Scale, p.Y + local.Y * p.Scale);

    static Vector2[] XformU(PropPlacement p, IEnumerable<Vector2> pts) =>
        pts.Select(q => XformU(p, q)).ToArray();

    /// <summary>Jittered blob polygon — the TS `blob()` helper.</summary>
    static Vector2[] Blob(
        double cx,
        double cy,
        double rad,
        Jitter rand,
        int n = 12,
        double jit = 0.12
    )
    {
        var pts = new Vector2[n];
        for (var k = 0; k < n; k++)
        {
            var a = (k / (double)n) * Math.PI * 2;
            var rr = rad * (1 - jit + rand.Next() * jit * 2);
            pts[k] = new Vector2((float)(cx + Math.Cos(a) * rr), (float)(cy + Math.Sin(a) * rr));
        }
        return pts;
    }

    /// <summary>Per-instance green: hue nudge toward warm/cool + a
    /// whisper of black or white — the TS color jitter.</summary>
    Color JitterGreen(Color baseColor, Jitter rand)
    {
        var t = rand.Next();
        var c = Palette.Mix(
            baseColor,
            t < 0.5 ? FoliageWarm : FoliageCool,
            Math.Abs(t - 0.5) * 0.7
        );
        return Palette.Mix(c, rand.Next() < 0.5 ? Colors.Black : Colors.White, rand.Next() * 0.12);
    }

    public override void _Draw()
    {
        foreach (var p in _props)
        {
            switch (p.Kind)
            {
                case PropKind.Tree:
                    DrawSpeciesTree(p);
                    break;
                case PropKind.Pine:
                    DrawPine(p, BiomeCfg?.SeasonLook == "winter" ? SceneConfig.Trees().ConiferSnow : 0);
                    break;
                case PropKind.Rock:
                    DrawRock(p);
                    break;
                case PropKind.Bush:
                    DrawBush(p);
                    break;
                case PropKind.House:
                    DrawHouse(p);
                    break;
                case PropKind.Tent:
                    DrawTent(p);
                    break;
                case PropKind.Campfire:
                    DrawCampfire(p);
                    break;
                case PropKind.Well:
                    DrawWell(p);
                    break;
                case PropKind.Ruin:
                    DrawRuin(p);
                    break;
                case PropKind.Grave:
                    DrawGrave(p);
                    break;
                case PropKind.Bones:
                    DrawBones(p);
                    break;
                case PropKind.Statue:
                    DrawStatue(p);
                    break;
                case PropKind.StoneCircle:
                    DrawStoneCircle(p);
                    break;
                case PropKind.Crystal:
                    DrawCrystal(p);
                    break;
                case PropKind.Bale:
                    DrawBale(p);
                    break;
                case PropKind.Crate:
                    DrawCrate(p);
                    break;
                case PropKind.Barrel:
                    DrawBarrel(p);
                    break;
                case PropKind.Tower:
                    DrawTower(p);
                    break;
                case PropKind.Stall:
                    DrawStall(p);
                    break;
                case PropKind.Cart:
                    DrawCart(p);
                    break;
                case PropKind.Flag:
                    DrawFlag(p);
                    break;
                case PropKind.Barricade:
                    DrawBarricade(p);
                    break;
                case PropKind.Lantern:
                    DrawLantern(p);
                    break;
                case PropKind.Fountain:
                    DrawFountain(p);
                    break;
                case PropKind.Pillar:
                    DrawPillar(p);
                    break;
                case PropKind.Marker:
                    DrawMarker(p);
                    break;
                case PropKind.Dock:
                    DrawDock(p);
                    break;
                case PropKind.Boat:
                    DrawBoat(p);
                    break;
                case PropKind.Chest:
                    DrawChest(p);
                    break;
                case PropKind.Portal:
                    DrawPortal(p);
                    break;
                case PropKind.Door:
                    DrawDoor(p);
                    break;
                case PropKind.Window:
                    DrawWindow(p);
                    break;
                case PropKind.Table:
                    DrawTable(p);
                    break;
                case PropKind.Bed:
                    DrawBed(p);
                    break;
                case PropKind.Shelf:
                    DrawShelf(p);
                    break;
                case PropKind.Rug:
                    DrawRug(p);
                    break;
                case PropKind.Fireplace:
                    DrawFireplace(p);
                    break;
                case PropKind.Label:
                    DrawLabel(p);
                    break;
            }
        }
    }

    // --- label --------------------------------------------------------------
    // Paper tag: cream rounded rect + ink frame + the caption text.
    static readonly Font LabelFont = ThemeDB.FallbackFont;

    void DrawLabel(PropPlacement p)
    {
        var text = string.IsNullOrWhiteSpace(p.Text) ? "?" : p.Text;
        var size = Mathf.Max(13, (int)(p.Scale * 0.9));
        var w = LabelFont.GetStringSize(text, fontSize: size).X;
        var h = size * 1.3f;
        var rect = new Rect2(p.X - w / 2 - 8, p.Y - h / 2, w + 16, h);
        var tag = new Vector2[]
        {
            rect.Position,
            new(rect.End.X, rect.Position.Y),
            rect.End,
            new(rect.Position.X, rect.End.Y),
        };
        Fill(tag, new Color(0.93f, 0.9f, 0.78f, 0.92f));
        DrawPolyline([.. tag, tag[0]], new Color(0.25f, 0.22f, 0.16f, 0.85f), 1.2f);
        DrawString(
            LabelFont,
            new Vector2((float)(rect.Position.X + 8), (float)(p.Y + size * 0.36)),
            text,
            fontSize: size,
            modulate: new Color(0.18f, 0.15f, 0.1f)
        );
    }

    // Soft contact shadow SE of the object — the heightmap raymarch adds
    // the directional shadow, this anchors the prop to the ground.
    void ContactShadow(PropPlacement p, double rx, double ry)
    {
        Fill(
            Xform(p, Poly.Ellipse(rx * 0.22, ry * 0.3, rx, ry * 0.8, 0, 12)),
            new Color(0.07f, 0.1f, 0.06f, 0.16f)
        );
    }

    // --- deciduous tree ---------------------------------------------------
    // Two seeded variants: a round oak-like lobed canopy, and a clump
    // variant where the lobes sit apart so sky shows between them.
    void DrawTree(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 24 * rand.Range(0.85, 1.15);
        var color = JitterGreen(Foliage, rand);
        var edge = Palette.Mix(color, Palette.Hex("#0d140a"), 0.5);
        var clump = rand.Next() < 0.3;

        ContactShadow(p, r * 0.95, r * 0.8);

        var parts = new List<Vector2[]>();
        var lobes = clump ? 4 + (int)(rand.Next() * 3) : 7 + (int)(rand.Next() * 4);
        var rot = rand.Next() * Math.PI * 2;
        for (var k = 0; k < lobes; k++)
        {
            var a = rot + (k / (double)lobes) * Math.PI * 2 + (rand.Next() - 0.5) * 0.5;
            var d = r * (clump ? 0.58 : 0.46) * rand.Range(0.8, 1.1);
            var lr = clump ? rand.Range(0.3, 0.42) : rand.Range(0.4, 0.56);
            parts.Add(Blob(Math.Cos(a) * d, Math.Sin(a) * d, r * lr, rand));
        }
        if (!clump)
            parts.Add(Blob(0, 0, r * 0.55, rand));

        // Shared silhouette: thick edge stroke under every piece reads
        // as one ink outline around the whole canopy.
        foreach (var part in parts)
        {
            var w = Xform(p, part);
            Fill(w, edge);
            Stroke(w, edge, (float)(2.4 * p.Scale));
        }
        // Base shade, then lit lobes shrunk toward the NW sun.
        foreach (var part in parts)
            Fill(Xform(p, part), Palette.Mix(color, Colors.Black, 0.2));
        var off = new Vector2((float)(-r * 0.06), (float)(-r * 0.08));
        foreach (var part in parts)
        {
            var lit = part.Select(q => q * 0.86f + off).ToArray();
            Fill(
                Xform(p, lit),
                Palette.Mix(color, rand.Next() < 0.5 ? Colors.Black : Colors.White, rand.Next() * 0.12)
            );
        }
        // Highlight blob NW, shade blob SE — the watercolor sun side.
        Fill(
            Xform(p, Blob(-r * 0.22, -r * 0.26, r * rand.Range(0.3, 0.42), rand, 10, 0.18)),
            Palette.Mix(color, Palette.Hex("#fffbe0"), 0.18)
        );
        Fill(
            Xform(p, Blob(r * 0.24, r * 0.2, r * rand.Range(0.22, 0.32), rand, 10, 0.18)),
            Palette.Mix(color, Colors.Black, 0.14)
        );
        // Trunk peeking out past the canopy's south edge — screen-south
        // (unrotated), drawn last so the lobes don't swallow it.
        var trunk = XformU(p, Poly.Ellipse(r * 0.06, r * 0.8, r * 0.07, r * 0.18, 0.1, 8));
        Fill(trunk, Palette.Mix(Wood, Colors.Black, 0.12));
        Stroke(trunk, Palette.Mix(Wood, Colors.Black, 0.5), (float)Math.Max(0.6, 0.9 * p.Scale));
        // Leaf-arc texture in two tones.
        foreach (var tone in new[] { Palette.Mix(color, Colors.Black, 0.2), Palette.Mix(color, Colors.White, 0.18) })
        {
            var n = (int)(r * 0.55);
            for (var k = 0; k < n; k++)
            {
                var a = rand.Next() * Math.PI * 2;
                var d = Math.Sqrt(rand.Next()) * r * 0.85;
                var s = 1.4 + rand.Next() * 2.4;
                DrawArc(
                    Xform(p, new Vector2((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d))),
                    (float)(s * p.Scale),
                    (float)(0.2 + p.Rotation),
                    (float)(Math.PI * 1.15 + p.Rotation),
                    5,
                    tone,
                    (float)Math.Max(0.6, 0.8 * p.Scale)
                );
            }
        }
    }

    // --- conifer ------------------------------------------------------------
    // Stacked star layers shrinking upward — the TS `conifer()` port.
    void DrawPine(PropPlacement p, double snow = 0, Color? colorOverride = null)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 22 * rand.Range(0.85, 1.15);
        var color = colorOverride ?? JitterGreen(PineGreen, rand);
        var edge = Palette.Mix(color, Palette.Hex("#0d140a"), 0.5);

        ContactShadow(p, r * 0.9, r * 0.8);

        var layers = 3 + (int)(rand.Next() * 3);
        var points = 8 + (int)(rand.Next() * 5);
        for (var l = 0; l < layers; l++)
        {
            var R = r * (1 - l * (0.62 / layers));
            var rot = rand.Next() * Math.PI;
            var star = new Vector2[points * 2];
            for (var k = 0; k < points * 2; k++)
            {
                var a = rot + (k / (double)(points * 2)) * Math.PI * 2;
                var rr = R * (k % 2 == 1 ? 0.7 + rand.Next() * 0.08 : 0.92 + rand.Next() * 0.1);
                star[k] = new Vector2((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
            }
            var w = Xform(p, star);
            if (l == 0)
                Stroke(w, edge, (float)(3 * p.Scale));
            Fill(
                w,
                Palette.Mix(color, l % 2 == 1 ? Colors.White : Colors.Black, 0.08 + (l / (double)layers) * 0.1)
            );
            Stroke(w, Palette.Mix(color, Colors.Black, 0.35), (float)Math.Max(0.6, 0.9 * p.Scale));
        }
        // NW snow-light dusting on the top layer + dark heart.
        var R2 = r * (1 - (layers - 1) * (0.62 / layers));
        Fill(
            Xform(p, Poly.Ellipse(-R2 * 0.18, -R2 * 0.2, R2 * 0.5, R2 * 0.4, -0.4, 9)),
            Palette.TintAlpha(color, 40, 0.35)
        );
        // Winter / snowfir: snow wedges on the north-west of each layer.
        if (snow > 0)
            for (var l = 0; l < layers; l++)
            {
                var R = r * (1 - l * (0.62 / layers));
                Fill(
                    Xform(p, Poly.Ellipse(-R * 0.22, -R * 0.24, R * 0.62 * snow, R * 0.5 * snow, -0.4, 9)),
                    SceneConfig.Trees().SnowColor
                );
            }
        DrawCircle(
            Xform(p, Vector2.Zero),
            (float)Math.Max(1.2, r * 0.06 * p.Scale),
            Palette.Mix(color, Colors.Black, 0.45)
        );
    }

    // --- rock -----------------------------------------------------------------
    // Angular 7-gon with a sunlit NW facet and a crack; a seeded
    // variant makes a tall crag with a small stone at its foot.
    void DrawRock(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 13 * rand.Range(0.9, 1.3);
        var color = Palette.Mix(
            Stone,
            rand.Next() < 0.5 ? Colors.White : Palette.Hex("#5a5c48"),
            rand.Next() * 0.18
        );
        var edge = Palette.Mix(color, Palette.Hex("#3d4137"), 0.7);

        ContactShadow(p, r * 0.9, r * 0.7);

        if (rand.Next() < 0.3)
        {
            // Tall crag + foot stone (TS rock model 1).
            Vector2[] Crag(double cx, double cy, double s, double tall)
            {
                var pts = new Vector2[7];
                for (var i = 0; i < 7; i++)
                {
                    var a = (i / 7.0) * Math.PI * 2;
                    var rad = s * (0.82 + (i % 3) * 0.09);
                    pts[i] = new Vector2(
                        (float)(cx + Math.Cos(a) * rad),
                        (float)(cy + Math.Sin(a) * rad * tall)
                    );
                }
                return pts;
            }
            var main = Xform(p, Crag(-r * 0.1, -r * 0.1, r * 0.68, 1.45));
            Fill(main, color);
            Stroke(main, edge, (float)Math.Max(0.8, 1.3 * p.Scale));
            var foot = Xform(p, Crag(r * 0.48, r * 0.55, r * 0.34, 0.9));
            Fill(foot, Palette.Tint(color, -8));
            Stroke(foot, edge, (float)Math.Max(0.8, 1.1 * p.Scale));
            DrawPolyline(
                Xform(
                    p,
                    new[]
                    {
                        new Vector2((float)(-r * 0.4), (float)(-r * 0.75)),
                        new Vector2((float)(-r * 0.1), (float)(-r * 0.3)),
                        new Vector2((float)(-r * 0.32), (float)(r * 0.1)),
                    }
                ),
                new Color(1f, 1f, 1f, 0.18f),
                (float)Math.Max(0.8, 1.6 * p.Scale)
            );
        }
        else
        {
            var pts = new Vector2[7];
            for (var i = 0; i < 7; i++)
            {
                var a = (i / 7.0) * Math.PI * 2;
                var rad = r * (0.8 + (i % 3) * 0.08);
                pts[i] = new Vector2((float)(Math.Cos(a) * rad), (float)(Math.Sin(a) * rad));
            }
            var w = Xform(p, pts);
            Fill(w, color);
            // NW facet — the sun side catches light.
            Fill(
                Xform(p, new[] { pts[0], pts[1], Vector2.Zero, pts[6] }),
                new Color(1f, 1f, 1f, 0.14f)
            );
            Stroke(w, edge, (float)Math.Max(0.8, 1.4 * p.Scale));
            DrawPolyline(
                Xform(p, new[] { pts[2], Vector2.Zero, pts[5] }),
                new Color(0.22f, 0.26f, 0.22f, 0.4f),
                (float)Math.Max(0.6, 1.0 * p.Scale)
            );
        }
    }

    // --- bush -------------------------------------------------------------------
    // A handful of overlapping discs on golden-angle offsets; some get
    // blossom speckles.
    void DrawBush(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var r = 10 * rand.Range(0.9, 1.3);
        var color = JitterGreen(Palette.Mix(Foliage, Colors.Black, 0.15), rand);
        var edge = Palette.Mix(color, Palette.Hex("#2e3a22"), 0.65);

        ContactShadow(p, r * 1.05, r * 0.8);

        for (var i = 0; i < 5; i++)
        {
            var a = i * 2.4;
            var disc = Poly.Ellipse(
                Math.Cos(a) * r * 0.42,
                Math.Sin(a) * r * 0.35,
                r * 0.55,
                r * 0.48,
                rand.Next() * 0.8,
                9
            );
            var w = Xform(p, disc);
            Fill(w, edge);
            var inner = Poly.Ellipse(
                Math.Cos(a) * r * 0.42 - r * 0.05,
                Math.Sin(a) * r * 0.35 - r * 0.06,
                r * 0.46,
                r * 0.4,
                0,
                9
            );
            Fill(Xform(p, inner), Palette.Mix(color, Colors.White, rand.Next() * 0.1));
        }
        if (rand.Next() < 0.45)
        {
            var blossom = rand.Next() < 0.5 ? Palette.Hex("#e8d3dc") : Palette.Hex("#e6d68a");
            for (var i = 0; i < 5; i++)
            {
                var a = i * 2.4 + 1.1;
                DrawCircle(
                    Xform(p, new Vector2((float)(Math.Cos(a) * r * 0.5), (float)(Math.Sin(a) * r * 0.42))),
                    (float)Math.Max(0.9, 1.3 * p.Scale),
                    blossom
                );
            }
        }
    }

    // --- house ------------------------------------------------------------------
    // Oblique mini-building: dark wall face hanging off the south edge,
    // gable roof with the ridge along the longer side, sunlit north
    // slope vs. shaded south slope, ridge cap, chimney.
    void DrawHouse(PropPlacement p)
    {
        var rand = new Jitter(p.VarSeed);
        var w = rand.Range(52, 64);
        var h = rand.Range(38, 46);
        var roof = Palette.Mix(
            Roof,
            rand.Next() < 0.5 ? Palette.Hex("#8a4f38") : Palette.Hex("#c08a5a"),
            rand.Next() * 0.35
        );
        var wallC = Palette.Mix(Wall, Colors.White, rand.Next() * 0.1);
        var edge = Palette.Hex("#4c4935");
        var wallH = 7 + rand.Next() * 4;

        var x0 = -w / 2;
        var x1 = w / 2;
        var y0 = -h / 2;
        var y1 = h / 2;
        var ym = (y0 + y1) / 2 + (rand.Next() - 0.5) * h * 0.1;

        ContactShadow(p, w * 0.55, h * 0.5 + wallH);

        // Wall faces — the oblique extrusion always drops screen-south,
        // so a rotated house still shows its south(-east/-west) walls.
        var foot = new[]
        {
            new Vector2((float)x0, (float)y0),
            new Vector2((float)x1, (float)y0),
            new Vector2((float)x1, (float)y1),
            new Vector2((float)x0, (float)y1),
        };
        var fw = Xform(p, foot);
        var ctr = Poly.Centroid(fw);
        var drop = new Vector2(0, (float)(wallH * p.Scale));
        var wallFaces = new List<(Vector2 A, Vector2 B)>();
        for (var i = 0; i < 4; i++)
        {
            var a = fw[i];
            var b = fw[(i + 1) % 4];
            if (((a + b) / 2).Y <= ctr.Y + 0.5f)
                continue;
            wallFaces.Add((a, b));
            var quad = new[] { a, b, b + drop, a + drop };
            Fill(quad, Palette.Mix(wallC, Colors.Black, 0.35));
            Stroke(quad, edge, (float)Math.Max(0.8, 1.4 * p.Scale));
        }
        // Door + a tiny window on the most southern wall face.
        if (wallFaces.Count > 0)
        {
            var face = wallFaces
                .OrderByDescending(f => ((f.A + f.B) / 2).Y)
                .First();
            var dir = (face.B - face.A).Normalized();
            var len = (face.B - face.A).Length();
            var doorW = (float)Math.Min(8 * p.Scale, len * 0.25);
            var at = face.A + dir * (len * (0.3f + (float)rand.Next() * 0.35f));
            var door = new[]
            {
                at,
                at + dir * doorW,
                at + dir * doorW + drop - new Vector2(0, 1),
                at + drop - new Vector2(0, 1),
            };
            Fill(door, Palette.Hex("#3a2c1c"));
            var wAt = face.A + dir * (at.X < (face.A.X + face.B.X) / 2 ? len * 0.75f : len * 0.12f);
            var winW = (float)Math.Min(5 * p.Scale, len * 0.15);
            Fill(
                new[]
                {
                    wAt + new Vector2(0, 1.5f),
                    wAt + dir * winW + new Vector2(0, 1.5f),
                    wAt + dir * winW + drop - new Vector2(0, 2),
                    wAt + drop - new Vector2(0, 2),
                },
                Palette.Hex("#2c2a22")
            );
        }

        // Roof — slight overhang past the walls, ridge along X.
        var ov = 1.6;
        var northFace = new[]
        {
            new Vector2((float)(x0 - ov), (float)(y0 - ov)),
            new Vector2((float)(x1 + ov), (float)(y0 - ov)),
            new Vector2((float)(x1 + ov), (float)ym),
            new Vector2((float)(x0 - ov), (float)ym),
        };
        var southFace = new[]
        {
            new Vector2((float)(x0 - ov), (float)ym),
            new Vector2((float)(x1 + ov), (float)ym),
            new Vector2((float)(x1 + ov), (float)(y1 + ov)),
            new Vector2((float)(x0 - ov), (float)(y1 + ov)),
        };
        // Sun is NW: the north slope is lit, the south slope shaded —
        // same `shadeColor` logic as the web roof pass.
        Fill(Xform(p, northFace), Palette.Mix(roof, Palette.Hex("#fff4dc"), 0.24));
        Fill(Xform(p, southFace), Palette.Mix(roof, Palette.Hex("#140e08"), 0.3));

        // Tile courses: staggered darker rows + a light stripe per face.
        foreach (var (face, lit) in new[] { (northFace, true), (southFace, false) })
        {
            var fy0 = lit ? y0 - ov : ym;
            var fy1 = lit ? ym : y1 + ov;
            for (var v = fy0 + 5.5; v < fy1 - 1; v += 5.5)
            {
                DrawPolyline(
                    Xform(
                        p,
                        new[]
                        {
                            new Vector2((float)(x0 - ov + 1), (float)v),
                            new Vector2((float)(x1 + ov - 1), (float)v),
                        }
                    ),
                    Palette.TintAlpha(roof, lit ? -30 : -46, 0.5),
                    0.9f
                );
            }
        }
        // Perimeter ink.
        Stroke(
            Xform(
                p,
                new[]
                {
                    new Vector2((float)(x0 - ov), (float)(y0 - ov)),
                    new Vector2((float)(x1 + ov), (float)(y0 - ov)),
                    new Vector2((float)(x1 + ov), (float)(y1 + ov)),
                    new Vector2((float)(x0 - ov), (float)(y1 + ov)),
                }
            ),
            edge,
            (float)Math.Max(0.9, 1.8 * p.Scale)
        );
        // Gable barge boards.
        foreach (var gx in new[] { x0 - ov + 1, x1 + ov - 1 })
            DrawPolyline(
                Xform(
                    p,
                    new[] { new Vector2((float)gx, (float)(y0 - ov)), new Vector2((float)gx, (float)(y1 + ov)) }
                ),
                Palette.Mix(roof, Palette.Hex("#1a120a"), 0.6),
                (float)Math.Max(0.8, 1.4 * p.Scale)
            );

        // Ridge cap — dark bed, lighter crest offset a hair north.
        var ridge = new[] { new Vector2((float)(x0 - ov), (float)ym), new Vector2((float)(x1 + ov), (float)ym) };
        DrawPolyline(Xform(p, ridge), Palette.Mix(roof, Colors.Black, 0.5), (float)(4.4 * p.Scale));
        DrawPolyline(Xform(p, ridge), Palette.Mix(roof, Colors.Black, 0.15), (float)(2.6 * p.Scale));
        DrawPolyline(
            Xform(p, ridge.Select(q => q + new Vector2(0, -0.8f)).ToArray()),
            Palette.Mix(roof, Palette.Hex("#fff2dc"), 0.3),
            (float)(0.9 * p.Scale)
        );
        // Ridge joints.
        for (var u = x0 + 4; u < x1; u += 7)
            DrawPolyline(
                Xform(
                    p,
                    new[] { new Vector2((float)u, (float)(ym - 1.6)), new Vector2((float)u, (float)(ym + 1.6)) }
                ),
                Palette.Mix(roof, Colors.Black, 0.4),
                0.7f
            );

        // Chimney — seeded along the ridge, sitting on the north slope.
        if (rand.Next() < 0.85)
        {
            var cs = Math.Max(4, Math.Min(w, h) * 0.15);
            var cu = x0 + w * (0.2 + rand.Next() * 0.55);
            var cv = ym - h * (0.12 + rand.Next() * 0.15);
            var rect = new[]
            {
                new Vector2((float)(cu - cs / 2), (float)(cv - cs / 2)),
                new Vector2((float)(cu + cs / 2), (float)(cv - cs / 2)),
                new Vector2((float)(cu + cs / 2), (float)(cv + cs / 2)),
                new Vector2((float)(cu - cs / 2), (float)(cv + cs / 2)),
            };
            Fill(Xform(p, rect), Palette.Hex("#3d342c"));
            var inner = rect.Select(q => new Vector2((float)(cu + (q.X - cu) * 0.78), (float)(cv + (q.Y - cv) * 0.78))).ToArray();
            Fill(Xform(p, inner), Palette.Hex("#7d6f60"));
            Stroke(Xform(p, rect), edge, 0.9f);
            // Cap shadow on the flue top.
            Fill(
                Xform(p, Poly.Ellipse(cu, cv - cs * 0.18, cs * 0.26, cs * 0.16, 0, 8)),
                Palette.Hex("#241d16")
            );
        }
    }
}
