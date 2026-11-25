// Scene config — Godot port of the web app's src/config system
// (src/dm-area/scene/config/scene-config.ts). Biome palettes live in
// res://assets/config/biomes/<id>.json, lighting moods in
// res://assets/config/moods.json. Both are validated on load with the
// same semantics as the web loader: unknown keys warn (PushWarning),
// missing required keys or unparsable colors throw a ConfigException
// naming the exact path — a typo fails loudly instead of silently
// rendering a broken map.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using JsonArr = Godot.Collections.Array;
using JsonDict = Godot.Collections.Dictionary;

namespace DmMap;

/// <summary>Config file failed validation — the message lists every
/// offending path, one per line, like the TS loader's error.</summary>
public class ConfigException(string message) : Exception(message);

/// <summary>One season entry from biome JSON — the look it is drawn
/// with plus its color overrides (TS SeasonEntry).</summary>
public class SeasonEntry
{
    public string Look = "summer";
    public string? Ground;
    public readonly Dictionary<string, Color> Roads = new();
    public string? WaterBank;
    public Color? WaterEdge;
}

/// <summary>A biome JSON resolved to engine values: the seven palette
/// colors plus extra named colors (colors.*, road.*/ground.dirt extras)
/// as one name→Color lookup with sane defaults.</summary>
public class ResolvedBiome
{
    public string Id = "";
    public string Label = "";
    /// <summary>palette.* colors, colors.* overrides and prefixed extras
    /// (road.dirt/road.stone/road.verge/road.halo, ground.dirt,
    /// ground.dryGrass, water.edge).</summary>
    public Dictionary<string, Color> Named { get; } = new();
    /// <summary>Seasons the biome offers, in file order.</summary>
    public List<string> Seasons { get; } = [];
    /// <summary>Season entries keyed by id (look + overrides).</summary>
    public Dictionary<string, SeasonEntry> SeasonEntries { get; } = new();
    /// <summary>Tree style + species list + summer tints (trees.*).</summary>
    public string TreeStyle = "broadleaf";
    public List<string> Species = ["oak", "spruce", "birch"];
    public List<(Color To, double Amount)> SummerTints = [];
    /// <summary>roofs.winterSnow — pitched roofs get snow in winter.</summary>
    public bool WinterSnow;
    /// <summary>Season the resolved palette was graded with (ResolveArt).</summary>
    public string Season = "summer";
    /// <summary>Season look applied (resolved via season entry).</summary>
    public string SeasonLook = "summer";
    /// <summary>Active art pack id ("classic", "storybook", "ink") —
    /// painters that have pack-specific dressing read this.</summary>
    public string ArtPack = "classic";
    /// <summary>Bank style override from the active season entry.</summary>
    public string? WaterBank;
    /// <summary>ground.grassTile — strength of the hand-painted grass tile
    /// stamped under the ground speckle (0 = off: sand, ash, snow, cave).</summary>
    public double GrassTile;
    /// <summary>ground.* scatter multipliers + extras for the ground pass
    /// (dirt patches, tufts, pebbles, flowers, leaves, snow, extra).</summary>
    public GroundTraits GroundCfg = new();
    /// <summary>Season scatter multipliers applied on top of the biome
    /// ground traits (frontend.json seasonScatter).</summary>
    public static readonly Dictionary<string, GroundTraits> SeasonScatter = new()
    {
        ["spring"] = new() { Flowers = 1.8, Grass = 1.2, Leaves = 0, Snow = 0 },
        ["summer"] = new() { Flowers = 1, Grass = 1, Leaves = 0, Snow = 0 },
        ["autumn"] = new() { Flowers = 0.2, Grass = 0.8, Leaves = 1, Snow = 0 },
        ["winter"] = new() { Flowers = 0, Grass = 0.45, Leaves = 0, Snow = 1 },
    };

    /// <summary>Numeric ground-scatter traits (all default like woodland
    /// so biomes missing a key stay sensible).</summary>
    public class GroundTraits
    {
        public double Grass = 1;
        public double Stones = 1;
        public double Flowers = 1;
        public double Patches = 1;
        public double Leaves = 0;
        public double Snow = 0;
        public string Extra = "none";
        public List<Color> FlowerColors = [];
    }

    /// <summary>Classic woodland fallback for palette lookups.</summary>
    static readonly Dictionary<string, Color> Defaults = new()
    {
        ["ground"] = Palette.Hex("#8aab54"),
        ["cliff"] = Palette.Hex("#b4b18a"),
        ["water"] = Palette.Hex("#298f99"),
        ["foliage"] = Palette.Hex("#4e7440"),
        ["stone"] = Palette.Hex("#a4a68e"),
        ["roof"] = Palette.Hex("#ac6948"),
        ["wood"] = Palette.Hex("#a37d4c"),
    };

    /// <summary>name→Color: the biome value, else the woodland default,
    /// else magenta so a genuinely unknown name stands out.</summary>
    public Color At(string key) =>
        Named.TryGetValue(key, out var c) ? c
        : Defaults.TryGetValue(key, out var d) ? d
        : Godot.Colors.Magenta;

    public Color Ground => At("ground");
    public Color Cliff => At("cliff");
    public Color Water => At("water");
    public Color Foliage => At("foliage");
    public Color Stone => At("stone");
    public Color Roof => At("roof");
    public Color Wood => At("wood");
}

