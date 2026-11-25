#nullable enable
// Tool card thumbnails — live mini-renders of what each tool paints,
// the Godot port of the web editor's Preview canvas (previewShapes):
// every card shows the tool's own output rendered by the real pipeline
// (hidden 120×76 SceneView), re-rendered whenever biome / art pack /
// season / mood changes. Tools that paint no scene content
// (select/erase/play) get a drawn glyph from the same pipeline;
// the fog tools render a small scene and post-process a veil over it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace DmMap;

/// <summary>Off-screen thumbnail renderer. Feed card texture slots via
/// <see cref="Register"/> — icons render one per ~2 frames and cache
/// per look signature; <see cref="SetLook"/> invalidates and re-renders.</summary>
public partial class ToolThumbs : Node2D
{
    /// <summary>Logical tile bounds (web previewShape canvas) × uiScale.</summary>
    public Vector2 Size { get; private set; } = new(120, 76);
    float _m = 1;

    SceneView _view = null!;
    SubViewport _glyphVp = null!;
    GlyphIcon _glyph = null!;
    readonly Dictionary<string, TextureRect> _targets = new();
    readonly Dictionary<string, ImageTexture> _cache = new();
    Image? _baseTile;
    ImageTexture _blank = null!;
    bool _running;
    bool _again;
    int _run;
    string _look = "";

    /// <summary>True while any registered tool still lacks a cached icon
    /// — the UI probe waits for this before dumping the screen.</summary>
    public bool Pending => _targets.Keys.Any(id => !_cache.ContainsKey(id));

    /// <summary>uiScale multiplies the render resolution so icons stay
    /// crisp inside the scaled UI slots.</summary>
    public static ToolThumbs Create(float uiScale = 1)
    {
        var t = new ToolThumbs { Position = new Vector2(-4000, -4000) };
        t._m = uiScale;
        t.Size = new Vector2(120 * uiScale, 76 * uiScale);
        t._view = SceneView.Create(t.Size);
        t.AddChild(t._view);
        t._glyphVp = new SubViewport
        {
            Size = (Vector2I)t.Size,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
            GuiDisableInput = true,
        };
        t._glyph = new GlyphIcon { Scale = Vector2.One * uiScale };
        t._glyphVp.AddChild(t._glyph);
        t.AddChild(t._glyphVp);
        var blank = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        blank.Fill(new Color(0, 0, 0, 0));
        t._blank = ImageTexture.CreateFromImage(blank);
        return t;
    }

    /// <summary>Sync the mini renderer with the editor's environment
    /// selections — clears the icon cache and re-renders all cards.</summary>
    public void SetLook(string biome, string mood, string art, string? season)
    {
        var look = $"{biome}|{mood}|{art}|{season ?? ""}";
        if (look == _look)
            return;
        _look = look;
        _view.SetBiome(biome);
        _view.SetMood(mood);
        _view.SetArtPack(art);
        _view.SetSeason(season);
        _run++;
        _cache.Clear();
        _baseTile = null;
        Kick();
    }

    /// <summary>Point a card's texture slot at a tool — serves the cache
    /// immediately or queues a render for the missing icon.</summary>
    public void Register(string toolId, TextureRect target)
    {
        _targets[toolId] = target;
        target.Texture = _cache.GetValueOrDefault(toolId);
        Kick();
    }

    async void Kick()
    {
        if (_running)
        {
            _again = true;
            return;
        }
        _running = true;
        do
        {
            _again = false;
            var gen = _run;
            foreach (var id in _targets.Keys.ToList())
            {
                if (gen != _run)
                    break;
                if (_cache.ContainsKey(id))
                    continue;
                var img = await RenderTool(id);
                if (gen != _run)
                    break;
                var tex = img != null ? ImageTexture.CreateFromImage(img) : _blank;
                _cache[id] = tex;
                if (_targets.TryGetValue(id, out var t) && IsInstanceValid(t))
                    t.Texture = tex;
            }
        } while (_again || Pending);
        _running = false;
    }

