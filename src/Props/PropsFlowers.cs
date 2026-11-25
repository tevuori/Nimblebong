#nullable enable
// Flower heads + flower patches for the flora drawer. A head style is a
// small top-down sprite (dandelion, poppy, daisy…); its default colors
// and size live in res://assets/config/flowers.json, a definition's
// "accents" override the colors. "flowerpatch" plants a field of
// 15–25 stems in an organic patch — a dandelion meadow, a poppy field.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

/// <summary>flowers.json — per head style: default colors + size.</summary>
public static class FlowerStyles
{
    public const string Path = "res://assets/config/flowers.json";

    public static readonly string[] Heads =
    [
        "simple", "dandelion", "poppy", "daisy", "bluebell", "cornflower",
        "lavender", "sunflower", "tulip", "clover",
    ];

    public record Style(List<Color> Colors, double Size);

    static Dictionary<string, Style>? _styles;

    public static Style For(string head) =>
        (_styles ??= Load()).TryGetValue(head, out var s) ? s : new([Palette.Hex("#f2d14b")], 1.6);

    static Dictionary<string, Style> Load()
    {
        var map = new Dictionary<string, Style>();
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file == null)
            return map;
        var json = Json.ParseString(file.GetAsText());
        if (json.VariantType != Variant.Type.Dictionary)
            throw new ConfigException($"flowers: {Path} is not a JSON object");
        var err = new List<string>();
        foreach (var (k, v) in json.AsGodotDictionary())
        {
            var id = k.AsString();
            if (!Heads.Contains(id))
            {
                GD.PushWarning($"flowers.{id}: unknown head style (have {string.Join("/", Heads)})");
                continue;
            }
            if (v.VariantType != Variant.Type.Dictionary)
            {
                err.Add($"flowers.{id}: expected {{colors, size}}");
                continue;
            }
            var d = v.AsGodotDictionary();
            var colors = new List<Color>();
            if (d.TryGetValue("colors", out var cv) && cv.VariantType == Variant.Type.Array)
                foreach (var c in cv.AsGodotArray())
                    colors.Add(new Color(c.AsString()));
            else
                err.Add($"flowers.{id}.colors: expected array of colors");
            var size = d.TryGetValue("size", out var sv) && sv.VariantType is Variant.Type.Int or Variant.Type.Float
                ? sv.AsDouble()
                : 1.6;
            map[id] = new Style(colors.Count > 0 ? colors : [Palette.Hex("#f2d14b")], size);
        }
        if (err.Count > 0)
            throw new ConfigException(string.Join("\n", err));
        return map;
    }
}

public partial class PropsPainter
{
    /// <summary>Head color: the definition's accents when given, else the
    /// style's flowers.json palette.</summary>
    Color HeadColor(FloraCtx f, FlowerStyles.Style style) =>
        f.S.Accents.Count > 0 ? f.Accent() : style.Colors[(int)(f.R.Next() * style.Colors.Count) % style.Colors.Count];

    string PickHead(FloraCtx f) =>
        f.S.Heads.Count == 0 ? "simple" : f.S.Heads[(int)(f.R.Next() * f.S.Heads.Count) % f.S.Heads.Count];

