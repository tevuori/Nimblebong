#nullable enable
// Smart fill config — res://assets/config/fill.json. The smart fill
// classifies every sample point into a context (road verge, shore,
// slope, village, open land, shallow or deep water) and each layer
// (main props, flora understory, critters…) picks from its own weighted
// table for that context. Kinds may be built-in names or prop
// definition ids; definitions outside the scene's biome drop out of the
// table automatically. A new layer, context table or plant is JSON only.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using JsonDict = Godot.Collections.Dictionary;

namespace DmMap;

public class FillConfig
{
    /// <summary>Contexts the classifier emits, in priority order (see SmartFill).</summary>
    public static readonly string[] Contexts =
        ["deepWater", "shallows", "village", "roadside", "shore", "slope", "land"];

    public double Road = 10, Roadside = 46, Shore = 44, Village = 100, DeepWater = 14;
    public HashSet<string> Buildings = [];
    public Dictionary<string, double> Sizes = new();
    public List<FillLayer> Layers = [];

    /// <summary>Footprint (px) of a scene kind: its own size entry, else
    /// its definition's base kind entry, else 24.</summary>
    public double SizeOf(string kind) =>
        Sizes.TryGetValue(kind, out var s) ? s
        : Sizes.TryGetValue(PropDefs.BaseKind(kind), out var b) ? b
        : 24;
}

public class FillLayer
{
    public string Id = "";
    /// <summary>Grid spacing at density 0 and 1 (lerped).</summary>
    public double SpacingSparse = 72, SpacingDense = 38;
    public int Limit = 420;
    /// <summary>How much props of other layers block this one (1 = fully,
    /// 0 = ignore them — e.g. grass may tuck in under tree edges).</summary>
    public double Crowding = 1;
    /// <summary>Thin out near the region outline.</summary>
    public bool EdgeFade = true;
    /// <summary>Contexts clumped into groves with clearings.</summary>
    public HashSet<string> Patchy = [];
    public Dictionary<string, FillTable> Contexts = new();
    /// <summary>Per-biome multiplier on every context chance (a biome
    /// whose only plant would otherwise carpet the map).</summary>
    public Dictionary<string, double> BiomeChance = new();

    public double ChanceIn(string biome) => BiomeChance.TryGetValue(biome, out var m) ? m : 1;

    public double Spacing(double density) =>
        Math.Max(8, SpacingSparse + (SpacingDense - SpacingSparse) * Math.Clamp(density, 0, 1));
}

public class FillTable
{
    /// <summary>Probability a sample point in this context spawns at all.</summary>
    public double Chance = 1;
    public List<(string Kind, double Weight)> Kinds = [];
}

public static class FillRules
{
    public const string Path = "res://assets/config/fill.json";

    static FillConfig? _cfg;

    public static FillConfig Config => _cfg ??= Load();

    public static void Reload() => _cfg = null;

