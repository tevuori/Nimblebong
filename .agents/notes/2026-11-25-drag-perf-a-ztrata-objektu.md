# 2026-11-25 — Drag objektů: sekání a mizení objektů

## Diagnostika

Uživatel hlásil: při přesouvání domu se editor strašně seká a smaže
všechny objekty na mapě. Rozpadlo se na tři nezávislé problémy:

### 1. Drag = permanentní full rebuild (sekání)

`_pendingRebuild` throttle spouštěl `SceneView.Rebuild()` každých 90 ms.
Měřeno na reálné mapě uživatele (16 shapes/13 strokes/126 props):

- jeden Rebuild (CPU): ~4–8 ms, ale frame po něm: ~100–300 ms
- drag loop 12 kroků: ~12 000 ms (worst case)
- idle frame: ~35 ms

Drahou částí není re-render statických viewportů (testováno
`DMMAP_NO_STATIC_REFRESH=1` — žádný rozdíl), ale znovuvytvoření všech
painter nodes (stovky canvas items znovu nahrané do rendering serveru)
+ shader warmup při prvním rebuildu.

Fix: během `_selMoving` se rebuild v `_Process` přeskakuje; ghost obrys
(`GhostLayer.SetSelection`) se překresluje živě a ukazuje pozici. Drop
commit repaintne jednou. Výsledek: tentýž drag loop za ~92 ms.

### 2. Loader tiše zahazoval props (mizení objektů)

`SceneJson.ReadProp` vyhodil každý prop, jehož kind není v hardcoded
`Kinds.Props`. Editor ale sám zapisuje jiné kindy:

- `house` — Smart fill / probe umisťují dům jako PROP (kind "house"),
  přitom "house" je zároveň area kind, takže nikdy nebyl v Props listu
- `birch`, `dark-pine`, `reeds`, `tree-stump`, `overgrown-ruin` —
  species z biom configů / vegetation brush / smart fill

Reálná mapa uživatele: 126 props v souboru, po loadu 108. Po re-save
by byla ztráta trvalá. Dokumentace modelu v `Scene.cs` explicitně říká
„any string in Props is accepted by the parser" — parser byl v rozporu
se specifikací. Fix: whitelist kontrola odstraněna, legacy kindy
(hill/mountains) se zahazují dál přes ReadKind.

### 3. Hit-test podle živé pozice myši místo pozice eventu

`BeginStroke`/`EndStroke`/mouse motion používaly
`GetGlobalMousePosition()` — pozici myši *v okamžiku zpracování
eventu*, ne při stisku/uvolnění. Při pomalých framech (a editor před
opravou 1–2 lagoval) se tah vyhodnotil na místě, kam myš mezitím
docestovala → miss objektu → marquee select náhodné oblasti → v chaosu
hromadné výběry/presuny. Nově `ScreenToWorld(event.Position)` (stejná
kamerová matika jako `ZoomAt`).

## Probe

`DMMAP_DRAG_PROBE=1` simuluje reálný drag: hit-test, live mutation,
awaitované framy, EndSelect commit, druhý drag hned za prvním.
Env doplňky: `DMMAP_DRAG_LOAD=1` (načti user://dmmap-scene.json),
`DMMAP_DRAG_LANDSCAPE=1` (vygeneruj krajinu), `DMMAP_DRAG_STEPS=1`
(per-step timing), `DMMAP_DRAG_REBUILD=1` (legacy cesta s rebuildem
za frame — pro srovnání), `DMMAP_DRAG_SHOT=path` (screenshot).

## Neověřená hypotéza

Samotný drag v probech nikdy objekty nesmazal (counts OK ve všech
scénářích). Než byla opravena perf a hit-testy, pravděpodobný řetězec:
lag → klik mimo objekt → marquee hromadný výběr → zmatené
klikání/Delete/Ctrl+Z během freeze → ztráty. Pokud uživatel i po
opravách uvidí mizení v jedné session bez save/load, potřebujeme
přesný repro (co přesně zmizelo — props/shapes/všechno, jestli se
vrátí po Ctrl+Z, jestli se objeví po restartu).
