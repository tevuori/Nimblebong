// Pond elevation pass — carves the pond polygon below the heightmap
// baseline so the lighting shader renders a depression: a shallow
// bank cut ringed around the outline, a deep basin inside. Same
// levels as WaterHeight so ponds sit like river pools.
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class AreaHeight : Node2D
{
    readonly List<Vector2[]> _ponds = [];

    public void AddPond(IReadOnlyList<Vector2> points)
    {
        if (points.Count >= 3)
            _ponds.Add(points is Vector2[] a ? a : [.. points]);
    }

    static Color Gray(double v) => new((float)v, (float)v, (float)v);

    void Fill(Vector2[] poly, Color color)
    {
        if (poly.Length >= 3 && Geometry2D.TriangulatePolygon(poly).Length > 0)
            DrawColoredPolygon(poly, color);
    }

    public override void _Draw()
    {
        foreach (var pts in _ponds)
        {
            // Bank slope: shallow cut on the rim, deep basin inside.
            foreach (var rim in TerrainGeometry.Offset(pts, 4))
                Fill(rim, Gray(WaterHeight.BankCut));
            foreach (var bowl in TerrainGeometry.Offset(pts, -10))
                Fill(bowl, Gray(WaterHeight.Channel));
        }
    }
}