    /// <summary>One flower head at local point c, `scale` × style size.</summary>
    void FlowerHead(FloraCtx f, Vector2 c, string head, double scale = 1)
    {
        var style = FlowerStyles.For(head);
        var r = style.Size * scale * f.R.Range(0.8, 1.2) * (f.S.AccentSize / 1.6);
        var col = HeadColor(f, style);
        var turn = f.R.Next() * Math.PI * 2;
        Vector2 At(double a, double d) => c + Polar(turn + a, d);
        switch (head)
        {
            case "dandelion":
                if (f.R.Next() < f.S.PuffChance)
                {
                    // Seed clock — pale disc with fine rays.
                    DrawCircle(Xform(f.P, c), W(f, r * 1.05), new Color(0.96f, 0.96f, 0.93f, 0.8f));
                    for (var k = 0; k < 10; k++)
                        DrawLine(Xform(f.P, c), Xform(f.P, At(k / 10.0 * Math.Tau, r * 1.15)),
                            new Color(0.85f, 0.85f, 0.8f, 0.7f), W(f, 0.25), true);
                }
                else
                {
                    for (var k = 0; k < 12; k++)
                        DrawCircle(Xform(f.P, At(k / 12.0 * Math.Tau, r * 0.7)), W(f, r * 0.38), col);
                    DrawCircle(Xform(f.P, c), W(f, r * 0.6), Palette.Mix(col, Palette.Hex("#c98a10"), 0.45));
                }
                break;
            case "poppy":
                for (var k = 0; k < 4; k++)
                    DrawCircle(Xform(f.P, At(k / 4.0 * Math.Tau, r * 0.45)), W(f, r * 0.72), col);
                DrawCircle(Xform(f.P, c), W(f, r * 0.3), Palette.Hex("#1e1a16"));
                break;
            case "daisy":
                for (var k = 0; k < 9; k++)
                {
                    var a = turn + k / 9.0 * Math.Tau;
                    var pc = c + Polar(a, r * 0.65);
                    Fill(Xform(f.P, Poly.Ellipse(pc.X, pc.Y, r * 0.5, r * 0.2, a, 6)), col);
                }
                DrawCircle(Xform(f.P, c), W(f, r * 0.34), Palette.Hex("#f2c23a"));
                break;
            case "bluebell":
                // Two or three drooping bells along a short arc.
                for (var k = 0; k < 3; k++)
                {
                    var bc = At(k * 0.7, r * (0.2 + k * 0.6));
                    Fill(Xform(f.P, Poly.Ellipse(bc.X, bc.Y, r * 0.42, r * 0.3, turn + k * 0.7, 7)), col);
                    DrawCircle(Xform(f.P, bc + Polar(turn + k * 0.7, r * 0.3)), W(f, r * 0.12), Palette.Tint(col, 50));
                }
                break;
            case "cornflower":
            {
                var star = new Vector2[16];
                for (var k = 0; k < 16; k++)
                    star[k] = At(k / 16.0 * Math.Tau, r * (k % 2 == 0 ? 1.0 : 0.62));
                Fill(Xform(f.P, star), col);
                DrawCircle(Xform(f.P, c), W(f, r * 0.28), Palette.Mix(col, Palette.Hex("#1c2350"), 0.6));
                break;
            }
            case "lavender":
                // Spike of florets along a short stroke.
                for (var k = 0; k < 6; k++)
                    DrawCircle(Xform(f.P, At(0, r * (k * 0.32 - 0.8)) + Polar(turn + Math.PI / 2, (k % 2 - 0.5) * r * 0.3)),
                        W(f, r * 0.24), k % 2 == 0 ? col : Palette.Tint(col, 18));
                break;
            case "sunflower":
                for (var k = 0; k < 14; k++)
                {
                    var a = turn + k / 14.0 * Math.Tau;
                    var pc = c + Polar(a, r * 0.78);
                    Fill(Xform(f.P, Poly.Ellipse(pc.X, pc.Y, r * 0.42, r * 0.17, a, 6)), col);
                }
                DrawCircle(Xform(f.P, c), W(f, r * 0.55), Palette.Hex("#5a3a1e"));
                DrawCircle(Xform(f.P, c + new Vector2((float)(-r * 0.12), (float)(-r * 0.12))), W(f, r * 0.3),
                    Palette.Hex("#7a5228"));
                break;
            case "tulip":
                for (var k = 0; k < 3; k++)
                {
                    var a = turn + k / 3.0 * Math.Tau;
                    var pc = c + Polar(a, r * 0.25);
                    Fill(Xform(f.P, Poly.Ellipse(pc.X, pc.Y, r * 0.55, r * 0.38, a, 7)), k == 0 ? Palette.Tint(col, 20) : col);
                }
                DrawCircle(Xform(f.P, c), W(f, r * 0.15), Palette.Mix(col, Palette.Hex("#3a2010"), 0.5));
                break;
            case "clover":
                for (var k = 0; k < 7; k++)
                    DrawCircle(Xform(f.P, At(k / 7.0 * Math.Tau, r * 0.35)), W(f, r * 0.4), k % 2 == 0 ? col : Palette.Tint(col, 25));
                break;
            default: // simple five-petal
                for (var k = 0; k < 5; k++)
                    DrawCircle(Xform(f.P, At(k / 5.0 * Math.Tau, r * 0.75)), W(f, r * 0.62), col);
                DrawCircle(Xform(f.P, c), W(f, r * 0.42), Palette.Mix(col, Palette.Hex("#e0a020"), 0.6));
                break;
        }
    }

    // --- flowerpatch: a field of stems in an organic clump ------------------
    void FloraFlowerPatch(FloraCtx f)
    {
        // Patch outline: a lumpy ellipse; stems cluster toward the middle.
        var stretch = f.R.Range(0.7, 1.0);
        var spin = f.R.Next() * Math.PI;
        var lobes = new double[5];
        for (var k = 0; k < lobes.Length; k++)
            lobes[k] = f.R.Range(0.75, 1.15);
        Vector2 InPatch()
        {
            var a = f.R.Next() * Math.Tau;
            var lobe = lobes[(int)(a / Math.Tau * lobes.Length) % lobes.Length];
            var d = f.S.Radius * lobe * Math.Pow(f.R.Next(), 0.65);
            var v = new Vector2((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d * stretch));
            return v.Rotated((float)spin);
        }

        // Soft green underlay so the patch reads as one mass, then grass.
        FloraShade(f, f.S.Radius * 0.85, 0.16);
        var blades = (int)(f.Count * f.S.Underlay * 1.5);
        for (var i = 0; i < blades; i++)
        {
            var b = InPatch();
            var a = f.R.Next() * Math.Tau;
            var t = b + Polar(a, f.Len() * 0.6);
            DrawLine(Xform(f.P, b), Xform(f.P, t), Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.4, 1)), W(f, f.S.Width), true);
        }

        // Stems: two tiny leaves + a head, drawn north→south so heads
        // further south overlap the ones behind.
        var stems = Enumerable.Range(0, f.Count).Select(_ => InPatch()).OrderBy(v => v.Y).ToList();
        foreach (var s in stems)
        {
            var la = f.R.Next() * Math.Tau;
            var leaf = Palette.Mix(f.Dark, f.Leaf, f.R.Range(0.5, 1));
            foreach (var side in new[] { 0.0, Math.PI })
            {
                var lc = s + Polar(la + side, f.S.Radius * 0.07);
                Fill(Xform(f.P, Poly.Ellipse(lc.X, lc.Y, f.S.Radius * 0.09, f.S.Radius * 0.04, la + side, 6)), leaf);
            }
            FlowerHead(f, s, PickHead(f));
        }
    }
}
