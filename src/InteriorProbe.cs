// Interiors probe: exercises the InteriorPainter without SceneView —
// a room with a door + window carved into its wall ring, an L-shaped
// room (clipper workout) with an arched door, a furnished room that
// skips procedural decor, three houses (gable / hip / thatch, one
// rotated) and two floors (planks / stone tiles).
// `-- --probe` / DMMAP_PROBE=1 dumps /tmp/godot-interiors*.png.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class InteriorProbe : Node2D
{
    static readonly Vector2 MapSize = new(1600, 1000);
    LightingStage _stage = null!;

    static List<Vector2> RectAt(double cx, double cy, double w, double h, double rot = 0)
    {
        var cos = Math.Cos(rot);
        var sin = Math.Sin(rot);
        var pts = new List<Vector2>();
        foreach (var (x, y) in new[] { (-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2) })
            pts.Add(new Vector2((float)(cx + x * cos - y * sin), (float)(cy + x * sin + y * cos)));
        return pts;
    }

    int _id = 1;

    Prop At(string kind, double x, double y, double scale = 24, int seed = 0) =>
        new()
        {
            Id = _id++,
            Kind = kind,
            X = x,
            Y = y,
            Scale = scale,
            Seed = seed,
        };

    Shape Area(string kind, List<Vector2> pts, double height = 0, double angle = 0, int model = 0) =>
        new()
        {
            Id = _id++,
            Kind = kind,
            Points = pts,
            Height = height,
            Angle = angle,
            Model = model,
        };

    public override void _Ready()
    {
        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);
        var biome = SceneConfig.LoadBiome("woodland");
        var mood = SceneConfig.LoadMood("day");
        _stage.Albedo.Ground = biome.Ground;
        _stage.Albedo.Cliff = biome.Cliff;
        _stage.Albedo.Stone = biome.Stone;
        _stage.Albedo.SetMassifs([]);
        _stage.Height.SetMassifs([]);

        var interior = new InteriorPainter { Biome = biome };
        var interiorH = new InteriorHeight();
        _stage.AlbedoVp.AddChild(interior);
        _stage.HeightVp.AddChild(interiorH);

        // Floors first — they sit under everything like the web's order.
        interior.Add(Area(Kinds.Floor, RectAt(430, 830, 260, 140))); // planks
        interior.Add(Area(Kinds.Floor, RectAt(760, 830, 200, 110), model: 2)); // stone tiles

        // Room A — door on the south edge, window on the west edge.
        var roomA = Area(Kinds.Room, RectAt(365, 330, 290, 180), height: 20);
        interior.Add(
            roomA,
            [At("door", 365, 420, 30), At("window", 220, 320, 24), At("window", 300, 240, 22)]
        );
        interiorH.Add(roomA);

        // Room B — L-shaped footprint: the clipper has to carve decor
        // and the wall ring has to carve a door on the inner leg.
        var roomB = Area(
            Kinds.Room,
            [new(90, 540), new(260, 540), new(260, 610), new(190, 610), new(190, 700), new(90, 700)],
            height: 20
        );
        interior.Add(roomB, [At("door", 140, 700, 28), At("window", 190, 655, 20, seed: 1)]);
        interiorH.Add(roomB);

        // Room C — small furnished room: a table prop suppresses the
        // procedural decor, and the door renders as an arch (model 2).
        var roomC = Area(Kinds.Room, RectAt(635, 315, 160, 110), height: 20);
        interior.Add(roomC, [At("door", 715, 320, 26, seed: 2), At("table", 640, 300)]);
        interiorH.Add(roomC);

        // Houses — gable rotated ~17°, hip taller-than-wide (the frame
        // quarter-turns the ridge), thatch slightly rotated.
        var houseA = Area(Kinds.House, RectAt(990, 300, 200, 120, 0.3), height: 22, angle: 0.3, model: 0);
        var houseB = Area(Kinds.House, RectAt(1270, 300, 140, 170, -0.15), height: 22, angle: -0.15, model: 1);
        var houseC = Area(Kinds.House, RectAt(1100, 620, 150, 100, -0.25), height: 22, angle: -0.25, model: 2);
        foreach (var hs in new[] { houseA, houseB, houseC })
        {
            interior.Add(hs);
            interiorH.Add(hs);
        }

        mood.ApplyTo(_stage);

        var probe = System.Environment.GetEnvironmentVariable("DMMAP_PROBE") == "1";
        foreach (var arg in OS.GetCmdlineArgs())
            if (arg is "--probe" or "--headless")
                probe = true;
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--probe")
                probe = true;
        if (probe)
            CallDeferred(nameof(DumpAndQuit));
    }

    async void DumpAndQuit()
    {
        // Give the viewports a few frames to paint before reading back.
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = _stage.Snapshot();
        img.SavePng("/tmp/godot-interiors.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-interiors-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-interiors-height.png");
        GD.Print("dumped /tmp/godot-interiors.png ", img.GetSize());
        GetTree().Quit();
    }
}
