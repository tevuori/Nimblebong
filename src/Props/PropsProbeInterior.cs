// Interior-props probe: a river band across the top carries the dock,
// raft and boats; the remaining nine kinds sit in two grid rows on open
// ground below. With DMMAP_PROBE=1 dumps /tmp/godot-interior*.png and
// quits.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class PropsProbeInterior : Node2D
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

    static readonly Vector2[] River =
    [
        new(-60, 190),
        new(300, 165),
        new(650, 200),
        new(1000, 170),
        new(1350, 210),
        new(1660, 185),
    ];

    static PropPlacement At(PropKind kind, double x, double y, double scale = 1.5, double rot = 0) =>
        new(kind, x, y, scale, rot, x * 0.37 + y * 0.53);

    public override void _Ready()
    {
        var shapes = new List<TerrainShape>
        {
            // A rocky knoll far south-east for context.
            Hill(1340, 860, 150, model: 2, height: 45, roughness: 50),
        };
        var massifs = TerrainGeometry.BuildMassifs(shapes);

        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);
        _stage.Albedo.SetMassifs(massifs);
        _stage.Height.SetMassifs(massifs);
        _stage.SetSun(new Vector2(-0.5f, -0.62f), 0.12f, true);

        var water = new WaterPainter { Name = "WaterPainter" };
        _stage.AlbedoVp.AddChild(water);
        water.AddRiver(River, 80);
        var waterHeight = new WaterHeight { Name = "WaterHeight" };
        _stage.HeightVp.AddChild(waterHeight);
        waterHeight.AddRiver(River, 80);

        var props = new List<PropPlacement>
        {
            // Water band: two docks reaching north from the south bank,
            // a raft and boats afloat.
            At(PropKind.Dock, 240, 355, 1.5, -Math.PI / 2 + 0.15),
            At(PropKind.Dock, 480, 370, 1.5, -Math.PI / 2 - 0.2),
            At(PropKind.Dock, 700, 350, 1.4, -Math.PI / 2 + 0.35),
            At(PropKind.Boat, 330, 185, 1.5, 0.35),
            At(PropKind.Boat, 560, 210, 1.5, -0.7),
            At(PropKind.Boat, 870, 180, 1.5, 2.2),
            At(PropKind.Boat, 1150, 200, 1.6, 0.9),

            // Row 1 — treasure + room openings.
            At(PropKind.Chest, 200, 560, 1.8),
            At(PropKind.Chest, 330, 580, 1.8, 0.5),
            At(PropKind.Portal, 500, 560, 1.7),
            At(PropKind.Portal, 650, 570, 1.6, 0.3),
            At(PropKind.Door, 810, 560, 2.0),
            At(PropKind.Door, 920, 570, 2.0, Math.PI / 2),
            At(PropKind.Window, 1060, 560, 2.0),
            At(PropKind.Window, 1190, 575, 2.0, Math.PI / 2),

            // Row 2 — furniture.
            At(PropKind.Table, 210, 810, 1.7),
            At(PropKind.Table, 350, 830, 1.7, 0.6),
            At(PropKind.Bed, 500, 810, 1.6),
            At(PropKind.Bed, 640, 830, 1.6, Math.PI / 2 + 0.2),
            At(PropKind.Shelf, 800, 810, 1.8),
            At(PropKind.Shelf, 930, 830, 1.8, -0.4),
            At(PropKind.Rug, 1090, 815, 1.7, 0.25),
            At(PropKind.Fireplace, 1260, 815, 1.8, Math.PI),
            At(PropKind.Chair, 1390, 810, 1.0),
            At(PropKind.Chair, 1455, 830, 1.0, 1.2),
            At(PropKind.Chair, 1520, 810, 1.0, -0.7),
            At(PropKind.Candle, 210, 930, 1.0),
            At(PropKind.Candle, 260, 940, 1.0),
        };

        var painter = new PropsPainter();
        _stage.AlbedoVp.AddChild(painter);
        painter.SetProps(props);
        var bumps = new PropsHeight();
        _stage.HeightVp.AddChild(bumps);
        bumps.SetMassifs(massifs);
        bumps.SetProps(props);

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
        img.SavePng("/tmp/godot-interior.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-interior-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-interior-height.png");
        GD.Print("dumped /tmp/godot-interior.png ", img.GetSize());
        GetTree().Quit();
    }
}
