// Scene view — turns a Model.Scene into the painted world: rebuilds
// all painters from scene data on every commit. Owns the LightingStage
// and the per-layer painter nodes; callers mutate the Scene, call
// Rebuild(), done.
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class SceneView : Node2D
{
    public LightingStage Stage = null!;
    public Scene Scene = new();
    public ResolvedBiome Biome = null!;
    public ResolvedMood Mood = null!;
    /// <summary>The massifs built during the last Rebuild — used by the
    /// editor for selection hit-testing and live previews.</summary>
    public List<Massif> Massifs = [];

    // Per-layer painters, rebuilt from scratch each commit.
    readonly List<Node> _layers = [];
    // Massif cache — props/strokes rebuilds skip the expensive clipper
    // when the terrain shapes didn't change.
    List<TerrainShape> _lastShapes = [];
    bool _massifsValid;

    public static SceneView Create(Vector2 mapSize)
    {
        var view = new SceneView { Stage = LightingStage.Create(mapSize) };
        view.AddChild(view.Stage);
        // Fog-of-war overlay — dark veil over unrevealed regions,
        // drawn above the lit map but below the editor's ghost layer.
        var fogMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/fog.gdshader") };
        fogMat.SetShaderParameter("uMask", view.Stage.FogVp.GetTexture());
        view.AddChild(
            new TextureRect
            {
                Texture = view.Stage.FogVp.GetTexture(),
                Material = fogMat,
                Size = mapSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }
        );
        view.Scene.Width = mapSize.X;
        view.Scene.Height = mapSize.Y;
        view.Scene.Biome = "woodland";
        view.Biome = SceneConfig.ResolveArt(
            SceneConfig.LoadBiome("woodland"), null, null);
        view.Mood = SceneConfig.LoadMood("day");
        view.Rebuild();
        return view;
    }

    public void SetBiome(string id)
    {
        Scene.Biome = id;
        Biome = SceneConfig.ResolveArt(
            SceneConfig.LoadBiome(id), Scene.ArtPack, Scene.Season);
        _massifsValid = false;
        Rebuild();
    }

    public void SetArtPack(string id)
    {
        Scene.ArtPack = id;
        ReResolveBiome();
    }

    public void SetSeason(string? id)
    {
        Scene.Season = id;
        ReResolveBiome();
    }

    void ReResolveBiome()
    {
        Biome = SceneConfig.ResolveArt(
            SceneConfig.LoadBiome(Scene.Biome ?? "woodland"),
            Scene.ArtPack,
            Scene.Season
        );
        _massifsValid = false;
        Rebuild();
    }

    public void SetMood(string id)
    {
        Mood = SceneConfig.LoadMood(id);
        Scene.Mood = id;
        Rebuild();
    }

    /// <summary>Repaint everything from the current Scene data.</summary>
    public void Rebuild()
    {
        foreach (var n in _layers)
            n.QueueFree();
        _layers.Clear();

        var shapes = Scene
            .Shapes.Where(s => s.Kind == Kinds.Terrain)
            .Select(s => new TerrainShape
            {
                Model = s.Model,
                Height = s.Height,
                Size = s.Size,
                Roughness = s.Roughness,
                Outline = [.. s.Points],
            })
            .ToList();
        if (!_massifsValid || !ShapesEqual(_lastShapes, shapes))
        {
            Massifs = TerrainGeometry.BuildMassifs(shapes);
            _lastShapes = shapes;
            _massifsValid = true;
            var albedo = Stage.Albedo;
            albedo.Biome = Biome;
            albedo.Ground = Biome.Ground;
            albedo.Cliff = Biome.Cliff;
            albedo.Stone = Biome.Stone;
            albedo.GrassTile = Biome.GrassTile;
            albedo.Winter = Biome.SeasonLook == "winter";
            albedo.SetMassifs(Massifs);
            Stage.Height.SetMassifs(Massifs);
        }
        var massifs = Massifs;

        // Area fills — ponds and fields under everything else.
        var areas = new AreaPainter { Water = Biome.Water, Ground = Biome.Ground };
        var areaH = new AreaHeight();
        var fortify = new FortifyPainter { Stone = Biome.Stone, Wood = Biome.Wood };
        foreach (var sh in Scene.Shapes)
        {
            if (sh.Kind is Kinds.Pond or Kinds.Field)
            {
                areas.Add(sh.Kind, sh.Points);
                if (sh.Kind == Kinds.Pond)
                    areaH.AddPond(sh.Points);
            }
        }
        AddLayer(Stage.AlbedoVp, areas);
        AddLayer(Stage.HeightVp, areaH);

        // Buildings & interiors — house/room/floor footprints with
        // roofs, wall rings and door/window openings carved from props.
        var interiors = new InteriorPainter { Biome = Biome };
        var interiorsH = new InteriorHeight();
        interiorsH.SetMassifs(massifs);
        foreach (var sh in Scene.Shapes)
            if (sh.Kind is Kinds.House or Kinds.Room or Kinds.Floor)
            {
                interiors.Add(sh, Scene.Props);
                interiorsH.Add(sh);
            }
        AddLayer(Stage.AlbedoVp, interiors);
        AddLayer(Stage.HeightVp, interiorsH);

        var water = new WaterPainter
        {
            Water = Biome.Water,
            RoadDirt = Biome.At("road.dirt"),
            RoadVerge = Biome.At("road.verge"),
            RoadHalo = Biome.At("road.halo"),
        };
        var waterH = new WaterHeight();
        var crossings = new CrossingPainter { WaterLight = Biome.Water };
        crossings.SetMassifs(massifs);
        // Walls/fences open a gated gap wherever they cross a road.
        var gateCuts = Scene
            .Strokes.Where(s => s.Kind == Kinds.Road)
            .Select(s => ((IReadOnlyList<Vector2>)s.Points, s.Width))
            .ToList();
        foreach (var st in Scene.Strokes)
        {
            if (st.Kind == Kinds.River)
            {
                water.AddRiver(st.Points, st.Width);
                waterH.AddRiver(st.Points, st.Width);
                crossings.AddStroke(st.Kind, st.Points, st.Width);
            }
            else if (st.Kind == Kinds.Road)
            {
                water.AddRoad(st.Points, st.Width);
                crossings.AddStroke(st.Kind, st.Points, st.Width);
            }
            else if (st.Kind is Kinds.Wall or Kinds.Fence)
            {
                fortify.Add(st.Kind, st.Points, st.Width, st.Model, gateCuts);
            }
        }
        water.SetRocks(
            Scene.Props.Where(p => MapProp(p.Kind) == PropKind.Rock).Select(p => new Vector2(
                (float)p.X,
                (float)p.Y
            ))
        );
        var bridges = new BridgePainter { Stone = Biome.Stone };
        bridges.SetStrokes(Scene.Strokes);
        AddLayer(Stage.AlbedoVp, water);
        Stage.Water.SetWater(Scene);
        Stage.Glow.SetGlow(Scene, Biome.Id);
        AddLayer(Stage.AlbedoVp, crossings);
        AddLayer(Stage.HeightVp, waterH);
        AddLayer(Stage.AlbedoVp, fortify);
        AddLayer(Stage.AlbedoVp, bridges);

        var anchored = interiors.AnchoredPropIds;
        var props = Scene
            .Props.Where(p => !anchored.Contains(p.Id))
            .Select(p =>
                new PropPlacement(
                    MapProp(p.Kind),
                    p.X,
                    p.Y,
                    p.Scale / 24.0,
                    p.Rotation,
                    p.Seed
                )
                {
                    Model = p.Model,
                    Species = p.Species,
                    Text = p.Text,
                }.WithDef(p.Kind)
            )
            .ToList();
        var propsPainter = new PropsPainter
        {
            BiomeCfg = Biome,
            Foliage = Biome.Foliage,
            Stone = Biome.Stone,
            Roof = Biome.Roof,
            Wood = Biome.Wood,
        };
        AddLayer(Stage.AlbedoVp, propsPainter);
        propsPainter.SetProps(props);
        var propsH = new PropsHeight();
        AddLayer(Stage.HeightVp, propsH);
        propsH.SetMassifs(massifs);
        propsH.SetProps(props);

        // Fog mask — repainted in place each commit.
        Stage.Fog.Strokes = Scene.Fog;
        Stage.Fog.CoverAll = Scene.FogCoverAll;
        Stage.Fog.Rooms = Scene.FogRooms
            ? Scene
                .Shapes.Where(sh => sh.Kind == Kinds.Room)
                .Select(sh => sh.Points.ToArray())
                .ToList()
            : [];
        Stage.Fog.QueueRedraw();

        Mood.ApplyTo(Stage);
    }

    void AddLayer(SubViewport vp, Node2D painter)
    {
        vp.AddChild(painter);
        _layers.Add(painter);
    }

    static bool ShapesEqual(List<TerrainShape> a, List<TerrainShape> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            var (x, y) = (a[i], b[i]);
            if (
                x.Model != y.Model
                || x.Height != y.Height
                || x.Size != y.Size
                || x.Roughness != y.Roughness
                || !x.Outline.SequenceEqual(y.Outline)
            )
                return false;
        }
        return true;
    }

    static readonly Dictionary<string, PropKind> PropKinds = new()
    {
        ["tree"] = PropKind.Tree,
        ["pine"] = PropKind.Pine,
        ["rock"] = PropKind.Rock,
        ["bush"] = PropKind.Bush,
        ["house"] = PropKind.House,
        ["tent"] = PropKind.Tent,
        ["campfire"] = PropKind.Campfire,
        ["well"] = PropKind.Well,
        ["ruin"] = PropKind.Ruin,
        ["grave"] = PropKind.Grave,
        ["bones"] = PropKind.Bones,
        ["statue"] = PropKind.Statue,
        ["stonecircle"] = PropKind.StoneCircle,
        ["crystal"] = PropKind.Crystal,
        ["bale"] = PropKind.Bale,
        ["crate"] = PropKind.Crate,
        ["barrel"] = PropKind.Barrel,
        ["tower"] = PropKind.Tower,
        ["stall"] = PropKind.Stall,
        ["cart"] = PropKind.Cart,
        ["flag"] = PropKind.Flag,
        ["barricade"] = PropKind.Barricade,
        ["lantern"] = PropKind.Lantern,
        ["fountain"] = PropKind.Fountain,
        ["pillar"] = PropKind.Pillar,
        ["marker"] = PropKind.Marker,
        ["dock"] = PropKind.Dock,
        ["boat"] = PropKind.Boat,
        ["chest"] = PropKind.Chest,
        ["portal"] = PropKind.Portal,
        ["door"] = PropKind.Door,
        ["window"] = PropKind.Window,
        ["table"] = PropKind.Table,
        ["bed"] = PropKind.Bed,
        ["shelf"] = PropKind.Shelf,
        ["rug"] = PropKind.Rug,
        ["fireplace"] = PropKind.Fireplace,
        ["label"] = PropKind.Label,
        ["butterfly"] = PropKind.Butterfly,
        ["bird"] = PropKind.Bird,
        ["flora"] = PropKind.Flora,
        ["detail"] = PropKind.Detail,
    };

    /// <summary>Built-in kind names only (JSON definitions excluded).</summary>
    public static bool TryMapBuiltinProp(string kind, out PropKind k) =>
        PropKinds.TryGetValue(kind, out k);

    /// <summary>Scene kind → drawer: built-in name, else the base of a
    /// JSON prop definition (assets/config/props), else a marker.</summary>
    public static PropKind MapProp(string kind) =>
        PropKinds.TryGetValue(kind, out var k) ? k
        : PropDefs.TryGet(kind, out var def) ? def.Base
        : PropKind.Marker;
}
