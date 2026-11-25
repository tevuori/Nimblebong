// Reference-comparison probe — loads a scene JSON given by
// DMMAP_SCENE=<abs path>, builds the full SceneView render stack and
// dumps the lit composite to DMMAP_OUT (default /tmp/godot-ref.png).
// Runs the same pipeline the editor uses, so ref comparisons test the
// real product, not a probe-only shortcut.
using Godot;

namespace DmMap;

public partial class RefProbe : Node2D
{
    public override void _Ready()
    {
        var scenePath =
            System.Environment.GetEnvironmentVariable("DMMAP_SCENE")
            ?? "user://dmmap-scene.json";
        var raw = FileAccess.GetFileAsString(scenePath);
        var warnings = new System.Collections.Generic.List<string>();
        if (!SceneJson.TryParse(raw, out var scene, warnings) || scene == null)
        {
            GD.PrintErr($"REFPROBE scene parse failed: {scenePath}");
            foreach (var w in warnings)
                GD.PrintErr("  " + w);
            GetTree().Quit(1);
            return;
        }
        foreach (var w in warnings)
            GD.Print("warn: " + w);

        var mapSize = new Vector2(
            scene.Width > 0 ? (float)scene.Width : 1600,
            scene.Height > 0 ? (float)scene.Height : 1000
        );
        var view = SceneView.Create(mapSize);
        view.Scene = scene;
        view.Scene.Width = mapSize.X;
        view.Scene.Height = mapSize.Y;
        view.SetBiome(scene.Biome ?? "woodland");
        if (scene.ArtPack is { Length: > 0 } art)
            view.SetArtPack(art);
        if (scene.Season != null)
            view.SetSeason(scene.Season);
        if (scene.Mood != null)
            view.SetMood(scene.Mood);
        // Fog off for refs — comparisons grade the bare lit map.
        view.Scene.FogCoverAll = false;
        view.Stage.Fog.CoverAll = false;
        view.Stage.Fog.QueueRedraw();
        view.Rebuild();
        AddChild(view);

        CallDeferred(nameof(DumpAndQuit));
    }

    async void DumpAndQuit()
    {
        for (var i = 0; i < 8; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var view = GetChild<SceneView>(0);
        var img = view.Stage.Snapshot();
        var outPath =
            System.Environment.GetEnvironmentVariable("DMMAP_OUT")
            ?? "/tmp/godot-ref.png";
        img.SavePng(outPath);
        GD.Print("dumped " + outPath + " " + img.GetSize());
        GetTree().Quit();
    }
}
