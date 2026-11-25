#nullable enable
// Surface definitions — ground areas painted under props:
// res://assets/config/surfaces/<id>.json. A scene stores a surface as an
// area shape of kind "surface:<id>", so a new surface (marsh, scree,
// dunes…) is one JSON file: a wash color with a soft feathered edge, a
// rim, a repeating pattern (dots, strokes, ripples, waves, cracks,
// stones, hatch) and props scattered inside at render time.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using JsonDict = Godot.Collections.Dictionary;

namespace DmMap;

public class SurfacePattern
{
    public static readonly string[] Types = ["dots", "strokes", "ripples", "waves", "cracks", "stones", "hatch"];
    public string Type = "dots";
    public string Color = "#00000033";
    public double Alpha = 0.5;
    public double Spacing = 14;
    public double Size = 3;
    /// <summary>Main direction (radians) for strokes / waves / hatch.</summary>
    public double Angle;
    /// <summary>Optional glow under cracks (lava) — color spec.</summary>
    public string? Glow;
}

public class SurfaceScatter
{
    public string Kind = "";
    public double Spacing = 30;
    public double Chance = 0.5;
    public double ScaleMin = 0.8, ScaleMax = 1.2;
    /// <summary>Scene scale (px) before def scale — fill.json size if 0.</summary>
    public double Size;
}

public class SurfaceDef
{
    public string Id = "";
    public string Label = "";
    public string Category = "Povrchy";
    public List<string> Biomes = [];
    public string Color = "#00000000";
    public double Alpha = 0.5;
    /// <summary>Soft outer edge width (px) and its step count.</summary>
    public double Feather = 8;
    public string? EdgeColor;
    public double EdgeAlpha = 0.4;
    public double EdgeWidth = 3;
    public List<SurfacePattern> Patterns = [];
    public List<SurfaceScatter> Scatter = [];

    public bool InBiome(string biome) => Biomes.Count == 0 || Biomes.Contains(biome);
}

public static class SurfaceDefs
{
    public const string Dir = "res://assets/config/surfaces";

    static Dictionary<string, SurfaceDef>? _defs;

    public static IReadOnlyDictionary<string, SurfaceDef> All => _defs ??= LoadAll();

    public static bool TryGet(string id, out SurfaceDef def) => All.TryGetValue(id, out def!);

    /// <summary>Def of a scene shape kind "surface:&lt;id&gt;".</summary>
    public static SurfaceDef? ForKind(string kind) =>
        Kinds.IsSurface(kind) && TryGet(kind[Kinds.SurfacePrefix.Length..], out var d) ? d : null;

    public static List<SurfaceDef> ForBiome(string biome) =>
        [.. All.Values.Where(d => d.InBiome(biome)).OrderBy(d => d.Id, StringComparer.Ordinal)];

    public static void Reload() => _defs = null;

    static Dictionary<string, SurfaceDef> LoadAll()
    {
        var defs = new Dictionary<string, SurfaceDef>();
        if (!DirAccess.DirExistsAbsolute(Dir))
            return defs;
        var errors = new List<string>();
        foreach (var f in DirAccess.GetFilesAt(Dir).Where(f => f.EndsWith(".json")).OrderBy(f => f, StringComparer.Ordinal))
        {
            var id = f[..^5];
            using var file = FileAccess.Open($"{Dir}/{f}", FileAccess.ModeFlags.Read);
            var json = Json.ParseString(file?.GetAsText() ?? "");
            if (json.VariantType != Variant.Type.Dictionary)
            {
                errors.Add($"surfaces.{id}: not a JSON object");
                continue;
            }
            var err = new List<string>();
            var def = Parse(id, json.AsGodotDictionary(), err);
            if (err.Count > 0)
                errors.AddRange(err);
            else
                defs[id] = def;
        }
        if (errors.Count > 0)
            throw new ConfigException(string.Join("\n", errors));
        return defs;
    }

