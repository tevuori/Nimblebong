// Fog-of-war mask painter — renders into FogVp as a grayscale mask:
// black = shrouded, white = revealed. Strokes apply in scene order
// ("clear" paints white, "shroud" paints black back over). The same
// texture drives the editor's dark overlay and the bridge's fog.png
// (alpha = 255 - mask).
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class FogMask : Node2D
{
    public Vector2 MapSize;
    public List<Stroke> Strokes = [];

    public override void _Draw()
    {
        // Start fully shrouded.
        DrawRect(new Rect2(Vector2.Zero, MapSize), Colors.Black);
        foreach (var s in Strokes)
        {
            var col = s.Kind == "clear" ? Colors.White : Colors.Black;
            var w = (float)s.Width;
            for (var i = 0; i + 1 < s.Points.Count; i++)
                DrawLine(s.Points[i], s.Points[i + 1], col, w);
            foreach (var p in s.Points)
                DrawCircle(p, w / 2, col);
        }
    }
}
