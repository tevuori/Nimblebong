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

## Editor (`src/Editor/`)

- `SceneView` — scéna→painter orchestrátor; cache masivů (`_massifsValid`
  + `ShapesEqual`) — rebuild props/řek přeskakuje drahý clipper.
  `Massifs` vystaven pro hit-test výběru a live preview.
- `MapEditor` — vstup/nástroje/kamera/české UI. Nástroje: Vybrat, Terén,
  Řeka, Cesta, props, Guma. Vše přes `SceneHistory` (undo-able).
- Výběr: `BeginSelect/MoveSelectionTo/EndSelect` — objekt se živě
  posouvá mutací in-place, při puštění se revertne a zapíše JEDEN commit
  (delta). Stejný vzor pro slidery (`OnTerrainSlider` live + commit na
  `DragEnded`). Po commitu `RefreshSelection` re-resolvne id v nové scéně.
- Live preview: během tahu terénu se throttle (120 ms) staví draft
  `BuildMassifs` jen z taženého tvaru → `GhostLayer.SetDraft`.
- `CrossingPainter` — schody/vodopády jako inside/outside přechody
  resamplovaných strokes proti `Massif.Tops`. Neukládá se do scény,
  kreslí se automaticky.
- `MapGenerator` — seed→celá `Scene` (masivy, řeka, cesta, lesy přes
  `PropScatter`, vesnice, scatter). Vstup do `SceneHistory` celý najednou.
- Klik prop-nástrojem na existující prop → `Seed++` (cyklování variant).

## Bridge (`src/Bridge/`)

`DrBridge.UploadMap` — DMmap backend (:3000). GraphQL jede přes
socket.io EIO4 long-polling (žádný WS client):

```
GET  /api/socket.io/?EIO=4&transport=polling   → "0{sid}"
POST sid  "40"                                 → connect
POST sid  42["authenticate",{password,desiredRole:"admin"}]
GET  sid                                       → 42["authenticated"]
POST sid  42["@graphql/execute",{id,operation,variables}]
GET  sid                                       → 42["@graphql/result",{...}]
PUT  {uploadUrl}   raw PNG (relativní → prepend baseUrl!)
POST /api/map/{id}/send  Bearer + multipart "image"  → živá mapa
```

- `mapImageRequestUpload`/`mapCreate` vyžadují socket auth jako admin.
- `BridgeDialog` — server/heslo/název/živá-mapa, status řádek.
- `DMMAP_BRIDGE_PROBE=1` v probe → end-to-end test proti :3000.

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
