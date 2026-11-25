#nullable enable
// Prop placement — the data model for map objects (trees, rocks,
// houses…) plus a deterministic scatter that fills a polygon with a
// jittered grid. All randomness comes from Rng.Noise seeded by the
// prop's map position, so a scene re-renders identically every time.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public enum PropKind
{
    /// <summary>Deciduous tree — lobed canopy blob + trunk + ink.</summary>
    Tree,
    /// <summary>Conifer — stacked star layers.</summary>
    Pine,
    /// <summary>Boulder / crag.</summary>
    Rock,
    /// <summary>Low scrub — a few overlapping discs.</summary>
    Bush,
    /// <summary>Walls + pitched roof + chimney.</summary>
    House,
    // Nature & wilderness (PropsNature.cs)
    Tent,
    Campfire,
    Well,
    Ruin,
    Grave,
    Bones,
    Statue,
    StoneCircle,
    Crystal,
    Bale,
    Crate,
    Barrel,
    // Village & structures (PropsVillage.cs)
    Tower,
    Stall,
    Cart,
    Flag,
    Barricade,
    Lantern,
    Fountain,
    Pillar,
    Marker,
    // Water / treasure / interior (PropsInterior.cs)
    Dock,
    Boat,
    Chest,
    Portal,
    Door,
    Window,
    Table,
    Bed,
    Shelf,
    Rug,
    Fireplace,
    /// <summary>Text caption — paper tag with the label string.</summary>
    Label,
}

/// <summary>One placed map object. Scale is a multiplier on the kind's
/// base size; Rotation is radians; Seed is the position-derived jitter
/// source shared by the albedo and height painters.</summary>
public class PropPlacement
{
    public PropKind Kind;
    public float X;
    public float Y;
    public float Scale = 1f;
    public float Rotation;
    public double Seed;
    /// <summary>Explicit variant index; -1 = seeded auto (TS prop.model).</summary>
    public int Model = -1;
    /// <summary>Seed shifted by the explicit variant so picking one gives
    /// a different deterministic look for kinds without discrete
    /// variant branches.</summary>
    public double VarSeed => Model >= 0 ? Seed + Model * 977 : Seed;
    /// <summary>Caption — the "label" kind draws this.</summary>
    public string? Text;

    public Vector2 Pos => new(X, Y);

    public PropPlacement(
        PropKind kind,
        double x,
        double y,
        double scale = 1,
        double rotation = 0,
        double seed = 0
    )
    {
        Kind = kind;
        X = (float)x;
        Y = (float)y;
        Scale = (float)scale;
        Rotation = (float)rotation;
        this.Seed = seed;
    }
}

public static class PropScatter
{
    /// <summary>Jittered grid inside `polygon`: a point per `spacing`
    /// cell (rows offset by half a cell for organic packing), jittered
    /// within the cell, kept only when inside the polygon and under
    /// `density`. Deterministic for the same inputs.</summary>
    public static List<PropPlacement> Scatter(
        IReadOnlyList<Vector2> polygon,
        double spacing,
        PropKind kind,
        double density = 1.0,
        double scaleMin = 0.85,
        double scaleMax = 1.2,
        double seed = 0
    )
    {
        var props = new List<PropPlacement>();
        var (xs, ys) = BBox(polygon);
        var step = (float)spacing;
        var row = 0;
        for (var y = ys.X; y <= ys.Y; y += step, row++)
        {
            var xoff = (row % 2) * step * 0.5f;
            for (var x = xs.X + xoff; x <= xs.Y; x += step)
            {
                var s = seed + x * 0.37 + y * 0.53;
                var p = new Vector2(
                    (float)(x + (Rng.Noise(s + 1) - 0.5) * spacing * 0.9),
                    (float)(y + (Rng.Noise(s + 2) - 0.5) * spacing * 0.9)
                );
                if (!Poly.PointInPolygon(p, polygon))
                    continue;
                if (Rng.Noise(s + 3) > density)
                    continue;
                props.Add(
                    new PropPlacement(
                        kind,
                        p.X,
                        p.Y,
                        (float)(scaleMin + Rng.Noise(s + 4) * (scaleMax - scaleMin)),
                        (float)(Rng.Noise(s + 5) * Math.PI * 2),
                        s
                    )
                );
            }
        }
        return props;
    }

    /// <summary>Same jittered grid, but the kind is picked per point
    /// from a weighted table — forests mix species deterministically.</summary>
    public static List<PropPlacement> ScatterMix(
        IReadOnlyList<Vector2> polygon,
        double spacing,
        (PropKind Kind, double Weight)[] kinds,
        double density = 1.0,
        double scaleMin = 0.85,
        double scaleMax = 1.2,
        double seed = 0
    )
    {
        var props = new List<PropPlacement>();
        var (xs, ys) = BBox(polygon);
        var step = (float)spacing;
        double total = 0;
        foreach (var k in kinds)
            total += k.Weight;
        var row = 0;
        for (var y = ys.X; y <= ys.Y; y += step, row++)
        {
            var xoff = (row % 2) * step * 0.5f;
            for (var x = xs.X + xoff; x <= xs.Y; x += step)
            {
                var s = seed + x * 0.37 + y * 0.53;
                var p = new Vector2(
                    (float)(x + (Rng.Noise(s + 1) - 0.5) * spacing * 0.9),
                    (float)(y + (Rng.Noise(s + 2) - 0.5) * spacing * 0.9)
                );
                if (!Poly.PointInPolygon(p, polygon))
                    continue;
                if (Rng.Noise(s + 3) > density)
                    continue;
                var roll = Rng.Noise(s + 6) * total;
                var kind = kinds[^1].Kind;
                foreach (var k in kinds)
                {
                    roll -= k.Weight;
                    if (roll <= 0)
                    {
                        kind = k.Kind;
                        break;
                    }
                }
                props.Add(
                    new PropPlacement(
                        kind,
                        p.X,
                        p.Y,
                        (float)(scaleMin + Rng.Noise(s + 4) * (scaleMax - scaleMin)),
                        (float)(Rng.Noise(s + 5) * Math.PI * 2),
                        s
                    )
                );
            }
        }
        return props;
    }

    static (Vector2 X, Vector2 Y) BBox(IReadOnlyList<Vector2> poly)
    {
        float x0 = float.MaxValue,
            x1 = float.MinValue,
            y0 = float.MaxValue,
            y1 = float.MinValue;
        foreach (var p in poly)
        {
            if (p.X < x0) x0 = p.X;
            if (p.X > x1) x1 = p.X;
            if (p.Y < y0) y0 = p.Y;
            if (p.Y > y1) y1 = p.Y;
        }
        return (new Vector2(x0, x1), new Vector2(y0, y1));
    }
}
