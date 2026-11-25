#nullable enable
// Scene data model — the Godot port of `scene-model.ts`. The web model
// keeps one flat `shapes` list; here it is split by geometry role into
// three typed lists: Shapes (closed areas), Strokes (polylines) and
// Props (single-point objects). Kind vocabulary and numeric ranges are
// 1:1 with the TS model so scene files stay interchangeable.
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

/// <summary>Kind vocabulary + classification, ported from scene-model.ts.</summary>
public static class Kinds
{
    // Areas — closed polygons painted as a footprint.
    public const string House = "house";
    public const string Room = "room";
    public const string Scatter = "scatter";
    public const string Pond = "pond";
    public const string Field = "field";
    public const string Floor = "floor";
    public const string Terrain = "terrain";

    // Lines — open polylines stroked at `width`.
    public const string Road = "road";
    public const string River = "river";
    public const string Wall = "wall";
    public const string Fence = "fence";

    // Point props (a representative subset of the TS list; any string in
    // `Props` is accepted by the parser).
    public const string Tree = "tree";
    public const string Rock = "rock";
    public const string Bush = "bush";
    public const string Tent = "tent";
    public const string Campfire = "campfire";
    public const string Camp = "camp"; // marker — always expands to props
    public const string Crate = "crate";
    public const string Barrel = "barrel";
    public const string Well = "well";
    public const string Bones = "bones";
    public const string Waterfall = "waterfall";
    public const string Dock = "dock";

    public static readonly string[] Areas =
        [House, Room, Scatter, Pond, Field, Floor, Terrain];

    public static readonly string[] Lines = [Road, River, Wall, Fence];

    /// <summary>Every point-prop kind the web editor offers (TS `kinds`
    /// minus areas and lines).</summary>
    public static readonly string[] Props =
    [
        Tree,
        "pine",
        Rock,
        "crate",
        "barrel",
        Bush,
        Tent,
        Campfire,
        "boat",
        Well,
        "statue",
        "crystal",
        "cart",
        Camp,
        "stall",
        "flag",
        "bale",
        "stonecircle",
        "tower",
        "barricade",
        "marker",
        "label",
        "fountain",
        "lantern",
        "pillar",
        "ruin",
        "grave",
        "chest",
        Bones,
        "portal",
        Waterfall,
        Dock,
        "door",
        "window",
        "table",
        "bed",
        "shelf",
        "rug",
        "fireplace",
    ];

    /// <summary>Removed legacy kinds — dropped silently like parseScene.</summary>
    public static readonly string[] Legacy = ["hill", "mountains"];

    public static readonly string[] Biomes =
        ["woodland", "desert", "alpine", "swamp", "volcanic", "cavern"];

    public static readonly string[] Moods = ["dawn", "day", "dusk", "night", "fog"];

    public static bool IsArea(string kind) => Areas.Contains(kind);

    public static bool IsLine(string kind) => Lines.Contains(kind);

    public static bool IsProp(string kind) => Props.Contains(kind);

    public static bool IsKnown(string kind) =>
        IsArea(kind) || IsLine(kind) || IsProp(kind);

    /// <summary>How many variants a kind offers (TS `variantCount`).</summary>
    public static int VariantCount(string kind) => kind == Bones ? 4 : 3;
}

/// <summary>A closed-area shape — the TS Shape restricted to area kinds.
/// `Points` is the outline directly (the freehand footprint); the editor
/// bakes rectangle/ellipse forms into points at commit time.</summary>
public class Shape
{
    public int Id;
    public string Kind = "";
    public List<Vector2> Points = [];
    /// <summary>Slope band width / brush size px (TS `size`).</summary>
    public double Size = 24;
    /// <summary>Elevation level 0..100 (terrain only).</summary>
    public double Height;
    /// <summary>Jaggedness 0..100 (terrain only).</summary>
    public double Roughness;
    /// <summary>Variant index 0..VariantCount-1 (TS `model`).</summary>
    public int Model;
    /// <summary>"#rrggbb" override, null = palette default.</summary>
    public string? Color;

    /// <summary>Variants this kind offers — TS `variantCount(kind)`.</summary>
    public int VariantCount => Kinds.VariantCount(Kind);

    public Shape DeepClone() =>
        new()
        {
            Id = Id,
            Kind = Kind,
            Points = [.. Points],
            Size = Size,
            Height = Height,
            Roughness = Roughness,
            Model = Model,
            Color = Color,
        };
}