/// <summary>A lighting mood resolved to shader uniforms: sun direction
/// (canvas coords, y down, toward the sun), elevation (height units per
/// px — smaller = longer shadows), ambient floor, sun color and the set
/// of object kinds that cast no shadow.</summary>
public class ResolvedMood
{
    public string Id = "";
    public Vector2 SunDir = new(-0.5f, -0.62f);
    public float Elev = 0.12f;
    public bool Shadows = true;
    public float Ambient = 0.66f;
    public Color SunColor = Palette.Hex("#6b614d");
    /// <summary>Multiplier on SunColor's diffuse contribution.</summary>
    public float Strength = 1f;
    /// <summary>Glowing surfaces repaint over the darkening (future).</summary>
    public bool Emissive;
    /// <summary>Object kinds without a contact shadow.</summary>
    public HashSet<string> Shadowless = [];

    /// <summary>Push the mood onto a lighting stage's shader uniforms.</summary>
    public void ApplyTo(LightingStage stage)
    {
        stage.SetSun(SunDir, Elev, Shadows);
        var mat = (ShaderMaterial)stage.DebugMaterial();
        mat.SetShaderParameter("uAmbient", Ambient);
        mat.SetShaderParameter(
            "uSunColor",
            new Vector3(SunColor.R * Strength, SunColor.G * Strength, SunColor.B * Strength)
        );
    }
}

/// <summary>The whole moods.json resolved: per-mood uniforms, the
/// shadowless kind list and the sky/sun/indirect light channels.</summary>
public class MoodsConfig
{
    public Dictionary<string, ResolvedMood> Moods { get; } = new();
    public HashSet<string> Shadowless { get; } = [];
    /// <summary>channel → (color, budget, intensity) — the web `channels`
    /// block, kept for the scene light-budget UI.</summary>
    public Dictionary<string, (Color Color, double Budget, double Intensity)> Channels { get; } =
        new();
}

public static class SceneConfig
{
    public const string BiomeDir = "res://assets/config/biomes";
    public const string MoodsPath = "res://assets/config/moods.json";

    static readonly string[] SeasonIds = ["spring", "summer", "autumn", "winter"];
    static readonly string[] PaletteKeys =
        ["ground", "cliff", "water", "foliage", "stone", "roof", "wood"];
    static readonly string[] TreeStyles =
        ["broadleaf", "palm", "pine", "mangrove", "charred", "mushroom"];
    static readonly string[] Species =
    [
        "oak", "spruce", "birch", "palm", "acacia", "cactus", "snowfir", "pine",
        "bare", "mangrove", "willow", "cypress", "charred", "stump", "deadtree",
        "mushroom", "shroomcluster", "coral",
    ];
    static readonly string[] GroundExtras =
        ["twigs", "ripples", "snow", "puddles", "cracks", "crystals"];
    static readonly string[] BankStyles = ["grass", "sand", "stone"];
    static readonly string[] RoofMaterials = ["tile", "shingle", "slate", "flat"];
    static readonly string[] RoofExtras = ["none", "snow", "ember-vent", "crystal-lamps"];
    static readonly string[] BiomeTopKeys =
    [
        "label", "seasons", "palette", "labels", "trees", "ground",
        "roofs", "water", "roads", "colors",
    ];
    /// <summary>Named colors that always have a value (frontend.json
    /// defaults on the web side).</summary>
    static readonly string[] SolidColorKeys =
    [
        "hill", "plateauTufts", "bankReeds", "causeway", "market", "plaza",
        "cemetery", "gate", "flag", "bale", "window", "bed", "bushBlossom",
        "tentHighlight", "pillarRune",
    ];
    /// <summary>Named colors that are off (null) unless a biome sets them.</summary>
    static readonly string[] OptionalColorKeys =
    [
        "lilyPads", "waterDeep", "waterShallow", "fieldStalks", "grave",
        "statueGem", "stonecircle", "pillarGem", "graveGem",
    ];

    static MoodsConfig _moods = null!;

    /// <summary>Biome ids = JSON file names under assets/config/biomes,
    /// sorted for deterministic iteration.</summary>
    public static List<string> BiomeIds()
    {
        var ids = new List<string>();
        foreach (var f in DirAccess.GetFilesAt(BiomeDir))
            if (f.EndsWith(".json", StringComparison.Ordinal))
                ids.Add(f[..^5]);
        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    public static List<string> MoodIds() => [.. LoadMoods().Moods.Keys];

    public static ResolvedMood LoadMood(string id) =>
        LoadMoods().Moods.TryGetValue(id, out var m)
            ? m
            : throw new ConfigException(
                $"moods.{id}: missing mood (have {string.Join("/", LoadMoods().Moods.Keys)})"
            );

    // --- JSON plumbing ------------------------------------------------------

    static JsonDict ReadJson(string path, string label)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
            throw new ConfigException($"{label}: cannot open {path} ({FileAccess.GetOpenError()})");
        var text = file.GetAsText();
        var json = Json.ParseString(text);
        if (json.VariantType != Variant.Type.Dictionary)
            throw new ConfigException($"{label}: {path} is not a JSON object");
        return json.AsGodotDictionary();
    }

