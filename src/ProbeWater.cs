// Rivers & roads probe: massifs + two rivers + a road painted through
// the lighting stage. Rivers are added to AlbedoVp AFTER the terrain
// painter so they visually carve through massifs, and to HeightVp
// AFTER the heightmap painter so the lighting shader digs a channel.
// With DMMAP_PROBE=1 (or --probe) dumps /tmp/godot-water*.png.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class ProbeWater : Node2D
{
    static readonly Vector2 MapSize = new(1600, 1000);
    LightingStage _stage = null!;

    static TerrainShape Hill(
        double cx,
        double cy,
        double r,
        int model = 0,
        double height = 30,
        double roughness = 0,
        double size = 46
    )
    {
        const int n = 28;
        var seed = cx * 0.31 + cy * 0.47;
        var pts = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = (i / (double)n) * Mathf.Tau;
            var w =
                1.0
                + 0.16 * Math.Sin(a * 2 + seed)
                + 0.1 * Math.Sin(a * 3 + seed * 1.7)
                + 0.07 * Math.Sin(a * 5 + seed * 2.3);
            pts[i] = new Vector2(
                (float)(cx + Mathf.Cos(a) * r * w),
                (float)(cy + Mathf.Sin(a) * r * w * 0.82)
            );
        }
        return new TerrainShape
        {
            Model = model,
            Height = height,
            Roughness = roughness,
            Size = size,
            Outline = pts,
        };
    }

    // Same control points feed the albedo and height painters so the
    // carved channel lines up exactly with the water surface.
    static readonly Vector2[] RiverA =
    [
        new(700, -60),
        new(660, 150),
        new(580, 320),
        new(545, 470),
        new(600, 640),
        new(545, 820),
        new(600, 1060),
    ];
    static readonly Vector2[] RiverB =
    [
        new(-60, 830),
        new(160, 880),
        new(360, 925),
        new(560, 880),
        new(780, 905),
        new(1000, 855),
        new(1220, 905),
        new(1440, 855),
        new(1660, 890),
    ];
    static readonly Vector2[] Road =
    [
        new(90, 1060),
        new(240, 965),
        new(400, 905),
        new(560, 878),
        new(790, 820),
        new(1010, 755),
        new(1210, 675),
        new(1410, 600),
        new(1660, 555),
    ];

    public override void _Ready()
    {
        var shapes = new List<TerrainShape>
        {
            Hill(480, 480, 170, height: 30),
            Hill(470, 460, 85, model: 2, height: 60, roughness: 55),
            Hill(1150, 300, 160, model: 2, height: 55, roughness: 50),
            Hill(1250, 720, 140, model: 1, height: 25),
            Hill(300, 240, 130, height: 20, roughness: 60),
        };
        var massifs = TerrainGeometry.BuildMassifs(shapes);
        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);
        _stage.Albedo.SetMassifs(massifs);
        _stage.Height.SetMassifs(massifs);
        _stage.SetSun(new Vector2(-0.5f, -0.62f), 0.12f, true);

        // Water paints OVER the terrain (child order = draw order) so
        // rivers carve through massifs; the height painter digs the
        // channel under them.
        var water = new WaterPainter { Name = "WaterPainter" };
        _stage.AlbedoVp.AddChild(water);
        water.AddRiver(RiverA, 34);
        water.AddRiver(RiverB, 26);
        water.AddRoad(Road, 18);
        var waterHeight = new WaterHeight { Name = "WaterHeight" };
        _stage.HeightVp.AddChild(waterHeight);
        waterHeight.AddRiver(RiverA, 34);
        waterHeight.AddRiver(RiverB, 26);

        var probe = System.Environment.GetEnvironmentVariable("DMMAP_PROBE") == "1";
        foreach (var arg in OS.GetCmdlineArgs())
            if (arg is "--probe" or "--headless")
                probe = true;
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg is "--probe")
                probe = true;
        if (probe)
            CallDeferred(nameof(DumpAndQuit));
    }

    async void DumpAndQuit()
    {
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = _stage.Snapshot();
        img.SavePng("/tmp/godot-water.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-water-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-water-height.png");
        GD.Print("dumped /tmp/godot-water.png ", img.GetSize());
        GetTree().Quit();
    }
}
