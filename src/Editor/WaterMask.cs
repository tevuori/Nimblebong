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
        // Polylines — same anti-triangulation reasoning as the albedo
        // water: a unioned ribbon polygon can defeat the triangulator.
        foreach (var (run, w) in _rivers)
            DrawPolyline(run, white, (float)w, true);
        foreach (var pond in _ponds)
            Fill(this, pond, white);
    }

    /// <summary>Same degenerate-sliver gate as the albedo painters —
    /// clipper output can defeat the triangulator.</summary>
    static void Fill(CanvasItem n, Vector2[] poly, Color color)
    {
        if (poly.Length < 3)
            return;
        if (Geometry2D.TriangulatePolygon(poly).Length > 0)
        {
            n.DrawColoredPolygon(poly, color);
            return;
        }
        var simple = Interactions.SimplifyPath(poly, 1.2);
        if (simple.Count >= 3 && Geometry2D.TriangulatePolygon([.. simple]).Length > 0)
            n.DrawColoredPolygon([.. simple], color);
    }
}
