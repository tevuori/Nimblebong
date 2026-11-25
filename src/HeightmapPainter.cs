// Elevation raster for the GPU lighting pass — grayscale heightmap
// where ground sits at Baseline, massifs ramp up as nested offset fills
// (smooth toe→rim pyramid). The lighting shader raymarches this.
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class HeightmapPainter : Node2D
{
    /// <summary>Ground level in the heightmap.</summary>
    public const float Baseline = 0.16f;
    public Vector2 MapSize = new(1600, 1000);

    List<Massif> _massifs = [];
    readonly Dictionary<Massif, List<(Vector2[] Poly, float Level)>> _ramps = new();

    public void SetMassifs(List<Massif> massifs)
    {
        _massifs = massifs;
        _ramps.Clear();
        foreach (var m in massifs)
        {
            var h01 = Baseline + Mathf.Clamp((float)m.Height / 100f, 0f, 1f) * 0.72f;
            var steps = new List<(Vector2[], float)>();
            // Nested filled offsets — many fine rings approximate a
            // continuous toe→rim ramp. Five coarse steps made every slope
            // read as contour-map terraces once the lighting shader took
            // the height gradient.
            for (var i = 0; i < 14; i++)
            {
                var f = 0.94 * (1 - i / 14.0);
                var level = Baseline + (h01 - Baseline) * (float)(1 - f);
                foreach (var top in m.Tops)
                foreach (var poly in TerrainGeometry.Offset(top, f * m.Size))
                    steps.Add((poly, level));
            }
            foreach (var top in m.Tops)
                steps.Add((top, h01));
            _ramps[m] = steps;
        }
        QueueRedraw();
    }

    static Color Gray(double v) => new((float)v, (float)v, (float)v);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, MapSize), Gray(Baseline));
        foreach (var m in _massifs)
        {
            Fill(m.Base, Gray(Baseline));
            foreach (var (poly, level) in _ramps[m])
                Fill(poly, Gray(level));
        }
    }

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }
}
