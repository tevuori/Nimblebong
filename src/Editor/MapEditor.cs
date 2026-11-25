#nullable enable
// Map editor — the interactive milestone: draw terrain strokes with
// the mouse, lay rivers/roads, place props, undo, pan/zoom. Czech UI.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class MapEditor : Node2D
{
    /// <summary>Input mode — how the tool turns mouse strokes into scene.</summary>
    public enum Mode
    {
        Select,
        Erase,
        Shape,
        Stroke,
        Prop,
        Label,
    }

    /// <summary>One palette entry: label + category + what it commits.</summary>
    public readonly record struct ToolDef(
        string Id,
        string Label,
        string Category,
        Mode Mode,
        string Kind
    );

    /// <summary>Full tool palette — categories follow the web editor's
    /// toolCategories; labels are the Czech toolLabels.</summary>
    public static readonly ToolDef[] Tools =
    [
        new("select", "Vybrat", "Nástroje", Mode.Select, ""),
        new("erase", "Guma", "Nástroje", Mode.Erase, ""),
        new("terrain", "Kopce a srázy", "Terén a voda", Mode.Shape, Kinds.Terrain),
        new("river", "Řeky", "Terén a voda", Mode.Stroke, Kinds.River),
        new("pond", "Jezírka", "Terén a voda", Mode.Shape, Kinds.Pond),
        new("field", "Pole a louky", "Terén a voda", Mode.Shape, Kinds.Field),
        new("road", "Cesty", "Cesty", Mode.Stroke, Kinds.Road),
        new("wall", "Kamenné zdi", "Cesty", Mode.Stroke, Kinds.Wall),
        new("fence", "Ploty", "Cesty", Mode.Stroke, Kinds.Fence),
        new("dock", "Doky a mola", "Cesty", Mode.Prop, "dock"),
        new("house", "Budovy", "Stavby", Mode.Prop, Kinds.House),
        new("tower", "Strážní věže", "Stavby", Mode.Prop, "tower"),
        new("stall", "Tržní stánky", "Stavby", Mode.Prop, "stall"),
        new("tent", "Stany", "Stavby", Mode.Prop, "tent"),
        new("door", "Dveře", "Interiér", Mode.Prop, "door"),
        new("window", "Okna", "Interiér", Mode.Prop, "window"),
        new("table", "Stoly", "Interiér", Mode.Prop, "table"),
        new("bed", "Postele", "Interiér", Mode.Prop, "bed"),
        new("shelf", "Knihovny a regály", "Interiér", Mode.Prop, "shelf"),
        new("rug", "Koberce", "Interiér", Mode.Prop, "rug"),
        new("fireplace", "Krby", "Interiér", Mode.Prop, "fireplace"),
        new("tree", "Stromy", "Porost a příroda", Mode.Prop, Kinds.Tree),
        new("pine", "Borovice", "Porost a příroda", Mode.Prop, "pine"),
        new("bush", "Keře", "Porost a příroda", Mode.Prop, Kinds.Bush),
        new("scatter", "Porosty", "Porost a příroda", Mode.Shape, Kinds.Scatter),
        new("rock", "Skály", "Porost a příroda", Mode.Prop, Kinds.Rock),
        new("stonecircle", "Kamenné kruhy", "Porost a příroda", Mode.Prop, "stonecircle"),
        new("crystal", "Krystaly", "Porost a příroda", Mode.Prop, "crystal"),
        new("bale", "Balíky", "Porost a příroda", Mode.Prop, "bale"),
        new("camp", "Tábory a skupiny", "Osídlení a dekorace", Mode.Prop, Kinds.Camp),
        new("campfire", "Ohniště", "Osídlení a dekorace", Mode.Prop, "campfire"),
        new("cart", "Vozy", "Osídlení a dekorace", Mode.Prop, "cart"),
        new("boat", "Lodě", "Osídlení a dekorace", Mode.Prop, "boat"),
        new("well", "Studny", "Osídlení a dekorace", Mode.Prop, "well"),
        new("crate", "Bedny", "Osídlení a dekorace", Mode.Prop, "crate"),
        new("barrel", "Sudy", "Osídlení a dekorace", Mode.Prop, "barrel"),
        new("fountain", "Kašny", "Osídlení a dekorace", Mode.Prop, "fountain"),
        new("lantern", "Lampy a pochodně", "Osídlení a dekorace", Mode.Prop, "lantern"),
        new("flag", "Praporky", "Osídlení a dekorace", Mode.Prop, "flag"),
        new("pillar", "Sloupy", "Osídlení a dekorace", Mode.Prop, "pillar"),
        new("statue", "Sochy", "Osídlení a dekorace", Mode.Prop, "statue"),
        new("ruin", "Zříceniny", "Osídlení a dekorace", Mode.Prop, "ruin"),
        new("grave", "Hroby", "Osídlení a dekorace", Mode.Prop, "grave"),
        new("chest", "Truhly", "Osídlení a dekorace", Mode.Prop, "chest"),
        new("bones", "Kosti a lebky", "Osídlení a dekorace", Mode.Prop, "bones"),
        new("barricade", "Zábrany", "Osídlení a dekorace", Mode.Prop, "barricade"),
        new("portal", "Magické brány", "Osídlení a dekorace", Mode.Prop, "portal"),
        new("marker", "Značky", "Mapa a hra", Mode.Prop, "marker"),
        new("label", "Popisky", "Mapa a hra", Mode.Label, "label"),
    ];

    static readonly Vector2 MapSize = new(1600, 1000);

    SceneView _view = null!;
    SceneHistory _history = null!;
    GhostLayer _ghost = null!;
    Camera2D _camera = null!;

    // Current tool + parameters.
    ToolDef _tool = Tools[2]; // terrain
    int _form; // 0 volný tah, 1 obdélník, 2 elipsa — pro plošné nástroje
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

    // Selection: what the select tool picked, where a move began and a
    // snapshot to revert to (the live object is mutated during drags /
    // slider edits so the canvas updates, then reverted and committed
    // once as a single history entry).
    Prop? _selProp;
    Stroke? _selStroke;
    Shape? _selShape;
    Vector2 _selOrigin;
    bool _selMoving;
    // Backups for live edits (restored before the single commit).
    List<Vector2>? _selPtsBackup;
    Vector2 _selPosBackup;
    (double H, double S, double R, int M)? _selShapeBackup;

    double _draftAt; // ticks when the pending live preview may run
    bool _pendingRebuild; // throttled Rebuild during selection drags
    bool _selEdited; // selection was live-edited → commit on release
    bool _syncing; // slider→selection feedback guard
    BridgeDialog? _bridge;
    HSlider _sHeight = null!;
    HSlider _sSlope = null!;
    HSlider _sRough = null!;
    OptionButton _modelOpt = null!;
    LineEdit _seedEdit = null!;
    OptionButton _presetOpt = null!;

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
        _bridge = BridgeDialog.Create(
            () => _view.Stage.Snapshot().SavePngToBuffer(),
            () => _history.Current
        );
        AddChild(_bridge);
        if (System.Environment.GetEnvironmentVariable("DMMAP_PROBE") == "1")
            CallDeferred(nameof(ProbeRun));
    }

    /// <summary>Throttled work: live massif preview while a terrain
    /// stroke is in flight, plus deferred rebuilds for selection drags.</summary>
    public override void _Process(double delta)
    {
        var now = Time.GetTicksMsec();
        if (_drawing && _tool.Kind == Kinds.Terrain && _draftAt > 0 && now >= _draftAt)
        {
            _draftAt = 0;
            UpdateDraftPreview();
        }
        if (_pendingRebuild && now - _lastRebuild >= 90)
        {
            _pendingRebuild = false;
            _lastRebuild = now;
            _view.Rebuild();
        }
    }

    ulong _lastRebuild;

    /// <summary>Draft massif from the in-flight stroke — band + top
    /// fills drawn translucently in the ghost layer.</summary>
    void UpdateDraftPreview()
    {
        if (_stroke.Count < 6)
            return;
        var massifs = TerrainGeometry.BuildMassifs(
            [
                new TerrainShape
                {
                    Model = _pModel,
                    Height = _pHeight,
                    Size = _pSlope,
                    Roughness = _pRough,
                    Outline = [.. Interactions.SimplifyPath(_stroke, 4)],
                },
            ]
        );
        if (massifs.Count > 0)
            _ghost.SetDraft(massifs[0]);
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
        // A river that cuts straight through the massif (waterfalls)
        // and a road crossing it (stairs) — exercises CrossingPainter.
        Commit(scene =>
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.River,
                    Points =
                    [
                        new(500, -10),
                        new(470, 250),
                        new(520, 500),
                        new(600, 1020),
                    ],
                    Width = 30,
                }
            )
        );
        Commit(scene =>
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points =
                    [
                        new(180, 320),
                        new(400, 300),
                        new(620, 280),
                    ],
                    Width = 18,
                }
            )
        );
        SetTool("tree");
        PlacePropAt(new Vector2(390, 280));
        SetTool("house");
        PlacePropAt(new Vector2(800, 600));
        Undo(); // house away — verify undo rebuilds cleanly

        // Wave-A features: pond, field, wall, fence, scatter, camp.
        var pond = new List<Vector2>();
        for (var i = 0; i < 14; i++)
        {
            var a = i / 14.0 * Mathf.Tau;
            pond.Add(new Vector2(1050 + (float)Math.Cos(a) * 90, 760 + (float)Math.Sin(a) * 60));
        }
        Commit(scene =>
            scene.Shapes = Interactions.MergeShapeInto(
                scene,
                new Shape { Id = scene.AllocId(), Kind = Kinds.Pond, Points = pond }
            )
        );
        var field = new List<Vector2>();
        for (var i = 0; i < 12; i++)
        {
            var a = i / 12.0 * Mathf.Tau;
            field.Add(new Vector2(1250 + (float)Math.Cos(a) * 130, 350 + (float)Math.Sin(a) * 80));
        }
        Commit(scene =>
            scene.Shapes = Interactions.MergeShapeInto(
                scene,
                new Shape { Id = scene.AllocId(), Kind = Kinds.Field, Points = field }
            )
        );
        Commit(scene =>
        {
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Wall,
                    Points = [new(950, 500), new(1150, 480), new(1250, 560), new(1240, 700)],
                    Width = 20,
                    Model = 1,
                }
            );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Fence,
                    Points = [new(1150, 260), new(1350, 300), new(1420, 420)],
                    Width = 10,
                }
            );
        });
        SetTool("scatter");
        BeginStroke(); // fake a scatter commit straight through Commit
        var scat = new List<Vector2>();
        for (var i = 0; i < 12; i++)
        {
            var a = i / 12.0 * Mathf.Tau;
            scat.Add(new Vector2(900 + (float)Math.Cos(a) * 110, 200 + (float)Math.Sin(a) * 70));
        }
        _stroke.Clear();
        _stroke.AddRange(scat);
        EndStroke();
        SetTool("camp");
        PlacePropAt(new Vector2(700, 750));
        // Label + marker — placed directly (PromptLabel opens a dialog).
        Commit(scene =>
        {
            scene.Props.Add(
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = "label",
                    X = 480,
                    Y = 120,
                    Scale = 16,
                    Text = "Tábor u řeky",
                }
            );
            scene.Props.Add(
                new Prop { Id = scene.AllocId(), Kind = "marker", X = 700, Y = 690, Scale = 22 }
            );
        });
        // Protection: a tree inside the campfire zone must be nudged out.
        SetTool("tree");
        PlacePropAt(new Vector2(700, 750));
        for (var i = 0; i < 8; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-editor.png");
        GD.Print("dumped /tmp/godot-editor.png");

        // ── generated map + selection move + crossings ──
        _seedEdit.Text = "42";
        Generate();
        for (var i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-generated.png");
        GD.Print("dumped /tmp/godot-generated.png");

        // dungeon preset
        _presetOpt.Selected = 1;
        Generate();
        for (var i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-dungeon.png");
        GD.Print("dumped /tmp/godot-dungeon.png");
        _presetOpt.Selected = 0;
        Generate();
        for (var i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // selection: grab the first house, move it, verify one commit
        var house = _view.Scene.Props.FirstOrDefault(p => p.Kind == Kinds.House);
        if (house != null)
        {
            var depth = _history.UndoDepth;
            BeginSelect(new Vector2((float)house.X, (float)house.Y));
            _selOrigin = new Vector2((float)house.X, (float)house.Y);
            MoveSelectionTo(new Vector2((float)house.X + 120, (float)house.Y));
            _selEdited = true;
            EndSelect(new Vector2((float)house.X + 120, (float)house.Y));
            GD.Print(
                $"select-move commits: {_history.UndoDepth - depth} (expect 1), "
                    + $"house at {_view.Scene.Props.First(p => p.Kind == Kinds.House).X:0}"
            );
        }
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-after-move.png");
        GD.Print("dumped /tmp/godot-after-move.png");

        // Optional: exercise the real backend bridge end-to-end.
        if (System.Environment.GetEnvironmentVariable("DMMAP_BRIDGE_PROBE") == "1")
        {
            var res = await DrBridge.UploadMap(
                "http://127.0.0.1:3000",
                "x",
                "Godot bridge test",
                _view.Stage.Snapshot().SavePngToBuffer(),
                setLive: true,
                s => GD.Print("bridge: ", s),
                tokens:
                [
                    new Dictionary<string, object>
                    {
                        ["x"] = 400.0,
                        ["y"] = 300.0,
                        ["color"] = "#e5484d",
                        ["label"] = "test token",
                        ["radius"] = 14.0,
                        ["isVisibleForPlayers"] = true,
                    },
                ]
            );
            GD.Print($"bridge result: ok={res.Ok} mapId={res.MapId} msg={res.Message}");
        }
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
                else if (k.Keycode is Key.Delete or Key.Backspace)
                    DeleteSelection();
                else if (k.Keycode == Key.Escape)
                    ClearSelection();
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
        if (_tool.Mode == Mode.Select)
        {
            if (_selMoving)
                MoveSelectionTo(p);
            return;
        }
        if (_stroke.Count == 0 || _stroke[^1].DistanceTo(p) > 4)
        {
            _stroke.Add(p);
            _ghost.SetStroke(_tool, _tool.Mode == Mode.Shape && _form > 0 ? FormPoints(_form) : _stroke, StrokeWidth());
            if (_tool.Kind == Kinds.Terrain && _stroke.Count >= 6 && _draftAt == 0)
                _draftAt = Time.GetTicksMsec() + 120;
            if (_tool.Mode == Mode.Prop && _stroke.Count > 1 && _stroke[^1].DistanceTo(_stroke[^2]) > 45)
                PlacePropAt(p);
        }
    }

    double StrokeWidth() =>
        _tool.Kind switch
        {
            Kinds.River => _pRiverW,
            Kinds.Road => _pRoadW,
            Kinds.Wall => 20,
            _ => 8,
        };

    /// <summary>Bounding-box rect/ellipse polygon for shape tools
    /// when a constrained form is selected.</summary>
    List<Vector2> FormPoints(int form)
    {
        var min = _stroke[0];
        var max = _stroke[0];
        foreach (var p in _stroke)
        {
            min = min.Min(p);
            max = max.Max(p);
        }
        if (form == 1)
            return [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
        var c = (min + max) / 2;
        var r = (max - min) / 2;
        var pts = new List<Vector2>();
        for (int i = 0; i < 28; i++)
        {
            var a = i / 28.0 * Mathf.Tau;
            pts.Add(c + new Vector2((float)(r.X * Math.Cos(a)), (float)(r.Y * Math.Sin(a))));
        }
        return pts;
    }

    void BeginStroke()
    {
        _stroke.Clear();
        _stroke.Add(GetGlobalMousePosition());
        _drawing = true;
        if (_tool.Mode == Mode.Select)
            BeginSelect(_stroke[0]);
        else if (_tool.Mode == Mode.Prop)
            PlacePropAt(_stroke[0]);
        else if (_tool.Mode == Mode.Label)
            PromptLabel(_stroke[0]);
        else if (_tool.Mode == Mode.Erase)
            EraseAt(_stroke[0]);
    }

    void EndStroke()
    {
        _drawing = false;
        _draftAt = 0;
        if (_tool.Mode == Mode.Select)
        {
            EndSelect(GetGlobalMousePosition());
            return;
        }
        _ghost.ClearDraft();
        _ghost.SetStroke(_tool, [], 0);
        if (_stroke.Count < 2)
            return;
        var pts = Interactions.SimplifyPath(_stroke, 2.0);
        if (_tool.Mode == Mode.Shape && _form > 0)
            pts = FormPoints(_form);
        switch (_tool.Mode)
        {
            case Mode.Stroke:
                Commit(scene =>
                {
                    var stroke = new Stroke
                    {
                        Id = scene.AllocId(),
                        Kind = _tool.Kind,
                        Points = pts,
                        Width = StrokeWidth(),
                    };
                    Interactions.AddStroke(scene, stroke);
                });
                break;
            case Mode.Shape when _tool.Kind == Kinds.Terrain:
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
            case Mode.Shape when _tool.Kind is Kinds.Pond or Kinds.Field:
                if (pts.Count >= 6)
                    Commit(scene =>
                    {
                        var shape = new Shape
                        {
                            Id = scene.AllocId(),
                            Kind = _tool.Kind,
                            Points = pts,
                            Size = 24,
                        };
                        scene.Shapes = Interactions.MergeShapeInto(scene, shape);
                    });
                break;
            case Mode.Shape when _tool.Kind == Kinds.Scatter:
                if (pts.Count >= 6)
                    Commit(scene =>
                    {
                        // Fill the outline with a deterministic forest
                        // mix — same scatter as the generator uses.
                        var mix = PropScatter.ScatterMix(
                            pts,
                            46,
                            [(PropKind.Tree, 3.0), (PropKind.Pine, 1.6), (PropKind.Bush, 0.7)],
                            density: 0.92,
                            seed: pts[0].X * 0.37 + pts[0].Y * 0.53
                        );
                        foreach (var pp in mix)
                        {
                            var kind = pp.Kind switch
                            {
                                PropKind.Pine => "pine",
                                PropKind.Bush => Kinds.Bush,
                                _ => Kinds.Tree,
                            };
                            Interactions.PlaceProtected(
                                scene,
                                new Prop
                                {
                                    Id = scene.AllocId(),
                                    Kind = kind,
                                    X = pp.X,
                                    Y = pp.Y,
                                    Scale = 22 * pp.Scale,
                                    Rotation = pp.Rotation,
                                    Seed = (int)pp.Seed,
                                }
                            );
                        }
                    });
                break;
        }
        _stroke.Clear();
    }



    // ── Selection (move / delete / param edit) ────────────────────

    void BeginSelect(Vector2 p)
    {
        ClearSelection();
        var scene = _view.Scene;
        // Same top-down priority as the eraser: prop → stroke → shape.
        _selProp = scene
            .Props.OrderByDescending(pr => pr.Id)
            .FirstOrDefault(pr => new Vector2((float)pr.X, (float)pr.Y).DistanceTo(p) < 24 * (pr.Scale / 24 + 0.4));
        if (_selProp != null)
        {
            _selPosBackup = new Vector2((float)_selProp.X, (float)_selProp.Y);
        }
        else if (
            (
                _selStroke = scene
                    .Strokes.OrderByDescending(s => s.Id)
                    .FirstOrDefault(s => StrokeHit(s, p))
            )
            != null
        )
        {
            _selPtsBackup = [.. _selStroke.Points];
        }
        else if (
            (
                _selShape = scene
                    .Shapes.OrderByDescending(s => s.Id)
                    .FirstOrDefault(s => Poly.PointInPolygon(p, [.. s.Points]))
            )
            != null
        )
        {
            _selPtsBackup = [.. _selShape.Points];
            _selShapeBackup = (_selShape.Height, _selShape.Size, _selShape.Roughness, _selShape.Model);
            SyncSlidersToSelection();
        }
        if (HasSelection())
        {
            _selMoving = true;
            _selOrigin = p;
            UpdateSelectionGhost();
        }
    }

    bool HasSelection() => _selProp != null || _selStroke != null || _selShape != null;

    /// <summary>Live-move the selected element by the drag delta
    /// (mutated in place; history entry is written on release).</summary>
    void MoveSelectionTo(Vector2 p)
    {
        var delta = p - _selOrigin;
        if (_selProp != null)
        {
            _selProp.X = _selPosBackup.X + delta.X;
            _selProp.Y = _selPosBackup.Y + delta.Y;
        }
        else if (_selPtsBackup != null && (_selStroke != null || _selShape != null))
        {
            var pts = _selStroke != null ? _selStroke.Points : _selShape!.Points;
            for (var i = 0; i < pts.Count; i++)
                pts[i] = _selPtsBackup[i] + delta;
        }
        _selEdited = delta.LengthSquared() > 1;
        UpdateSelectionGhost();
        _pendingRebuild = true;
    }

    /// <summary>Revert the live mutation and write one history entry
    /// with the final offset.</summary>
    void EndSelect(Vector2 endPos)
    {
        _selMoving = false;
        if (!_selEdited)
            return;
        _selEdited = false;
        var delta = endPos - _selOrigin;
        // Restore the pre-drag values — Commit() re-applies the delta.
        if (_selProp != null)
        {
            _selProp.X = _selPosBackup.X;
            _selProp.Y = _selPosBackup.Y;
        }
        if (_selPtsBackup != null && (_selStroke != null || _selShape != null))
        {
            var pts = _selStroke != null ? _selStroke.Points : _selShape!.Points;
            for (var i = 0; i < pts.Count; i++)
                pts[i] = _selPtsBackup[i];
        }
        if (delta.LengthSquared() < 1)
        {
            _view.Rebuild();
            return;
        }
        var (propId, strokeId, shapeId) = (_selProp?.Id, _selStroke?.Id, _selShape?.Id);
        Commit(scene =>
        {
            if (propId is { } pid && scene.Props.FirstOrDefault(q => q.Id == pid) is { } pr)
            {
                pr.X += delta.X;
                pr.Y += delta.Y;
            }
            else if (strokeId is { } stid && scene.Strokes.FirstOrDefault(s => s.Id == stid) is { } st)
            {
                for (var i = 0; i < st.Points.Count; i++)
                    st.Points[i] += delta;
            }
            else if (shapeId is { } shid && scene.Shapes.FirstOrDefault(s => s.Id == shid) is { } sh)
            {
                for (var i = 0; i < sh.Points.Count; i++)
                    sh.Points[i] += delta;
            }
        });
        RefreshSelection();
    }

    /// <summary>Commit replaces the scene object — re-resolve the
    /// selection to the matching element in the new scene.</summary>
    void RefreshSelection()
    {
        var scene = _view.Scene;
        if (_selProp != null)
        {
            _selProp = scene.Props.FirstOrDefault(q => q.Id == _selProp.Id) ?? _selProp;
            _selPosBackup = new Vector2((float)_selProp.X, (float)_selProp.Y);
        }
        if (_selStroke != null)
        {
            _selStroke = scene.Strokes.FirstOrDefault(s => s.Id == _selStroke.Id) ?? _selStroke;
            _selPtsBackup = [.. _selStroke.Points];
        }
        if (_selShape != null)
        {
            _selShape = scene.Shapes.FirstOrDefault(s => s.Id == _selShape.Id) ?? _selShape;
            _selPtsBackup = [.. _selShape.Points];
            _selShapeBackup = (_selShape.Height, _selShape.Size, _selShape.Roughness, _selShape.Model);
        }
        UpdateSelectionGhost();
    }

    void DeleteSelection()
    {
        if (!HasSelection())
            return;
        var (pid, stid, shid) = (_selProp?.Id, _selStroke?.Id, _selShape?.Id);
        Commit(scene =>
        {
            scene.Props.RemoveAll(q => q.Id == pid);
            if (pid == null)
            {
                scene.Strokes.RemoveAll(s => s.Id == stid);
                if (stid == null)
                    scene.Shapes.RemoveAll(s => s.Id == shid);
            }
        });
        ClearSelection();
    }

    void ClearSelection()
    {
        _selProp = null;
        _selStroke = null;
        _selShape = null;
        _selPtsBackup = null;
        _selMoving = false;
        _selEdited = false;
        _ghost.SetSelection(null, null, null);
    }

    /// <summary>Sliders write into the selected terrain shape live;
    /// the change commits when the drag ends (one undo step).</summary>
    void OnTerrainSlider(double h, double s, double r, int m, bool commit)
    {
        (_pHeight, _pSlope, _pRough, _pModel) = (h, s, r, m);
        if (_selShape is not { Kind: Kinds.Terrain } shape)
            return;
        if (commit)
        {
            // Revert live values then write one history entry.
            if (_selShapeBackup is { } b)
                (shape.Height, shape.Size, shape.Roughness, shape.Model) = b;
            var id = shape.Id;
            Commit(scene =>
            {
                if (scene.Shapes.FirstOrDefault(q => q.Id == id) is { } sh)
                    (sh.Height, sh.Size, sh.Roughness, sh.Model) = (h, s, r, m);
            });
            RefreshSelection();
        }
        else
        {
            (shape.Height, shape.Size, shape.Roughness, shape.Model) = (h, s, r, m);
            _pendingRebuild = true;
        }
    }

    void UpdateSelectionGhost()
    {
        _ghost.SetSelection(
            _selProp == null ? null : new Vector2((float)_selProp.X, (float)_selProp.Y),
            _selStroke == null ? null : [.. _selStroke.Points],
            _selShape == null ? null : [.. _selShape.Points]
        );
    }

    /// <summary>Clicking a prop tool on an existing prop cycles its
    /// variant (seed++) instead of stacking a new prop on top.</summary>
    void PlacePropAt(Vector2 p)
    {
        var existing = _view
            .Scene.Props.OrderByDescending(pr => pr.Id)
            .FirstOrDefault(pr => new Vector2((float)pr.X, (float)pr.Y).DistanceTo(p) < 20);
        if (existing != null)
        {
            var id = existing.Id;
            Commit(scene =>
            {
                if (scene.Props.FirstOrDefault(q => q.Id == id) is { } pr)
                    pr.Seed++;
            });
            return;
        }
        var kind = _tool.Kind;
        Commit(scene =>
            Interactions.PlaceProtected(
                scene,
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = kind,
                    X = p.X,
                    Y = p.Y,
                    Scale = _tool.Kind is Kinds.House or "tower" or "stall" ? 26 : 24,
                    Rotation = 0,
                    Seed = (int)(p.X * 0.37 + p.Y * 0.53),
                }
            )
        );
    }

    /// <summary>Small text prompt — on OK places a "label" prop with
    /// the entered caption at `pos`.</summary>
    void PromptLabel(Vector2 pos)
    {
        var dlg = new ConfirmationDialog { Title = "Popisek" };
        var edit = new LineEdit { PlaceholderText = "Text popisku…", CustomMinimumSize = new Vector2(220, 0) };
        dlg.AddChild(edit);
        dlg.RegisterTextEnter(edit);
        dlg.Confirmed += () =>
        {
            var text = edit.Text.Trim();
            if (text.Length > 0)
                Commit(scene =>
                    scene.Props.Add(
                        new Prop
                        {
                            Id = scene.AllocId(),
                            Kind = "label",
                            X = pos.X,
                            Y = pos.Y,
                            Scale = 16,
                            Text = text,
                        }
                    )
                );
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
        edit.GrabFocus();
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

    /// <summary>Seed → whole generated scene as one undo-able commit.</summary>
    void Generate()
    {
        var seed = int.TryParse(_seedEdit.Text, out var s)
            ? s
            : _seedEdit.Text.GetHashCode();
        var gen =
            _presetOpt.Selected == 1
                ? MapGenerator.GenerateDungeon(seed, MapSize)
                : MapGenerator.GenerateLandscape(seed, MapSize);
        ClearSelection();
        _history.Commit(gen);
        _view.Scene = _history.Current;
        _view.Rebuild();
    }

    /// <summary>Sliders reflect the selected terrain shape's params.</summary>
    void SyncSlidersToSelection()
    {
        if (_selShape is not { Kind: Kinds.Terrain } sh)
            return;
        _syncing = true;
        _sHeight.Value = sh.Height;
        _sSlope.Value = sh.Size;
        _sRough.Value = sh.Roughness;
        _modelOpt.Selected = Math.Clamp(sh.Model, 0, 2);
        _syncing = false;
    }

    // ── Tool palette ────────────────────────────────────────────────

    Tree _toolTree = null!;

    void SetTool(string id)
    {
        foreach (var t in Tools)
            if (t.Id == id)
            {
                _tool = t;
                return;
            }
    }

    /// <summary>Categorized tool browser — Nimblehold-style sections,
    /// filtered by the search box (diacritics-folded contains).</summary>
    void PopulateToolTree(string needle)
    {
        needle = Fold(needle.Trim());
        _toolTree.Clear();
        var root = _toolTree.CreateItem();
        foreach (var cat in Tools.Select(t => t.Category).Distinct())
        {
            var items = Tools
                .Where(t => t.Category == cat)
                .Where(t => needle == "" || Fold(t.Label + " " + t.Id).Contains(needle))
                .ToList();
            if (items.Count == 0)
                continue;
            var catItem = _toolTree.CreateItem(root);
            catItem.SetText(0, cat);
            catItem.SetSelectable(0, false);
            foreach (var t in items)
            {
                var it = _toolTree.CreateItem(catItem);
                it.SetText(0, t.Label);
                it.SetMetadata(0, t.Id);
            }
            catItem.Collapsed = needle == "" && cat != _tool.Category;
        }
    }

    /// <summary>Lowercase + strip Czech diacritics for search.</summary>
    static string Fold(string s) =>
        s.ToLowerInvariant()
            .Replace("á", "a")
            .Replace("č", "c")
            .Replace("ď", "d")
            .Replace("é", "e")
            .Replace("ě", "e")
            .Replace("í", "i")
            .Replace("ň", "n")
            .Replace("ó", "o")
            .Replace("ř", "r")
            .Replace("š", "s")
            .Replace("ť", "t")
            .Replace("ú", "u")
            .Replace("ů", "u")
            .Replace("ý", "y")
            .Replace("ž", "z");

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
        var search = new LineEdit { PlaceholderText = "Hledat nástroj…" };
        box.AddChild(search);
        _toolTree = new Tree
        {
            HideRoot = true,
            CustomMinimumSize = new Vector2(0, 260),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _toolTree.ItemSelected += () =>
        {
            if (_toolTree.GetSelected()?.GetMetadata(0).AsString() is { Length: > 0 } id)
                SetTool(id);
        };
        search.TextChanged += _ => PopulateToolTree(search.Text);
        box.AddChild(_toolTree);
        PopulateToolTree("");

        var form = new OptionButton();
        form.AddItem("Volný tah");
        form.AddItem("Obdélník");
        form.AddItem("Elipsa");
        form.ItemSelected += i => _form = (int)i;
        box.AddChild(form);

        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Terén" });
        _sHeight = TerrainSlider(box, "Výška", 0, 100, _pHeight, v => _pHeight = v);
        _sSlope = TerrainSlider(box, "Šířka svahu", 10, 90, _pSlope, v => _pSlope = v);
        _sRough = TerrainSlider(box, "Nerovnosti", 0, 100, _pRough, v => _pRough = v);
        _modelOpt = new OptionButton();
        foreach (var name in new[] { "Skalnatý", "Hlínitý", "Hřeben" })
            _modelOpt.AddItem(name);
        _modelOpt.Selected = _pModel;
        _modelOpt.ItemSelected += i =>
        {
            _pModel = (int)i;
            OnTerrainSlider(_pHeight, _pSlope, _pRough, _pModel, commit: true);
        };
        box.AddChild(_modelOpt);

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
        _presetOpt = new OptionButton();
        _presetOpt.AddItem("Krajina");
        _presetOpt.AddItem("Dungeon");
        box.AddChild(_presetOpt);
        var seedRow = new HBoxContainer();
        _seedEdit = new LineEdit
        {
            Text = "42",
            PlaceholderText = "seed",
            CustomMinimumSize = new Vector2(80, 0),
        };
        seedRow.AddChild(_seedEdit);
        var gen = new Godot.Button { Text = "Generovat", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gen.Pressed += Generate;
        seedRow.AddChild(gen);
        box.AddChild(seedRow);

        box.AddChild(new HSeparator());
        Button(box, "Zpět (Ctrl+Z)", Undo);
        Button(box, "Uložit", Save);
        Button(box, "Načíst", Load);
        Button(box, "Export PNG", ExportPng);
        Button(box, "Odeslat do DR", () => _bridge?.PopupCentered());
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

    /// <summary>Terrain slider: live-writes into the selected shape
    /// while dragging, single commit when the drag ends.</summary>
    HSlider TerrainSlider(VBoxContainer box, string label, double min, double max, double value, Action<double> set)
    {
        var l = new Label();
        var s = new HSlider { MinValue = min, MaxValue = max, Value = value };
        void Sync() => l.Text = $"{label}: {s.Value:0}";
        s.ValueChanged += v =>
        {
            if (_syncing)
            {
                Sync();
                return;
            }
            set(v);
            Sync();
            OnTerrainSlider(_pHeight, _pSlope, _pRough, _pModel, commit: false);
        };
        s.DragEnded += changed =>
        {
            if (changed && !_syncing)
                OnTerrainSlider(_pHeight, _pSlope, _pRough, _pModel, commit: true);
        };
        Sync();
        box.AddChild(l);
        box.AddChild(s);
        return s;
    }

    static void Button(VBoxContainer box, string label, Action action)
    {
        var b = new Godot.Button { Text = label };
        b.Pressed += () => action();
        box.AddChild(b);
    }
}

/// <summary>Ghost preview — in-progress stroke, live massif draft
/// and the selection highlight, drawn in the main viewport above
/// the baked composite.</summary>
public partial class GhostLayer : Node2D
{
    MapEditor.Mode _mode;
    string _kind = "";
    List<Vector2> _pts = [];
    double _width;
    Massif? _draft;
    Vector2? _selProp;
    Vector2[]? _selLine;
    Vector2[]? _selOutline;

    public void SetStroke(MapEditor.ToolDef tool, List<Vector2> pts, double width)
    {
        (_mode, _kind, _pts, _width) = (tool.Mode, tool.Kind, [.. pts], width);
        QueueRedraw();
    }

    public void Clear()
    {
        _pts = [];
        _draft = null;
        QueueRedraw();
    }

    /// <summary>Translucent massif ghost while a terrain stroke is
    /// being drawn — band + top fills at half alpha.</summary>
    public void SetDraft(Massif m)
    {
        _draft = m;
        QueueRedraw();
    }

    public void ClearDraft()
    {
        _draft = null;
        QueueRedraw();
    }

    public void SetSelection(Vector2? prop, Vector2[]? line, Vector2[]? outline)
    {
        (_selProp, _selLine, _selOutline) = (prop, line, outline);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // selection highlight
        var hi = new Color(1f, 0.85f, 0.25f, 0.9f);
        if (_selOutline is { Length: > 2 } o)
            DrawPolyline([.. o, o[0]], hi, 2.5f);
        if (_selLine is { Length: > 1 } l)
            DrawPolyline(l, hi, 3);
        if (_selProp is { } pp)
            DrawArc(pp, 20, 0, Mathf.Tau, 24, hi, 2.5f);

        // terrain draft — the massif the stroke would create
        if (_draft is { } m)
        {
            var band = new Color(0.71f, 0.69f, 0.54f, 0.45f);
            var top = new Color(0.49f, 0.65f, 0.32f, 0.5f);
            foreach (var poly in m.Band)
                if (Geometry2D.TriangulatePolygon(poly).Length > 0)
                    DrawColoredPolygon(poly, band);
            foreach (var poly in m.Tops)
                if (Geometry2D.TriangulatePolygon(poly).Length > 0)
                    DrawColoredPolygon(poly, top);
            foreach (var poly in m.Tops)
                DrawPolyline([.. poly, poly[0]], new Color(0.2f, 0.18f, 0.1f, 0.8f), 2);
        }

        // the raw stroke on top
        if (_pts.Count < 2)
            return;
        var pts = _pts.ToArray();
        if (_mode == MapEditor.Mode.Shape)
        {
            DrawPolyline([.. pts, pts[0]], new Color(0.2f, 0.18f, 0.1f, 0.7f), 2);
            return;
        }
        var col = _kind switch
        {
            Kinds.Wall => new Color(0.72f, 0.71f, 0.6f, 0.5f),
            Kinds.Fence => new Color(0.43f, 0.34f, 0.24f, 0.5f),
            Kinds.River => new Color(0.16f, 0.56f, 0.6f, 0.5f),
            _ => new Color(0.82f, 0.72f, 0.49f, 0.5f),
        };
        DrawPolyline(pts, col, (float)_width);
    }
}
