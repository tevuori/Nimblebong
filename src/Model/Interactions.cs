#nullable enable
// Edit interactions — the `mergeIntoShapes` port (it lives in
// scene-geometry.ts upstream). Same semantics, adapted to the split
// model: strokes concatenate at touching endpoints or splice in as a
// T-junction arm, compatible areas boolean-union into one blob, and a
// wall stroke closing on itself promotes to a house footprint.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public static class Interactions
{
    // -- committing new elements -----------------------------------------

    /// <summary>Add a stroke to the scene: a wall drawn as a closed loop
    /// becomes a house footprint; other lines merge into a touching
    /// same-kind stroke (endpoint concat, then T-junction attach), else
    /// append. Mirrors mergeIntoShapes' line branch.</summary>
    public static void AddStroke(Scene scene, Stroke stroke)
    {
        if (stroke.Kind == Kinds.Wall && ClosedLoop(stroke.Points, stroke.Width))
        {
            var step = Math.Max(1, (int)Math.Ceiling(stroke.Points.Count / 128.0));
            scene.Shapes.Add(
                new Shape
                {
                    Id = stroke.Id,
                    Kind = Kinds.House,
                    Points = [.. stroke.Points.Where((_, i) => i % step == 0)],
                    Size = stroke.Width,
                }
            );
            return;
        }
        // Simplify before merging — drawing oversamples the path at ~4 px
        // and dense control points make the spline ribbons self-intersect.
        var host = stroke.DeepClone();
        host.Points = SimplifyPath(host.Points);
        var merged = MergeStrokeInto(scene.Strokes, host);
        scene.Strokes.Clear();
        scene.Strokes.AddRange(merged);
    }

    /// <summary>Add an area shape: mergeable kinds (pond, field, scatter,
    /// terrain) fuse into existing same-material shapes they overlap —
    /// the union comes from the clipper (TerrainGeometry.Union), one
    /// resulting contour per merged shape. Terrain only fuses on the same
    /// elevation level so terraces keep stacking.</summary>
    public static void AddShape(Scene scene, Shape shape)
    {
        var merged = MergeShapeInto(scene, shape);
        scene.Shapes.Clear();
        scene.Shapes.AddRange(merged);
    }

    /// <summary>Add a prop — a "camp" marker never persists, it unfolds
    /// into a deterministic prop cluster like expandGroup.</summary>
    public static void AddProp(Scene scene, Prop prop)
    {
        if (prop.Kind == Kinds.Camp)
            scene.Props.AddRange(ExpandCamp(prop, scene));
        else
            scene.Props.Add(prop);
    }

    // -- stroke merging (the isLine branch of mergeIntoShapes) -----------

    /// <summary>Fuse a fresh stroke into the list: endpoint concat within
    /// tolerance, then T-junction attach, else append. Same kind+width is
    /// "same line" here — the TS color/variant/model checks have no
    /// counterparts on Stroke.</summary>
    public static List<Stroke> MergeStrokeInto(IReadOnlyList<Stroke> strokes, Stroke added)
    {
        var tolerance = Math.Max(24, added.Width);
        bool Near(Vector2 p, Vector2 q) => p.DistanceTo(q) <= tolerance;
        bool SameLine(Stroke other) => other.Kind == added.Kind && other.Width == added.Width;
        var existingLinePoints = strokes
            .Where(s => s.Kind is Kinds.Road or Kinds.River)
            .Sum(s => s.Points.Count);
        bool FitsLimits(int removed, int count) =>
            count <= 4800
            && (
                added.Kind is not (Kinds.Road or Kinds.River)
                || existingLinePoints - removed + count <= 1950
            );

        // Endpoint concat: the new stroke's ends against each existing
        // line's ends, in all four orientations.
        for (var i = 0; i < strokes.Count; i++)
        {
            var other = strokes[i];
            if (!SameLine(other))
                continue;
            var a0 = added.Points[0];
            var a1 = added.Points[^1];
            var b0 = other.Points[0];
            var b1 = other.Points[^1];
            List<Vector2>? points = null;
            if (Near(a0, b1))
                points = [.. other.Points, .. added.Points];
            else if (Near(a1, b0))
                points = [.. added.Points, .. other.Points];
            else if (Near(a0, b0))
                points = [.. other.Points.AsEnumerable().Reverse(), .. added.Points];
            else if (Near(a1, b1))
                points = [.. added.Points, .. other.Points.AsEnumerable().Reverse()];
            if (points != null)
                points = SimplifyPath(points);
            if (points == null || !FitsLimits(other.Points.Count, points.Count))
                continue;
            var next = new List<Stroke>(strokes);
            var host = other.DeepClone();
            host.Points = points;
            next[i] = host;
            return next;
        }

        // T-junction: an endpoint of one polyline lands on the interior
        // of the other — splice the touching line in as an out-and-back
        // arm so the junction becomes a single object.
        for (var i = 0; i < strokes.Count; i++)
        {
            var other = strokes[i];
            if (!SameLine(other))
                continue;
            var points = Attach(other, added, tolerance) ?? Attach(added, other, tolerance);
            if (points == null || !FitsLimits(other.Points.Count, points.Count))
                continue;
            var next = new List<Stroke>(strokes);
            var host = other.DeepClone();
            host.Points = points;
            next[i] = host;
            return next;
        }
        return [.. strokes, added];
    }

    /// <summary>Try to splice `arm` into `host` at whichever of arm's
    /// endpoints touches host's interior — the arm retraces its own
    /// points on the way back so it stays one stroke.</summary>
    static List<Vector2>? Attach(Stroke host, Stroke arm, double tolerance)
    {
        foreach (var end in new[] { 0, arm.Points.Count - 1 })
        {
            var p = arm.Points[end];
            var bestSeg = -1;
            var bestPoint = Vector2.Zero;
            var bestDistance = double.MaxValue;
            for (var s = 1; s < host.Points.Count; s++)
            {
                var a = host.Points[s - 1];
                var b = host.Points[s];
                var d = b - a;
                var length2 = d.LengthSquared();
                if (length2 < 1e-9)
                    continue;
                var t = Math.Clamp((p - a).Dot(d) / length2, 0f, 1f);
                var point = a + d * t;
                var distance = p.DistanceTo(point);
                if (distance < bestDistance)
                {
                    bestSeg = s;
                    bestPoint = point;
                    bestDistance = distance;
                }
            }
            if (bestSeg < 0 || bestDistance > tolerance)
                continue;
            var out_ = SimplifyPath(
                end == 0
                    ? arm.Points.Skip(1).ToList()
                    : arm.Points.Take(arm.Points.Count - 1).Reverse().ToList()
            );
            return
            [
                .. host.Points.Take(bestSeg),
                bestPoint,
                .. out_,
                .. out_.Take(out_.Count - 1).Reverse(),
                bestPoint,
                .. host.Points.Skip(bestSeg),
            ];
        }
        return null;
    }

    // -- area merging (the mergeableAreaKinds branch) ---------------------

    static readonly HashSet<string> MergeableAreaKinds = [Kinds.Pond, Kinds.Field, Kinds.Scatter, Kinds.Terrain];

    static bool MergeableArea(Shape other, Shape added) =>
        other.Kind == added.Kind
        && other.Model == added.Model
        && other.Color == added.Color
        // Elevated masses only fuse on the same elevation level —
        // different heights stay separate so they stack into terraces.
        && (added.Kind != Kinds.Terrain || other.Height == added.Height);

    /// <summary>Fuse an area shape into the list: transitively absorb
    /// every same-material shape it overlaps, union the footprints via
    /// the clipper, emit one merged shape per resulting contour
    /// (point-touching pieces stay separate instead of being dropped).
    /// Non-mergeable kinds append unchanged.</summary>
    public static List<Shape> MergeShapeInto(Scene scene, Shape added)
    {
        var shapes = scene.Shapes;
        if (!MergeableAreaKinds.Contains(added.Kind))
            return [.. shapes, added];
        var acc = new List<Vector2[]> { added.Points.ToArray() };
        var absorbed = new HashSet<Shape>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var other in shapes)
            {
                if (absorbed.Contains(other) || !MergeableArea(other, added))
                    continue;
                var poly = other.Points.ToArray();
                if (acc.Any(p => PolygonsOverlap(p, poly)))
                {
                    absorbed.Add(other);
                    acc.Add(poly);
                    changed = true;
                }
            }
        }
        if (absorbed.Count == 0)
            return [.. shapes, added];
        var contours = TerrainGeometry
            .Union([.. acc])
            .OrderByDescending(c => Math.Abs(TerrainGeometry.SignedArea(c)))
            .ToList();
        var result = shapes.Where(s => !absorbed.Contains(s)).ToList();
        for (var i = 0; i < contours.Count; i++)
            result.Add(
                new Shape
                {
                    // The absorbed merge keeps the new shape's id; extra
                    // contours (point-touching pieces) get fresh ones.
                    Id = i == 0 ? added.Id : scene.AllocId(),
                    Kind = added.Kind,
                    Points = [.. contours[i]],
                    Size = added.Size,
                    Height = added.Height,
                    Roughness = added.Roughness,
                    Model = added.Model,
                    Angle = added.Angle,
                    Color = added.Color,
                }
            );
        return result;
    }

    // -- snapping + predicates ---------------------------------------------

    /// <summary>A freehand stroke counts as a closed loop when it ends
    /// near its own start — TS `closedLoop`.</summary>
    public static bool ClosedLoop(IReadOnlyList<Vector2> points, double width) =>
        points.Count >= 8
        && points[0].DistanceTo(points[^1]) <= Math.Max(40, width);

    /// <summary>Snap a stroke endpoint to the nearest endpoint of an
    /// existing stroke — same kind first, then any kind — within
    /// `tolerance`. The finish-line glue the editor applies before
    /// MergeStrokeInto runs.</summary>
    public static Vector2? SnapEndpoint(
        Vector2 point,
        IReadOnlyList<Stroke> strokes,
        string? kind = null,
        double tolerance = 24
    )
    {
        var best = Vector2.Zero;
        var bestDistance = tolerance;
        var found = false;
        // Same-kind endpoints win ties by scanning first with the full
        // tolerance, then other kinds only when strictly closer.
        foreach (var pass in new[] { true, false })
        foreach (var stroke in strokes)
        {
            if (pass && kind != null && stroke.Kind != kind)
                continue;
            if (!pass && kind != null && stroke.Kind == kind)
                continue;
            foreach (var end in new[] { stroke.Points[0], stroke.Points[^1] })
            {
                var d = point.DistanceTo(end);
                if (d < bestDistance || (!found && d <= tolerance))
                {
                    best = end;
                    bestDistance = d;
                    found = true;
                }
            }
        }
        return found ? best : null;
    }

    /// <summary>Project `point` onto the nearest segment of `stroke` —
    /// the snapping query used to glue a stroke to another's interior.</summary>
    public static (int Segment, Vector2 Point, double Distance) NearestOnStroke(
        Stroke stroke,
        Vector2 point
    )
    {
        var seg = -1;
        var bestPoint = Vector2.Zero;
        var distance = double.MaxValue;
        for (var s = 1; s < stroke.Points.Count; s++)
        {
            var a = stroke.Points[s - 1];
            var b = stroke.Points[s];
            var d = b - a;
            var length2 = d.LengthSquared();
            if (length2 < 1e-9)
                continue;
            var t = Math.Clamp((point - a).Dot(d) / length2, 0f, 1f);
            var candidate = a + d * t;
            var dist = point.DistanceTo(candidate);
            if (dist < distance)
            {
                seg = s;
                bestPoint = candidate;
                distance = dist;
            }
        }
        return (seg, bestPoint, distance);
    }

    // -- geometry helpers (scene-geometry.ts ports) ------------------------

    /// <summary>TS `segmentIntersection` — null unless the segments meet
    /// strictly inside (or at endpoints when asked).</summary>
    public static Vector2? SegmentIntersection(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        bool includeEndpoints = false
    )
    {
        var r = b - a;
        var s = d - c;
        var cross = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(cross) < 0.00001)
            return null;
        var t = ((c.X - a.X) * s.Y - (c.Y - a.Y) * s.X) / cross;
        var u = ((c.X - a.X) * r.Y - (c.Y - a.Y) * r.X) / cross;
        var hit = includeEndpoints
            ? t is >= 0 and <= 1 && u is >= 0 and <= 1
            : t is > 0 and < 1 && u is > 0 and < 1;
        return hit ? a + r * t : null;
    }

    public static double DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var d = b - a;
        var length2 = d.LengthSquared();
        var t = length2 < 1e-12 ? 0f : Math.Clamp((p - a).Dot(d) / length2, 0f, 1f);
        return p.DistanceTo(a + d * t);
    }

    /// <summary>TS `polygonsOverlap` — containment either way or any
    /// segment crossing (endpoints included).</summary>
    public static bool PolygonsOverlap(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
    {
        if (a.Any(p => Poly.PointInPolygon(p, b)) || b.Any(p => Poly.PointInPolygon(p, a)))
            return true;
        for (var i = 0; i < a.Count; i++)
        for (var j = 0; j < b.Count; j++)
            if (
                SegmentIntersection(
                    a[i],
                    a[(i + 1) % a.Count],
                    b[j],
                    b[(j + 1) % b.Count],
                    true
                ) != null
            )
                return true;
        return false;
    }

    public static double PolygonArea(IReadOnlyList<Vector2> points) =>
        Math.Abs(TerrainGeometry.SignedArea(points));

    /// <summary>Drop near-duplicate and near-collinear points — drawing
    /// oversamples the path and merged polylines grow into the thousands.
    /// Kept at a tiny tolerance so junctions and hand-drawn wobble
    /// survive (TS `simplifyPath`).</summary>
    public static List<Vector2> SimplifyPath(IReadOnlyList<Vector2> points, double tol = 1.5)
    {
        if (points.Count <= 2)
            return [.. points];
        var out_ = new List<Vector2> { points[0] };
        for (var i = 1; i < points.Count - 1; i++)
        {
            var a = out_[^1];
            var b = points[i];
            var c = points[i + 1];
            var d = c - a;
            var length2 = d.LengthSquared();
            var t = length2 < 1e-12 ? 0f : Math.Clamp((b - a).Dot(d) / length2, 0f, 1f);
            var dev = b.DistanceTo(a + d * t);
            if (dev > tol || b.DistanceTo(a) > 12)
                out_.Add(b);
        }
        out_.Add(points[^1]);
        return out_;
    }

    // -- camp expansion (expandGroup port) ----------------------------------

    /// <summary>A "camp" prop never persists — placing it unfolds into a
    /// small cluster of ordinary props around its anchor, deterministic
    /// per anchor position (and the prop's Seed) so previews match the
    /// committed result. Model I tábor, II karavan, III tržiště picked
    /// by seed % 3.</summary>
    public static List<Prop> ExpandCamp(Prop camp, Scene scene)
    {
        if (camp.Kind != Kinds.Camp)
            return [camp];
        var result = new List<Prop>();
        var anchor = new Vector2((float)camp.X, (float)camp.Y);
        var seed = camp.X * 0.37 + camp.Y * 0.91 + camp.Seed * 19;
        var spread = Math.Max(24, camp.Scale * 1.5);
        Vector2 Clamp(Vector2 p) =>
            new(
                (float)Math.Clamp(p.X, 0, scene.Width),
                (float)Math.Clamp(p.Y, 0, scene.Height)
            );
        Prop Make(string kind, int slot, double distance, double size, int model = -1)
        {
            var angle = Rng.Noise(seed + slot * 7.3) * Math.PI * 2;
            var reach = distance * (0.7 + Rng.Noise(seed + slot * 3.1) * 0.35);
            var pos = Clamp(
                anchor + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (float)reach
            );
            return new Prop
            {
                Id = scene.AllocId(),
                Kind = kind,
                X = pos.X,
                Y = pos.Y,
                Scale = Math.Max(8, size * (0.85 + Rng.Noise(seed + slot * 1.7) * 0.3)),
                Rotation = angle,
                Seed = model >= 0 ? model : (int)(Rng.Noise(seed + slot * 11.3) * int.MaxValue),
            };
        }
        switch (camp.Seed % 3)
        {
            case 1:
                // Karavan: vozy, stan a naložené zásoby.
                result.Add(Make("cart", 1, spread, camp.Scale));
                result.Add(Make("cart", 2, spread * 0.95, camp.Scale * 0.95));
                result.Add(Make("tent", 3, spread * 0.8, camp.Scale * 0.85));
                result.Add(Make("crate", 4, spread * 0.45, camp.Scale * 0.45));
                result.Add(Make("barrel", 5, spread * 0.45, camp.Scale * 0.4));
                result.Add(Make("flag", 6, spread * 0.9, camp.Scale * 0.55));
                break;
            case 2:
                // Tržiště: stánky kolem společného prostranství.
                result.Add(Make("stall", 1, spread * 0.75, camp.Scale * 1.05, 0));
                result.Add(Make("stall", 2, spread * 0.75, camp.Scale, 1));
                result.Add(Make("stall", 3, spread * 0.8, camp.Scale * 0.95, 2));
                result.Add(Make("crate", 4, spread * 0.4, camp.Scale * 0.45));
                result.Add(Make("barrel", 5, spread * 0.4, camp.Scale * 0.4));
                result.Add(Make("flag", 6, spread * 0.85, camp.Scale * 0.55));
                break;
            default:
                // Tábor: ohniště uprostřed, stany a zásoby kolem.
                result.Add(Make("campfire", 1, 0, camp.Scale * 0.6));
                result.Add(Make("tent", 2, spread * 0.85, camp.Scale * 0.95));
                result.Add(Make("tent", 3, spread * 0.9, camp.Scale * 0.9));
                result.Add(Make("crate", 4, spread * 0.5, camp.Scale * 0.45));
                result.Add(Make("barrel", 5, spread * 0.5, camp.Scale * 0.4));
                break;
        }
        return result;
    }

    // -- protection zones (scene-interactions.ts port) ---------------------

    /// <summary>A keep-out circle around a protected prop.</summary>
    public readonly record struct ProtectionZone(Vector2 Center, double Radius);

    /// <summary>Protection factors per kind — `protection.kinds` from
    /// the web frontend.json. Radius = (scale/2)*factor + padding.</summary>
    public static readonly Dictionary<string, double> ProtectedKinds = new()
    {
        ["campfire"] = 1,
        ["well"] = 1,
        ["tent"] = 0.9,
        ["lantern"] = 1,
        ["marker"] = 1,
        ["portal"] = 1,
        ["fountain"] = 1,
        ["stall"] = 0.9,
        ["statue"] = 0.8,
        ["chest"] = 1,
    };

    const double ProtectionPadding = 6;

    /// <summary>Kinds exempt from keep-out (keepOut.except).</summary>
    static readonly HashSet<string> KeepOutExcept = ["label", "marker", "door", "window"];

    /// <summary>Zones around every protected prop in the scene.</summary>
    public static List<ProtectionZone> ProtectionZones(Scene scene)
    {
        var zones = new List<ProtectionZone>();
        foreach (var p in scene.Props)
            if (ProtectedKinds.TryGetValue(p.Kind, out var factor))
                zones.Add(
                    new ProtectionZone(
                        new Vector2((float)p.X, (float)p.Y),
                        p.Scale / 2 * factor + ProtectionPadding
                    )
                );
        return zones;
    }

    /// <summary>Whether this kind must stand outside every zone —
    /// keepOut.kinds = ["*"], minus protected kinds and exceptions.</summary>
    public static bool KeptOut(string kind) =>
        !ProtectedKinds.ContainsKey(kind) && !KeepOutExcept.Contains(kind);

    /// <summary>The zone a circle of `reach` at `point` overlaps, if any.</summary>
    public static ProtectionZone? ZoneHit(
        Vector2 point,
        double reach,
        List<ProtectionZone> zones
    )
    {
        foreach (var z in zones)
            if (point.DistanceTo(z.Center) < z.Radius + reach)
                return z;
        return null;
    }

    /// <summary>Push a prop of `reach` out of every zone it overlaps —
    /// straight away from the zone center, a few passes for neighbours.</summary>
    public static Vector2 NudgeOut(Vector2 point, double reach, List<ProtectionZone> zones)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var z = ZoneHit(point, reach, zones);
            if (z == null)
                break;
            var d = point - z.Value.Center;
            if (d.LengthSquared() < 1e-6f)
                d = new Vector2(0, 1);
            var to = z.Value.Radius + reach + 0.5;
            point = z.Value.Center + d.Normalized() * (float)to;
        }
        return point;
    }

    /// <summary>Place a prop under the protection rules:
    /// kept-out kinds get nudged out of existing zones; a protected kind
    /// clears kept-out props from its new zone (clearOnPlace). Returns
    /// false when the placement is fully blocked (point lands inside a
    /// zone it cannot leave — map edge clamp).</summary>
    public static bool PlaceProtected(Scene scene, Prop prop)
    {
        var zones = ProtectionZones(scene);
        var reach = prop.Scale / 2;
        if (KeptOut(prop.Kind))
        {
            var p = new Vector2((float)prop.X, (float)prop.Y);
            var nudged = NudgeOut(p, reach, zones);
            if (
                ZoneHit(nudged, reach, zones) != null
                || nudged.X < 0
                || nudged.Y < 0
                || nudged.X > scene.Width
                || nudged.Y > scene.Height
            )
                return false;
            prop.X = nudged.X;
            prop.Y = nudged.Y;
        }
        AddProp(scene, prop);
        if (ProtectedKinds.ContainsKey(prop.Kind))
        {
            var zone = new ProtectionZone(
                new Vector2((float)prop.X, (float)prop.Y),
                prop.Scale / 2 * ProtectedKinds[prop.Kind] + ProtectionPadding
            );
            scene.Props.RemoveAll(
                q =>
                    q.Id != prop.Id
                    && KeptOut(q.Kind)
                    && zone.Center.DistanceTo(new Vector2((float)q.X, (float)q.Y))
                        < zone.Radius + q.Scale / 2
            );
        }
        return true;
    }
}