    static Variant Get(JsonDict d, string key) =>
        d.TryGetValue(key, out var v) ? v : default;

    static bool IsNum(Variant v) =>
        v.VariantType is Variant.Type.Int or Variant.Type.Float;

    static bool IsStr(Variant v) => v.VariantType == Variant.Type.String;

    static bool IsBool(Variant v) => v.VariantType == Variant.Type.Bool;

    static bool IsDict(Variant v, out JsonDict d)
    {
        d = v.VariantType == Variant.Type.Dictionary ? v.AsGodotDictionary() : [];
        return v.VariantType == Variant.Type.Dictionary;
    }

    static bool IsArr(Variant v, out JsonArr a)
    {
        a = v.VariantType == Variant.Type.Array ? v.AsGodotArray() : [];
        return v.VariantType == Variant.Type.Array;
    }

    /// <summary>`#rrggbb` or `#rrggbbaa` — same contract as the TS isColor.</summary>
    static bool IsColor(Variant v)
    {
        if (v.VariantType != Variant.Type.String)
            return false;
        var s = v.AsString();
        if (s.Length is not (7 or 9) || s[0] != '#')
            return false;
        foreach (var c in s[1..])
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
    }

    static Color ToColor(Variant v) => new(v.AsString());

    sealed class Checker
    {
        public readonly List<string> Errors = [];

        public void Need(bool ok, string path, string what)
        {
            if (!ok)
                Errors.Add($"{path}: expected {what}");
        }

        /// <summary>Warn for keys the schema doesn't know — the file
        /// still loads, but the typo is visible.</summary>
        public void KnownKeys(JsonDict d, string path, string[] keys)
        {
            foreach (var k in d.Keys)
            {
                var s = k.AsString();
                if (!keys.Contains(s))
                    GD.PushWarning($"{path}: unknown key \"{s}\"");
            }
        }
    }

    // --- biomes --------------------------------------------------------------

