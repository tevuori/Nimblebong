// Terrain albedo painter — Godot port of `terrainTop` + `terrainMassif`
// from the web renderer. Everything is emitted as plain polygon fills /
// polylines (CanvasItem._Draw has no clip state), so the slope band is
// painted as pre-clipped Band pieces and the strata as actual filled
// terrace rings — closer to the Nimblehold layering than stroked bands.
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class AlbedoPainter : Node2D
{
    public Color Ground = Palette.Hex("#8aab54");
    public Color Cliff = Palette.Hex("#b4b18a");
    public Color Stone = Palette.Hex("#a4a68e");
    public Vector2 MapSize = new(1600, 1000);

    List<Massif> _massifs = [];
    // Strata terrace rings cached per massif: filled bands between two
    // offset levels, alternating two tones.
    readonly Dictionary<Massif, List<(Vector2[] Poly, Color Tone)>> _strata = new();
    readonly Dictionary<Massif, List<Vector2[]>> _contours = new();

    public void SetMassifs(List<Massif> massifs)
    {
        _massifs = massifs;
        _strata.Clear();
        _contours.Clear();
        foreach (var m in massifs)
            BakeTerraces(m);
        QueueRedraw();
    }

    // Terrace rings between successive offset levels — filled bands
    // following the silhouette exactly, plus two thin contour echoes.
    void BakeTerraces(Massif m)
    {
        var rings = new List<(Vector2[], Color)>();
        var contours = new List<Vector2[]>();
        var face = FaceColor(m);
        (double f, double tone)[] steps =
        [
            (0.88, -6),
            (0.58, 5),
            (0.3, -7),
        ];
        for (var s = 0; s < steps.Length; s++)
        {
            var (fOut, tone) = steps[s];
            var fIn = s + 1 < steps.Length ? steps[s + 1].f : 0.02;
            foreach (var top in m.Tops)
            {
                var outer = TerrainGeometry.Offset(top, fOut * m.Size);
                var inner = TerrainGeometry.Offset(top, fIn * m.Size);
                var c = Palette.Tint(face, tone);
                foreach (var o in outer)
                {
                    var pieces = TerrainGeometry.Subtract(o, inner.ToArray());
                    foreach (var ring in pieces)
                        rings.Add((ring, c));
                }
            }
        }
        // One hairline contour echo floating in the face.
        foreach (var top in m.Tops)
            contours.AddRange(TerrainGeometry.Offset(top, 0.45 * m.Size));
        _strata[m] = rings;
        _contours[m] = contours;
    }

    static Color FaceColor(Massif m, Color cliff)
    {
        var faceJit = Rng.Noise(m.Base[0].X * 0.17 + m.Base[0].Y * 0.23);
        return Palette.Mix(
            m.Color.A > 0 ? m.Color : m.Model == 1 ? Palette.Hex("#b8a67e") : cliff,
            faceJit > 0.5 ? Colors.White : Palette.Hex("#101408"),
            Mathf.Abs(faceJit - 0.5) * 0.18
        );
    }

    Color FaceColor(Massif m) => FaceColor(m, Cliff);

    /// <summary>Fill a polygon only when it triangulates — clipper
    /// output can contain degenerate slivers the canvas renderer
    /// rejects.</summary>
    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    static bool InBand(Massif m, Vector2 p) =>
        Poly.PointInPolygon(p, m.Base) && !m.Tops.Exists(t => Poly.PointInPolygon(p, t));

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, MapSize), Ground);
        foreach (var m in _massifs)
        {
            var face = FaceColor(m);
            var faceDark = Palette.Tint(face, m.Model == 1 ? -38 : -34);
            var faceLight = Palette.Tint(face, 16);
            var ink = m.Model == 1 ? Palette.Hex("#7a6749") : Palette.Hex("#555a45");
            var dirt = m.Model == 1;
            var higher = _massifs
                .FindAll(h => h.Height > m.Height)
                .SelectMany(h => h.Tops);

            // The wall hangs off the south side — painted first so the
            // band overlaps its top edge.
            var wall = TerrainGeometry.Subtract(m.Wall, higher);
            foreach (var poly in wall)
                Fill(poly, Palette.Tint(face, dirt ? -10 : -15));

            // Slope band — pre-clipped pieces, punched out of higher tops.
            foreach (var poly in TerrainGeometry.Subtract(m.Band, higher))
                Fill(poly, face);

            // Terrace rings — the sedimentary strata.
            foreach (var (ring, tone) in _strata[m])
            foreach (var poly in TerrainGeometry.Subtract(ring, higher))
                Fill(poly, tone);

            // Toe contact + south shading — the foot of the face sinks.
            DrawPolyline(m.Base, Palette.TintAlpha(face, -20, 0.55), (float)System.Math.Max(8, m.Size * 0.3), true);
            var cy = Poly.Centroid(m.Base).Y;
            foreach (var arc in Poly.Runs(m.Base, p => p.Y > cy))
                if (arc.Count > 1)
                    DrawPolyline(
                        [.. arc],
                        Palette.TintAlpha(face, -28, 0.5),
                        (float)System.Math.Max(5, m.Size * 0.24)
                    );

            // Hairline contour echo.
            foreach (var c in _contours[m])
                DrawPolyline(c, Palette.TintAlpha(face, -16, 0.5), 1.2f, true);

            // Material patches — soft ellipses in shifted tints.
            foreach (var (point, angle, index) in Poly.SampleLine(m.Base, System.Math.Max(30, m.Size * 1.4)))
            {
                var seed = point.X * 0.23 + point.Y * 0.41 + index * 7;
                var roll = Rng.Noise(seed);
                if (roll < 0.6)
                    continue;
                var na = angle + System.Math.PI / 2;
                var probe = point + new Vector2((float)System.Math.Cos(na), (float)System.Math.Sin(na)) * 8;
                if (!InBand(m, probe))
                    na = angle - System.Math.PI / 2;
                var variant =
                    roll < 0.8
                        ? Palette.TintAlpha(face, 10, 0.5)
                        : dirt
                            ? Palette.TintAlpha(Stone, 8, 0.5)
                            : new Color(0.71f, 0.62f, 0.44f, 0.5f);
                Fill(
                    Poly.Ellipse(
                        point.X + System.Math.Cos(na) * m.Size * 0.5,
                        point.Y + System.Math.Sin(na) * m.Size * 0.5,
                        m.Size * (0.4 + Rng.Noise(seed + 1) * 0.45),
                        m.Size * (0.28 + Rng.Noise(seed + 2) * 0.25),
                        na
                    ),
                    variant
                );
            }

            // Cobble mosaic — larger rounded stones with real gaps, the
            // hand-drawn packed-rock look of the reference.
            var (xs, ys) = BBox(m.Base);
            for (var y = ys.X; y < ys.Y; y += 15)
            for (var x = xs.X; x < xs.Y; x += 15)
            {
                var seed = x * 0.37 + y * 0.53;
                if (Rng.Noise(seed) < 0.3)
                    continue;
                var p = new Vector2(
                    (float)(x + (Rng.Noise(seed + 1) - 0.5) * 8),
                    (float)(y + (Rng.Noise(seed + 2) - 0.5) * 8)
                );
                if (!InBand(m, p))
                    continue;
                var r = 4.6 + Rng.Noise(seed + 3) * (dirt ? 2.4 : 4.2);
                var pebble = Poly.Ellipse(
                    p.X,
                    p.Y,
                    r,
                    r * (0.72 + Rng.Noise(seed + 4) * 0.3),
                    (Rng.Noise(seed + 5) - 0.5) * 0.9
                );
                Fill(
                    pebble,
                    Palette.Tint(face, -3 + Rng.Noise(seed + 6) * (12 + m.Roughness / 100 * 12))
                );
                DrawPolyline(pebble, Palette.TintAlpha(face, -24, 0.4), 0.9f, true);
            }

            // Wall interior: AO under the rim + strata arcs + cracks.
            foreach (var top in m.Tops)
            {
                var tcy = Poly.Centroid(top).Y;
                foreach (var arc in Poly.Runs(top, p => p.Y > tcy))
                {
                    if (arc.Count < 2)
                        continue;
                    DrawPolyline([.. arc], new Color(0.086f, 0.078f, 0.047f, 0.32f), 5);
                    foreach (var t in new[] { 0.38, 0.72 })
                    {
                        var dropped = new Vector2[arc.Count];
                        for (var i = 0; i < arc.Count; i++)
                            dropped[i] = arc[i] + m.Face * (float)t;
                        DrawPolyline(
                            dropped,
                            t == 0.38 ? Palette.Tint(face, 14) : Palette.Tint(face, -26),
                            1.7f
                        );
                    }
                }
            }

            // Boulders cluster at the south foot of the face — debris
            // rolls downhill toward the viewer.
            if (!dirt)
            {
                var footY = Poly.Centroid(m.Base).Y;
                foreach (var (point, angle, index) in Poly.SampleLine(m.Base, 46))
                {
                    var seed = point.X * 0.19 + point.Y * 0.43 + index;
                    if (point.Y < footY - m.Size * 0.2)
                        continue;
                    if (Rng.Noise(seed) < 0.62 - m.Roughness / 100 * 0.3)
                        continue;
                    var na = angle + System.Math.PI / 2;
                    foreach (var dir in new[] { 1, -1 })
                    {
                        var p = point
                            + new Vector2((float)System.Math.Cos(na), (float)System.Math.Sin(na))
                            * dir
                            * (float)(6 + Rng.Noise(seed + 2) * 16);
                        if (!Poly.PointInPolygon(p, m.Base))
                        {
                            var r =
                                (4 + Rng.Noise(seed + 4) * (m.Model == 2 ? 8 : 6))
                                * (0.8 + m.Roughness / 100 * 0.4);
                            DrawCircle(
                                p,
                                (float)r,
                                Palette.Tint(face, 4 + Rng.Noise(seed + 6) * 14)
                            );
                            DrawArc(p, (float)r, 0, Mathf.Tau, 12, ink, 1.1f);
                            break;
                        }
                    }
                }
            }

            // Rim lumps + ink + light line.
            if (!dirt)
                foreach (var top in m.Tops)
                    foreach (var (point, index) in Sample(top, 36))
                    {
                        var seed = point.X * 0.23 + point.Y * 0.47 + index * 3;
                        if (Rng.Noise(seed) < 0.9 - m.Roughness / 100 * 0.55)
                            continue;
                        DrawCircle(
                            point
                                + new Vector2(
                                    (float)(Rng.Noise(seed + 1) - 0.5) * 6,
                                    (float)(Rng.Noise(seed + 2) - 0.5) * 6
                                ),
                            (float)(2 + Rng.Noise(seed + 3) * 3.5),
                            Palette.Tint(face, 4 + Rng.Noise(seed + 4) * 12)
                        );
                    }
            foreach (var top in m.Tops)
            {
                DrawPolyline(top, ink, (float)(dirt ? 2.0 : 2.6), true);
                DrawPolyline(top, Palette.Tint(face, 26), 1.6f, true);
            }

            // Tops — turf fill + mottle + speckle + scalloped lip.
            for (var ti = 0; ti < m.Tops.Count; ti++)
            {
                var top = m.Tops[ti];
                var tR = (m.TopRoughness[ti]) / 100;
                var tseed = top[0].X * 0.31 + top[0].Y * 0.47;
                var dry = System.Math.Min(
                    0.18,
                    0.04 + m.Height * 0.0008 + tR * 0.05 + Rng.Noise(tseed) * 0.04
                );
                var lift = 3 + Rng.Noise(tseed + 1) * 5;
                Fill(top, Palette.Tint(Palette.Mix(Ground, Stone, dry), lift));
                var (tx, ty) = BBox(top);
                for (var i = 0; i < 12; i++)
                {
                    var seed = tx.X * 0.13 + ty.X * 0.29 + i * 7.7;
                    Fill(
                        Poly.Ellipse(
                            tx.X + Rng.Noise(seed) * (tx.Y - tx.X),
                            ty.X + Rng.Noise(seed + 1) * (ty.Y - ty.X),
                            26 + Rng.Noise(seed + 2) * 46,
                            16 + Rng.Noise(seed + 3) * 32,
                            Rng.Noise(seed + 4) * System.Math.PI
                        ),
                        Rng.Noise(seed + 5) > 0.5
                            ? Palette.TintAlpha(Ground, 32, 0.16)
                            : Palette.TintAlpha(Ground, -4, 0.16)
                    );
                }
                // Grass speckle.
                for (var y = ty.X; y < ty.Y; y += 15)
                for (var x = tx.X; x < tx.Y; x += 15)
                {
                    var seed = x * 0.41 + y * 0.29;
                    if (Rng.Noise(seed) < 0.62)
                        continue;
                    var p = new Vector2(
                        (float)(x + (Rng.Noise(seed + 1) - 0.5) * 9),
                        (float)(y + (Rng.Noise(seed + 2) - 0.5) * 9)
                    );
                    if (!Poly.PointInPolygon(p, top))
                        continue;
                    Fill(
                        Poly.Ellipse(
                            p.X,
                            p.Y,
                            1.2 + Rng.Noise(seed + 3) * 2.2,
                            0.8 + Rng.Noise(seed + 4) * 1.6,
                            Rng.Noise(seed + 5) * System.Math.PI,
                            6
                        ),
                        Rng.Noise(seed + 6) > 0.5
                            ? Palette.TintAlpha(Ground, 36, 0.09)
                            : Palette.TintAlpha(Ground, -12, 0.09)
                    );
                }
                // Scalloped turf edge — darker arcs opening toward the
                // plateau's center, just inside the lip.
                var tcen = Poly.Centroid(top);
                foreach (var (point, index) in Sample(top, 14))
                {
                    var seed = point.X * 0.31 + point.Y * 0.37 + index;
                    var rr = 4 + Rng.Noise(seed) * 5;
                    var inward = Mathf.Atan2(tcen.Y - point.Y, tcen.X - point.X);
                    DrawArc(
                        point,
                        (float)rr,
                        inward - 1.5f,
                        inward + 1.5f,
                        8,
                        Palette.TintAlpha(Ground, -10, 0.22),
                        2.5f
                    );
                }
            }
        }
    }

    static IEnumerable<(Vector2 Point, int Index)> Sample(
        IReadOnlyList<Vector2> poly,
        double step
    )
    {
        var index = 0;
        foreach (var (p, _, _) in Poly.SampleLine(poly, step))
            yield return (p, index++);
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
