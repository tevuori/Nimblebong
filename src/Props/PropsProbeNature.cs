// Nature-props probe: all 12 wilderness kinds laid out in a labeled-free
// grid — three rows so the seeded variants show — plus a few standing on
// a rocky plateau top. With DMMAP_PROBE=1 dumps the composite plus the
// raw albedo/height viewports to /tmp/godot-props-nature*.png and quits.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class PropsProbeNature : Node2D
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

    static readonly PropKind[] Grid =
    [
        PropKind.Tent,
        PropKind.Campfire,
        PropKind.Well,
        PropKind.Statue,
        PropKind.StoneCircle,
        PropKind.Crystal,
        PropKind.Ruin,
        PropKind.Grave,
        PropKind.Bones,
        PropKind.Bale,
        PropKind.Crate,
        PropKind.Barrel,
    ];

    public override void _Ready()
    {
        var shapes = new List<TerrainShape>
        {
            // Rocky plateau north-west — a few props stand on its top.
            Hill(360, 250, 180, model: 2, height: 55, roughness: 55, size: 50),
        };
        var massifs = TerrainGeometry.BuildMassifs(shapes);

        _stage = LightingStage.Create(MapSize);
        _stage.Name = "LightingStage";
        AddChild(_stage);
        _stage.Albedo.SetMassifs(massifs);
        _stage.Height.SetMassifs(massifs);

        var props = new List<PropPlacement>();

        // Three rows × the 12 kinds — every prop gets a different seed,
        // rotation and scale so the seeded variants all appear.
        for (var row = 0; row < 3; row++)
        for (var col = 0; col < Grid.Length; col++)
        {
            var x = 150 + col * 118;
            var y = 560 + row * 150;
            var s = x * 0.37 + y * 0.53;
            props.Add(
                new PropPlacement(
                    Grid[col],
                    x,
                    y,
                    0.9 + Rng.Noise(s + 4) * 0.35,
                    (row * 0.8 + col * 0.55) % (Math.PI * 2),
                    s
                )
            );
        }

        // A stone circle, statue and crystal on the plateau top.
        var plateau = massifs.Find(m => m.Height == 55);
        if (plateau != null && plateau.Tops.Count > 0)
        {
            var c = Poly.Centroid(plateau.Tops[0]);
            props.Add(new PropPlacement(PropKind.StoneCircle, c.X - 55, c.Y - 10, 0.85, 0.3, 991));
            props.Add(new PropPlacement(PropKind.Statue, c.X + 45, c.Y + 25, 1.0, -0.4, 771));
            props.Add(new PropPlacement(PropKind.Crystal, c.X + 90, c.Y - 40, 1.1, 0.9, 553));
            props.Add(new PropPlacement(PropKind.Tent, c.X - 90, c.Y + 55, 0.9, 2.4, 313));
        }

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
        img.SavePng("/tmp/godot-props-nature.png");
        _stage.AlbedoVp.GetTexture().GetImage().SavePng("/tmp/godot-props-nature-albedo.png");
        _stage.HeightVp.GetTexture().GetImage().SavePng("/tmp/godot-props-nature-height.png");
        GD.Print("dumped /tmp/godot-props-nature.png ", img.GetSize());
        GetTree().Quit();
    }
}
