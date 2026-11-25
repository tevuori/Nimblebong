# 2026-11-25 — Globální rendering optimalizace

## Metodika měření

- `DMMAP_DRAG_PROBE=1 DMMAP_DRAG_LOAD=1` — reálná mapa uživatele,
  idle/commit/drag timing. VŽDY nechat settle framy (20+), jinak
  měří shader warmup a lazy kompilaci — čísla jsou 3-4× horší než
  realita a měření ještě ke všemu silně kolísají mezi běhy.

## Co se změřilo (ustálená čísla, AMD 780M)

| stav | idle frame | commit frame |
|---|---|---|
| před refactorem | ~35 ms | ~100–300+ ms, silně hlučné |
| po bake splitu | **8,3 ms** | ~310 ms konzistentně |

## Architektura po refactoru

```
AlbedoVp/HeightVp/FogVp/WaterVp  (Once, full-res, static input)
        │
        ▼
LitVp  (Once, HALF res) — lighting_bake.gdshader
        diffuse + raymarch stíny → pouze světelný multiplier (bez albeda!)
        │
        ▼
FinalVp (Always) — water_overlay.gdshader
        albedo(TEXTURE) × light(uLight) + voda TIME + grain + parchment
```

- Bake deferred 2 framy — render order subviewportů není garantován,
  jinak hrozí trvale zastaralý bake.
- Half-res bake: světlo je hladké, upscale je neviditelný. Albedo detail
  a grain zůstávají full-res, protože se aplikují až v per-frame passu.
- Proč ne bake=včetně albeda: upscale by rozmazal art detail i grain.

## Zbývající bottleneck: commit ~310 ms

Rozpad nákladů jednoho commitu (126 props):
- re-rasterizace static viewportů: ~85 ms (měřeno DMMAP_NO_STATIC_REFRESH)
- re-kreace painter nodes/draw commands: ~200+ ms ← dominantní
- bake raymarch: minorita (half-res)

Reálné opravy pro příště (odhadované náklady):
1. **Fingerprint skip painterů**: painters se při rebuildu QueueFree
   a tvoří znovu i když se jejich vstup nezměnil. Fingerprint vstupů
   (shapes/strokes vs props) + přeskočení nezměněných skupin. POZOR:
   canvas z-order je tree order — skupiny je nutné nejprve převést na
   explicitní ZIndex, jinak se přeskočením rozbije layering.
   InteriorPainter závisí na shapes I props (furniture carving) →
   patří do props skupiny, nebo rebuild při změně obou.
2. **Per-object canvas items**: každý prop vlastní Node2D — přesun/
   přidání jednoho prop redrawne jen jeho item. ~250+ nodů navíc,
   engine zvládne. Rasterizace viewportu se ale překreslí celá vždy.
3. Redraw kompletního props painteru i tak zůstává pro group operace.

## Interakce — prop drag batching

Tah s prop nástrojem: pozice se sbírají do `_propTrail` + ghost
kolečka (`GhostLayer.SetPropTrail`), commit až v EndStroke — jeden
undo krok za tah místo jednoho za 45 px.

## Env kill-switche pro experimenty

- `DMMAP_FINAL_ONCE=1` — vypne per-frame render FinalVp (měření)
- `DMMAP_NO_STATIC_REFRESH=1` — skip RefreshStatic v Rebuild (měření)
- `DMMAP_RAYSTEPS=n` — přepíše uSteps raymarchu
