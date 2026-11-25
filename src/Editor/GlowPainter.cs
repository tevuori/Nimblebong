// Light glow overlay — the mood pass from scene-renderer.ts: warm halo
// discs + flicker for campfires, lanterns, fireplaces, crystals,
// portals and stone circles, drawn above the lit composite so the
// light stays bright. Animates via _Process → QueueRedraw.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class GlowPainter : Node2D
{
    record Emitter(Vector2 Pos, double R, string Kind, double Seed);

    List<Emitter> _emitters = [];
    Color _warm = Palette.Hex("#ffc46b");
    Color _magic = Palette.Hex("#c4aef0");

    double Now => Time.GetTicksMsec() / 1000.0;

    public void SetGlow(Scene scene, string biomeId)
    {
        _warm = biomeId switch
        {
            "volcanic" => Palette.Hex("#ff9a4d"),
            "cavern" => Palette.Hex("#8fe8e0"),
            _ => Palette.Hex("#ffc46b"),
        };
        _magic = biomeId switch
        {
            "volcanic" => Palette.Hex("#ff9a4d"),
            "cavern" => Palette.Hex("#8fe8e0"),
            "swamp" => Palette.Hex("#a5d89a"),
            _ => Palette.Hex("#c4aef0"),
        };
        _emitters = scene
            .Props.Where(p =>
                p.Kind is "campfire" or "lantern" or "fireplace" or "crystal" or "portal" or "stonecircle"
            )
            .Select(p => new Emitter(
                new Vector2((float)p.X, (float)p.Y),
                p.Scale,
                p.Kind,
                p.Seed
            ))
            .ToList();
        Visible = _emitters.Count > 0;
        QueueRedraw();
    }

    public override void _Process(double delta) => QueueRedraw();

    void Halo(Vector2 pos, double radius, Color color, double alpha)
    {
        if (alpha <= 0.004 || radius <= 0)
            return;
        DrawCircle(pos, (float)radius, color with { A = (float)Math.Min(1, alpha) });
    }

    public override void _Draw()
    {
        var time = Now;
        foreach (var e in _emitters)
        {
            var r = e.R;
            var phase = e.Seed * 0.7;
            var seed = e.Seed;
            switch (e.Kind)
            {
                case "campfire":
                case "fireplace":
                {
                    var flicker =
                        0.14
                        + 0.05 * Math.Sin(time * 9 + phase)
                        + 0.03 * Math.Sin(time * 23 + seed);
                    var outer = e.Kind == "campfire"
                        ? Palette.Hex("#eda63f")
                        : _warm;
                    Halo(e.Pos, r * 1.5, outer, flicker);
                    Halo(e.Pos, r * 0.75, Palette.Hex("#ffd98a"), flicker * 0.8);
                    // Rising embers on a loop.
                    for (var i = 0; i < 3; i++)
                    {
                        var t = (time * 0.6 + (i + Rng.Noise(seed + i * 3)) / 3) % 1;
                        DrawCircle(
                            e.Pos
                                + new Vector2(
                                    (float)(Math.Sin(t * 9 + seed + i) * r * 0.3),
                                    (float)(-r * 0.3 - t * r * 2)
                                ),
                            (float)Math.Max(0.4, 1.2 - t),
                            Palette.Hex("#ffca7a") with { A = (float)(0.75 * (1 - t)) }
                        );
                    }
                    break;
                }
                case "lantern":
                {
                    var flicker =
                        0.1
                        + 0.05 * Math.Sin(time * 11 + phase)
                        + 0.03 * Math.Sin(time * 27 + phase * 3);
                    Halo(e.Pos, r * 1.05, _warm, flicker);
                    Halo(e.Pos, r * 0.4, Palette.Hex("#fff0c0"), flicker * 1.4);
                    break;
                }
                case "crystal":
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var glint = Math.Pow(
                            Math.Max(0, Math.Sin(time * 2.2 + phase + i * 2.1)),
                            3
                        );
                        if (glint < 0.05)
                            continue;
                        var px = e.Pos.X + (float)((Rng.Noise(seed + i * 5) - 0.5) * r * 0.9);
                        var py = e.Pos.Y - (float)(r * (0.15 + Rng.Noise(seed + i * 5 + 1) * 0.8));
                        var s = 1.2 + glint * 2.4;
                        var gp = new Vector2(px, py);
                        Halo(gp, s * 1.8, Palette.Hex("#d1eceb"), glint * 0.25);
                        DrawLine(
                            gp + new Vector2((float)-s, 0),
                            gp + new Vector2((float)s, 0),
                            Colors.White with { A = (float)(glint * 0.8) },
                            1
                        );
                        DrawLine(
                            gp + new Vector2(0, (float)-s),
                            gp + new Vector2(0, (float)s),
                            Colors.White with { A = (float)(glint * 0.8) },
                            1
                        );
                    }
                    break;
                }
                case "portal":
                {
                    Halo(
                        e.Pos,
                        r * 0.62,
                        _magic,
                        0.12 + 0.07 * Math.Sin(time * 2.6 + phase)
                    );
                    // Rotating dash vortex.
                    var orbit = r * 0.66;
                    var dashes = 10;
                    var off = time * 26 * 0.04;
                    for (var k = 0; k < dashes; k++)
                    {
                        var a = (k / (double)dashes) * Math.PI * 2 + off;
                        var arc = 0.28;
                        DrawArc(
                            e.Pos,
                            (float)orbit,
                            (float)a,
                            (float)(a + arc),
                            4,
                            _magic with { A = 0.55f },
                            2
                        );
                    }
                    break;
                }
                case "stonecircle":
                {
                    var pulse = 0.05 + 0.06 * (0.5 + 0.5 * Math.Sin(time * 1.4 + phase));
                    Halo(e.Pos, r * 1.1, Palette.Hex("#8fd8d0"), pulse);
                    break;
                }
            }
        }
    }
}
