// Props elevation pass — paints into the same height viewport as
// HeightmapPainter (added after it, so bumps sit on top). Each prop is
// 2–3 concentric shrinking ellipses at increasing gray: a soft bump
// the lighting shader turns into diffuse shading + a raymarched shadow
// thrown southeast of the object. The bump's base level samples the
// terrain height at the prop position, so trees on a plateau top rise
// above the plateau instead of cutting a pit into it.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class PropsHeight : Node2D
{
    List<PropPlacement> _props = [];

    // Gray contribution above the local terrain base per kind —
    // house tallest, bush barely a bump (× uScale=60 → world relief px).
    static readonly Dictionary<PropKind, (double R, double H)> Sizes = new()
    {
        { PropKind.Tree, (26, 0.11) },
        { PropKind.Pine, (24, 0.13) },
        { PropKind.Rock, (15, 0.06) },
        { PropKind.Bush, (11, 0.045) },
        { PropKind.House, (36, 0.13) },
        { PropKind.Tent, (20, 0.07) },
        { PropKind.Campfire, (9, 0.02) },
        { PropKind.Well, (12, 0.06) },
        { PropKind.Ruin, (20, 0.07) },
        { PropKind.Grave, (9, 0.035) },
        { PropKind.Bones, (10, 0.02) },
        { PropKind.Statue, (10, 0.12) },
        { PropKind.StoneCircle, (22, 0.08) },
        { PropKind.Crystal, (10, 0.08) },
        { PropKind.Bale, (10, 0.045) },
        { PropKind.Crate, (9, 0.05) },
        { PropKind.Barrel, (8, 0.05) },
        { PropKind.Tower, (20, 0.16) },
        { PropKind.Stall, (22, 0.07) },
        { PropKind.Cart, (16, 0.05) },
        { PropKind.Flag, (4, 0.1) },
        { PropKind.Barricade, (16, 0.04) },
        { PropKind.Lantern, (4, 0.08) },
        { PropKind.Fountain, (14, 0.05) },
        { PropKind.Pillar, (8, 0.11) },
        { PropKind.Marker, (8, 0.02) },
        { PropKind.Dock, (18, 0.02) },
        { PropKind.Boat, (14, 0.03) },
        { PropKind.Chest, (10, 0.04) },
        { PropKind.Portal, (12, 0.09) },
        { PropKind.Door, (7, 0.03) },
        { PropKind.Window, (6, 0.02) },
        { PropKind.Table, (13, 0.035) },
        { PropKind.Bed, (14, 0.03) },
        { PropKind.Shelf, (12, 0.05) },
        { PropKind.Rug, (12, 0.008) },
        { PropKind.Fireplace, (13, 0.06) },
        // Flying critters — small body, low bump: a taller one throws
        // a long raymarched streak that reads as a scratch, not a bird.
        { PropKind.Butterfly, (3, 0.02) },
        { PropKind.Bird, (4, 0.04) },
        { PropKind.Flora, (8, 0.012) },
    };

    // Terrain height sampler — terrace rings cached like the heightmap
    // ramp so HeightAt() is just point-in-polygon lookups.
    readonly List<(Vector2[] Poly, float Level)> _terrain = new();

    public void SetMassifs(List<Massif> massifs)
    {
        _terrain.Clear();
        foreach (var m in massifs)
        {
            var h01 =
                HeightmapPainter.Baseline
                + Mathf.Clamp((float)m.Height / 100f, 0f, 1f) * 0.72f;
            foreach (var top in m.Tops)
            {
                _terrain.Add((top, h01));
                foreach (var f in new[] { 0.18, 0.36, 0.54, 0.72, 0.9 })
                {
                    var level =
                        HeightmapPainter.Baseline
                        + (h01 - HeightmapPainter.Baseline) * (float)(1 - f);
                    foreach (var ring in TerrainGeometry.Offset(top, f * m.Size))
                        _terrain.Add((ring, level));
                }
            }
        }
    }

    /// <summary>Highest terrace level under `p`, else ground baseline.</summary>
    float TerrainAt(Vector2 p)
    {
        var level = HeightmapPainter.Baseline;
        foreach (var (poly, l) in _terrain)
            if (l > level && Poly.PointInPolygon(p, poly))
                level = l;
        return level;
    }

    public void SetProps(IEnumerable<PropPlacement> props)
    {
        _props = [.. props];
        QueueRedraw();
    }

    static Color Gray(double v) => new((float)v, (float)v, (float)v);

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    public override void _Draw()
    {
        foreach (var p in _props)
        {
            if (!Sizes.TryGetValue(p.Kind, out var dim))
                continue;
            var (r, h) = dim;
            if (p.Def != null)
                (r, h) = (r * p.Def.Footprint, h * p.Def.Height);
            // Flat props (lily pads, rugs of moss) — no bump at all, or
            // the baseline disc pokes out of carved water and casts shadow.
            if (h <= 0)
                continue;
            var base_ = TerrainAt(p.Pos);
            var top = Math.Min(0.97, base_ + h);
            // Houses are elongated; everything else is near-round. The
            // canopy blob is a bit smaller than its albedo footprint so
            // the bump edge hides under the ink outline.
            var rx = r * p.Scale * (p.Kind == PropKind.House ? 0.95 : 0.8);
            var ry =
                r * p.Scale
                * (p.Kind == PropKind.House ? 0.62 : p.Kind == PropKind.Rock ? 0.7 : 0.8);
            // 1.0 → 0.68 → 0.38 radii at ⅓ → ⅔ → full height: a soft
            // stepped bump whose ring edges stay gentle under lighting.
            var steps = new[] { 1.0, 0.68, 0.38 };
            for (var i = 0; i < steps.Length; i++)
            {
                var level = base_ + (top - base_) * (i + 1) / steps.Length;
                Fill(
                    Poly.Ellipse(p.X, p.Y, rx * steps[i], ry * steps[i], p.Rotation, 12),
                    Gray(level)
                );
            }
        }
    }
}
