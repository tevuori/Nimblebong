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

    // Per-layer painters, rebuilt from scratch each commit.
    readonly List<Node> _layers = [];

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
        var massifs = TerrainGeometry.BuildMassifs(shapes);
        var albedo = Stage.Albedo;
        albedo.Ground = Biome.Ground;
        albedo.Cliff = Biome.Cliff;
        albedo.Stone = Biome.Stone;
        albedo.SetMassifs(massifs);
        Stage.Height.SetMassifs(massifs);

        var water = new WaterPainter { Water = Biome.Water };
        var waterH = new WaterHeight();
        foreach (var st in Scene.Strokes)
        {
            if (st.Kind == Kinds.River)
            {
                water.AddRiver(st.Points, st.Width);
                waterH.AddRiver(st.Points, st.Width);
            }
            else if (st.Kind == Kinds.Road)
            {
                water.AddRoad(st.Points, st.Width);
            }
        }
        AddLayer(Stage.AlbedoVp, water);
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

    static PropKind MapProp(string kind) =>
        kind switch
        {
            "pine" => PropKind.Pine,
            "rock" => PropKind.Rock,
            "bush" => PropKind.Bush,
            "house" => PropKind.House,
            _ => PropKind.Tree,
        };
}
