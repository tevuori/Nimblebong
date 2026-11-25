// Data-driven palette entries + the vegetation brush.
//
// Palette: every prop definition (assets/config/props) valid in the
// current biome becomes a Prop tool card in the section named by its
// "category" — a new JSON plant/rock/ruin shows up in the editor with no
// C# change. Tool id = "prop:<def id>", tool kind = the def id.
//
// Vegetation brush ("Štětec vegetace"): drag over the map and the chosen
// plant (or a context-aware mix from fill.json's flora layer) is planted
// in clumps under the brush circles. Deterministic per position.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class MapEditor
{
    /// <summary>Tool kind of the vegetation brush.</summary>
    public const string VegKind = "veg";
    const string DefToolPrefix = "prop:";

    VBoxContainer _vegSection = null!;
    OptionButton _vegPlant = null!;
    readonly List<string?> _vegPlantIds = []; // null = mix
    double _vegDensity = 60;
    string _toolNeedle = "";

    static string CategoryOf(PropDef d) => d.Category.Length > 0 ? d.Category : "Další";

    static ToolDef DefTool(PropDef d) =>
        new($"{DefToolPrefix}{d.Id}", d.Label, CategoryOf(d), Mode.Prop, d.Id);

    static ToolDef SurfaceTool(SurfaceDef d) =>
        new($"{Kinds.SurfacePrefix}{d.Id}", d.Label, d.Category, Mode.Shape, $"{Kinds.SurfacePrefix}{d.Id}");

    /// <summary>Built-in tools, then the biome's prop definitions and
    /// surfaces (assets/config/props, assets/config/surfaces).</summary>
    IEnumerable<ToolDef> PaletteTools() =>
        Tools.Concat(PropDefs.ForBiome(_lookBiome).Select(DefTool))
            .Concat(SurfaceDefs.ForBiome(_lookBiome).Select(SurfaceTool));

    /// <summary>Any tool by id — built-ins or a definition tool (biome
    /// agnostic; ToolThumbs renders cards through this).</summary>
    public static ToolDef? FindTool(string id)
    {
        foreach (var t in Tools)
            if (t.Id == id)
                return t;
        if (id.StartsWith(DefToolPrefix) && PropDefs.TryGet(id[DefToolPrefix.Length..], out var d))
            return DefTool(d);
        if (SurfaceDefs.ForKind(id) is { } sd)
            return SurfaceTool(sd);
        return null;
    }

    /// <summary>Scene scale a click-placed prop gets: definitions of
    /// base flora use the fill.json footprint so hand-placed plants match
    /// smart-filled ones; everything else keeps the editor default.</summary>
    static double PropToolScale(string kind) =>
        kind is Kinds.House or "tower" or "stall" ? 26
        : PropDefs.TryGet(kind, out var d) && d.Base == PropKind.Flora ? FillRules.Config.SizeOf(kind)
        : 24;

    /// <summary>Click radius that cycles an existing prop's variant instead
    /// of placing a new one — small for plants so meadows can be dense.</summary>
    static float CycleRadius(string kind) =>
        PropDefs.TryGet(kind, out var d) && d.Base == PropKind.Flora ? 6 : 20;

    // --- vegetation brush UI ------------------------------------------------

    void BuildVegSection(VBoxContainer box)
    {
        _vegSection = new VBoxContainer { Visible = false };
        box.AddChild(_vegSection);
        _vegPlant = new OptionButton { TooltipText = "Co štětec sází. Směs vybírá podle okolí (u vody rákos, ve vodě lekníny…) z fill.json." };
        _vegSection.AddChild(_vegPlant);
        Slider(_vegSection, "Hustota", 15, 100, _vegDensity, v => _vegDensity = v);
        RefreshVegPlants();
    }

    /// <summary>Brush list for the current biome: "Směs" + every flora
    /// and detail definition, grouped under their palette sections.
    /// Separator rows are stored as "" (not selectable); null = mix.</summary>
    void RefreshVegPlants()
    {
        if (_vegPlant == null)
            return;
        var keep = _vegPlant.Selected >= 0 && _vegPlant.Selected < _vegPlantIds.Count
            ? _vegPlantIds[_vegPlant.Selected]
            : null;
        _vegPlant.Clear();
        _vegPlantIds.Clear();
        _vegPlant.AddItem("Směs (podle okolí)");
        _vegPlantIds.Add(null);
        var brushable = PropDefs.ForBiome(_lookBiome)
            .Where(d => d.Base is PropKind.Flora or PropKind.Detail)
            .GroupBy(CategoryOf);
        foreach (var group in brushable)
        {
            _vegPlant.AddSeparator(group.Key);
            _vegPlantIds.Add("");
            foreach (var d in group)
            {
                _vegPlant.AddItem(d.Label);
                _vegPlantIds.Add(d.Id);
            }
        }
        var idx = keep == null ? 0 : _vegPlantIds.IndexOf(keep);
        _vegPlant.Selected = Math.Max(0, idx);
    }

    /// <summary>Biome switched — palette cards and plant list follow it.</summary>
    void OnPaletteBiomeChanged()
    {
        PopulateToolCards(_toolNeedle);
        RefreshVegPlants();
    }

    // --- vegetation brush commit --------------------------------------------

    void CommitVegBrush()
    {
        var chosen = _vegPlant.Selected >= 0 && _vegPlant.Selected < _vegPlantIds.Count
            ? _vegPlantIds[_vegPlant.Selected]
            : null;
        if (chosen == "")
            chosen = null;
        var stroke = _stroke.ToList();
        var radius = _brushSize;
        var density = _vegDensity / 100.0;
        var biome = _lookBiome;
        Commit(scene => PlantAlong(scene, stroke, radius, density, chosen, biome));
    }

    /// <summary>Plant clumps under each brush circle. Spacing comes from
    /// the plant footprint and density; existing props, protection zones
    /// and the dry-land rule are respected (protection obeys the override).</summary>
    internal static void PlantAlong(
        Scene scene, List<Vector2> stroke, double radius, double density, string? chosen, string biome
    )
    {
        var cfg = FillRules.Config;
        var flora = cfg.Layers.FirstOrDefault(l => l.Id == "flora");
        var zones = Placement.ProtectionActive ? Interactions.ProtectionZones(scene) : [];
        var placed = scene.Props.Select(p => (P: new Vector2((float)p.X, (float)p.Y), R: p.Scale * 0.3)).ToList();
        var spacing = Math.Max(6, cfg.SizeOf("flora") * 0.6 * (1.6 - density));

        string? MixPick(Vector2 p, double seed)
        {
            if (flora == null)
                return null;
            var ctx = Interactions.InWater(scene, p, 0) ? "shallows"
                : Interactions.InWater(scene, p, cfg.Shore) ? "shore"
                : "land";
            if (!flora.Contexts.TryGetValue(ctx, out var table))
                return null;
            var options = table.Kinds
                .Where(k => !PropDefs.TryGet(k.Kind, out var d) || d.InBiome(biome))
                .ToList();
            var total = options.Sum(k => k.Weight);
            if (total <= 0)
                return null;
            var roll = Rng.Noise(seed + 41) * total;
            foreach (var (kind, w) in options)
                if ((roll -= w) <= 0)
                    return kind;
            return options[^1].Kind;
        }

        foreach (var c in stroke)
        {
            var r2 = radius * radius;
            for (var y = -radius; y <= radius; y += spacing)
                for (var x = -radius; x <= radius; x += spacing)
                {
                    if (x * x + y * y > r2)
                        continue;
                    var seed = (c.X + x) * 0.37 + (c.Y + y) * 0.53;
                    var p = new Vector2(
                        (float)(c.X + x + (Rng.Noise(seed + 1) - 0.5) * spacing * 0.9),
                        (float)(c.Y + y + (Rng.Noise(seed + 2) - 0.5) * spacing * 0.9)
                    );
                    if (Rng.Noise(seed + 3) > 0.35 + density * 0.6)
                        continue;
                    var kind = chosen ?? MixPick(p, seed);
                    if (kind == null)
                        continue;
                    var size = cfg.SizeOf(kind) * (0.8 + Rng.Noise(seed + 4) * 0.5);
                    var foot = size * (PropDefs.TryGet(kind, out var def) ? def.Scale : 1) * 0.42;
                    if (placed.Any(q => q.P.DistanceTo(p) < q.R + foot * 0.8))
                        continue;
                    if (!Interactions.WaterKind(kind) && Interactions.InWater(scene, p, foot * 0.5))
                        continue;
                    if (Interactions.KeptOut(kind) && Interactions.ZoneHit(p, foot, zones) != null)
                        continue;
                    placed.Add((p, foot));
                    scene.Props.Add(new Prop
                    {
                        Id = scene.AllocId(),
                        Kind = kind,
                        X = p.X,
                        Y = p.Y,
                        Scale = size,
                        Rotation = Rng.Noise(seed + 5) * Math.PI * 2,
                        Seed = (int)seed,
                    });
                }
        }
    }

    /// <summary>Probe (DMMAP_VEG_SHOT=&lt;png&gt;): open the vegetation brush
    /// with the palette visible, paint a mix stroke, dump the window.</summary>
    async void VegShot(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SetTool("veg-brush");
        if (System.Environment.GetEnvironmentVariable("DMMAP_VEG_SEARCH") is { } q)
            PopulateToolCards(q);
        var stroke = new List<Vector2> { new(300, 300), new(340, 310), new(380, 330), new(420, 340) };
        var biome = _lookBiome;
        Commit(scene => PlantAlong(scene, stroke, 42, 0.7, null, biome));
        for (var i = 0; i < 150; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"VEG props={_view.Scene.Props.Count}");
        GetTree().Quit();
    }
}
