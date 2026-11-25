#nullable enable
// Per-prop style scope for JSON prop definitions (PropDefs). While a
// prop with a Def draws, the painter's palette slots are swapped for the
// definition's colors and every color reaching the canvas goes through
// Restyle (tint mix + HSV adjust). The Draw* methods below hide
// CanvasItem's so all prop drawers — written long before definitions
// existed — get restyled without touching their code. Built-in props
// (Def == null) pass through untouched.
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    PropDef? _style;
    Color _tint;
    (Color, Color, Color, Color, Color, Color, Color, Color, Color) _saved;

    void BeginStyle(PropPlacement p)
    {
        _style = p.Def;
        if (_style == null)
            return;
        _saved = (Foliage, FoliageWarm, FoliageCool, PineGreen, Stone, Wood, Wall, Roof, Ink);
        foreach (var (slot, spec) in _style.Palette)
        {
            var c = PropDefs.ResolveColor(spec, BiomeCfg);
            switch (slot)
            {
                case "foliage": Foliage = c; break;
                case "foliageWarm": FoliageWarm = c; break;
                case "foliageCool": FoliageCool = c; break;
                case "pine": PineGreen = c; break;
                case "stone": Stone = c; break;
                case "wood": Wood = c; break;
                case "wall": Wall = c; break;
                case "roof": Roof = c; break;
                case "ink": Ink = c; break;
            }
        }
        _tint = _style.TintColor != null
            ? PropDefs.ResolveColor(_style.TintColor, BiomeCfg)
            : Colors.White;
    }

    void EndStyle(PropPlacement p)
    {
        if (p.Def != null)
            (Foliage, FoliageWarm, FoliageCool, PineGreen, Stone, Wood, Wall, Roof, Ink) = _saved;
        _style = null;
    }

    /// <summary>Apply the active definition's tint + HSV adjust; alpha kept.</summary>
    Color Restyle(Color c)
    {
        var s = _style;
        if (s == null || !s.Restyles)
            return c;
        var a = c.A;
        if (s.TintColor != null && s.TintAmount > 0)
            c = Palette.Mix(c, _tint, s.TintAmount);
        if (s.Hue != 0 || s.Saturation != 1 || s.Value != 1)
        {
            var h = Mathf.PosMod(c.H + (float)s.Hue, 1f);
            c = Color.FromHsv(
                h,
                Mathf.Clamp(c.S * (float)s.Saturation, 0f, 1f),
                Mathf.Clamp(c.V * (float)s.Value, 0f, 1f)
            );
        }
        return c with { A = a };
    }

    // --- restyled CanvasItem draw calls --------------------------------------

    public new void DrawColoredPolygon(
        Vector2[] points, Color color, Vector2[]? uvs = null, Texture2D? texture = null
    ) => base.DrawColoredPolygon(points, Restyle(color), uvs, texture);

    public new void DrawPolyline(
        Vector2[] points, Color color, float width = -1f, bool antialiased = false
    ) => base.DrawPolyline(points, Restyle(color), width, antialiased);

    public new void DrawLine(
        Vector2 from, Vector2 to, Color color, float width = -1f, bool antialiased = false
    ) => base.DrawLine(from, to, Restyle(color), width, antialiased);

    public new void DrawCircle(
        Vector2 position, float radius, Color color, bool filled = true,
        float width = -1f, bool antialiased = false
    ) => base.DrawCircle(position, radius, Restyle(color), filled, width, antialiased);

    public new void DrawArc(
        Vector2 center, float radius, float startAngle, float endAngle, int pointCount,
        Color color, float width = -1f, bool antialiased = false
    ) => base.DrawArc(center, radius, startAngle, endAngle, pointCount, Restyle(color), width, antialiased);
}