/// <summary>An open polyline — roads, rivers, walls, fences (TS line
/// kinds). `Width` is the TS `size` field renamed to what it measures.</summary>
public class Stroke
{
    public int Id;
    public string Kind = "";
    public List<Vector2> Points = [];
    /// <summary>Drawn width px.</summary>
    public double Width = 24;
    /// <summary>Variant index — e.g. wall crenellation / double wall.</summary>
    public int Model;

    public Stroke DeepClone() =>
        new() { Id = Id, Kind = Kind, Points = [.. Points], Width = Width, Model = Model };
}

/// <summary>A single-anchor prop — every non-area non-line kind. Maps to a
/// TS Shape with one point: X/Y = points[0], Scale = size, Rotation =
/// angle, Seed drives deterministic per-prop variation.</summary>
public class Prop
{
    public int Id;
    public string Kind = "";
    public double X;
    public double Y;
    /// <summary>Stamp size px (TS `size`).</summary>
    public double Scale = 24;
    /// <summary>Rotation in radians (TS `angle`).</summary>
    public double Rotation;
    /// <summary>Deterministic variation seed — seeded noise input,
    /// replaces per-kind model where a continuous variation is wanted.</summary>
    public int Seed;
    /// <summary>Caption text — only the "label" prop kind uses it.</summary>
    public string? Text;

    public Prop DeepClone() =>
        new()
        {
            Id = Id,
            Kind = Kind,
            X = X,
            Y = Y,
            Scale = Scale,
            Rotation = Rotation,
            Seed = Seed,
            Text = Text,
        };
}

/// <summary>The scene document: name + the three element lists + global
/// look settings. `CastShadows` is the TS `lighting.castShadows` toggle
/// lifted to a flat field; Mood/Biome carry the TS ids.</summary>
public class Scene
{
    public int Version = 1;
    public string Name = "";
    public double Width = 1600;
    public double Height = 1000;
    public string? Biome;
    public string? Mood;
    /// <summary>Global shadow toggle — default on like the web app.</summary>
    public bool CastShadows = true;
    /// <summary>Id allocator — every element gets a unique id.</summary>
    public int NextId = 1;
    public List<Shape> Shapes = [];
    public List<Stroke> Strokes = [];
    public List<Prop> Props = [];

    public int AllocId() => NextId++;

    public Scene DeepClone() =>
        new()
        {
            Version = Version,
            Name = Name,
            Width = Width,
            Height = Height,
            Biome = Biome,
            Mood = Mood,
            CastShadows = CastShadows,
            NextId = NextId,
            Shapes = [.. Shapes.Select(s => s.DeepClone())],
            Strokes = [.. Strokes.Select(s => s.DeepClone())],
            Props = [.. Props.Select(p => p.DeepClone())],
        };

    /// <summary>Structural equality — exact field/list comparison (parsed
    /// floats round-trip bit-exact, so no epsilon is needed).</summary>
    public bool ContentEquals(Scene o) =>
        Version == o.Version
        && Name == o.Name
        && Width == o.Width
        && Height == o.Height
        && Biome == o.Biome
        && Mood == o.Mood
        && CastShadows == o.CastShadows
        && NextId == o.NextId
        && Shapes.Count == o.Shapes.Count
        && Strokes.Count == o.Strokes.Count
        && Props.Count == o.Props.Count
        && Shapes.Zip(o.Shapes).All(p => ShapeEq(p.First, p.Second))
        && Strokes.Zip(o.Strokes).All(p => StrokeEq(p.First, p.Second))
        && Props.Zip(o.Props).All(p => PropEq(p.First, p.Second));

    static bool ShapeEq(Shape a, Shape b) =>
        a.Id == b.Id
        && a.Kind == b.Kind
        && a.Size == b.Size
        && a.Height == b.Height
        && a.Roughness == b.Roughness
        && a.Model == b.Model
        && a.Color == b.Color
        && a.Points.SequenceEqual(b.Points);

    static bool StrokeEq(Stroke a, Stroke b) =>
        a.Id == b.Id
        && a.Kind == b.Kind
        && a.Width == b.Width
        && a.Model == b.Model
        && a.Points.SequenceEqual(b.Points);

    static bool PropEq(Prop a, Prop b) =>
        a.Id == b.Id
        && a.Kind == b.Kind
        && a.X == b.X
        && a.Y == b.Y
        && a.Scale == b.Scale
        && a.Rotation == b.Rotation
        && a.Seed == b.Seed;
}
