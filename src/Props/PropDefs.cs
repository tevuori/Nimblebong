#nullable enable
// Prop definitions — data-driven map objects loaded from
// res://assets/config/props/<id>.json. A definition reuses one of the
// built-in C# prop drawers (its "base" kind) and restyles it: palette
// overrides, a global tint/HSV adjust, scale and heightfield size. A new
// variant (mossy boulder, red-capped mushroom, birch grove tree…) is a
// new JSON file — no C# change. The file name is the id a scene stores
// in prop.kind, so scenes stay plain strings.
//
// Validation follows SceneConfig: unknown keys warn, a bad value throws a
// ConfigException naming the exact path, so a typo fails loudly.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using JsonDict = Godot.Collections.Dictionary;

namespace DmMap;

/// <summary>One prop definition resolved from JSON.</summary>
public class PropDef
{
    public string Id = "";
    public string Label = "";
    /// <summary>Built-in drawer this definition restyles.</summary>
    public PropKind Base;
    /// <summary>Scene name of the base kind ("rock", "campfire"…).</summary>
    public string BaseName = "";
    /// <summary>Free-form grouping for tool palettes ("nature", "village"…).</summary>
    public string Category = "";
    /// <summary>Biomes the prop belongs to; empty = every biome.</summary>
    public List<string> Biomes = [];
    /// <summary>Multiplier on the placement scale.</summary>
    public float Scale = 1f;
    /// <summary>Heightfield multipliers — footprint radius and bump
    /// height (taller bump = longer cast shadow).</summary>
    public double Footprint = 1;
    public double Height = 1;
    /// <summary>Defaults applied when the scene prop leaves them unset.</summary>
    public string? Species;
    public int Model = -1;
    /// <summary>Painter palette slot → color spec ("#rrggbb[aa]" or
    /// "@biomeColor"). Slots: foliage, foliageWarm, foliageCool, pine,
    /// stone, wood, wall, roof, ink.</summary>
    public Dictionary<string, string> Palette = new();
    /// <summary>Mixed into every color the drawer emits.</summary>
    public string? TintColor;
    public double TintAmount;
    /// <summary>HSV adjust on every emitted color: hue shift in turns,
    /// saturation and value multipliers.</summary>
    public double Hue;
    public double Saturation = 1;
    public double Value = 1;

    /// <summary>Protection zone factor (placement.json semantics);
    /// 0 = unprotected; null = inherit from the base kind.</summary>
    public double? Protect;
    /// <summary>Must stay outside protection zones; null = inherit.</summary>
    public bool? KeepOut;

    /// <summary>Allowed in water (lily pads, reeds…); null = inherit
    /// from the base kind (Interactions.WaterKind).</summary>
    public bool? Water;
    /// <summary>Parametric plant shape — required when Base is Flora.</summary>
    public FloraSpec? Flora;

    public bool Restyles =>
        TintColor != null && TintAmount > 0 || Hue != 0 || Saturation != 1 || Value != 1;

    public bool InBiome(string biome) => Biomes.Count == 0 || Biomes.Contains(biome);
}

/// <summary>Parametric plant for the flora drawer (PropsFlora.cs):
/// one of a few top-down forms, sized and colored from JSON.</summary>
public class FloraSpec
{
    public static readonly string[] Forms =
        ["tuft", "fern", "flowers", "flowerpatch", "reeds", "mushrooms", "lilypads", "groundcover"];

    public string Form = "tuft";
    /// <summary>Blades / fronds / stems / caps / pads / blobs, min–max.</summary>
    public int CountMin = 5, CountMax = 9;
    /// <summary>Spread of the clump in prop-local px (24 px prop ≈ 12).</summary>
    public double Radius = 8;
    /// <summary>Blade / frond / stem length min–max (local px).</summary>
    public double LengthMin = 5, LengthMax = 9;
    /// <summary>Stroke width of blades and ribs (local px).</summary>
    public double Width = 1.2;
    /// <summary>Color specs ("#hex" / "@biomeColor"); LeafDark null =
    /// Leaf darkened.</summary>
    public string Leaf = "@foliage";
    public string? LeafDark;
    /// <summary>Flower heads, mushroom caps, cattails, pad blossoms.</summary>
    public List<string> Accents = [];
    /// <summary>Accent size (local px) and share of stems/pads that get one.</summary>
    public double AccentSize = 1.6;
    public double AccentChance = 0.5;
    /// <summary>Ink outline strength 0–1.</summary>
    public double Ink = 0.4;
    /// <summary>Flower head styles (flowers.json ids) for flowers /
    /// flowerpatch — picked per stem; empty = "simple".</summary>
    public List<string> Heads = [];
    /// <summary>Grass under a flower patch, 0–1.</summary>
    public double Underlay = 0.5;
    /// <summary>Dandelions shown as seed clocks, 0–1.</summary>
    public double PuffChance = 0.15;
}

