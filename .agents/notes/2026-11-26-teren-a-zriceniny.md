# 2026-11-26 — Archetypový terén + generátor zřícenin

## Terén: proč archetypy

Původní GenerateLandscape: 4-7 blob kopců náhodně roztroušených + sinus
řeka + cesta + les + vesnice. User chtěl čistý podklad terén+voda
" s duší". Klíčové poznatky:

- Terén umí jen PŘIDÁVAT výšku → údolí = nížina ponechaná mezi hřebeny.
- Massify se slévají při stejné (Model, Height, Size) → terasy se
  staví stackem protáhlých tvarů podél páteře, NE jedním tvarem.
- Malý `Size` (14-22) = prudký svah čtený jako útes/sráz.
- `Wander` (kumulativní drift s pull-back 0.86) vypadá přirozeněji než
  sinusoidy — řeky/údolí/hrany plošin.
- RidgePoly: capsule polygon podél polyline — stavební kámen hřebenů.

Iterační probe: DMMAP_LAND_PROBE=<seed>, zoom 0.45, kamera na střed.
UI panel zakrývá spodek — hodnotit horní polovinu.

## Zříceniny

- tower prop Model 2 = zřícený pašt (existuje v PropsVillage).
- BrokenWall resampluje linii po 10px a krájí na fragmenty 3-7 vzorků,
  ~28% sekcí je plně zborcených → sutě (ruin/rock props).
- Kruhová hradba: ring generovaný po oblouku s výseki pro bránu, runs
  split podle distance continuity.
- Nástroj je Mode.Prop s kind sentinel "ruins-complex", intercept v
  PlacePropAt. Drag-trail je pro něj vypnutý (jinak by se stampoval
  neznámý kind).
