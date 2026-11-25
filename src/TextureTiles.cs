// Hand-painted texture tiles — the web hybrid pipeline
// (scene-textures.ts + textures.json): PNG tiles stamped clipped inside
// map polygons, procedural ink/shadow layers on top. Tiles live in
// assets/textures/ and repeat by UV (the painting CanvasItem needs
// TextureRepeat enabled).
using Godot;

namespace DmMap;

public static class TextureTiles
{
    static Texture2D? _grass, _stone, _water;

    public static Texture2D Grass =>
        _grass ??= GD.Load<Texture2D>("res://assets/textures/ground-grass.png");

    public static Texture2D Stone =>
        _stone ??= GD.Load<Texture2D>("res://assets/textures/plateau-stone.png");

    public static Texture2D Water =>
        _water ??= GD.Load<Texture2D>("res://assets/textures/water-surface.png");

    /// <summary>Stamp a repeating tile clipped inside `poly`. UVs come from
    /// (world position − offset) / tile size / scale, so the pattern is
    /// anchored to the map, not the shape.</summary>
    public static void Stamp(
        CanvasItem ci,
        Vector2[] poly,
        Texture2D tex,
        float alpha,
        double scale = 1,
        Vector2 offset = default
    )
    {
        if (poly.Length < 3 || Geometry2D.TriangulatePolygon(poly).Length == 0)
            return;
        var sz = tex.GetSize() * (float)scale;
        var uvs = new Vector2[poly.Length];
        var colors = new Color[poly.Length];
        for (var i = 0; i < poly.Length; i++)
        {
            uvs[i] = (poly[i] - offset) / sz;
            colors[i] = new Color(1, 1, 1, alpha);
        }
        ci.DrawPolygon(poly, colors, uvs, tex);
    }
}
