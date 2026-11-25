#nullable enable
// Model-layer probe — exercises the scene model without any rendering:
// serialize round-trip, terrain merge union, stroke merge, snapping,
// undo/redo and JSON validation. Prints PASS/FAIL lines and quits.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class ModelProbe : Node
{
    int _failures;

    void Check(bool ok, string name, string detail = "")
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
        if (!ok)
            _failures++;
    }

    /// <summary>Organic blob — same construction as TerrainProbe.Hill,
    /// returned as a model Shape instead of a render TerrainShape.</summary>
    static Shape Blob(double cx, double cy, double r, double height = 30, double roughness = 0)
    {
        const int n = 28;
        var seed = cx * 0.31 + cy * 0.47;
        var pts = new List<Vector2>();
        for (var i = 0; i < n; i++)
        {
            var a = i / (double)n * Mathf.Tau;
            var w =
                1.0
                + 0.16 * Math.Sin(a * 2 + seed)
                + 0.1 * Math.Sin(a * 3 + seed * 1.7)
                + 0.07 * Math.Sin(a * 5 + seed * 2.3);
            pts.Add(
                new Vector2(
                    (float)(cx + Math.Cos(a) * r * w),
                    (float)(cy + Math.Sin(a) * r * w * 0.82)
                )
            );
        }
        return new Shape
        {
            Kind = Kinds.Terrain,
            Points = pts,
            Size = 46,
            Height = height,
            Roughness = roughness,
        };
    }

    static Scene BuildScene()
    {
        var scene = new Scene { Name = "Probe map", Biome = "woodland", Mood = "dusk" };
        var hill = Blob(300, 260, 170);
        hill.Id = scene.AllocId();
        scene.Shapes.Add(hill);
        var road = new Stroke
        {
            Id = scene.AllocId(),
            Kind = Kinds.Road,
            Width = 18,
            Points = [.. Enumerable.Range(0, 9).Select(i => new Vector2(60 + i * 120f, 700 + (i % 2) * 30f))],
        };
        scene.Strokes.Add(road);
        scene.Props.Add(
            new Prop { Id = scene.AllocId(), Kind = Kinds.Tree, X = 500, Y = 500, Scale = 30, Rotation = 0.4, Seed = 7 }
        );
        scene.Props.Add(
            new Prop { Id = scene.AllocId(), Kind = Kinds.Campfire, X = 620, Y = 640, Scale = 22, Seed = 3 }
        );
        return scene;
    }

    void TestSerializeRoundTrip()
    {
        var scene = BuildScene();
        var json = SceneJson.Serialize(scene);
        var parsed = SceneJson.Parse(json);
        Check(parsed.ContentEquals(scene), "serialize-roundtrip");
        Check(SceneJson.Serialize(parsed) == json, "serialize-stable");
    }

    void TestTerrainMerge()
    {
        var scene = new Scene();
        var a = Blob(300, 300, 120);
        a.Id = scene.AllocId();
        var b = Blob(370, 320, 120); // overlapping, same height/model
        b.Id = scene.AllocId();
        var areaA = Interactions.PolygonArea(a.Points);
        Interactions.AddShape(scene, a);
        Interactions.AddShape(scene, b);
        Check(
            scene.Shapes.Count == 1
                && Interactions.PolygonArea(scene.Shapes[0].Points) > areaA
                && scene.Shapes[0].Points.Count >= 3,
            "terrain-merge-union",
            $"shapes={scene.Shapes.Count}"
        );
        // Different elevation must NOT merge — terraces stack.
        var c = Blob(340, 310, 100, height: 60);
        c.Id = scene.AllocId();
        Interactions.AddShape(scene, c);
        Check(scene.Shapes.Count == 2, "terrain-merge-levels", $"shapes={scene.Shapes.Count}");
        // Non-overlapping same-level terrain stays a separate shape.
        var d = Blob(1200, 700, 100);
        d.Id = scene.AllocId();
        Interactions.AddShape(scene, d);
        Check(scene.Shapes.Count == 3, "terrain-merge-disjoint", $"shapes={scene.Shapes.Count}");
    }

    void TestStrokeMerge()
    {
        var scene = new Scene();
        var first = new Stroke
        {
            Id = scene.AllocId(),
            Kind = Kinds.Road,
            Width = 18,
            Points = [new Vector2(100, 500), new Vector2(300, 500), new Vector2(500, 520)],
        };
        Interactions.AddStroke(scene, first);
        // Continuation starting right at the first stroke's end.
        var cont = new Stroke
        {
            Id = scene.AllocId(),
            Kind = Kinds.Road,
            Width = 18,
            Points = [new Vector2(505, 522), new Vector2(700, 540), new Vector2(900, 540)],
        };
        Interactions.AddStroke(scene, cont);
        Check(
            scene.Strokes.Count == 1
                && scene.Strokes[0].Points[0].DistanceTo(new Vector2(100, 500)) < 0.01
                && scene.Strokes[0].Points[^1].DistanceTo(new Vector2(900, 540)) < 0.01,
            "stroke-merge-concat",
            $"strokes={scene.Strokes.Count} points={scene.Strokes[0].Points.Count}"
        );
        // T-junction: a stroke ending on the merged line's interior.
        var arm = new Stroke
        {
            Id = scene.AllocId(),
            Kind = Kinds.Road,
            Width = 18,
            Points = [new Vector2(300, 700), new Vector2(302, 505)],
        };
        Interactions.AddStroke(scene, arm);
        Check(scene.Strokes.Count == 1, "stroke-merge-tjunction", $"strokes={scene.Strokes.Count}");
        // A different width must not merge.
        var wide = new Stroke
        {
            Id = scene.AllocId(),
            Kind = Kinds.Road,
            Width = 40,
            Points = [new Vector2(900, 545), new Vector2(1100, 560)],
        };
        Interactions.AddStroke(scene, wide);
        Check(scene.Strokes.Count == 2, "stroke-merge-width", $"strokes={scene.Strokes.Count}");
    }

    void TestSnap()
    {
        var strokes = new List<Stroke>
        {
            new() { Kind = Kinds.River, Width = 30, Points = [new Vector2(100, 100), new Vector2(400, 120)] },
        };
        var snapped = Interactions.SnapEndpoint(new Vector2(410, 115), strokes, Kinds.River, 30);
        Check(
            snapped.HasValue && snapped.Value.DistanceTo(new Vector2(400, 120)) < 0.01,
            "stroke-snap-endpoint"
        );
        Check(!Interactions.SnapEndpoint(new Vector2(410, 300), strokes, Kinds.River, 30).HasValue, "stroke-snap-miss");
        var hit = Interactions.NearestOnStroke(strokes[0], new Vector2(250, 60));
        // Projection onto (100,100)→(400,120): t≈0.489 → ≈(246.7, 109.8).
        Check(
            hit.Segment == 1 && hit.Point.DistanceTo(new Vector2(246.7f, 109.8f)) < 0.5,
            "stroke-nearest-point",
            $"dist={hit.Distance:0.0}"
        );
    }

    void TestWallToHouse()
    {
        var scene = new Scene();
        var loop = new Stroke { Id = scene.AllocId(), Kind = Kinds.Wall, Width = 14 };
        for (var i = 0; i <= 12; i++)
        {
            var a = i / 12.0 * Mathf.Tau;
            loop.Points.Add(new Vector2(400 + (float)Math.Cos(a) * 90, 400 + (float)Math.Sin(a) * 70));
        }
        Interactions.AddStroke(scene, loop);
        Check(
            scene.Shapes.Count == 1 && scene.Shapes[0].Kind == Kinds.House && scene.Strokes.Count == 0,
            "wall-loop-to-house"
        );
    }

    void TestHistory()
    {
        var scene0 = BuildScene();
        var history = new SceneHistory(scene0);
        var edit = scene0.DeepClone();
        edit.Props.Add(new Prop { Id = edit.AllocId(), Kind = Kinds.Rock, X = 100, Y = 100, Scale = 20, Seed = 1 });
        history.Commit(edit);
        Check(history.CanUndo && history.Current.Props.Count == scene0.Props.Count + 1, "history-commit");
        var undone = history.Undo();
        Check(undone != null && undone.ContentEquals(scene0) && history.CanRedo, "history-undo");
        var redone = history.Redo();
        Check(redone != null && redone.Props.Count == scene0.Props.Count + 1 && !history.CanRedo, "history-redo");
        // Cap: 60 commits → only 50 undos.
        var h2 = new SceneHistory(new Scene { Name = "0" });
        for (var i = 1; i <= 60; i++)
            h2.Commit(new Scene { Name = i.ToString() });
        var undos = 0;
        while (h2.Undo() != null)
            undos++;
        Check(undos == 50, "history-cap", $"undos={undos}");
    }

    void TestInvalidJson()
    {
        var warn = new List<string>();
        Check(!SceneJson.TryParse("{ this is not json", out _, warn), "json-garbage-rejected");
        Check(
            !SceneJson.TryParse("{\"version\":2,\"width\":1600,\"height\":1000}", out _, warn),
            "json-bad-version-rejected"
        );
        // Malformed entries drop with warnings; valid ones survive; a
        // legacy kind vanishes silently; size clamps into range.
        var doc = """
            {
              "version": 1, "width": 1600, "height": 1000,
              "shapes": [
                { "id": 1, "kind": "nonsense", "points": [[1,2],[3,4],[5,6]], "size": 30 },
                { "id": 2, "kind": "hill", "points": [[1,2],[3,4],[5,6]], "size": 30 },
                { "id": 3, "kind": "terrain", "points": [[100,100],[400,100],[400,300]], "size": 9999, "height": 250 }
              ],
              "strokes": [ { "id": 4, "kind": "road", "points": "oops", "width": 18 } ],
              "props": [ { "id": 5, "kind": "tree", "x": 200, "y": 200, "scale": 30 } ]
            }
            """;
        warn.Clear();
        var ok = SceneJson.TryParse(doc, out var parsed, warn);
        Check(
            ok
                && parsed!.Shapes.Count == 1
                && parsed.Shapes[0].Size == 160
                && parsed.Shapes[0].Height == 100
                && parsed.Strokes.Count == 0
                && parsed.Props.Count == 1
                && parsed.Props[0].Seed >= 0
                && warn.Count >= 2,
            "json-drops-malformed",
            $"shapes={parsed?.Shapes.Count} strokes={parsed?.Strokes.Count} props={parsed?.Props.Count} warn={warn.Count}"
        );
    }

    void TestCampExpansion()
    {
        var scene = new Scene();
        var camp = new Prop { Id = scene.AllocId(), Kind = Kinds.Camp, X = 800, Y = 500, Scale = 40, Seed = 0 };
        Interactions.AddProp(scene, camp);
        Check(
            scene.Props.Count >= 5 && scene.Props.All(p => p.Kind != Kinds.Camp) && scene.Props.All(p => p.Id > 0),
            "camp-expands",
            $"props={scene.Props.Count} kinds={string.Join(",", scene.Props.Select(p => p.Kind))}"
        );
    }

    public override void _Ready()
    {
        try
        {
            TestSerializeRoundTrip();
            TestTerrainMerge();
            TestStrokeMerge();
            TestSnap();
            TestWallToHouse();
            TestHistory();
            TestInvalidJson();
            TestCampExpansion();
        }
        catch (Exception e)
        {
            GD.Print($"FAIL exception — {e}");
            _failures++;
        }
        GD.Print(_failures == 0 ? "ALL PASS" : $"{_failures} FAILURES");
        GetTree().Quit();
    }
}