public static class PropDefs
{
    public const string Dir = "res://assets/config/props";

    public static readonly string[] PaletteSlots =
    [
        "foliage", "foliageWarm", "foliageCool", "pine", "stone", "wood", "wall", "roof", "ink",
    ];

    static Dictionary<string, PropDef>? _defs;

    /// <summary>All definitions keyed by id (lazy, loaded once).</summary>
    public static IReadOnlyDictionary<string, PropDef> All => _defs ??= LoadAll();

    public static bool TryGet(string id, out PropDef def) => All.TryGetValue(id, out def!);

    /// <summary>Definitions for one biome, sorted by id.</summary>
    public static List<PropDef> ForBiome(string biome) =>
        [.. All.Values.Where(d => d.InBiome(biome)).OrderBy(d => d.Id, StringComparer.Ordinal)];

    /// <summary>Scene kind → built-in kind name: a definition's base,
    /// else the kind itself. Systems keyed on kind strings (glow, water
    /// ripples…) use this so definitions behave like their base.</summary>
    public static string BaseKind(string kind) =>
        TryGet(kind, out var def) ? def.BaseName : kind;

    /// <summary>Drop the cache — the next access re-reads the folder.</summary>
    public static void Reload() => _defs = null;

    /// <summary>Attach the definition named by a scene kind (no-op for
    /// built-in kinds): scale multiplier, species/model defaults.</summary>
    public static PropPlacement WithDef(this PropPlacement p, string kind)
    {
        if (!TryGet(kind, out var def))
            return p;
        p.Def = def;
        p.Scale *= def.Scale;
        p.Species ??= def.Species;
        if (p.Model < 0)
            p.Model = def.Model;
        return p;
    }

    static Dictionary<string, PropDef> LoadAll()
    {
        var defs = new Dictionary<string, PropDef>();
        if (!DirAccess.DirExistsAbsolute(Dir))
            return defs;
        var files = DirAccess.GetFilesAt(Dir)
            .Where(f => f.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var f in files)
        {
            var id = f[..^5];
            try
            {
                defs[id] = Parse(id, Read($"{Dir}/{f}", $"props.{id}"));
            }
            catch (ConfigException e)
            {
                errors.Add(e.Message);
            }
        }
        if (errors.Count > 0)
            throw new ConfigException(string.Join("\n", errors));
        return defs;
    }

    static readonly string[] Keys =
    [
        "label", "base", "category", "biomes", "scale", "footprint", "height",
        "species", "model", "palette", "tint", "adjust", "protect", "keepOut",
        "water", "flora",
    ];

