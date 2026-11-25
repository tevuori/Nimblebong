// Terrain massif construction — the Godot port of `terrainContours`.
// Same contract: same-height shapes merge into shared tops, heights
// stack into terraces, the slope band + oblique extruded silhouette are
// precomputed as plain polygon sets ready for _Draw fills.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public class TerrainShape
{
    public int Model; // 0 rocky slope, 1 dirt mound, 2 rocky ridge
    public double Height; // elevation level 0..100
    public double Size; // slope band width px
    public double Roughness; // 0..100
    public Vector2[] Outline = []; // walkable TOP edge
}

public class Massif
{
    public int Model;
    public double Height;
    public double Size;
    public double Roughness;
    public Color Color = Colors.Transparent;
    /// <summary>Unioned walkable-top contours (elevation edge).</summary>
    public List<Vector2[]> Tops = [];
    /// <summary>Per-top roughness (parallel to Tops).</summary>
    public List<double> TopRoughness = [];
    /// <summary>Outermost toe edge — band lives between base and tops.</summary>
    public Vector2[] Base = [];
    /// <summary>tops ∪ tops+Face — the oblique cliff wall outline.</summary>
    public List<Vector2[]> Silhouettes = [];
    /// <summary>South extrusion vector; length encodes elevation.</summary>
    public Vector2 Face;
    /// <summary>Base ∖ tops — the slope band pieces (no holes).</summary>
    public List<Vector2[]> Band = [];
    /// <summary>Silhouettes ∖ tops — the south wall pieces.</summary>
    public List<Vector2[]> Wall = [];
}

public static class TerrainGeometry
{
    /// <summary>Group terrain shapes into rendered massifs — sorted
    /// ascending by height so terraces paint bottom-up.</summary>
    public static List<Massif> BuildMassifs(IEnumerable<TerrainShape> shapes)
    {
        var massifs = new List<Massif>();
        foreach (var members in shapes.GroupBy(s => (s.Model, s.Height, s.Size)))
        {
            var list = members.ToList();
            var first = list[0];
            var size = Mathf.Max(14, first.Size);

            var topPolys = list.Select(m => m.Outline).ToArray();
            var tops = Union(topPolys);
            if (tops.Count == 0)
                continue;

            var topRough = tops
                .Select(top =>
                    list.Where(m => Inside(m.Outline, top))
                        .Select(m => m.Roughness)
                        .DefaultIfEmpty(0)
                        .Max()
                )
                .ToList();

            // Oblique extrusion — the cliff wall hangs off the south side.
            var height = first.Height;
            var dirt = first.Model == 1;
            var wseed = first.Outline[0].X * 0.37 + first.Outline[0].Y * 0.61;
            double Wrand(double n) => Rng.Noise(wseed + n * 127.1);
            var faceH = Mathf.Min(46, (dirt ? 0.55 : 1.0) * (7 + height * 0.42));
            var face = new Vector2((float)(faceH * (Wrand(9) - 0.5) * 0.55), (float)faceH);
            var shifted = tops
                .Select(top => top.Select(p => p + face).ToArray())
                .ToArray();
            var silhouettes = Union([.. tops, .. shifted]);

            // Band: dilated silhouette, wobbled outward per-vertex for the
            // asymmetric foot Nimblehold slopes have.
            foreach (var sil in silhouettes)
            foreach (var rawBase in Offset(sil, size))
            // The wobble can self-intersect where the band pinches —
            // unioning a polygon with itself repairs the winding and
            // splits any bow-ties into clean pieces.
            foreach (var base_ in Geometry2D.MergePolygons(
                    Wobble(rawBase, size, wseed),
                    Wobble(rawBase, size, wseed)
                ))
            {
                var inside = tops
                    .Select((top, i) => (top, i))
                    .Where(t => Poly.PointInPolygon(t.top[0], base_))
                    .ToList();
                var massif = new Massif
                {
                    Model = first.Model,
                    Height = height,
                    Size = size,
                    Roughness = inside.Count > 0
                        ? inside.Max(t => topRough[t.i])
                        : topRough[0],
                    Tops = inside.Count > 0 ? inside.Select(t => t.top).ToList() : [tops[0]],
                    TopRoughness = inside.Count > 0
                        ? inside.Select(t => topRough[t.i]).ToList()
                        : [topRough[0]],
                    Base = base_,
                    Silhouettes = silhouettes,
                    Face = face,
                };
                massif.Band = Subtract(base_, massif.Tops);
                massif.Wall = Subtract(silhouettes, massif.Tops);
                massifs.Add(massif);
            }
        }
        massifs.Sort((a, b) => a.Height.CompareTo(b.Height));
        return massifs;
    }