    /// <summary>Load + validate assets/config/biomes/&lt;id&gt;.json.</summary>
    public static ResolvedBiome LoadBiome(string id)
    {
        var p = $"biomes.{id}";
        var root = ReadJson($"{BiomeDir}/{id}.json", p);
        var check = new Checker();
        check.KnownKeys(root, p, BiomeTopKeys);
        check.Need(IsStr(Get(root, "label")), $"{p}.label", "string");

        // Seasons — at least one; each entry maps to a look and may
        // override ground/roads/water/scatter.
        if (IsDict(Get(root, "seasons"), out var seasons))
        {
            check.Need(seasons.Count > 0, $"{p}.seasons", "at least one season");
            foreach (var key in seasons.Keys)
            {
                var name = key.AsString();
                var sp = $"{p}.seasons.{name}";
                check.Need(SeasonIds.Contains(name), sp, "spring/summer/autumn/winter key");
                if (!IsDict(seasons[key], out var e))
                {
                    check.Need(false, sp, "season entry object");
                    continue;
                }
                check.KnownKeys(
                    e,
                    sp,
                    ["look", "label", "ground", "scatter", "roads", "water"]
                );
                check.Need(
                    IsStr(Get(e, "look")) && SeasonIds.Contains(Get(e, "look").AsString()),
                    $"{sp}.look",
                    "spring/summer/autumn/winter"
                );
                var label = Get(e, "label");
                check.Need(
                    label.VariantType is Variant.Type.Nil or Variant.Type.String,
                    $"{sp}.label",
                    "string"
                );
                var ground = Get(e, "ground");
                check.Need(
                    ground.VariantType == Variant.Type.Nil || IsColor(ground),
                    $"{sp}.ground",
                    "color"
                );
                if (IsDict(Get(e, "scatter"), out var scatter))
                    foreach (var k in scatter.Keys)
                    {
                        var sk = k.AsString();
                        check.Need(
                            new[] { "flowers", "grass", "leaves", "snow" }.Contains(sk)
                                && IsNum(scatter[k]),
                            $"{sp}.scatter.{sk}",
                            "flowers/grass/leaves/snow number"
                        );
                    }
                if (IsDict(Get(e, "roads"), out var sroads))
                    foreach (var k in sroads.Keys)
                    {
                        var rk = k.AsString();
                        check.Need(
                            new[] { "dirt", "stone", "verge", "halo" }.Contains(rk)
                                && IsColor(sroads[k]),
                            $"{sp}.roads.{rk}",
                            "dirt/stone/verge/halo color"
                        );
                    }
                if (IsDict(Get(e, "water"), out var swater))
                {
                    var bank = Get(swater, "bank");
                    if (bank.VariantType != Variant.Type.Nil)
                        check.Need(
                            IsStr(bank) && BankStyles.Contains(bank.AsString()),
                            $"{sp}.water.bank",
                            "grass/sand/stone"
                        );
                    var edge = Get(swater, "edge");
                    if (edge.VariantType != Variant.Type.Nil)
                        check.Need(
                            edge.VariantType == Variant.Type.Nil || IsColor(edge),
                            $"{sp}.water.edge",
                            "color or null"
                        );
                }
            }
        }
        else
            check.Need(false, $"{p}.seasons", "object with at least one season");

        // Palette — the seven required colors; the Godot painters read
        // ground/cliff/stone today, the rest feed props later.
        if (IsDict(Get(root, "palette"), out var pal))
        {
            check.KnownKeys(pal, $"{p}.palette", PaletteKeys);
            foreach (var k in PaletteKeys)
                check.Need(IsColor(Get(pal, k)), $"{p}.palette.{k}", "#rrggbb color");
        }
        else
            check.Need(false, $"{p}.palette", "object of colors");

        if (IsDict(Get(root, "labels"), out var labels))
        {
            check.KnownKeys(labels, $"{p}.labels", ["trees", "bush", "house", "scatter"]);
            foreach (var k in new[] { "trees", "bush", "house", "scatter" })
                check.Need(IsStr(Get(labels, k)), $"{p}.labels.{k}", "string");
        }
        else
            check.Need(false, $"{p}.labels", "object");

        if (IsDict(Get(root, "trees"), out var trees))
        {
            check.KnownKeys(trees, $"{p}.trees", ["style", "species", "summerTint"]);
            check.Need(
                IsStr(Get(trees, "style")) && TreeStyles.Contains(Get(trees, "style").AsString()),
                $"{p}.trees.style",
                string.Join("/", TreeStyles)
            );
            check.Need(
                IsArr(Get(trees, "species"), out var species)
                    && species.Count == 3
                    && species.All(s =>
                        s.VariantType == Variant.Type.String && Species.Contains(s.AsString())
                    ),
                $"{p}.trees.species",
                $"3 of {string.Join("/", Species)}"
            );
            check.Need(
                IsArr(Get(trees, "summerTint"), out var tints)
                    && tints.All(t =>
                        IsDict(t, out var td) && IsColor(Get(td, "to")) && IsNum(Get(td, "amount"))
                    ),
                $"{p}.trees.summerTint",
                "list of {to: color, amount: number}"
            );
        }
        else
            check.Need(false, $"{p}.trees", "object");

        if (IsDict(Get(root, "ground"), out var g))
        {
            check.KnownKeys(
                g,
                $"{p}.ground",
                [
                    "dirt", "dryGrass", "grass", "stones", "flowers", "flowerColors",
                    "patches", "leaves", "snow", "extra", "grassTile",
                ]
            );
            check.Need(IsColor(Get(g, "dirt")), $"{p}.ground.dirt", "#rrggbb color");
            var dry = Get(g, "dryGrass");
            check.Need(
                dry.VariantType == Variant.Type.Nil || IsColor(dry),
                $"{p}.ground.dryGrass",
                "color"
            );
            foreach (var k in new[] { "grass", "stones", "flowers", "patches", "leaves", "snow" })
            {
                var n = Get(g, k);
                check.Need(
                    IsNum(n) && n.AsDouble() >= 0,
                    $"{p}.ground.{k}",
                    "number ≥ 0"
                );
            }
            check.Need(
                IsArr(Get(g, "flowerColors"), out var flowers)
                    && flowers.All(IsColor),
                $"{p}.ground.flowerColors",
                "list of colors"
            );
            check.Need(
                IsStr(Get(g, "extra")) && GroundExtras.Contains(Get(g, "extra").AsString()),
                $"{p}.ground.extra",
                string.Join("/", GroundExtras)
            );
            check.Need(IsNum(Get(g, "grassTile")), $"{p}.ground.grassTile", "number 0–1");
        }
        else
            check.Need(false, $"{p}.ground", "object");

        if (IsDict(Get(root, "roofs"), out var roofs))
        {
            check.KnownKeys(
                roofs,
                $"{p}.roofs",
                ["material", "chimney", "moss", "extra", "winterSnow"]
            );
            check.Need(
                IsStr(Get(roofs, "material"))
                    && RoofMaterials.Contains(Get(roofs, "material").AsString()),
                $"{p}.roofs.material",
                string.Join("/", RoofMaterials)
            );
            check.Need(IsBool(Get(roofs, "chimney")), $"{p}.roofs.chimney", "boolean");
            check.Need(IsNum(Get(roofs, "moss")), $"{p}.roofs.moss", "number");
            check.Need(
                IsStr(Get(roofs, "extra")) && RoofExtras.Contains(Get(roofs, "extra").AsString()),
                $"{p}.roofs.extra",
                string.Join("/", RoofExtras)
            );
            check.Need(IsBool(Get(roofs, "winterSnow")), $"{p}.roofs.winterSnow", "boolean");
        }
        else
            check.Need(false, $"{p}.roofs", "object");

        if (IsDict(Get(root, "water"), out var water))
        {
            check.KnownKeys(water, $"{p}.water", ["bank", "edge"]);
            check.Need(
                IsStr(Get(water, "bank")) && BankStyles.Contains(Get(water, "bank").AsString()),
                $"{p}.water.bank",
                "grass/sand/stone"
            );
            check.Need(
                Get(water, "edge").VariantType == Variant.Type.Nil || IsColor(Get(water, "edge")),
                $"{p}.water.edge",
                "color or null"
            );
        }
        else
            check.Need(false, $"{p}.water", "object");

        if (IsDict(Get(root, "roads"), out var roads))
        {
            check.KnownKeys(roads, $"{p}.roads", ["dirt", "stone", "verge", "halo"]);
            foreach (var k in new[] { "dirt", "stone", "verge", "halo" })
                check.Need(IsColor(Get(roads, k)), $"{p}.roads.{k}", "#rrggbb color");
        }
        else
            check.Need(false, $"{p}.roads", "object");

        if (IsDict(Get(root, "colors"), out var colors))
        {
            var all = SolidColorKeys.Concat(OptionalColorKeys).ToArray();
            foreach (var k in colors.Keys)
            {
                var ck = k.AsString();
                var cv = colors[k];
                if (!all.Contains(ck))
                    GD.PushWarning($"{p}.colors.{ck}: unknown color name");
                else
                    check.Need(
                        IsColor(cv)
                            || (cv.VariantType == Variant.Type.Nil
                                && OptionalColorKeys.Contains(ck)),
                        $"{p}.colors.{ck}",
                        OptionalColorKeys.Contains(ck) ? "#rrggbb color or null" : "#rrggbb color"
                    );
            }
        }

        if (check.Errors.Count > 0)
            throw new ConfigException($"{p} is invalid:\n  {string.Join("\n  ", check.Errors)}");
        return Resolve(id, root);
    }

