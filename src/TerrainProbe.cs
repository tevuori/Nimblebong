// Integrated showcase probe: terrain + rivers/roads + props, palette
// and mood driven by the JSON config — the whole render stack in one
// scene. `-- --probe` / DMMAP_PROBE=1 dumps /tmp/godot-terrain.png.
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
            Hill(280, 250, 170, height: 30),
            Hill(300, 235, 90, model: 2, height: 62, roughness: 60),
            Hill(1330, 260, 160, model: 2, roughness: 50),
            Hill(380, 720, 140, roughness: 55),
            Hill(950, 780, 120, roughness: 45),
            Hill(1250, 290, 115, model: 1, height: 22, roughness: 30),
            Hill(720, 520, 110, height: 20, roughness: 45),
            Hill(900, 510, 110, height: 70, roughness: 50),
            Hill(810, 430, 85, height: 45, roughness: 60),
        };
        var massifs = TerrainGeometry.BuildMassifs(shapes);
        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);

        // Config-driven palette + mood.
        var biome = SceneConfig.LoadBiome("woodland");
        var mood = SceneConfig.LoadMood("day");
        _stage.Albedo.Ground = biome.Ground;
        _stage.Albedo.Cliff = biome.Cliff;
        _stage.Albedo.Stone = biome.Stone;

        _stage.Albedo.SetMassifs(massifs);
        _stage.Height.SetMassifs(massifs);

        // Rivers + a road — albedo painter after terrain so water
        // carves through bands; height painter carves the channel.
        var water = new WaterPainter { Water = biome.Water };
        var waterH = new WaterHeight();
        var river = new List<Vector2>
        {
            new(430, -20),
            new(450, 160),
            new(520, 330),
            new(490, 480),
            new(560, 620),
            new(540, 780),
            new(600, 1020),
        };
        var road = new List<Vector2>
        {
            new(-20, 560),
            new(300, 540),
            new(560, 605),
            new(900, 660),
            new(1300, 700),
            new(1620, 720),
        };
        water.AddRiver(river, 30);
        water.AddRoad(road, 16);
        waterH.AddRiver(river, 30);
        _stage.AlbedoVp.AddChild(water);
        _stage.HeightVp.AddChild(waterH);

        // Props: grove on a plateau, forest on the ground, rocks at a
        // south foot, two houses by the road.
        var props = new List<PropPlacement>();
        var plateau = massifs.Find(m => m.Height == 62);
        if (plateau != null)
            foreach (var top in plateau.Tops)
                props.AddRange(
                    PropScatter.ScatterMix(
                        top,
                        64,
                        [(PropKind.Tree, 3.0), (PropKind.Pine, 1.0)],
                        density: 0.8,
                        scaleMin: 0.7,
                        scaleMax: 1.05
                    )
                );
        var mound = massifs.Find(m => m.Model == 1);
        if (mound != null)
            foreach (var top in mound.Tops)
                props.AddRange(
                    PropScatter.Scatter(top, 62, PropKind.Pine, density: 0.75, scaleMin: 0.6, scaleMax: 0.95)
                );
        var forest = Poly.Ellipse(1120, 780, 220, 110, 0.1, 26);
        props.AddRange(
            PropScatter.ScatterMix(
                forest,
                58,
                [(PropKind.Tree, 3.0), (PropKind.Pine, 1.6), (PropKind.Bush, 1.2)],
                density: 0.85
            )
        );
        var rocky = massifs.Find(m => m is { Height: 45 });
        if (rocky != null)
            props.AddRange(
                PropScatter.Scatter(rocky.Base, 120, PropKind.Rock, density: 0.5, scaleMin: 0.9, scaleMax: 1.4)
            );
        props.Add(new PropPlacement(PropKind.House, 690, 660, 1.0, 0.12, 690 * 0.37 + 660 * 0.53));
        props.Add(new PropPlacement(PropKind.House, 1460, 640, 0.85, -0.35, 1460 * 0.37 + 640 * 0.53));
        props.Add(new PropPlacement(PropKind.Tree, 620, 660, 1.15, 0, 620 * 0.37 + 660 * 0.53));

        var propsPainter = new PropsPainter
        {
            Foliage = biome.Foliage,
            Stone = biome.Stone,
            Roof = biome.Roof,
            Wood = biome.Wood,
        };
        _stage.AlbedoVp.AddChild(propsPainter);
        propsPainter.SetProps(props);
        var propsH = new PropsHeight();
        _stage.HeightVp.AddChild(propsH);
        propsH.SetMassifs(massifs);
        propsH.SetProps(props);

        mood.ApplyTo(_stage);

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
        img.SavePng("/tmp/godot-terrain.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-height.png");
        GD.Print("dumped /tmp/godot-terrain.png ", img.GetSize());
        GetTree().Quit();
    }
}
