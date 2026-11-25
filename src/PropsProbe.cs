// Props probe: a small scene exercising every prop kind — a mixed
// forest scattered on open ground, trees standing on a plateau top,
// rocks tumbled at a slope foot, two houses — painted through the
// lighting stage. With DMMAP_PROBE=1 dumps the composite plus the raw
// albedo/height viewports to /tmp/godot-props*.png and quits.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class PropsProbe : Node2D
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
            // Rocky plateau — carries a little grove on its top.
            Hill(380, 300, 190, model: 2, height: 55, roughness: 55, size: 50),
            // Broad rocky hill south-east — rocks tumble at its foot.
            Hill(1170, 660, 150, height: 40, roughness: 45),
            // Gentle dirt mound, pines on top.
            Hill(1250, 290, 120, model: 1, height: 22, roughness: 30),
        };
        var massifs = TerrainGeometry.BuildMassifs(shapes);

        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);
        _stage.Albedo.SetMassifs(massifs);
        _stage.Height.SetMassifs(massifs);

        var props = new List<Prop>();

        // Mixed forest patch on open ground, south-west.
        var forest = Poly.Ellipse(620, 800, 270, 130, 0.12, 26);
        props.AddRange(
            PropScatter.ScatterMix(
                forest,
                58,
                [(PropKind.Tree, 3.0), (PropKind.Pine, 1.6), (PropKind.Bush, 1.2)],
                density: 0.9
            )
        );

        // Grove on the plateau top (uses the massif's unioned top).
        var plateau = massifs.Find(m => m.Height == 55);
        if (plateau != null)
            foreach (var top in plateau.Tops)
                props.AddRange(
                    PropScatter.ScatterMix(
                        top,
                        66,
                        [(PropKind.Tree, 3.0), (PropKind.Pine, 1.0)],
                        density: 0.8,
                        scaleMin: 0.7,
                        scaleMax: 1.05
                    )
                );

        // Pines scattered on the dirt mound.
        var mound = massifs.Find(m => m.Model == 1);
        if (mound != null)
            foreach (var top in mound.Tops)
                props.AddRange(
                    PropScatter.Scatter(top, 62, PropKind.Pine, density: 0.75, scaleMin: 0.6, scaleMax: 0.95)
                );

        // Rock field hugging the rocky hill's south foot + a few bushes.
        var rocks = Poly.Ellipse(1180, 915, 160, 55, 0.05, 18);
        props.AddRange(
            PropScatter.ScatterMix(
                rocks,
                44,
                [(PropKind.Rock, 3.0), (PropKind.Bush, 1.0)],
                density: 0.75,
                scaleMin: 0.8,
                scaleMax: 1.5
            )
        );
        // A couple of strays up on the slope band itself.
        var hill = massifs.Find(m => m is { Height: 40, Model: 0 });
        if (hill != null)
            props.AddRange(
                PropScatter.Scatter(hill.Base, 120, PropKind.Rock, density: 0.5, scaleMin: 0.9, scaleMax: 1.4)
            );

        // Two houses on open ground.
        props.Add(new Prop(PropKind.House, 985, 505, 1.0, 0.12, 985 * 0.37 + 505 * 0.53));
        props.Add(new Prop(PropKind.House, 1435, 640, 0.85, -0.35, 1435 * 0.37 + 640 * 0.53));
        // A path-side bush + lone tree near the houses for scale.
        props.Add(new Prop(PropKind.Bush, 930, 555, 1.1, 0, 930 * 0.37 + 555 * 0.53));
        props.Add(new Prop(PropKind.Tree, 1055, 555, 1.15, 0, 1055 * 0.37 + 555 * 0.53));

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
        img.SavePng("/tmp/godot-props.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-props-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-props-height.png");
        var mat = (ShaderMaterial)_stage.DebugMaterial();
        mat.SetShaderParameter("uDebug", 2);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _stage.Snapshot().SavePng("/tmp/godot-props-shadow.png");
        mat.SetShaderParameter("uDebug", 0);
        GD.Print("dumped /tmp/godot-props.png ", img.GetSize());
        GetTree().Quit();
    }
}