    static ResolvedBiome Resolve(string id, JsonDict root)
    {
        var b = new ResolvedBiome { Id = id, Label = Get(root, "label").AsString() };
        foreach (var k in PaletteKeys)
            b.Named[k] = ToColor(Get(root["palette"].AsGodotDictionary(), k));
        if (IsDict(Get(root, "colors"), out var colors))
            foreach (var k in colors.Keys)
            {
                var v = colors[k];
                if (IsColor(v))
                    b.Named[k.AsString()] = ToColor(v);
            }
        if (IsDict(Get(root, "roads"), out var roads))
            foreach (var k in new[] { "dirt", "stone", "verge", "halo" })
                b.Named[$"road.{k}"] = ToColor(Get(roads, k));
        if (IsDict(Get(root, "ground"), out var g))
        {
            b.Named["ground.dirt"] = ToColor(Get(g, "dirt"));
            var dry = Get(g, "dryGrass");
            if (IsColor(dry))
                b.Named["ground.dryGrass"] = ToColor(dry);
            var tile = Get(g, "grassTile");
            if (tile.VariantType == Variant.Type.Float || tile.VariantType == Variant.Type.Int)
                b.GrassTile = tile.AsDouble();
            b.GroundCfg = new ResolvedBiome.GroundTraits
            {
                Grass = Get(g, "grass").AsDouble(),
                Stones = Get(g, "stones").AsDouble(),
                Flowers = Get(g, "flowers").AsDouble(),
                Patches = Get(g, "patches").AsDouble(),
                Leaves = Get(g, "leaves").AsDouble(),
                Snow = Get(g, "snow").AsDouble(),
                Extra = Get(g, "extra").AsString(),
                FlowerColors = [.. Get(g, "flowerColors").AsGodotArray().Select(ToColor)],
            };
        }
        if (IsDict(Get(root, "water"), out var water) && IsColor(Get(water, "edge")))
            b.Named["water.edge"] = ToColor(Get(water, "edge"));
        if (IsDict(Get(root, "seasons"), out var seasons))
        {
            b.Seasons.AddRange(seasons.Keys.Select(k => k.AsString()));
            foreach (var k in seasons.Keys)
            {
                var e = new SeasonEntry();
                if (IsDict(seasons[k], out var ed))
                {
                    if (IsStr(Get(ed, "look")))
                        e.Look = Get(ed, "look").AsString();
                    if (IsColor(Get(ed, "ground")))
                        e.Ground = Get(ed, "ground").AsString();
                    if (IsDict(Get(ed, "roads"), out var sr))
                        foreach (var rk in new[] { "dirt", "stone", "verge", "halo" })
                            if (IsColor(Get(sr, rk)))
                                e.Roads[rk] = ToColor(Get(sr, rk));
                    if (IsDict(Get(ed, "water"), out var sw))
                    {
                        if (IsStr(Get(sw, "bank")))
                            e.WaterBank = Get(sw, "bank").AsString();
                        if (IsColor(Get(sw, "edge")))
                            e.WaterEdge = ToColor(Get(sw, "edge"));
                    }
                }
                b.SeasonEntries[k.AsString()] = e;
            }
        }
        if (IsDict(Get(root, "trees"), out var trees))
        {
            if (IsStr(Get(trees, "style")))
                b.TreeStyle = Get(trees, "style").AsString();
            if (IsArr(Get(trees, "species"), out var sp))
                b.Species = [.. sp.Select(x => x.AsString())];
            if (IsArr(Get(trees, "summerTint"), out var tints))
                foreach (var t in tints)
                    if (IsDict(t, out var td) && IsColor(Get(td, "to")) && IsNum(Get(td, "amount")))
                        b.SummerTints.Add((ToColor(Get(td, "to")), Get(td, "amount").AsDouble()));
        }
        if (IsDict(Get(root, "roofs"), out var roofs) && IsBool(Get(roofs, "winterSnow")))
            b.WinterSnow = Get(roofs, "winterSnow").AsBool();
        return b;
    }

    // --- art packs & seasons (scene-biomes.ts port) ------------------------