    /// <summary>Contact-sheet dump of every cached icon — UI probe aid.</summary>
    public void DumpSheet(string path, int cols = 8)
    {
        var ids = _cache.Keys.ToList();
        var rows = (ids.Count + cols - 1) / cols;
        var w = (int)Size.X;
        var h = (int)Size.Y;
        var sheet = Image.CreateEmpty(cols * w, rows * h, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.12f, 0.11f, 0.10f));
        for (var i = 0; i < ids.Count; i++)
        {
            var src = _cache[ids[i]].GetImage();
            sheet.BlitRect(src, new Rect2I(0, 0, w, h), new Vector2I(i % cols * w, i / cols * h));
        }
        sheet.SavePng(path);
    }

    async Task<Image?> RenderTool(string id)
    {
        if (id is "select" or "erase" or "play")
        {
            // Glyph over a dimmed grass tile — reads as an overlay
            // icon on the map, consistent with the painted cards.
            var bg = await BaseTile();
            var tile = (Image)bg.Duplicate();
            _glyph.Kind = id;
            _glyph.QueueRedraw();
            await Frames(1);
            var g = _glyphVp.GetTexture().GetImage();
            tile.BlendRect(g, new Rect2I(Vector2I.Zero, g.GetSize()), Vector2I.Zero);
            return tile;
        }
        var tool = MapEditor.Tools.FirstOrDefault(t => t.Id == id);
        var scene = PreviewScene(tool);
        if (scene == null)
            return null;
        _view.Scene = scene;
        _view.Rebuild();
        await Frames(2);
        var img = _view.Stage.Snapshot();
        if (id is "fog-clear" or "fog-shroud")
            Veil(img, id == "fog-clear", _m);
        return img;
    }

    async Task Frames(int n)
    {
        for (var i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Empty-map tile, darkened — the shared backdrop for the
    /// drawn glyph icons (select/erase/play). Cached per look.</summary>
    async Task<Image> BaseTile()
    {
        if (_baseTile != null)
            return _baseTile;
        _view.Scene = new Scene { Width = Size.X, Height = Size.Y };
        _view.Rebuild();
        await Frames(2);
        var img = _view.Stage.Snapshot();
        var dark = new Color(0.16f, 0.15f, 0.13f);
        for (var y = 0; y < img.GetHeight(); y++)
        for (var x = 0; x < img.GetWidth(); x++)
            img.SetPixel(x, y, img.GetPixel(x, y).Lerp(dark, 0.45f));
        _baseTile = img;
        return img;
    }

    // ── preview scenes (port of web previewShape / previewShapes) ──────

    static readonly Dictionary<string, double> PropScale = new()
    {
        ["camp"] = 24,
        ["tree"] = 48,
        ["pine"] = 30,
        ["bush"] = 28,
        ["rock"] = 35,
        ["label"] = 26,
        ["fountain"] = 40,
        ["lantern"] = 34,
        ["pillar"] = 32,
        ["ruin"] = 38,
        ["grave"] = 30,
        ["chest"] = 34,
        ["bones"] = 40,
        ["portal"] = 44,
        ["waterfall"] = 30,
        ["dock"] = 46,
        ["door"] = 34,
        ["window"] = 34,
        ["table"] = 44,
        ["bed"] = 46,
        ["shelf"] = 40,
        ["rug"] = 60,
        ["fireplace"] = 40,
        ["marker"] = 22,
        ["tower"] = 40,
        ["stall"] = 36,
        ["tent"] = 40,
        ["cart"] = 36,
        ["well"] = 34,
        ["statue"] = 36,
        ["crystal"] = 36,
        ["bale"] = 30,
        ["stonecircle"] = 46,
        ["barricade"] = 38,
        ["flag"] = 30,
        ["boat"] = 40,
        ["crate"] = 26,
        ["barrel"] = 26,
        ["campfire"] = 28,
    };

    /// <summary>The small scene each tool paints — mirrors the web
    /// previewShape points/sizes (120×76 card bounds, scaled by _m).
    /// Only pixel-semantics scale: heights/roughness/elevations stay.</summary>
    Scene? PreviewScene(MapEditor.ToolDef t)
    {
        var m = _m;
        var s = new Scene { Width = Size.X, Height = Size.Y };
        Vector2 P(double x, double y) => new((float)(x * m), (float)(y * m));
        // Web previewShape area bounds + line path.
        Vector2[] rect = [P(23, 18), P(91, 18), P(91, 58), P(23, 58)];
        Vector2[] line = [P(0, 57), P(30, 50), P(65, 22), P(120, 17)];
        Shape Area(string kind, double size, double height = 0, double rough = 0) =>
            new()
            {
                Id = s.AllocId(),
                Kind = kind,
                Points = [.. rect],
                Size = size,
                Height = height,
                Roughness = rough,
            };
        Prop PropAt(string kind, Vector2 p, double scale, int model = -1) =>
            new()
            {
                Id = s.AllocId(),
                Kind = kind,
                X = p.X,
                Y = p.Y,
                Scale = scale * m,
                Model = model,
            };
        Shape House(Vector2 a, Vector2 b) =>
            new()
            {
                Id = s.AllocId(),
                Kind = Kinds.House,
                Points = [a, new(b.X, a.Y), b, new(a.X, b.Y)],
                Size = 20 * m,
                Height = 18,
            };
        switch (t.Id)
        {
            case "select" or "erase" or "play":
                return null; // glyph
            case "fill":
                // Region fill output: a pocket village — two houses, a
                // road, trees and a market stall.
                s.Shapes.Add(House(P(18, 16), P(46, 38)));
                s.Shapes.Add(House(P(74, 10), P(98, 30)));
                s.Strokes.Add(
                    new Stroke
                    {
                        Id = s.AllocId(),
                        Kind = Kinds.Road,
                        Points = [P(0, 62), P(45, 58), P(80, 60), P(120, 52)],
                        Width = 12 * m,
                    }
                );
                s.Props.Add(PropAt("tree", P(102, 46), 30));
                s.Props.Add(PropAt("tree", P(14, 52), 26));
                s.Props.Add(PropAt("stall", P(56, 46), 24));
                s.Props.Add(PropAt("campfire", P(88, 62), 16));
                return s;
            case "fog-clear" or "fog-shroud":
                // Small map fragment — the veil is post-processed on top.
                s.Shapes.Add(House(P(28, 20), P(62, 48)));
                s.Strokes.Add(
                    new Stroke
                    {
                        Id = s.AllocId(),
                        Kind = Kinds.Road,
                        Points = [P(0, 66), P(45, 60), P(85, 62), P(120, 54)],
                        Width = 12 * m,
                    }
                );
                s.Props.Add(PropAt("tree", P(82, 26), 32));
                s.Props.Add(PropAt("tree", P(96, 50), 26));
                s.Props.Add(PropAt("campfire", P(80, 62), 18));
                return s;
            case "brush":
                // Two overlapping stamps — stacked terrace like real use.
                s.Shapes.Add(
                    new Shape
                    {
                        Id = s.AllocId(),
                        Kind = Kinds.Terrain,
                        Points = [.. Blob(P(48, 42), 17 * m)],
                        Size = 16 * m,
                        Height = 15,
                        Roughness = 40,
                    }
                );
                s.Shapes.Add(
                    new Shape
                    {
                        Id = s.AllocId(),
                        Kind = Kinds.Terrain,
                        Points = [.. Blob(P(72, 36), 21 * m)],
                        Size = 16 * m,
                        Height = 28,
                        Roughness = 40,
                    }
                );
                return s;
            case "terrain":
                // Slightly irregular octagon — organic plateau, reads
                // better than the web's plain rectangle.
                s.Shapes.Add(
                    new Shape
                    {
                        Id = s.AllocId(),
                        Kind = Kinds.Terrain,
                        Points =
                        [
                            P(33, 18),
                            P(80, 15),
                            P(94, 26),
                            P(96, 50),
                            P(82, 60),
                            P(36, 58),
                            P(22, 46),
                            P(25, 26),
                        ],
                        Size = 18 * m,
                        Height = 34,
                        Roughness = 50,
                    }
                );
                return s;
            case "scatter":
            {
                var mix = PropScatter.ScatterMix(
                    rect,
                    19 * m,
                    [(PropKind.Tree, 3.0), (PropKind.Pine, 1.6), (PropKind.Bush, 0.7)],
                    density: 0.95,
                    seed: 7.3
                );
                foreach (var pp in mix)
                {
                    var kind = pp.Kind switch
                    {
                        PropKind.Pine => "pine",
                        PropKind.Bush => Kinds.Bush,
                        _ => Kinds.Tree,
                    };
                    s.Props.Add(
                        new Prop
                        {
                            Id = s.AllocId(),
                            Kind = kind,
                            X = pp.X,
                            Y = pp.Y,
                            Scale = 19 * m * pp.Scale,
                            Rotation = pp.Rotation,
                            Seed = (int)pp.Seed,
                        }
                    );
                }
                return s;
            }
        }
        switch (t.Mode)
        {
            case MapEditor.Mode.Stroke:
            {
                var width = t.Kind switch
                {
                    Kinds.River => 18,
                    Kinds.Road => 13,
                    Kinds.Wall => 12,
                    _ => 9,
                };
                s.Strokes.Add(
                    new Stroke
                    {
                        Id = s.AllocId(),
                        Kind = t.Kind,
                        Points = [.. line],
                        Width = width * m,
                    }
                );
                return s;
            }
            case MapEditor.Mode.Shape:
            {
                var height = t.Kind is Kinds.House or Kinds.Room ? 18 : 0;
                var size = t.Kind is Kinds.Pond or Kinds.Field ? 24 : 20;
                // Water/field read better as rounded blobs — a 4-point
                // rect leaves diagonal artifacts in the pond painter.
                s.Shapes.Add(
                    t.Kind is Kinds.Pond or Kinds.Field
                        ? new Shape
                        {
                            Id = s.AllocId(),
                            Kind = t.Kind,
                            Points = [.. Blob(P(57, 38), 26 * m, 12)],
                            Size = size * m,
                        }
                        : Area(t.Kind, size * m, height)
                );
                return s;
            }
            case MapEditor.Mode.Label:
                var lbl = PropAt("label", P(60, 38), 26);
                lbl.Text = "Město";
                s.Props.Add(lbl);
                return s;
            case MapEditor.Mode.Prop:
            {
                var scale = PropScale.GetValueOrDefault(t.Kind, 18);
                // Door/window only make sense anchored in a room wall —
                // the interior painter carves the opening at the edge.
                if (t.Kind is "door" or "window")
                {
                    s.Shapes.Add(
                        new Shape
                        {
                            Id = s.AllocId(),
                            Kind = Kinds.Room,
                            Points = [P(30, 14), P(90, 14), P(90, 62), P(30, 62)],
                            Size = 20 * m,
                            Height = 18,
                        }
                    );
                    s.Props.Add(PropAt(t.Kind, P(60, t.Kind == "door" ? 62 : 14), scale));
                    return s;
                }
                var pts = t.Kind switch
                {
                    "pillar" => new[] { P(38, 38), P(82, 38) },
                    "grave" or "bones" => new[] { P(40, 46), P(64, 28), P(84, 48) },
                    _ => new[] { P(60, 38) },
                };
                foreach (var p in pts)
                {
                    var prop = PropAt(t.Kind, p, scale);
                    if (t.Kind == "marker")
                        prop.Model = 0;
                    s.Props.Add(prop);
                }
                if (t.Kind == "camp")
                    s.Props = Interactions.ExpandCamp(s.Props[0], s);
                return s;
            }
        }
        return s.Shapes.Count + s.Strokes.Count + s.Props.Count > 0 ? s : null;
    }

    /// <summary>Deterministic wobbly blob for brush stamps.</summary>
    static List<Vector2> Blob(Vector2 c, double r, int n = 10, double seed = 3.7)
    {
        var pts = new List<Vector2>(n);
        for (var i = 0; i < n; i++)
        {
            var a = i / (double)n * Math.PI * 2;
            var rr = r * (0.86 + Rng.Noise(seed + i * 3.1) * 0.24);
            pts.Add(c + new Vector2((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr)));
        }
        return pts;
    }

    /// <summary>Fog-of-war look baked onto a rendered scene: shroud =
    /// dark veil thickening to the bottom right; clear = revealed
    /// corridor sweeping from the bottom-left to the top-right.</summary>
    static void Veil(Image img, bool reveal, float m)
    {
        var night = new Color(0.04f, 0.06f, 0.09f);
        var w = img.GetWidth();
        var h = img.GetHeight();
        var a = new Vector2(6 * m, 66 * m);
        var b = new Vector2(114 * m, 10 * m);
        var ab = b - a;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var p = img.GetPixel(x, y);
            float cov;
            if (reveal)
            {
                var t = Mathf.Clamp(
                    (new Vector2(x, y) - a).Dot(ab) / ab.LengthSquared(),
                    0,
                    1
                );
                var d = ((a + ab * t) - new Vector2(x, y)).Length();
                cov = Mathf.Lerp(0.0f, 0.75f, Mathf.Clamp((d - 12 * m) / (26f * m), 0, 1));
            }
            else
            {
                cov = 0.56f + 0.32f * ((x + (h - y)) / (float)(w + h));
            }
            img.SetPixel(x, y, p.Lerp(night, cov));
        }
    }
}

