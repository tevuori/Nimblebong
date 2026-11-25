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
    /// <summary>Strength of the hand-painted grass tile under the ground
    /// speckle — biome ground.grassTile (0 = off: sand, ash, snow, cave).</summary>
    public double GrassTile;
    /// <summary>Winter look suppresses the grass tile (web parity).</summary>
    public bool Winter;
    /// <summary>Resolved biome for the ground pass — fbm field blotches
    /// plus the planned detail scatter (patches/tufts/pebbles/flowers).
    /// Null → legacy flat fill (probes that don't set a biome).</summary>
    ResolvedBiome _biome;
    public ResolvedBiome Biome
    {
        get => _biome;
        set
        {
            _biome = value;
            _fieldTex = null;
            QueueRedraw();
        }
    }
    /// <summary>Cached fbm field texture (map-sized watercolor blotches).</summary>
    ImageTexture _fieldTex;

    public override void _Ready() => TextureRepeat = TextureRepeatEnum.Enabled;

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
        // Nimblehold cliff faces are continuous painted stone, not
        // contour terraces: keep the structure cache but no opaque rings.
        (double f, double tone)[] steps = [];
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
        // No mechanical contour echo — cracks/cobble carry the face.
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

    /// <summary>Three-direction jittered line grid clipped to a stone
    /// face piece — reads as the packed polygonal-slab crack pattern of
    /// the 2mt ink pack. Deterministic via position-hashed wobble.</summary>
    void CrackMesh(Vector2[] poly, Color face, float alpha = 0.55f)
    {
        var (xs, ys) = BBox(poly);
        var ink = Palette.TintAlpha(face, -40, alpha);
        const double spacing = 26;
        foreach (var deg in new[] { 10.0, 70.0, -50.0 })
        {
            var rad = Mathf.DegToRad(deg);
            var dir = new Vector2(
                (float)System.Math.Cos(rad),
                (float)System.Math.Sin(rad)
            );
            var n = new Vector2(-dir.Y, dir.X);
            // Walk lines across the bbox in the normal direction.
            var corners = new[]
            {
                new Vector2((float)xs.X, (float)ys.X),
                new Vector2((float)xs.Y, (float)ys.X),
                new Vector2((float)xs.X, (float)ys.Y),
                new Vector2((float)xs.Y, (float)ys.Y),
            };
            double lo = double.MaxValue,
                hi = double.MinValue,
                alo = double.MaxValue,
                ahi = double.MinValue;
            foreach (var c in corners)
            {
                lo = System.Math.Min(lo, c.Dot(n));
                hi = System.Math.Max(hi, c.Dot(n));
                alo = System.Math.Min(alo, c.Dot(dir));
                ahi = System.Math.Max(ahi, c.Dot(dir));
            }
            for (var d = lo; d <= hi; d += spacing)
            {
                var pts = new List<Vector2>();
                for (var a = alo - 8; a <= ahi + 8; a += 14)
                {
                    var wob =
                        (Rng.Noise(a * 0.23 + d * 0.41 + deg) - 0.5) * spacing * 0.5;
                    pts.Add(dir * (float)a + n * (float)(d + wob));
                }
                Vector2[] line = [.. pts];
                foreach (
                    var seg in Geometry2D.ClipPolylineWithPolygon(line, (Vector2[])poly)
                )
                    if (seg.Length > 1)
                        DrawPolyline(seg, ink, 1.2f, true);
            }
        }
    }

    /// <summary>Fill a polygon only when it triangulates — clipper
    /// output can contain degenerate slivers the canvas renderer
    /// rejects.</summary>
    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
        else if (System.Environment.GetEnvironmentVariable("DMMAP_DEBUG_GEO") != null)
            GD.PrintErr($"Albedo fill reject pts={poly.Length} area={TerrainGeometry.SignedArea(poly):0.0}");
    }

    static bool InBand(Massif m, Vector2 p) =>
        Poly.PointInPolygon(p, m.Base) && !m.Tops.Exists(t => Poly.PointInPolygon(p, t));

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, MapSize), Ground);
        // Watercolor ground fields — low-res fbm blotches upscaled over
        // the whole map (the mottled turf of the references), then the
        // planned detail scatter. Under everything, like the web pass.
        if (Biome is { } biome)
        {
            _fieldTex ??= GroundPass.FieldTexture(MapSize, GroundPass.BuildPalette(biome, biome.SeasonLook), 7);
            DrawTextureRect(_fieldTex, new Rect2(Vector2.Zero, MapSize), false);
            GroundPass.Features(this, MapSize, biome, GroundPass.BuildPalette(biome, biome.SeasonLook));
        }
        // Hand-painted grass tile folded into the turf (web hybrid
        // pipeline) — faint source-over under the speckle, off in winter.
        if (GrassTile > 0 && !Winter)
            DrawTextureRect(
                TextureTiles.Grass,
                new Rect2(Vector2.Zero, MapSize),
                true,
                new Color(1, 1, 1, (float)GrassTile)
            );
        foreach (var m in _massifs)
        {
            if (System.Environment.GetEnvironmentVariable("DMMAP_DEBUG_GEO") != null)
            {
                var areas = string.Join(",", m.Tops.ConvertAll(t => TerrainGeometry.SignedArea(t).ToString("0")));
                GD.PrintErr($"massif model={m.Model} h={m.Height} tops={m.Tops.Count} area={areas} band={m.Band.Count} wall={m.Wall.Count}");
            }
            var face = FaceColor(m);
            var faceDark = Palette.Tint(face, m.Model == 1 ? -38 : -34);
            var faceLight = Palette.Tint(face, 16);
            var ink = m.Model == 1 ? Palette.Hex("#7a6749") : Palette.Hex("#514734");
            var dirt = m.Model == 1;
            var higher = _massifs
                .FindAll(h => h.Height > m.Height)
                .SelectMany(h => h.Tops);

            // The wall hangs off the south side — painted first so the
            // band overlaps its top edge.
            var wall = TerrainGeometry.Subtract(m.Wall, higher);
            foreach (var poly in wall)
            {
                Fill(poly, Palette.Tint(face, dirt ? -10 : -28));
                // Strata texture on the hanging face — the reference's
                // striped sedimentary cliff wall.
                if (!dirt)
                    TextureTiles.Stamp(this, poly, TextureTiles.Strata, 0.35f, 0.6);
            }

            // Slope band — pre-clipped pieces, punched out of higher tops.
            var bandPieces = TerrainGeometry.Subtract(m.Band, higher);
            foreach (var poly in bandPieces)
            {
                Fill(poly, face);
                // Hand-painted stone tile folded into the rocky face —
                // the ink pack leaves the slab face clean so the crack
                // mesh carries the texture.
                if (!dirt && Biome?.ArtPack != "ink")
                {
                    TextureTiles.Stamp(this, poly, TextureTiles.Stone, 0.18f, 1.42);
                    // The cliff-face masonry tile is deliberately not
                    // stretched across broad slopes: at map scale it reads
                    // as mechanical brick courses instead of natural rock.
                }
            }
            // Crack mesh is the ink pack's 2mt slab signature. Classic
            // faces stay painterly: cobble, strata arcs and rim scree
            // carry the texture without a geometric wireframe overlay.
            if (!dirt && Biome?.ArtPack == "ink")
            foreach (var poly in bandPieces)
                CrackMesh(poly, face, 0.78f);

            // Terrace rings — the sedimentary strata.
            foreach (var (ring, tone) in _strata[m])
            foreach (var poly in TerrainGeometry.Subtract(ring, higher))
                Fill(poly, tone);

            // Toe contact + south shading — the foot of the face sinks.
            DrawPolyline(m.Base, Palette.TintAlpha(face, -24, 0.68), (float)System.Math.Max(8, m.Size * 0.3), true);
            var cy = Poly.Centroid(m.Base).Y;
            foreach (var arc in Poly.Runs(m.Base, p => p.Y > cy))
                if (arc.Count > 1)
                    DrawPolyline(
                        [.. arc],
                        Palette.TintAlpha(face, -28, 0.5),
                        (float)System.Math.Max(5, m.Size * 0.24)
                    );


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
            for (var y = ys.X; y < ys.Y; y += 22)
            for (var x = xs.X; x < xs.Y; x += 22)
            {
                var seed = x * 0.37 + y * 0.53;
                if (Rng.Noise(seed) < 0.68)
                    continue;
                var p = new Vector2(
                    (float)(x + (Rng.Noise(seed + 1) - 0.5) * 9),
                    (float)(y + (Rng.Noise(seed + 2) - 0.5) * 9)
                );
                if (!InBand(m, p))
                    continue;
                var r = 4.2 + Rng.Noise(seed + 3) * (dirt ? 2.8 : 4.6);
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
                DrawPolyline(pebble, Palette.TintAlpha(face, -28, 0.7), 1.1f, true);
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
                DrawPolyline(top, Palette.Hex("#2e261a") with { A = 0.9f }, (float)(dirt ? 2.2 : 3.1), true);
                DrawPolyline(top, Palette.Tint(face, 22) with { A = 0.55f }, 1.1f, true);
            }

            // Tops — turf fill + mottle + speckle + scalloped lip.
            for (var ti = 0; ti < m.Tops.Count; ti++)
            {
                var top = m.Tops[ti];
                var tR = (m.TopRoughness[ti]) / 100;
                var tseed = top[0].X * 0.31 + top[0].Y * 0.47;
                // Terrain model 2 = barren rock plateau — canyon rims
                // like the Nimblehold grey-table tops, not turf.
                var barren = m.Model == 2;
                var lift = 3 + Rng.Noise(tseed + 1) * 5;
                Fill(
                    top,
                    barren
                        ? Palette.Tint(
                            Palette.Mix(Stone, Cliff, 0.5 + Rng.Noise(tseed) * 0.25),
                            lift + 4
                        )
                        : Palette.Tint(
                            Palette.Mix(
                                Ground,
                                Stone,
                                System.Math.Min(
                                    0.11,
                                    0.03 + m.Height * 0.0005 + tR * 0.03
                                        + Rng.Noise(tseed) * 0.03
                                )
                            ),
                            lift
                        )
                );
                if (barren)
                    TextureTiles.Stamp(this, top, TextureTiles.Stone, 0.14f, 1.4);
                else if (GrassTile > 0 && !Winter)
                    TextureTiles.Stamp(this, top, TextureTiles.Grass, 0.3f);
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

                // ── Roughness décor on the plateau ──
                // Eroded step rings — nested stone contours shrinking
                // inward, the erosional terraces of the web version.
                if (tR > 0.55)
                {
                    var rings = tR > 0.82 ? 2 : 1;
                    for (var r = 1; r <= rings; r++)
                    {
                        var depth = m.Size * (0.35 + r * 0.5) * tR;
                        foreach (
                            var eroded in TerrainGeometry.Offset(top, -depth)
                        )
                            DrawPolyline(
                                eroded,
                                Palette.TintAlpha(Cliff, -12, 0.2),
                                1.1f,
                                true
                            );
                    }
                }
                // Rocky outcrops scattered on rough plateaus.
                if (tR > 0.25)
                {
                    var (ox, oy) = BBox(top);
                    for (var i = 0; i < tR * 10; i++)
                    {
                        var seed = ox.X * 0.17 + oy.X * 0.23 + i * 13.7;
                        if (Rng.Noise(seed) < 0.45)
                            continue;
                        var p = new Vector2(
                            (float)(ox.X + Rng.Noise(seed + 1) * (ox.Y - ox.X)),
                            (float)(oy.X + Rng.Noise(seed + 2) * (oy.Y - oy.X))
                        );
                        if (!Poly.PointInPolygon(p, top))
                            continue;
                        var r = 2.5 + Rng.Noise(seed + 3) * 5;
                        var lump = Poly.Ellipse(
                            p.X,
                            p.Y,
                            r,
                            r * 0.8,
                            Rng.Noise(seed + 4) * System.Math.PI,
                            7
                        );
                        Fill(lump, Palette.Tint(Cliff, -6 + Rng.Noise(seed + 5) * 10));
                        DrawPolyline(lump, Palette.TintAlpha(Cliff, -24, 0.4), 0.8f, true);
                    }
                }
                // Summit crag — a jagged light/shadow split rock at the
                // very top of the roughest massifs.
                if (tR > 0.8)
                {
                    var cragSeed = top[0].X * 0.11 + top[0].Y * 0.19;
                    var cpts = new Vector2[9];
                    for (var i = 0; i < 9; i++)
                    {
                        var a = (i / 9.0) * Mathf.Tau;
                        var rr = 12 + Rng.Noise(cragSeed + i * 7) * 16;
                        cpts[i] = tcen + new Vector2(
                            (float)(System.Math.Cos(a) * rr),
                            (float)(System.Math.Sin(a) * rr * 0.7)
                        );
                    }
                    Fill(cpts, Palette.Tint(Cliff, 10));
                    // Shadow split — the SE half sits in shade.
                    var dark = cpts
                        .Where(p => p.Y > tcen.Y)
                        .Append(tcen)
                        .ToArray();
                    if (dark.Length >= 3)
                        Fill(dark, Palette.TintAlpha(Cliff, -18, 0.5));
                    DrawPolyline(cpts, ink, 1.6f, true);
                }
            }

            // Long ink fissures following the rim tangent — the hand-drawn
            // contour cracks that make the pale cliff masses read as rock
            // rather than paved paths. Sparse and seeded, never a regular
            // contour mesh.
            if (!dirt)
            foreach (var top in m.Tops)
            {
                var samples = Poly.SampleLine(top, System.Math.Max(70, m.Size * 2.2)).ToArray();
                foreach (var (cp, angle, index) in samples)
                {
                    var seed = cp.X * 0.19 + cp.Y * 0.43 + index * 5.7;
                    if (Rng.Noise(seed) < 0.48)
                        continue;
                    var tangent = new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle));
                    var inward = new Vector2(-tangent.Y, tangent.X);
                    var len = 34 + Rng.Noise(seed + 1) * 58;
                    var pts = new System.Collections.Generic.List<Vector2>();
                    for (var k = 0; k <= 5; k++)
                    {
                        var t = k / 5.0 - 0.5;
                        var q = cp + tangent * (float)(t * len)
                            + inward * (float)((Rng.Noise(seed + 2 + k) - 0.5) * 9 + 4);
                        pts.Add(q);
                    }
                    DrawPolyline([.. pts], Palette.Hex("#3d3424") with { A = 0.62f }, 1.8f, true);
                    // Occasional short branch, like a rootlet splitting off
                    // the main fissure.
                    if (Rng.Noise(seed + 9) > 0.45)
                    {
                        var bp = pts[2];
                        DrawPolyline(
                            [bp, bp + tangent * 10 + inward * (float)(8 + Rng.Noise(seed + 10) * 10)],
                            Palette.Hex("#3d3424") with { A = 0.5f },
                            1.2f,
                            true
                        );
                    }
                }
            }

            // Rim scree — jumbled pebbles straddling the lip break the
            // smooth contour into the reference's rocky crumble edge.
            if (!dirt)
            foreach (var top in m.Tops)
            {
                var tcen2 = Poly.Centroid(top);
                foreach (var (point, index) in Sample(top, 11))
                {
                    var seed = point.X * 0.29 + point.Y * 0.41 + index * 1.7;
                    if (Rng.Noise(seed) < 0.38)
                        continue;
                    var inAng = Mathf.Atan2(tcen2.Y - point.Y, tcen2.X - point.X);
                    var dir2 = new Vector2(
                        (float)System.Math.Cos(inAng),
                        (float)System.Math.Sin(inAng)
                    );
                    var c = point + dir2 * (float)((Rng.Noise(seed + 1) - 0.58) * 15);
                    var count = Rng.Noise(seed + 6) < 0.4 ? 2 : 1;
                    for (var k = 0; k < count; k++)
                    {
                        var cc = c
                            + new Vector2(
                                (float)(Rng.Noise(seed + 7 + k) - 0.5) * 8,
                                (float)(Rng.Noise(seed + 8 + k) - 0.5) * 8
                            );
                        var r = 2.6 + Rng.Noise(seed + 2 + k) * 6.5;
                        var lump = Poly.Ellipse(
                            cc.X,
                            cc.Y,
                            r,
                            r * (0.65 + Rng.Noise(seed + 3 + k) * 0.35),
                            Rng.Noise(seed + 4 + k) * System.Math.PI,
                            7
                        );
                        Fill(lump, Palette.Tint(face, -10 + Rng.Noise(seed + 5 + k) * 24));
                        DrawPolyline(lump, Palette.TintAlpha(face, -30, 0.55), 1f, true);
                    }
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