    /// <summary>Union of polygon sets via the engine clipper — merge
    /// pairs to a fixpoint; a merge that covers less area than the
    /// inputs overlaps, a same-area result means disjoint.</summary>
    public static List<Vector2[]> Union(Vector2[][] polys)
    {
        var acc = new List<Vector2[]>(polys);
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < acc.Count && !changed; i++)
            for (var j = i + 1; j < acc.Count && !changed; j++)
            {
                var m = Geometry2D.MergePolygons(acc[i], acc[j]);
                double mergedArea = m.Sum(p => Math.Abs(SignedArea(p)));
                var origArea = Math.Abs(SignedArea(acc[i])) + Math.Abs(SignedArea(acc[j]));
                if (mergedArea < origArea - 0.5)
                {
                    acc[i] = m[0];
                    acc.RemoveAt(j);
                    acc.AddRange(m.Skip(1));
                    changed = true;
                }
            }
        }
        return acc;
    }

    public static double SignedArea(IReadOnlyList<Vector2> poly)
    {
        double area = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area / 2;
    }

    /// <summary>Dilate a polygon outward by `delta` px (round joins).</summary>
    public static List<Vector2[]> Offset(Vector2[] poly, double delta) =>
        [.. Geometry2D.OffsetPolygon(poly, (float)delta, Geometry2D.PolyJoinType.Round)];

    /// <summary>a ∖ b-set — plain polygon pieces, no holes.</summary>
    public static List<Vector2[]> Subtract(Vector2[] a, IEnumerable<Vector2[]> b) =>
        Subtract([a], b);

    /// <summary>a ∖ b — plain polygon pieces, no holes.</summary>
    public static List<Vector2[]> Subtract(Vector2[] a, Vector2[] b) =>
        [.. Geometry2D.ClipPolygons(a, b)];

    public static List<Vector2[]> Subtract(IEnumerable<Vector2[]> a, IEnumerable<Vector2[]> b)
    {
        var result = new List<Vector2[]>();
        foreach (var pa in a)
        {
            var pieces = new List<Vector2[]> { pa };
            foreach (var pb in b)
                pieces = pieces.SelectMany(p => Subtract(p, pb)).ToList();
            result.AddRange(pieces);
        }
        return result;
    }

    static bool Inside(Vector2[] member, Vector2[] contour)
    {
        for (var i = 0; i < member.Length; i++)
            if (
                Poly.PointInPolygon(member[i], contour)
                || Poly.PointInPolygon(
                    (member[i] + member[(i + 1) % member.Length]) / 2,
                    contour
                )
            )
                return true;
        return false;
    }

    // Per-vertex outward push by a smooth low-frequency wave — the slope
    // swells and pinches along the contour instead of staying uniform.
    static Vector2[] Wobble(Vector2[] poly, double size, double seed)
    {
        var n = poly.Length;
        var result = new Vector2[n];
        var sign = Geometry2D.IsPolygonClockwise(poly) ? 1f : -1f;
        for (var i = 0; i < n; i++)
        {
            var prev = poly[(i + n - 1) % n];
            var next = poly[(i + 1) % n];
            var edge = (next - prev).Normalized();
            var normal = new Vector2(edge.Y, -edge.X) * sign;
            var (x, y) = (poly[i].X, poly[i].Y);
            var wave =
                Math.Sin(x * 0.011 + seed) * 0.5
                + Math.Sin(y * 0.013 + seed * 1.7) * 0.35
                + Math.Sin((x + y) * 0.007 + seed * 2.3) * 0.3;
            result[i] = poly[i] + normal * (float)(wave * size * 0.22);
        }
        return result;
    }
}