    /// <summary>Art-pack palette grade — TS applyPack: storybook warms
    /// everything pastel, ink pulls toward warm grey at 65%.</summary>
    static void ApplyPack(ResolvedBiome b, string pack)
    {
        Color H(string hex) => Palette.Hex(hex);
        if (pack == "storybook")
        {
            b.Named["ground"] = Palette.Mix(Palette.Desaturate(b.At("ground"), 0.85), H("#cfe0ae"), 0.3);
            b.Named["water"] = Palette.Mix(Palette.Desaturate(b.At("water"), 0.8), H("#93cfd3"), 0.4);
            b.Named["foliage"] = Palette.Mix(Palette.Desaturate(b.At("foliage"), 0.85), H("#a8d49a"), 0.35);
            b.Named["stone"] = Palette.Mix(Palette.Desaturate(b.At("stone"), 0.85), H("#ddcfb4"), 0.3);
            b.Named["roof"] = Palette.Mix(Palette.Desaturate(b.At("roof"), 0.85), H("#e2a28e"), 0.4);
            b.Named["wood"] = Palette.Mix(Palette.Desaturate(b.At("wood"), 0.85), H("#d3ac85"), 0.35);
        }
        else if (pack == "ink")
        {
            b.Named["ground"] = Palette.Mix(Palette.Desaturate(b.At("ground"), 0.65), H("#8c8478"), 0.12);
            // Pale bone-white stone so the crack mesh reads — the 2mt
            // canyon signature.
            b.Named["cliff"] = Palette.Mix(
                Palette.Desaturate(b.At("cliff"), 0.5),
                H("#d8d4bc"),
                0.45
            );
            b.Named["water"] = Palette.Mix(
                Palette.Desaturate(b.At("water"), 0.35),
                H("#3fb8c9"),
                0.35
            );
            b.Named["foliage"] = Palette.Mix(Palette.Desaturate(b.At("foliage"), 0.65), H("#8c8478"), 0.12);
            b.Named["stone"] = Palette.Mix(Palette.Desaturate(b.At("stone"), 0.65), H("#8c8478"), 0.12);
            b.Named["roof"] = Palette.Mix(Palette.Desaturate(b.At("roof"), 0.65), H("#8c8478"), 0.12);
            b.Named["wood"] = Palette.Mix(Palette.Desaturate(b.At("wood"), 0.65), H("#8c8478"), 0.12);
        }
    }

    /// <summary>Season palette grade — TS applySeason: autumn burns
    /// toward ochres, spring freshens greens, winter cools + greys.</summary>
    static void ApplySeasonGrade(ResolvedBiome b, string look)
    {
        Color H(string hex) => Palette.Hex(hex);
        if (look == "autumn")
        {
            b.Named["ground"] = Palette.Mix(b.At("ground"), H("#c9963e"), 0.32);
            b.Named["foliage"] = Palette.Mix(b.At("foliage"), H("#bf6224"), 0.55);
            b.Named["wood"] = Palette.Mix(b.At("wood"), H("#9e6234"), 0.2);
            b.Named["water"] = Palette.Desaturate(Palette.Mix(b.At("water"), H("#2e6f7a"), 0.18), 0.9);
        }
        else if (look == "spring")
        {
            b.Named["ground"] = Palette.Mix(b.At("ground"), H("#8ec95c"), 0.3);
            b.Named["foliage"] = Palette.Mix(b.At("foliage"), H("#63b24b"), 0.45);
            b.Named["water"] = Palette.Mix(b.At("water"), H("#8fd0da"), 0.25);
        }
        else if (look == "winter")
        {
            b.Named["ground"] = Palette.Mix(Palette.Desaturate(b.At("ground"), 0.7), H("#a9bcc2"), 0.2);
            b.Named["water"] = Palette.Mix(Palette.Desaturate(b.At("water"), 0.7), H("#50759a"), 0.45);
            b.Named["foliage"] = Palette.Mix(Palette.Desaturate(b.At("foliage"), 0.65), H("#6f8178"), 0.45);
            b.Named["stone"] = Palette.Desaturate(b.At("stone"), 0.75);
            b.Named["roof"] = Palette.Desaturate(b.At("roof"), 0.7);
            b.Named["wood"] = Palette.Mix(Palette.Desaturate(b.At("wood"), 0.7), H("#7d838a"), 0.15);
        }
    }

    /// <summary>trees.json — per-species size/shadow/season behavior +
    /// seasonal tints (TS trees.species / autumn / spring / winter).</summary>
    public class TreeConfig
    {
        public record Meta(double Size, double Shadow, bool Deciduous, bool Evergreen);
        public Dictionary<string, Meta> Species { get; } = new();
        public List<(Color? To, double Amount)> AutumnColors = [];
        public (Color To, double Amount) AutumnEvergreen;
        public double BlossomChance = 0.4;
        public List<Color> BlossomColors = [];
        public Color SnowColor = Palette.Hex("#f4f7f8");
        public double ConiferSnow = 0.7;
        public double SnowfirSnow = 0.85;

        public Meta For(string id) =>
            Species.TryGetValue(id, out var m) ? m : new Meta(1, 0.9, false, false);
    }

    static TreeConfig? _trees;

    public static TreeConfig Trees()
    {
        if (_trees != null)
            return _trees;
        var d = ReadJson("res://assets/config/trees.json", "trees.");
        _trees = BuildTrees(d);
        return _trees;
    }