/// <summary>Drawn glyph for tools with no paintable output — the web
/// rail icons (cursor arrow, eraser ring, play tokens).</summary>
public partial class GlyphIcon : Node2D
{
    public string Kind = "";

    static readonly Color Paper = new(0.91f, 0.84f, 0.65f);
    static readonly Color Ink = new(0.23f, 0.19f, 0.14f);

    public override void _Draw()
    {
        switch (Kind)
        {
            case "select":
                // Cursor arrow — the web rail icon scaled up.
                var arrow = new Vector2[]
                {
                    new(5, 3),
                    new(5, 20),
                    new(10, 15),
                    new(14, 22),
                    new(17, 20),
                    new(13, 13),
                    new(20, 13),
                }
                    .Select(p => p * 2.7f + new Vector2(30, 8))
                    .ToArray();
                DrawColoredPolygon(arrow, Paper);
                DrawPolyline([.. arrow, arrow[0]], Ink, 2.4f, true);
                break;
            case "erase":
                // Eraser ring with ticks — the web eraser icon.
                var c = new Vector2(60, 38);
                DrawArc(c, 20, 0, Mathf.Tau, 32, Paper, 3, true);
                DrawArc(c, 20, 0, Mathf.Tau, 32, Ink, 1.2f, true);
                foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
                    DrawLine(
                        c + new Vector2(dx * 25, dy * 25),
                        c + new Vector2(dx * 31, dy * 31),
                        Paper,
                        3,
                        true
                    );
                break;
            case "play":
                // Token chips like the play layer — spawn / player / foe.
                Chip(new(40, 44), Palette.Hex("#7aa05b"), "S");
                Chip(new(62, 32), Palette.Hex("#4a7a9a"), "H");
                Chip(new(84, 46), Palette.Hex("#b3453a"), "N");
                break;
        }
    }

    void Chip(Vector2 c, Color fill, string mark)
    {
        DrawCircle(c, 12, fill);
        DrawArc(c, 12, 0, Mathf.Tau, 28, Ink, 1.6f, true);
        var font = ThemeDB.FallbackFont;
        var w = font.GetStringSize(mark, fontSize: 13).X;
        DrawString(
            font,
            c + new Vector2(-w / 2, 5),
            mark,
            fontSize: 13,
            modulate: new Color(0.96f, 0.93f, 0.85f)
        );
    }
}
