// Flora probe — gallery + smart-fill check for JSON prop definitions.
// Left: every prop definition valid in the biome, drawn big in a grid
// with its id as a label. Right: a pond, a river and a hill run through
// SmartFill (all fill.json layers). Renders through the real SceneView
// stack. Env: DMMAP_BIOME (default woodland), DMMAP_SEASON, DMMAP_OUT
// (default /tmp/godot-flora-<biome>.png).
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class FloraProbe : Node2D
{
    public override void _Ready()
    {
        var biome = System.Environment.GetEnvironmentVariable("DMMAP_BIOME") ?? "woodland";
        var scene = new Scene { Width = 1400, Height = 1180, Biome = biome };
        scene.Season = System.Environment.GetEnvironmentVariable("DMMAP_SEASON");

        // Gallery — 4 columns, big scale so details read.
        var defs = PropDefs.ForBiome(biome)
            .OrderBy(d => d.Base == PropKind.Flora ? 0 : 1)
            .ThenBy(d => d.Id)
            .ToList();
        for (var i = 0; i < defs.Count; i++)
        {
            var (col, row) = (i % 5, i / 5);
            var x = 56 + col * 108;
            var y = 50 + row * 108;
            scene.Props.Add(new Prop
            {
                Id = scene.AllocId(), Kind = defs[i].Id, X = x, Y = y,
                Scale = defs[i].Base == PropKind.Flora ? 60 : 40, Seed = 11 + i * 7,
            });
            scene.Props.Add(new Prop
            {
                Id = scene.AllocId(), Kind = "label", X = x, Y = y + 40, Scale = 13,
                Text = defs[i].Id,
            });
        }

        // Fill playground — pond, river, a low hill.
        var pond = new List<Vector2>();
        for (var k = 0; k < 20; k++)
        {
            var a = k / 20f * Mathf.Tau;
            pond.Add(new Vector2(800 + Mathf.Cos(a) * 110 * (1 + 0.12f * Mathf.Sin(a * 3)),
                560 + Mathf.Sin(a) * 75));
        }
        scene.Shapes.Add(new Shape { Id = scene.AllocId(), Kind = Kinds.Pond, Points = pond });
        scene.Strokes.Add(new Stroke
        {
            Id = scene.AllocId(), Kind = Kinds.River, Width = 24,
            Points = [new(1250, 40), new(1180, 300), new(1260, 560), new(1220, 800)],
        });
        scene.Shapes.Add(new Shape
        {
            Id = scene.AllocId(), Kind = Kinds.Terrain, Height = 30, Size = 40, Roughness = 30,
            Points = [new(700, 80), new(960, 60), new(1010, 230), new(720, 260)],
        });
        var region = new List<Vector2> { new(560, 20), new(1380, 20), new(1380, 800), new(560, 800) };
        var fill = SmartFill.Generate(region, scene, 7, 0.75);
        foreach (var p in fill.Props)
        {
            p.Id = scene.AllocId();
            scene.Props.Add(p);
        }
        // Vegetation brush — a mushroom strip and a mix stroke along a
        // small pond in the gallery's free lower-left corner.
        var pond2 = new List<Vector2>();
        for (var k = 0; k < 16; k++)
        {
            var a = k / 16f * Mathf.Tau;
            pond2.Add(new Vector2(260 + Mathf.Cos(a) * 70, 1060 + Mathf.Sin(a) * 45));
        }
        scene.Shapes.Add(new Shape { Id = scene.AllocId(), Kind = Kinds.Pond, Points = pond2 });
        MapEditor.PlantAlong(scene, [new(60, 920), new(110, 930), new(160, 945)], 26, 0.8, "mushrooms", biome);
        MapEditor.PlantAlong(scene, [new(180, 1010), new(260, 1000), new(340, 1015), new(350, 1090), new(260, 1115)], 40, 0.7, null, biome);
        var byKind = fill.Props.GroupBy(p => p.Kind).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}={g.Count()}");
        GD.Print($"FLORA biome={biome} defs={defs.Count} fill={fill.Props.Count}: {string.Join(" ", byKind)}");

        var view = SceneView.Create(new Vector2((float)scene.Width, (float)scene.Height));
        view.Scene = scene;
        view.SetBiome(biome);
        if (scene.Season != null)
            view.SetSeason(scene.Season);
        view.Scene.FogCoverAll = false;
        view.Stage.Fog.CoverAll = false;
        view.Rebuild();
        AddChild(view);
        CallDeferred(nameof(DumpAndQuit), biome);
    }

    async void DumpAndQuit(string biome)
    {
        for (var i = 0; i < 8; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var view = GetChild<SceneView>(0);
        var outPath = System.Environment.GetEnvironmentVariable("DMMAP_OUT")
            ?? $"/tmp/godot-flora-{biome}.png";
        view.Stage.Snapshot().SavePng(outPath);
        GD.Print("dumped " + outPath);
        GetTree().Quit();
    }
}
