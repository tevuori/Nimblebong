# DMmap-Godot — architektura

2.5D map editor inspirovaný Nimbleholdem (terrain → heightfield → GPU
lighting). Godot 4.7.2 mono, C# (`net8.0`), `gl_compatibility`.

## Render pipeline

```
Shape data (core/) ──► TerrainGeometry.BuildMassifs()
                            │  clipper: union / offset / clip
                            ▼
        ┌──────────┬───────────────┬────────────┐
        ▼          ▼               ▼            ▼
   AlbedoPainter  HeightmapPainter  (voda)   (props)
        │          │               │            │
   AlbedoVp    HeightVp        (vp)         (vp)      SubViewports
        └──────────┴───────┬───────┴────────────┘
                           ▼
                    FinalVp + lighting.gdshader
                    = normal-map diffuse + raymarch stíny + mood
                           ▼
                       PNG / screen
```

- Každý **painter** je `Node2D` s `_Draw()` — žádný clip state, vše se
  řeší předem přes `Geometry2D` (ClipPolygons/OffsetPolygon/MergePolygons).
- `Fill(poly, color)` — VŽDY přes tuhle bránu: clipper vrací
  degenerativní slivery a `DrawColoredPolygon` na nich padá
  ("Invalid polygon data, triangulation failed").
- Heightmap: baseline `0.16`, masivy stupňovitá rampa (vnořené offset
  fills), řeky vyryté POD baseline, props hrbolky nad baseline.

## Pravidla

- **Deterministika**: žádné `Random`/`GD.Rand*` — vše se seeduje z pozic
  přes `Rng.Noise` (sin-hash, stejný jako web renderer).
- `Palette.Tint/Mix/TintAlpha` — 1:1 port TS `tint`/`mix` helperů.
- Self-intersecting polygony čistit `Geometry2D.MergePolygons(p, p)`.
- Painters nesahají na cizí vrstvy; nové prvky = nový painter soubor,
  integrace přes `LightingStage`.

## Probe workflow (self-verify vizuálně)

```
dotnet build
DMMAP_PROBE=1 godot --path . [scenes/probe-x.tscn]   # dumpne /tmp/godot-*.png
```

- `scenes/<name>.tscn` lze spustit přímo jako poziční arg — neměň
  `run/main_scene` v `project.godot` kvůli probe scénám.
- `DumpAndQuit` čeká ~6 framů než čte `FinalVp` texturu.
- Headless režim NERENDERUJE (dummy rasterizer) — vždy přes reálné okno.
- Godot binary: `/home/tevuori/Tools/godot/Godot_v4.7.2-stable_mono_linux.x86_64`

## Reference

- Nimblehold analýza: `/home/tevuori/Documents/DnD/nimblehold-analysis/ANALYZA.md`
- Web zdroják (původní TS): `/home/tevuori/Documents/DnD/DMmap/src/dm-area/scene/`
- Biome/mood JSON configy: `/home/tevuori/Documents/DnD/DMmap/src/config/`