    static TreeConfig BuildTrees(JsonDict d)
    {
        var t = new TreeConfig();
            if (IsDict(Get(d, "species"), out var sp))
                foreach (var k in sp.Keys)
                    if (IsDict(sp[k], out var sd))
                        t.Species[k.AsString()] = new TreeConfig.Meta(
                            IsNum(Get(sd, "size")) ? Get(sd, "size").AsDouble() : 1,
                            IsNum(Get(sd, "shadow")) ? Get(sd, "shadow").AsDouble() : 0.9,
                            IsBool(Get(sd, "deciduous")) && Get(sd, "deciduous").AsBool(),
                            IsBool(Get(sd, "evergreen")) && Get(sd, "evergreen").AsBool()
                        );
            if (IsDict(Get(d, "autumn"), out var au))
            {
                if (IsArr(Get(au, "colors"), out var ac))
                    foreach (var c in ac)
                        if (c.VariantType == Variant.Type.Nil)
                            t.AutumnColors.Add((null, 0));
                        else if (IsDict(c, out var cd))
                            t.AutumnColors.Add((ToColor(Get(cd, "to")), Get(cd, "amount").AsDouble()));
                if (IsDict(Get(au, "evergreenTint"), out var eg))
                    t.AutumnEvergreen = (ToColor(Get(eg, "to")), Get(eg, "amount").AsDouble());
            }
            if (IsDict(Get(d, "spring"), out var spr))
            {
                if (IsNum(Get(spr, "blossomChance")))
                    t.BlossomChance = Get(spr, "blossomChance").AsDouble();
                if (IsArr(Get(spr, "blossomColors"), out var bc))
                    t.BlossomColors = [.. bc.Select(b => ToColor(b))];
            }
            if (IsDict(Get(d, "winter"), out var w))
            {
                if (IsColor(Get(w, "snowColor")))
                    t.SnowColor = ToColor(Get(w, "snowColor"));
                if (IsNum(Get(w, "coniferSnow")))
                    t.ConiferSnow = Get(w, "coniferSnow").AsDouble();
                if (IsNum(Get(w, "snowfirSnow")))
                    t.SnowfirSnow = Get(w, "snowfirSnow").AsDouble();
            }
        return t;
    }

    /// <summary>Season the scene is drawn in for a biome — the picked one
    /// when offered, else the biome's first (TS activeSeason).</summary>
    public static string ActiveSeason(ResolvedBiome b, string? season)
    {
        if (season != null && b.Seasons.Contains(season))
            return season;
        return b.Seasons.Count > 0 ? b.Seasons[0] : "summer";
    }

    /// <summary>Effective palette for a scene: biome run through the art
    /// pack, then the seasonal grade, then the season entry's own
    /// overrides (ground, roads.*, water.bank/edge). `classic`+summer is
    /// a strict no-op returning the base object (TS resolveProfile).</summary>
    public static ResolvedBiome ResolveArt(ResolvedBiome b, string? artPack, string? season)
    {
        var entry = b.SeasonEntries.TryGetValue(ActiveSeason(b, season), out var e)
            ? e
            : new SeasonEntry();
        var pack = artPack ?? "classic";
        if (pack == "classic" && entry.Look == "summer" && entry.Ground == null
            && entry.Roads.Count == 0 && entry.WaterEdge == null && entry.WaterBank == null)
            return b;
        var copy = new ResolvedBiome
        {
            Id = b.Id,
            Label = b.Label,
            TreeStyle = b.TreeStyle,
            Species = [.. b.Species],
            SummerTints = [.. b.SummerTints],
            WinterSnow = b.WinterSnow,
            Season = ActiveSeason(b, season),
            SeasonLook = entry.Look,
            ArtPack = pack,
            WaterBank = entry.WaterBank,
            GrassTile = b.GrassTile,
            GroundCfg = b.GroundCfg,
        };
        foreach (var kv in b.Named)
            copy.Named[kv.Key] = kv.Value;
        foreach (var kv in b.SeasonEntries)
            copy.SeasonEntries[kv.Key] = kv.Value;
        copy.Seasons.AddRange(b.Seasons);
        ApplyPack(copy, pack);
        ApplySeasonGrade(copy, entry.Look);
        // Season overrides run through the pack so they fit the sheet.
        if (entry.Ground != null)
        {
            var g = new ResolvedBiome();
            g.Named["ground"] = Palette.Hex(entry.Ground);
            ApplyPack(g, pack);
            copy.Named["ground"] = g.Named["ground"];
        }
        foreach (var kv in entry.Roads)
            copy.Named[$"road.{kv.Key}"] = kv.Value;
        if (entry.WaterEdge is { } we)
            copy.Named["water.edge"] = we;
        return copy;
    }

    // --- moods ---------------------------------------------------------------

