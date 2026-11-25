// Props probe for the village & structures batch: a 9×3 grid on open
// ground — one column per kind (tower … marker), one row per seeded
// model variant — plus a watchtower on a rocky plateau and a barricade
// at its foot. With DMMAP_PROBE=1 dumps the composite plus the raw
// albedo/height viewports to /tmp/godot-props-village*.png and quits.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class PropsProbeVillage : Node2D
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

    public override void _Ready()
    {
        var shapes = new List<TerrainShape>
        {
            // Rocky plateau west — carries a watchtower.
            Hill(190, 230, 120, model: 2, height: 50, roughness: 45, size: 44),
        };
        var massifs = TerrainGeometry.BuildMassifs(shapes);

        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);
        _stage.Albedo.SetMassifs(massifs);
        _stage.Height.SetMassifs(massifs);

        var props = new List<PropPlacement>();

        // 9 kinds × 3 model variants. Model comes from the first Jitter
        // draw — floor(Noise(seed) * 3) — so the row seeds below are
        // picked to land each variant: <⅓ → 0, <⅔ → 1, else 2.
        var kinds = new[]
        {
            PropKind.Tower,
            PropKind.Stall,
            PropKind.Cart,
            PropKind.Flag,
            PropKind.Barricade,
            PropKind.Lantern,
            PropKind.Fountain,
            PropKind.Pillar,
            PropKind.Marker,
        };
        // Row = model variant; per-cell seeds all hash into the right
        // bucket while keeping each cell's jitter stream distinct.
        var seeds = new[]
        {
            new[] { 1.0, 2.5, 6.5, 7.5, 8.5, 10.5, 12.0, 12.5, 14.0 }, // model 0
            new[] { 0.5, 3.0, 3.5, 4.5, 5.0, 9.5, 11.0, 13.5, 15.5 }, // model 1
            new[] { 1.5, 2.0, 4.0, 5.5, 6.0, 7.0, 8.0, 9.0, 10.0 }, // model 2
        };
        var rowRots = new[] { 0.06, -0.1, 0.18 };
        for (var m = 0; m < 3; m++)
        for (var k = 0; k < kinds.Length; k++)
        {
            var x = 400 + k * 135;
            var y = 250 + m * 270;
            props.Add(new PropPlacement(kinds[k], x, y, 1.0, rowRots[m], seeds[m][k]));
        }

        // Watchtower on the plateau top + barricade at its south foot.
        props.Add(new PropPlacement(PropKind.Tower, 190, 225, 0.9, 0, 2.5));
        props.Add(new PropPlacement(PropKind.Barricade, 185, 330, 1.0, 0.3, 4.0));
        props.Add(new PropPlacement(PropKind.Flag, 260, 250, 1.0, -0.2, 0.5));

        var painter = new PropsPainter();
        _stage.AlbedoVp.AddChild(painter);
        painter.SetProps(props);
        var bumps = new PropsHeight();
        _stage.HeightVp.AddChild(bumps);
        bumps.SetMassifs(massifs);
        bumps.SetProps(props);

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
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = _stage.Snapshot();
        img.SavePng("/tmp/godot-props-village.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-props-village-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-props-village-height.png");
        var mat = (ShaderMaterial)_stage.DebugMaterial();
        mat.SetShaderParameter("uDebug", 2);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _stage.Snapshot().SavePng("/tmp/godot-props-village-shadow.png");
        mat.SetShaderParameter("uDebug", 0);
        GD.Print("dumped /tmp/godot-props-village.png ", img.GetSize());
        GetTree().Quit();
    }
}
