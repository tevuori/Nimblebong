// Map editor — the interactive milestone: draw terrain strokes with
// the mouse, lay rivers/roads, place props, undo, pan/zoom. Czech UI.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class MapEditor : Node2D
{
    public enum Tool
    {
        Terrain,
        River,
        Road,
        Tree,
        Pine,
        Rock,
        Bush,
        House,
        Erase,
    }

    static readonly Vector2 MapSize = new(1600, 1000);

    SceneView _view = null!;
    SceneHistory _history = null!;
    GhostLayer _ghost = null!;
    Camera2D _camera = null!;

    // Current tool + parameters.
    Tool _tool = Tool.Terrain;
    double _pHeight = 30;
    double _pSlope = 46;
    double _pRough = 0;
    int _pModel;
    double _pRiverW = 30;
    double _pRoadW = 16;

    // Active stroke recording.
    readonly List<Vector2> _stroke = [];
    bool _drawing;
    bool _panning;
    Vector2 _panFrom;

    public override void _Ready()
    {
        _view = SceneView.Create(MapSize);
        AddChild(_view);
        _history = new SceneHistory(_view.Scene);
        _ghost = new GhostLayer();
        AddChild(_ghost);
        _camera = new Camera2D { Position = MapSize / 2, Enabled = true };
        AddChild(_camera);
        BuildUi();
        if (System.Environment.GetEnvironmentVariable("DMMAP_PROBE") == "1")
            CallDeferred(nameof(ProbeRun));
    }

    // Scripted self-test — exercises stroke commits, merge, props,
    // undo, and dumps the lit composite like the user would see it.
    async void ProbeRun()
    {
        // Terrain blob via the same path a mouse stroke takes.
        var pts = new List<Vector2>();
        for (var i = 0; i < 28; i++)
        {
            var a = i / 28.0 * Mathf.Tau;
            var r = 150 * (1 + 0.15 * Math.Sin(a * 3));
            pts.Add(new Vector2(400 + (float)(Math.Cos(a) * r), 300 + (float)(Math.Sin(a) * r * 0.8)));
        }
        Commit(scene =>
            scene.Shapes = Interactions.MergeShapeInto(
                scene,
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Terrain,
                    Points = pts,
                    Size = 46,
                    Height = 40,
                    Roughness = 50,
                }
            )
        );
        // A river + road.
        Commit(scene =>
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.River,
                    Points =
                    [
                        new(650, -10),
                        new(680, 250),
                        new(620, 500),
                        new(700, 1020),
                    ],
                    Width = 30,
                }
            )
        );
        _tool = Tool.Tree;
        PlacePropAt(new Vector2(390, 280));
        _tool = Tool.House;
        PlacePropAt(new Vector2(800, 600));
        Undo(); // house away — verify undo rebuilds cleanly
        for (var i = 0; i < 8; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-editor.png");
        GD.Print("dumped /tmp/godot-editor.png");
        GetTree().Quit();
    }

    // ── Input ────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton b:
                OnMouseButton(b);
                break;
            case InputEventMouseMotion m:
                OnMouseMotion(m);
                break;
            case InputEventKey { Pressed: true } k:
                if (k.IsActionPressed("ui_undo"))
                    Undo();
                else if (k.IsActionPressed("ui_redo"))
                    Redo();
                break;
        }
    }

    void OnMouseButton(InputEventMouseButton b)
    {
        if (b.ButtonIndex == MouseButton.WheelUp && b.Pressed)
        {
            ZoomAt(b.Position, 1.15);
        }
        else if (b.ButtonIndex == MouseButton.WheelDown && b.Pressed)
        {
            ZoomAt(b.Position, 1 / 1.15);
        }
        else if (b.ButtonIndex is MouseButton.Middle or MouseButton.Right)
        {
            _panning = b.Pressed;
            _panFrom = b.Position;
        }
        else if (b.ButtonIndex == MouseButton.Left)
        {
            if (b.Pressed)
                BeginStroke();
            else
                EndStroke();
        }
    }

    void ZoomAt(Vector2 screenPos, double factor)
    {
        // Keep the world point under the cursor fixed.
        var half = GetViewportRect().Size / 2;
        var worldAtCursor = _camera.Position + (screenPos - half) / _camera.Zoom;
        var z = Mathf.Clamp(_camera.Zoom.X * factor, 0.4f, 3f);
        _camera.Zoom = new Vector2((float)z, (float)z);
        _camera.Position = worldAtCursor - (screenPos - half) / _camera.Zoom;
    }

    void OnMouseMotion(InputEventMouseMotion m)
    {
        if (_panning)
        {
            _camera.Position -= m.Relative / _camera.Zoom;
            _panFrom = m.Position;
            return;
        }
        if (!_drawing)
            return;
        var p = GetGlobalMousePosition();
        if (_stroke.Count == 0 || _stroke[^1].DistanceTo(p) > 4)
        {
            _stroke.Add(p);
            _ghost.SetStroke(_tool, _stroke, StrokeWidth());
            if (IsPropTool(_tool) && _stroke.Count > 1 && _stroke[^1].DistanceTo(_stroke[^2]) > 45)
                PlacePropAt(p);
        }
    }

    double StrokeWidth() =>
        _tool switch
        {
            Tool.River => _pRiverW,
            Tool.Road => _pRoadW,
            _ => 8,
        };

    void BeginStroke()
    {
        _stroke.Clear();
        _stroke.Add(GetGlobalMousePosition());
        _drawing = true;
        if (IsPropTool(_tool))
            PlacePropAt(_stroke[0]);
        else if (_tool == Tool.Erase)
            EraseAt(_stroke[0]);
    }

    void EndStroke()
    {
        _drawing = false;
        _ghost.Clear();
        if (_stroke.Count < 2)
            return;
        var pts = Interactions.SimplifyPath(_stroke, 2.0);
        switch (_tool)
        {
            case Tool.Terrain:
                if (pts.Count >= 6)
                    Commit(scene =>
                    {
                        var shape = new Shape
                        {
                            Id = scene.AllocId(),
                            Kind = Kinds.Terrain,
                            Points = pts,
                            Size = _pSlope,
                            Height = _pHeight,
                            Roughness = _pRough,
                            Model = _pModel,
                        };
                        scene.Shapes = Interactions.MergeShapeInto(scene, shape);
                    });
                break;
            case Tool.River:
            case Tool.Road:
                Commit(scene =>
                {
                    var stroke = new Stroke
                    {
                        Id = scene.AllocId(),
                        Kind = _tool == Tool.River ? Kinds.River : Kinds.Road,
                        Points = pts,
                        Width = StrokeWidth(),
                    };
                    Interactions.AddStroke(scene, stroke);
                });
                break;
        }
        _stroke.Clear();
    }

    static bool IsPropTool(Tool t) => t is Tool.Tree or Tool.Pine or Tool.Rock or Tool.Bush or Tool.House;

    void PlacePropAt(Vector2 p)
    {
        var kind = _tool switch
        {
            Tool.Pine => "pine",
            Tool.Rock => Kinds.Rock,
            Tool.Bush => Kinds.Bush,
            Tool.House => Kinds.House,
            _ => Kinds.Tree,
        };
        Commit(scene =>
            Interactions.AddProp(
                scene,
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = kind,
                    X = p.X,
                    Y = p.Y,
                    Scale = _tool == Tool.House ? 26 : 24,
                    Rotation = 0,
                    Seed = (int)(p.X * 0.37 + p.Y * 0.53),
                }
            )
        );
    }

    void EraseAt(Vector2 p)
    {
        Commit(scene =>
        {
            // Topmost first: props, then strokes, then shapes.
            var prop = scene
                .Props.OrderByDescending(pr => pr.Id)
                .FirstOrDefault(pr => new Vector2((float)pr.X, (float)pr.Y).DistanceTo(p) < 24 * (pr.Scale / 24 + 0.4));
            if (prop != null)
            {
                scene.Props.Remove(prop);
                return;
            }
            var stroke = scene
                .Strokes.OrderByDescending(s => s.Id)
                .FirstOrDefault(s => StrokeHit(s, p));
            if (stroke != null)
            {
                scene.Strokes.Remove(stroke);
                return;
            }
            var shape = scene
                .Shapes.OrderByDescending(s => s.Id)
                .FirstOrDefault(s => Poly.PointInPolygon(p, [.. s.Points]));
            if (shape != null)
                scene.Shapes.Remove(shape);
        });
    }

    static bool StrokeHit(Stroke s, Vector2 p)
    {
        for (var i = 0; i + 1 < s.Points.Count; i++)
            if (Interactions.DistanceToSegment(p, s.Points[i], s.Points[i + 1]) < s.Width / 2 + 6)
                return true;
        return false;
    }

    // ── History ──────────────────────────────────────────────────

    void Commit(Action<Scene> mutate)
    {
        var next = _view.Scene.DeepClone();
        mutate(next);
        _history.Commit(next);
        _view.Scene = _history.Current;
        _view.Rebuild();
    }

    void Undo()
    {
        if (_history.Undo() is { } s)
        {
            _view.Scene = s;
            _view.Rebuild();
        }
    }

    void Redo()
    {
        if (_history.Redo() is { } s)
        {
            _view.Scene = s;
            _view.Rebuild();
        }
    }

    // ── Persistence ──────────────────────────────────────────────

    void Save()
    {
        using var f = FileAccess.Open("user://dmmap-scene.json", FileAccess.ModeFlags.Write);
        f.StoreString(SceneJson.Serialize(_view.Scene));
        GD.Print("saved ", ProjectSettings.GlobalizePath("user://dmmap-scene.json"));
    }

    void Load()
    {
        var path = "user://dmmap-scene.json";
        if (!FileAccess.FileExists(path))
            return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var warnings = new List<string>();
        if (SceneJson.TryParse(f.GetAsText(), out var scene, warnings))
        {
            _history.Commit(scene!);
            _view.Scene = _history.Current;
            _view.Rebuild();
        }
        foreach (var err in warnings)
            GD.PushWarning(err);
    }

    void ExportPng()
    {
        var img = _view.Stage.Snapshot();
        img.SavePng("user://dmmap-export.png");
        GD.Print("exported ", ProjectSettings.GlobalizePath("user://dmmap-export.png"));
    }

    // ── UI ───────────────────────────────────────────────────────

    void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new PanelContainer
        {
            Position = new Vector2(10, 10),
            CustomMinimumSize = new Vector2(210, 0),
        };
        layer.AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);

        box.AddChild(new Label { Text = "Nástroje" });
        var grid = new GridContainer { Columns = 2 };
        box.AddChild(grid);
        var group = new ButtonGroup();
        (string Label, Tool T)[] tools =
        [
            ("Terén", Tool.Terrain),
            ("Řeka", Tool.River),
            ("Cesta", Tool.Road),
            ("Strom", Tool.Tree),
            ("Borovice", Tool.Pine),
            ("Kámen", Tool.Rock),
            ("Keř", Tool.Bush),
            ("Dům", Tool.House),
            ("Guma", Tool.Erase),
        ];
        foreach (var (label, t) in tools)
        {
            var btn = new Button
            {
                Text = label,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = t == _tool,
            };
            var tool = t;
            btn.Pressed += () => _tool = tool;
            grid.AddChild(btn);
        }

        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Terén" });
        Slider(box, "Výška", 0, 100, _pHeight, v => _pHeight = v);
        Slider(box, "Šířka svahu", 10, 90, _pSlope, v => _pSlope = v);
        Slider(box, "Nerovnosti", 0, 100, _pRough, v => _pRough = v);
        var model = new OptionButton();
        foreach (var name in new[] { "Skalnatý", "Hlínitý", "Hřeben" })
            model.AddItem(name);
        model.Selected = _pModel;
        model.ItemSelected += i => _pModel = (int)i;
        box.AddChild(model);

        box.AddChild(new HSeparator());
        Slider(box, "Šířka řeky", 10, 60, _pRiverW, v => _pRiverW = v);
        Slider(box, "Šířka cesty", 8, 40, _pRoadW, v => _pRoadW = v);

        box.AddChild(new HSeparator());
        var biome = new OptionButton();
        foreach (var id in SceneConfig.BiomeIds())
            biome.AddItem(id);
        biome.Selected = Math.Max(0, SceneConfig.BiomeIds().IndexOf("woodland"));
        biome.ItemSelected += i => _view.SetBiome(biome.GetItemText((int)i));
        box.AddChild(biome);

        var mood = new OptionButton();
        foreach (var id in SceneConfig.MoodIds())
            mood.AddItem(id);
        mood.Selected = Math.Max(0, SceneConfig.MoodIds().IndexOf("day"));
        mood.ItemSelected += i => _view.SetMood(mood.GetItemText((int)i));
        box.AddChild(mood);

        box.AddChild(new HSeparator());
        Button(box, "Zpět (Ctrl+Z)", Undo);
        Button(box, "Uložit", Save);
        Button(box, "Načíst", Load);
        Button(box, "Export PNG", ExportPng);
        Button(box, "Nový", () =>
            Commit(scene =>
            {
                scene.Shapes.Clear();
                scene.Strokes.Clear();
                scene.Props.Clear();
            })
        );
    }

    static void Slider(VBoxContainer box, string label, double min, double max, double value, Action<double> set)
    {
        var l = new Label();
        var s = new HSlider { MinValue = min, MaxValue = max, Value = value };
        void Sync() => l.Text = $"{label}: {s.Value:0}";
        s.ValueChanged += v =>
        {
            set(v);
            Sync();
        };
        Sync();
        box.AddChild(l);
        box.AddChild(s);
    }

    static void Button(VBoxContainer box, string label, Action action)
    {
        var b = new Godot.Button { Text = label };
        b.Pressed += () => action();
        box.AddChild(b);
    }
}

/// <summary>Ghost preview of the in-progress stroke — drawn in the
/// main viewport above the baked composite.</summary>
public partial class GhostLayer : Node2D
{
    MapEditor.Tool _tool;
    List<Vector2> _pts = [];
    double _width;

    public void SetStroke(MapEditor.Tool tool, List<Vector2> pts, double width)
    {
        (_tool, _pts, _width) = (tool, [.. pts], width);
        QueueRedraw();
    }

    public void Clear()
    {
        _pts = [];
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_pts.Count < 2)
            return;
        var pts = _pts.ToArray();
        switch (_tool)
        {
            case MapEditor.Tool.Terrain:
                DrawPolyline([.. pts, pts[0]], new Color(0.2f, 0.18f, 0.1f, 0.7f), 2);
                break;
            case MapEditor.Tool.River:
                DrawPolyline(pts, new Color(0.16f, 0.56f, 0.6f, 0.5f), (float)_width);
                break;
            case MapEditor.Tool.Road:
                DrawPolyline(pts, new Color(0.82f, 0.72f, 0.49f, 0.5f), (float)_width);
                break;
        }
    }
}
