// Seeded map generator — builds a whole Scene from one integer:
// a meandering river, terraced massifs, a road that fords the river,
// forest patches and a hamlet. Everything derives from Rng.Noise so
// the same seed always produces the same map.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public static class MapGenerator
{
    /// <summary>Deterministic [0,1) value for (seed, index) — Rng.Noise
    /// is hash-based so arbitrary seeds are fine.</summary>
    static double N(int seed, int i) => Rng.Noise(seed * 0.618033 + i * 7.13);

    public static Scene Generate(int seed, Vector2 size)
    {
        var scene = new Scene { Width = size.X, Height = size.Y, Name = $"Mapa {seed}" };
        var cx = size.X / 2;
        var cy = size.Y / 2;

        // ── massifs: 4-7 hills placed away from the map center ──
        var hillCount = 4 + (int)(N(seed, 1) * 3);
        for (var i = 0; i < hillCount; i++)
        {
            var a = N(seed, 10 + i) * Math.PI * 2;
            var d = 0.28 + N(seed, 30 + i) * 0.3;
            var center = new Vector2(
                (float)(cx + Math.Cos(a) * cx * 1.5 * d),
                (float)(cy + Math.Sin(a) * cy * 1.5 * d)
            );
            var radius = 90 + N(seed, 50 + i) * 130;
            var pts = new List<Vector2>();
            var lobes = 2 + (int)(N(seed, 70 + i) * 3);
            for (var k = 0; k < 16; k++)
            {
                var t = k / 16.0 * Math.PI * 2;
                var r = radius * (0.78 + 0.32 * N(seed, 90 + i * 17 + k));
                pts.Add(
                    new Vector2(
                        (float)(center.X + Math.Cos(t) * r * (1 + 0.2 * Math.Cos(t * lobes))),
                        (float)(center.Y + Math.Sin(t) * r)
                    )
                );
            }
            scene.Shapes.Add(
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Terrain,
                    Points = pts,
                    Height = 15 + N(seed, 120 + i) * 55,
                    Size = 34 + N(seed, 140 + i) * 28,
                    Roughness = N(seed, 160 + i) * 85,
                    Model = (int)(N(seed, 180 + i) * 3),
                }
            );
        }

        // ── river: top edge to bottom edge, meandering ──
        var riverPts = new List<Vector2>();
        var rx = size.X * (0.2 + N(seed, 200) * 0.6);
        var phase = N(seed, 201) * Math.PI * 2;
        var amp = 60 + N(seed, 202) * 90;
        var nSeg = 7;
        for (var i = 0; i <= nSeg; i++)
        {
            var y = -40 + (size.Y + 80) * i / nSeg;
            var x = rx + amp * Math.Sin(phase + i * 1.1);
            riverPts.Add(new Vector2((float)x, (float)y));
        }
        scene.Strokes.Add(
            new Stroke
            {
                Id = scene.AllocId(),
                Kind = Kinds.River,
                Points = riverPts,
                Width = 26 + N(seed, 203) * 14,
            }
        );

        // ── road: left edge to right edge through the map ──
        var roadPts = new List<Vector2>();
        var ry = size.Y * (0.3 + N(seed, 210) * 0.4);
        var rphase = N(seed, 211) * Math.PI * 2;
        for (var i = 0; i <= 6; i++)
        {
            var x = -40 + (size.X + 80) * i / 6;
            var y = ry + 50 * Math.Sin(rphase + i * 0.9);
            roadPts.Add(new Vector2((float)x, (float)y));
        }
        scene.Strokes.Add(
            new Stroke
            {
                Id = scene.AllocId(),
                Kind = Kinds.Road,
                Points = roadPts,
                Width = 14 + N(seed, 212) * 8,
            }
        );

        // ── forests: 2-4 elliptical patches on the lowlands ──
        var forests = 2 + (int)(N(seed, 220) * 3);
        for (var i = 0; i < forests; i++)
        {
            var center = new Vector2(
                (float)(size.X * (0.12 + N(seed, 240 + i) * 0.76)),
                (float)(size.Y * (0.15 + N(seed, 260 + i) * 0.7))
            );
            var rx2 = 90 + N(seed, 280 + i) * 120;
            var ry2 = 60 + N(seed, 300 + i) * 90;
            var patch = new List<Vector2>();
            for (var k = 0; k < 14; k++)
            {
                var t = k / 14.0 * Math.PI * 2;
                patch.Add(
                    new Vector2(
                        (float)(center.X + Math.Cos(t) * rx2 * (0.8 + 0.35 * N(seed, 320 + i * 31 + k))),
                        (float)(center.Y + Math.Sin(t) * ry2 * (0.8 + 0.35 * N(seed, 420 + i * 31 + k)))
                    )
                );
            }
            var mix = PropScatter.ScatterMix(
                patch,
                46,
                [(PropKind.Tree, 3.0), (PropKind.Pine, 1.6), (PropKind.Bush, 0.7)],
                density: 0.92,
                seed: seed + i * 101
            );
            foreach (var p in mix)
                scene.Props.Add(FromPlacement(scene, p));
        }

        // ── hamlet: houses clustered at a road point ──
        var hx = roadPts[2 + (int)(N(seed, 500) * 2)].X;
        var hy = roadPts[2 + (int)(N(seed, 501) * 2)].Y + 60;
        var houses = 3 + (int)(N(seed, 502) * 3);
        for (var i = 0; i < houses; i++)
        {
            var a = i / (double)houses * Math.PI * 2 + N(seed, 510 + i);
            var d = 30 + N(seed, 520 + i) * 55;
            scene.Props.Add(
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.House,
                    X = hx + Math.Cos(a) * d,
                    Y = hy + Math.Sin(a) * d * 0.7,
                    Scale = 24 + N(seed, 530 + i) * 8,
                    Rotation = N(seed, 540 + i) * 0.6 - 0.3,
                    Seed = seed * 13 + i,
                }
            );
        }

        // ── sprinkled rocks & bushes ──
        var scatter = 14 + (int)(N(seed, 600) * 10);
        for (var i = 0; i < scatter; i++)
        {
            scene.Props.Add(
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = N(seed, 610 + i) < 0.5 ? Kinds.Rock : Kinds.Bush,
                    X = size.X * N(seed, 620 + i * 3),
                    Y = size.Y * N(seed, 621 + i * 3),
                    Scale = 14 + N(seed, 622 + i * 3) * 12,
                    Seed = seed * 29 + i,
                }
            );
        }
        return scene;
    }

    static Prop FromPlacement(Scene scene, PropPlacement p) =>
        new()
        {
            Id = scene.AllocId(),
            Kind = p.Kind switch
            {
                PropKind.Pine => "pine",
                PropKind.Rock => Kinds.Rock,
                PropKind.Bush => Kinds.Bush,
                PropKind.House => Kinds.House,
                _ => Kinds.Tree,
            },
            X = p.X,
            Y = p.Y,
            Scale = 22 * p.Scale,
            Rotation = p.Rotation,
            Seed = (int)p.Seed,
        };
}