    static FillConfig Load()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file == null)
            throw new ConfigException($"fill: cannot open {Path} ({FileAccess.GetOpenError()})");
        var json = Json.ParseString(file.GetAsText());
        if (json.VariantType != Variant.Type.Dictionary)
            throw new ConfigException($"fill: {Path} is not a JSON object");
        var root = json.AsGodotDictionary();
        var err = new List<string>();
        var cfg = new FillConfig();
        Warn(root, "fill", ["classify", "sizes", "layers"]);

        if (Dict(root, "classify", "fill", err) is { } cl)
        {
            Warn(cl, "fill.classify", ["road", "roadside", "shore", "village", "deepWater", "buildings"]);
            cfg.Road = Num(cl, "road", cfg.Road, "fill.classify", err);
            cfg.Roadside = Num(cl, "roadside", cfg.Roadside, "fill.classify", err);
            cfg.Shore = Num(cl, "shore", cfg.Shore, "fill.classify", err);
            cfg.Village = Num(cl, "village", cfg.Village, "fill.classify", err);
            cfg.DeepWater = Num(cl, "deepWater", cfg.DeepWater, "fill.classify", err);
            if (cl.TryGetValue("buildings", out var bv) && bv.VariantType == Variant.Type.Array)
                cfg.Buildings = [.. bv.AsGodotArray().Select(v => v.AsString())];
            else
                err.Add("fill.classify.buildings: expected array of kinds");
        }
        if (Dict(root, "sizes", "fill", err) is { } sizes)
            foreach (var (k, v) in sizes)
            {
                if (v.VariantType is Variant.Type.Int or Variant.Type.Float && v.AsDouble() > 0)
                    cfg.Sizes[k.AsString()] = v.AsDouble();
                else
                    err.Add($"fill.sizes.{k.AsString()}: expected number > 0");
            }
        if (root.TryGetValue("layers", out var lv) && lv.VariantType == Variant.Type.Array)
        {
            var i = 0;
            foreach (var item in lv.AsGodotArray())
            {
                var at = $"fill.layers[{i++}]";
                if (item.VariantType != Variant.Type.Dictionary)
                {
                    err.Add($"{at}: expected object");
                    continue;
                }
                cfg.Layers.Add(ParseLayer(item.AsGodotDictionary(), at, err));
            }
        }
        else
            err.Add("fill.layers: expected array of layers");

        if (err.Count > 0)
            throw new ConfigException($"fill.json is invalid:\n  {string.Join("\n  ", err)}");
        return cfg;
    }

    static FillLayer ParseLayer(JsonDict d, string at, List<string> err)
    {
        Warn(d, at, ["id", "spacing", "limit", "crowding", "edgeFade", "patchy", "contexts", "biomeChance"]);
        var layer = new FillLayer();
        if (d.TryGetValue("id", out var id) && id.VariantType == Variant.Type.String)
        {
            layer.Id = id.AsString();
            at = $"fill.layers.{layer.Id}";
        }
        else
            err.Add($"{at}.id: expected string");
        if (d.TryGetValue("spacing", out var sp) && sp.VariantType == Variant.Type.Array
            && sp.AsGodotArray() is { Count: 2 } arr && arr.All(IsNum) && arr.All(v => v.AsDouble() > 0))
            (layer.SpacingSparse, layer.SpacingDense) = (arr[0].AsDouble(), arr[1].AsDouble());
        else
            err.Add($"{at}.spacing: expected [sparse, dense] px > 0");
        layer.Limit = (int)Num(d, "limit", layer.Limit, at, err);
        layer.Crowding = Num(d, "crowding", layer.Crowding, at, err);
        if (d.TryGetValue("edgeFade", out var ef))
        {
            if (ef.VariantType == Variant.Type.Bool) layer.EdgeFade = ef.AsBool();
            else err.Add($"{at}.edgeFade: expected bool");
        }
        if (d.TryGetValue("patchy", out var pv))
        {
            if (pv.VariantType == Variant.Type.Array)
                foreach (var c in pv.AsGodotArray())
                {
                    if (FillConfig.Contexts.Contains(c.AsString())) layer.Patchy.Add(c.AsString());
                    else err.Add($"{at}.patchy: \"{c.AsString()}\" is not a context ({string.Join("/", FillConfig.Contexts)})");
                }
            else
                err.Add($"{at}.patchy: expected array of contexts");
        }
        if (d.TryGetValue("biomeChance", out var bc))
        {
            if (bc.VariantType != Variant.Type.Dictionary)
                err.Add($"{at}.biomeChance: expected object {{biome: multiplier}}");
            else
                foreach (var (k, v) in bc.AsGodotDictionary())
                {
                    if (IsNum(v) && v.AsDouble() >= 0) layer.BiomeChance[k.AsString()] = v.AsDouble();
                    else err.Add($"{at}.biomeChance.{k.AsString()}: expected number ≥ 0");
                }
        }
        if (Dict(d, "contexts", at, err) is { } ctx)
            foreach (var (k, v) in ctx)
            {
                var name = k.AsString();
                var cp = $"{at}.contexts.{name}";
                if (!FillConfig.Contexts.Contains(name))
                {
                    err.Add($"{cp}: unknown context (have {string.Join("/", FillConfig.Contexts)})");
                    continue;
                }
                if (v.VariantType != Variant.Type.Dictionary)
                {
                    err.Add($"{cp}: expected {{chance, kinds}}");
                    continue;
                }
                var t = v.AsGodotDictionary();
                Warn(t, cp, ["chance", "kinds"]);
                var table = new FillTable { Chance = Num(t, "chance", 1, cp, err) };
                if (Dict(t, "kinds", cp, err) is { } kinds)
                    foreach (var (kk, w) in kinds)
                    {
                        var kind = kk.AsString();
                        if (!IsNum(w) || w.AsDouble() < 0)
                            err.Add($"{cp}.kinds.{kind}: expected weight ≥ 0");
                        else if (!SceneView.TryMapBuiltinProp(kind, out _) && !PropDefs.TryGet(kind, out _))
                            err.Add($"{cp}.kinds.{kind}: unknown kind (not built-in, no props/{kind}.json)");
                        else
                            table.Kinds.Add((kind, w.AsDouble()));
                    }
                layer.Contexts[name] = table;
            }
        return layer;
    }

    static bool IsNum(Variant v) => v.VariantType is Variant.Type.Int or Variant.Type.Float;

    static JsonDict? Dict(JsonDict d, string key, string at, List<string> err)
    {
        if (d.TryGetValue(key, out var v) && v.VariantType == Variant.Type.Dictionary)
            return v.AsGodotDictionary();
        err.Add($"{at}.{key}: expected object");
        return null;
    }

    static double Num(JsonDict d, string key, double fallback, string at, List<string> err)
    {
        if (!d.TryGetValue(key, out var v))
            return fallback;
        if (IsNum(v) && v.AsDouble() >= 0)
            return v.AsDouble();
        err.Add($"{at}.{key}: expected number ≥ 0");
        return fallback;
    }

    static void Warn(JsonDict d, string path, string[] known)
    {
        foreach (var k in d.Keys)
            if (!known.Contains(k.AsString()))
                GD.PushWarning($"{path}: unknown key \"{k.AsString()}\"");
    }
}
