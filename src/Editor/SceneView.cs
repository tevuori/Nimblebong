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
        view.Scene.Width = mapSize.X;
        view.Scene.Height = mapSize.Y;
        view.Biome = SceneConfig.LoadBiome("woodland");
        view.Mood = SceneConfig.LoadMood("day");
        view.Rebuild();
        return view;
    }

    public void SetBiome(string id)
    {
        Biome = SceneConfig.LoadBiome(id);
        Scene.Biome = id;
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
            albedo.Ground = Biome.Ground;
            albedo.Cliff = Biome.Cliff;
            albedo.Stone = Biome.Stone;
            albedo.SetMassifs(Massifs);
            Stage.Height.SetMassifs(Massifs);
        }
        var massifs = Massifs;

        var water = new WaterPainter { Water = Biome.Water };
        var waterH = new WaterHeight();
        var crossings = new CrossingPainter { WaterLight = Biome.Water };
        crossings.SetMassifs(massifs);
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
        }
        AddLayer(Stage.AlbedoVp, water);
        AddLayer(Stage.AlbedoVp, crossings);
        AddLayer(Stage.HeightVp, waterH);

        var props = Scene
            .Props.Select(p => new PropPlacement(
                MapProp(p.Kind),
                p.X,
                p.Y,
                p.Scale / 24.0,
                p.Rotation,
                p.Seed
            ))
            .ToList();
        var propsPainter = new PropsPainter
        {
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
    };

    static PropKind MapProp(string kind) =>
        PropKinds.TryGetValue(kind, out var k) ? k : PropKind.Marker;
}