    static SurfaceDef Parse(string id, JsonDict d, List<string> err)
    {
        var p = $"surfaces.{id}";
        Warn(d, p, ["label", "category", "biomes", "fill", "edge", "patterns", "scatter"]);
        var def = new SurfaceDef { Id = id };
        def.Label = Str(d, "label", p, err) ?? id;
        def.Category = d.TryGetValue("category", out var cv) ? cv.AsString() : def.Category;
        if (d.TryGetValue("biomes", out var bv) && bv.VariantType == Variant.Type.Array)
            def.Biomes = [.. bv.AsGodotArray().Select(v => v.AsString())];
        if (Obj(d, "fill", p, err, required: true) is { } fill)
        {
            Warn(fill, $"{p}.fill", ["color", "alpha", "feather"]);
            def.Color = ColorSpec(fill, "color", $"{p}.fill", err) ?? def.Color;
            def.Alpha = Num(fill, "alpha", def.Alpha, $"{p}.fill", err, 0, 1);
            def.Feather = Num(fill, "feather", def.Feather, $"{p}.fill", err, 0, 200);
        }
        if (Obj(d, "edge", p, err) is { } edge)
        {
            Warn(edge, $"{p}.edge", ["color", "alpha", "width"]);
            def.EdgeColor = ColorSpec(edge, "color", $"{p}.edge", err);
            def.EdgeAlpha = Num(edge, "alpha", def.EdgeAlpha, $"{p}.edge", err, 0, 1);
            def.EdgeWidth = Num(edge, "width", def.EdgeWidth, $"{p}.edge", err, 0, 100);
        }
        if (d.TryGetValue("patterns", out var pv))
        {
            if (pv.VariantType != Variant.Type.Array)
                err.Add($"{p}.patterns: expected array");
            else
            {
                var i = 0;
                foreach (var item in pv.AsGodotArray())
                {
                    var at = $"{p}.patterns[{i++}]";
                    if (item.VariantType != Variant.Type.Dictionary)
                    {
                        err.Add($"{at}: expected object");
                        continue;
                    }
                    var o = item.AsGodotDictionary();
                    Warn(o, at, ["type", "color", "alpha", "spacing", "size", "angle", "glow"]);
                    var pat = new SurfacePattern();
                    var type = Str(o, "type", at, err);
                    if (type != null && !SurfacePattern.Types.Contains(type))
                        err.Add($"{at}.type: expected {string.Join("/", SurfacePattern.Types)}");
                    pat.Type = type ?? pat.Type;
                    pat.Color = ColorSpec(o, "color", at, err) ?? pat.Color;
                    pat.Alpha = Num(o, "alpha", pat.Alpha, at, err, 0, 1);
                    pat.Spacing = Num(o, "spacing", pat.Spacing, at, err, 3, 400);
                    pat.Size = Num(o, "size", pat.Size, at, err, 0.2, 200);
                    pat.Angle = Num(o, "angle", 0, at, err, -360, 360) * Math.PI / 180;
                    pat.Glow = ColorSpec(o, "glow", at, err);
                    def.Patterns.Add(pat);
                }
            }
        }
        if (d.TryGetValue("scatter", out var sv))
        {
            if (sv.VariantType != Variant.Type.Array)
                err.Add($"{p}.scatter: expected array");
            else
            {
                var i = 0;
                foreach (var item in sv.AsGodotArray())
                {
                    var at = $"{p}.scatter[{i++}]";
                    if (item.VariantType != Variant.Type.Dictionary)
                    {
                        err.Add($"{at}: expected object");
                        continue;
                    }
                    var o = item.AsGodotDictionary();
                    Warn(o, at, ["kind", "spacing", "chance", "scale", "size"]);
                    var sc = new SurfaceScatter { Kind = Str(o, "kind", at, err) ?? "" };
                    if (sc.Kind != "" && !SceneView.TryMapBuiltinProp(sc.Kind, out _) && !PropDefs.TryGet(sc.Kind, out _))
                        err.Add($"{at}.kind: unknown prop kind \"{sc.Kind}\"");
                    sc.Spacing = Num(o, "spacing", sc.Spacing, at, err, 4, 1000);
                    sc.Chance = Num(o, "chance", sc.Chance, at, err, 0, 1);
                    sc.Size = Num(o, "size", 0, at, err, 0, 400);
                    if (o.TryGetValue("scale", out var scv) && scv.VariantType == Variant.Type.Array
                        && scv.AsGodotArray() is { Count: 2 } arr)
                        (sc.ScaleMin, sc.ScaleMax) = (arr[0].AsDouble(), arr[1].AsDouble());
                    def.Scatter.Add(sc);
                }
            }
        }
        return def;
    }

    static void Warn(JsonDict d, string path, string[] known)
    {
        foreach (var k in d.Keys)
            if (!known.Contains(k.AsString()))
                GD.PushWarning($"{path}: unknown key \"{k.AsString()}\"");
    }

    static string? Str(JsonDict d, string key, string at, List<string> err)
    {
        if (!d.TryGetValue(key, out var v))
        {
            err.Add($"{at}.{key}: required string");
            return null;
        }
        if (v.VariantType == Variant.Type.String)
            return v.AsString();
        err.Add($"{at}.{key}: expected string");
        return null;
    }

    static JsonDict? Obj(JsonDict d, string key, string at, List<string> err, bool required = false)
    {
        if (!d.TryGetValue(key, out var v))
        {
            if (required)
                err.Add($"{at}.{key}: required object");
            return null;
        }
        if (v.VariantType == Variant.Type.Dictionary)
            return v.AsGodotDictionary();
        err.Add($"{at}.{key}: expected object");
        return null;
    }

    static double Num(JsonDict d, string key, double fallback, string at, List<string> err, double min, double max)
    {
        if (!d.TryGetValue(key, out var v))
            return fallback;
        if (v.VariantType is Variant.Type.Int or Variant.Type.Float && v.AsDouble() >= min && v.AsDouble() <= max)
            return v.AsDouble();
        err.Add($"{at}.{key}: expected number in [{min}, {max}]");
        return fallback;
    }

    static string? ColorSpec(JsonDict d, string key, string at, List<string> err)
    {
        if (!d.TryGetValue(key, out var v))
            return null;
        var s = v.VariantType == Variant.Type.String ? v.AsString() : "";
        if (s.StartsWith('@') && s.Length > 1 || s.Length is 7 or 9 && s[0] == '#' && s[1..].All(Uri.IsHexDigit))
            return s;
        err.Add($"{at}.{key}: expected \"#rrggbb[aa]\" or \"@biomeColor\"");
        return null;
    }
}
