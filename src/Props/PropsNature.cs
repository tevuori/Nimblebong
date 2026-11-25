// Prop painters — nature & wilderness batch. Owns DrawTent,
// DrawCampfire, DrawWell, DrawRuin, DrawGrave, DrawBones, DrawStatue,
// DrawStoneCircle, DrawCrystal, DrawBale, DrawCrate, DrawBarrel.
// Ported from DMmap/src/dm-area/scene/render/scene-props.ts.
using Godot;

namespace DmMap;

public partial class PropsPainter
{
    void DrawTent(PropPlacement p) => Stub(p);
    void DrawCampfire(PropPlacement p) => Stub(p);
    void DrawWell(PropPlacement p) => Stub(p);
    void DrawRuin(PropPlacement p) => Stub(p);
    void DrawGrave(PropPlacement p) => Stub(p);
    void DrawBones(PropPlacement p) => Stub(p);
    void DrawStatue(PropPlacement p) => Stub(p);
    void DrawStoneCircle(PropPlacement p) => Stub(p);
    void DrawCrystal(PropPlacement p) => Stub(p);
    void DrawBale(PropPlacement p) => Stub(p);
    void DrawCrate(PropPlacement p) => Stub(p);
    void DrawBarrel(PropPlacement p) => Stub(p);

    void Stub(PropPlacement p) =>
        DrawCircle(p.Pos, 4 * p.Scale, new Color(0.4f, 0.2f, 0.6f, 0.5f));
}
