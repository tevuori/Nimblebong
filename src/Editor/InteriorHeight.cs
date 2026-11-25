// Building elevation pass — house/room footprints bump the heightmap
// by ~0.25·height/100 so the lighting shader shades their extrusion
// and throws a contact shadow south-east. The bump base samples the
// terrain under the footprint like PropsHeight, so a house on a
// plateau rises above it instead of cutting a pit. Floors do nothing.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class InteriorHeight : Node2D
{
    readonly List<(Vector2[] Pts, double Height)> _buildings = [];

    public void Add(Shape s)
    {
        if (s.Points.Count >= 3 && s.Kind is Kinds.House or Kinds.Room)
            _buildings.Add(([.. s.Points], s.Height));
        QueueRedraw();
    }

    // Terrain height sampler — terrace rings cached like the heightmap
    // ramp so the bump sits on the local ground level.
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

    static Color Gray(double v) => new((float)v, (float)v, (float)v);

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    public override void _Draw()
    {
        foreach (var (pts, height) in _buildings)
        {
            var h = height > 0 ? height : 18.0;
            var base_ = TerrainAt(Poly.Centroid(pts));
            Fill(pts, Gray(Math.Min(0.97, base_ + 0.25 * (h / 100))));
        }
    }
}
