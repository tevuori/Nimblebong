// Milestone-1 probe: builds a demo terrain scene, paints it through the
// lighting stage, and with `-- --probe` (or headless) dumps the
// composite to /tmp/godot-terrain.png for visual review.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class TerrainProbe : Node2D
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
        // Organic blob — radius modulated by a few seeded sinusoids,
        // the way a drawn hill outline wanders.
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

    public override void _Ready()
    {
        var shapes = new List<TerrainShape>
        {
            Hill(300, 260, 170, height: 30),
            Hill(300, 240, 90, model: 2, height: 60, roughness: 60),
            Hill(800, 300, 150, model: 1),
            Hill(1300, 280, 160, model: 2, roughness: 50),
            Hill(400, 700, 130, roughness: 90),
            Hill(950, 760, 110, roughness: 0),
            Hill(1210, 760, 110, roughness: 50),
            Hill(1470, 760, 100, roughness: 100),
            Hill(680, 560, 110, height: 20, roughness: 45),
            Hill(870, 550, 110, height: 70, roughness: 45),
            Hill(775, 470, 90, height: 45, roughness: 60),
        };
        var massifs = TerrainGeometry.BuildMassifs(shapes);
        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);
        _stage.Albedo.SetMassifs(massifs);
        _stage.Height.SetMassifs(massifs);
        _stage.SetSun(new Vector2(-0.5f, -0.62f), 0.12f, true);
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
        // Give the viewports a few frames to paint before reading back.
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = _stage.Snapshot();
        var mat = (ShaderMaterial)_stage.DebugMaterial();
        mat.SetShaderParameter("uDebug", 2);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _stage.Snapshot().SavePng("/tmp/godot-debug-height.png");
        mat.SetShaderParameter("uDebug", 0);
        img.SavePng("/tmp/godot-terrain.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-height.png");
        GD.Print("dumped /tmp/godot-terrain.png ", img.GetSize());
        GetTree().Quit();
    }
}
