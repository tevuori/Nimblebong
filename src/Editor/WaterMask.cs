// Water coverage mask — white fills where the scene has water
// (unioned river ribbons + pond shapes) so the lighting shader can
// confine its shimmer to actual water pixels.
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class WaterMask : Node2D
{
    public Vector2 MapSize;

    List<(Vector2[] Run, double Width)> _rivers = [];
    List<Vector2[]> _ponds = [];

    public void SetWater(Scene scene)
    {
        _rivers = scene
            .Strokes.Where(s => s.Kind == Kinds.River)
            .Select(s => (Spline.Resample(s.Points, 14), s.Width))
            .ToList();
        _ponds = scene
            .Shapes.Where(s => s.Kind == Kinds.Pond)
            .Select(s => s.Points.ToArray())
            .ToList();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var white = Colors.White;
        foreach (var piece in WaterPainter.UnionAll(_rivers, 0))
            DrawColoredPolygon(piece, white);
        foreach (var pond in _ponds)
            if (pond.Length >= 3 && Geometry2D.TriangulatePolygon(pond).Length > 0)
                DrawColoredPolygon(pond, white);
    }
}