    /// <summary>Validate + resolve one definition. Public so probes and
    /// tests can feed JSON without touching the folder.</summary>
    public static PropDef Parse(string id, JsonDict d)
    {
        var path = $"props.{id}";
        var err = new List<string>();
        void Need(bool ok, string at, string what)
        {
            if (!ok)
                err.Add($"{path}.{at}: expected {what}");
        }
        Warn(d, path, Keys);

        var def = new PropDef { Id = id };
        var label = Get(d, "label");
        Need(IsStr(label), "label", "string");
        def.Label = label.AsString();

        var baseV = Get(d, "base");
        if (IsStr(baseV) && SceneView.TryMapBuiltinProp(baseV.AsString(), out var k))
            (def.Base, def.BaseName) = (k, baseV.AsString());
        else
            Need(false, "base", "built-in prop kind (tree, rock, house…)");

        if (d.ContainsKey("category"))
        {
            Need(IsStr(d["category"]), "category", "string");
            def.Category = d["category"].AsString();
        }
        if (d.ContainsKey("biomes"))
        {
            var ok = d["biomes"].VariantType == Variant.Type.Array;
            Need(ok, "biomes", "array of biome ids");
            if (ok)
                foreach (var b in d["biomes"].AsGodotArray())
                {
                    Need(IsStr(b), "biomes[]", "string");
                    def.Biomes.Add(b.AsString());
                }
        }
        def.Scale = (float)Num(d, "scale", 1, err, path, min: 0.05);
        def.Footprint = Num(d, "footprint", 1, err, path, min: 0);
        def.Height = Num(d, "height", 1, err, path, min: 0);
        if (d.ContainsKey("species"))
        {
            Need(IsStr(d["species"]), "species", "string");
            def.Species = d["species"].AsString();
        }
        def.Model = (int)Num(d, "model", -1, err, path, min: -1);
        if (d.ContainsKey("protect"))
            def.Protect = Num(d, "protect", 0, err, path, min: 0);
        if (d.ContainsKey("keepOut"))
        {
            Need(d["keepOut"].VariantType == Variant.Type.Bool, "keepOut", "bool");
            def.KeepOut = d["keepOut"].AsBool();
        }

        if (d.ContainsKey("palette"))
        {
            if (d["palette"].VariantType != Variant.Type.Dictionary)
                Need(false, "palette", "object");
            else
            {
                var pal = d["palette"].AsGodotDictionary();
                Warn(pal, $"{path}.palette", PaletteSlots);
                foreach (var (key, v) in pal)
                {
                    var slot = key.AsString();
                    Need(IsColorSpec(v), $"palette.{slot}", "\"#rrggbb[aa]\" or \"@biomeColor\"");
                    if (PaletteSlots.Contains(slot))
                        def.Palette[slot] = v.AsString();
                }
            }
        }
        if (d.ContainsKey("tint"))
        {
            if (d["tint"].VariantType != Variant.Type.Dictionary)
                Need(false, "tint", "object {color, amount}");
            else
            {
                var t = d["tint"].AsGodotDictionary();
                Warn(t, $"{path}.tint", ["color", "amount"]);
                Need(IsColorSpec(Get(t, "color")), "tint.color", "\"#rrggbb\" or \"@biomeColor\"");
                def.TintColor = Get(t, "color").AsString();
                def.TintAmount = Num(t, "amount", 0.3, err, $"{path}.tint", min: 0, max: 1);
            }
        }
        if (d.ContainsKey("adjust"))
        {
            if (d["adjust"].VariantType != Variant.Type.Dictionary)
                Need(false, "adjust", "object {hue, saturation, value}");
            else
            {
                var a = d["adjust"].AsGodotDictionary();
                Warn(a, $"{path}.adjust", ["hue", "saturation", "value"]);
                def.Hue = Num(a, "hue", 0, err, $"{path}.adjust", min: -1, max: 1);
                def.Saturation = Num(a, "saturation", 1, err, $"{path}.adjust", min: 0);
                def.Value = Num(a, "value", 1, err, $"{path}.adjust", min: 0);
            }
        }
        if (d.ContainsKey("water"))
        {
            Need(d["water"].VariantType == Variant.Type.Bool, "water", "bool");
            def.Water = d["water"].AsBool();
        }
        if (d.ContainsKey("flora"))
        {
            if (d["flora"].VariantType != Variant.Type.Dictionary)
                Need(false, "flora", "object");
            else
                def.Flora = ParseFlora(d["flora"].AsGodotDictionary(), $"{path}.flora", err);
        }
        if (def.Base == PropKind.Flora && def.Flora == null)
            Need(false, "flora", "object (required for base \"flora\")");
        if (err.Count > 0)
            throw new ConfigException(string.Join("\n", err));
        return def;
    }

