// River channel raster for the GPU lighting pass — fills river ribbons
// BELOW HeightmapPainter.Baseline so the lighting shader raymarches a
// carved valley: a shallow bank cut under the verge and a deeper
// channel under the water surface. Added to HeightVp AFTER
// HeightmapPainter so it digs through massif ramps.
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class WaterHeight : Node2D
{
    /// <summary>Bank cut level — just under the ground baseline.</summary>
    public const float BankCut = 0.115f;
    /// <summary>Channel bed — the deep carved level.</summary>
    public const float Channel = 0.05f;

    readonly List<(Vector2[] Run, double Width)> _rivers = [];

    public void AddRiver(IReadOnlyList<Vector2> control, double width)
    {
        _rivers.Add((Spline.Ink(Spline.Resample(control)), width));
        QueueRedraw();
    }

    static Color Gray(double v) => new((float)v, (float)v, (float)v);

    public override void _Draw()
    {
        foreach (var (run, w) in _rivers)
        {
            // Wide shallow cut where the banks slope in, deep channel
            // under the water surface itself — polylines can't fail
            // triangulation the way ribbon polygons can.
            DrawPolyline(run, Gray(BankCut), (float)(w + 12), true);
            DrawPolyline(run, Gray(Channel), (float)w, true);
        }
    }
}