    /// <summary>Load + validate assets/config/moods.json (cached).</summary>
    public static MoodsConfig LoadMoods()
    {
        if (_moods != null)
            return _moods;
        const string p = "moods";
        var root = ReadJson(MoodsPath, p);
        var check = new Checker();
        check.KnownKeys(root, p, ["shadowless", "suns", "channels", "moods"]);

        var shadowless = new HashSet<string>();
        if (IsArr(Get(root, "shadowless"), out var sl))
            foreach (var k in sl)
            {
                if (k.VariantType == Variant.Type.String)
                    shadowless.Add(k.AsString());
                else
                    check.Need(false, "moods.shadowless", "list of object kinds (strings)");
            }
        else
            check.Need(false, "moods.shadowless", "list of object kinds");

        // Sun presets: named (dir, elev) pairs the moods reference — the
        // web's shadow.sun high/east/west, plus night moonlight.
        var suns = new Dictionary<string, (Vector2 Dir, double Elev)>();
        if (IsDict(Get(root, "suns"), out var sunDict))
            foreach (var k in sunDict.Keys)
            {
                var name = k.AsString();
                var sp = $"moods.suns.{name}";
                if (!IsDict(sunDict[k], out var s))
                {
                    check.Need(false, sp, "{dir: [x, y], elev: number}");
                    continue;
                }
                check.KnownKeys(s, sp, ["dir", "elev"]);
                var okDir =
                    IsArr(Get(s, "dir"), out var dir)
                    && dir.Count == 2
                    && dir.All(IsNum);
                check.Need(okDir, $"{sp}.dir", "[x, y] numbers");
                var elev = Get(s, "elev");
                check.Need(IsNum(elev) && elev.AsDouble() >= 0, $"{sp}.elev", "number ≥ 0");
                if (okDir && IsNum(elev))
                    suns[name] = (
                        new Vector2(dir[0].AsSingle(), dir[1].AsSingle()),
                        elev.AsDouble()
                    );
            }
        else
            check.Need(false, "moods.suns", "object of sun presets");

        var config = new MoodsConfig();
        config.Shadowless.UnionWith(shadowless);
        if (IsDict(Get(root, "channels"), out var channels))
            foreach (var k in channels.Keys)
            {
                var name = k.AsString();
                var cp = $"moods.channels.{name}";
                if (!IsDict(channels[k], out var c))
                {
                    check.Need(false, cp, "{color, budget, intensity}");
                    continue;
                }
                check.KnownKeys(c, cp, ["color", "budget", "intensity"]);
                check.Need(IsColor(Get(c, "color")), $"{cp}.color", "#rrggbb color");
                check.Need(IsNum(Get(c, "budget")), $"{cp}.budget", "number 0–1");
                check.Need(IsNum(Get(c, "intensity")), $"{cp}.intensity", "number 0–1");
                if (IsColor(Get(c, "color")))
                    config.Channels[name] = (
                        ToColor(Get(c, "color")),
                        IsNum(Get(c, "budget")) ? Get(c, "budget").AsDouble() : 0,
                        IsNum(Get(c, "intensity")) ? Get(c, "intensity").AsDouble() : 0
                    );
            }

        if (IsDict(Get(root, "moods"), out var moods))
        {
            check.Need(moods.Count > 0, "moods.moods", "at least one mood");
            foreach (var k in moods.Keys)
            {
                var name = k.AsString();
                var mp = $"moods.moods.{name}";
                if (!IsDict(moods[k], out var m))
                {
                    check.Need(false, mp, "mood object");
                    continue;
                }
                check.KnownKeys(
                    m,
                    mp,
                    ["sun", "shadows", "ambient", "sunColor", "strength", "emissive"]
                );
                var sun = Get(m, "sun");
                check.Need(
                    IsStr(sun) && suns.ContainsKey(sun.AsString()),
                    $"{mp}.sun",
                    $"one of {string.Join("/", suns.Keys)}"
                );
                check.Need(IsBool(Get(m, "shadows")), $"{mp}.shadows", "boolean");
                var ambient = Get(m, "ambient");
                check.Need(IsNum(ambient) && ambient.AsDouble() >= 0, $"{mp}.ambient", "number ≥ 0");
                check.Need(IsColor(Get(m, "sunColor")), $"{mp}.sunColor", "#rrggbb color");
                var strength = Get(m, "strength");
                check.Need(
                    IsNum(strength) && strength.AsDouble() >= 0,
                    $"{mp}.strength",
                    "number ≥ 0"
                );
                var emissive = Get(m, "emissive");
                check.Need(
                    emissive.VariantType is Variant.Type.Nil or Variant.Type.Bool,
                    $"{mp}.emissive",
                    "boolean"
                );
                if (!IsStr(sun) || !suns.ContainsKey(sun.AsString()))
                    continue;
                var (dir, elev) = suns[sun.AsString()];
                config.Moods[name] = new ResolvedMood
                {
                    Id = name,
                    SunDir = dir,
                    Elev = (float)elev,
                    Shadows = IsBool(Get(m, "shadows")) && Get(m, "shadows").AsBool(),
                    Ambient = IsNum(ambient) ? ambient.AsSingle() : 0.66f,
                    SunColor = IsColor(Get(m, "sunColor"))
                        ? ToColor(Get(m, "sunColor"))
                        : Palette.Hex("#6b614d"),
                    Strength = IsNum(strength) ? strength.AsSingle() : 1f,
                    Emissive = emissive.VariantType == Variant.Type.Bool && emissive.AsBool(),
                    Shadowless = [.. shadowless],
                };
            }
        }
        else
            check.Need(false, "moods.moods", "object of moods");

        if (check.Errors.Count > 0)
            throw new ConfigException(
                $"moods.json is invalid:\n  {string.Join("\n  ", check.Errors)}"
            );
        _moods = config;
        return config;
    }
}