    static FloraSpec ParseFlora(JsonDict f, string path, List<string> err)
    {
        Warn(f, path,
        [
            "form", "count", "radius", "length", "width", "leaf", "leafDark",
            "accents", "accentSize", "accentChance", "ink", "heads", "underlay", "puffChance",
        ]);
        var spec = new FloraSpec();
        var form = Get(f, "form");
        if (IsStr(form) && FloraSpec.Forms.Contains(form.AsString()))
            spec.Form = form.AsString();
        else
            err.Add($"{path}.form: expected {string.Join("/", FloraSpec.Forms)}");
        (double, double) Range(string key, double a, double b)
        {
            if (!f.ContainsKey(key))
                return (a, b);
            var v = f[key];
            if (IsNum(v) && v.AsDouble() >= 0)
                return (v.AsDouble(), v.AsDouble());
            if (v.VariantType == Variant.Type.Array && v.AsGodotArray() is { Count: 2 } arr
                && arr.All(IsNum) && arr[0].AsDouble() >= 0 && arr[0].AsDouble() <= arr[1].AsDouble())
                return (arr[0].AsDouble(), arr[1].AsDouble());
            err.Add($"{path}.{key}: expected number or [min, max]");
            return (a, b);
        }
        var (cmin, cmax) = Range("count", spec.CountMin, spec.CountMax);
        (spec.CountMin, spec.CountMax) = ((int)Math.Round(cmin), (int)Math.Round(cmax));
        (spec.LengthMin, spec.LengthMax) = Range("length", spec.LengthMin, spec.LengthMax);
        spec.Radius = Num(f, "radius", spec.Radius, err, path, min: 0);
        spec.Width = Num(f, "width", spec.Width, err, path, min: 0.1);
        spec.AccentSize = Num(f, "accentSize", spec.AccentSize, err, path, min: 0);
        spec.AccentChance = Num(f, "accentChance", spec.AccentChance, err, path, min: 0, max: 1);
        spec.Ink = Num(f, "ink", spec.Ink, err, path, min: 0, max: 1);
        spec.Underlay = Num(f, "underlay", spec.Underlay, err, path, min: 0, max: 1);
        spec.PuffChance = Num(f, "puffChance", spec.PuffChance, err, path, min: 0, max: 1);
        if (f.ContainsKey("heads"))
        {
            if (f["heads"].VariantType != Variant.Type.Array)
                err.Add($"{path}.heads: expected array of {string.Join("/", FlowerStyles.Heads)}");
            else
                foreach (var h in f["heads"].AsGodotArray())
                {
                    if (IsStr(h) && FlowerStyles.Heads.Contains(h.AsString()))
                        spec.Heads.Add(h.AsString());
                    else
                        err.Add($"{path}.heads[]: expected {string.Join("/", FlowerStyles.Heads)}");
                }
        }
        string? Color(string key)
        {
            if (!f.ContainsKey(key))
                return null;
            if (IsColorSpec(f[key]))
                return f[key].AsString();
            err.Add($"{path}.{key}: expected \"#rrggbb[aa]\" or \"@biomeColor\"");
            return null;
        }
        spec.Leaf = Color("leaf") ?? spec.Leaf;
        spec.LeafDark = Color("leafDark");
        if (f.ContainsKey("accents"))
        {
            if (f["accents"].VariantType != Variant.Type.Array)
                err.Add($"{path}.accents: expected array of colors");
            else
                foreach (var a in f["accents"].AsGodotArray())
                {
                    if (IsColorSpec(a))
                        spec.Accents.Add(a.AsString());
                    else
                        err.Add($"{path}.accents[]: expected \"#rrggbb[aa]\" or \"@biomeColor\"");
                }
        }
        return spec;
    }

    /// <summary>Resolve a color spec against the active biome:
    /// "#rrggbb[aa]" literal or "@name" biome color.</summary>
    public static Color ResolveColor(string spec, ResolvedBiome? biome) =>
        spec.StartsWith('@')
            ? (biome ?? new ResolvedBiome()).At(spec[1..])
            : new Color(spec);

    // --- JSON plumbing (mirrors SceneConfig's) ------------------------------

    static JsonDict Read(string path, string label)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
            throw new ConfigException($"{label}: cannot open {path} ({FileAccess.GetOpenError()})");
        var json = Json.ParseString(file.GetAsText());
        if (json.VariantType != Variant.Type.Dictionary)
            throw new ConfigException($"{label}: {path} is not a JSON object");
        return json.AsGodotDictionary();
    }

    static Variant Get(JsonDict d, string key) => d.TryGetValue(key, out var v) ? v : default;

    static bool IsStr(Variant v) => v.VariantType == Variant.Type.String;

    static bool IsNum(Variant v) => v.VariantType is Variant.Type.Int or Variant.Type.Float;

    static double Num(
        JsonDict d, string key, double fallback, List<string> err, string path,
        double min = double.MinValue, double max = double.MaxValue
    )
    {
        if (!d.ContainsKey(key))
            return fallback;
        var v = d[key];
        if (!IsNum(v) || v.AsDouble() < min || v.AsDouble() > max)
        {
            var range = max < double.MaxValue ? $" in [{min}, {max}]" : min > double.MinValue ? $" ≥ {min}" : "";
            err.Add($"{path}.{key}: expected number{range}");
            return fallback;
        }
        return v.AsDouble();
    }

    static bool IsColorSpec(Variant v)
    {
        if (!IsStr(v))
            return false;
        var s = v.AsString();
        if (s.StartsWith('@'))
            return s.Length > 1;
        return s.Length is 7 or 9 && s[0] == '#' && s[1..].All(Uri.IsHexDigit);
    }

    static void Warn(JsonDict d, string path, string[] known)
    {
        foreach (var k in d.Keys)
            if (!known.Contains(k.AsString()))
                GD.PushWarning($"{path}: unknown key \"{k.AsString()}\"");
    }
}
