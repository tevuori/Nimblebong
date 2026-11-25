# Handoff log

Nejnovější záznam nahoře. Formát: `## YYYY-MM-DD — <agent>`, pak sekce
Hotovo / Rozpracováno / Problémy / Další kroky.

## 2026-11-25 (9) — Devin, rotace objektů

**Hotovo:**
- Pivot rotace tvarů/strokes opraven z bbox centra na centroid. Bbox se
  po každém kroku rotace změnil, takže opakované mačkání R tvar
  „uneslo“ — orbitovalo místo otáčení na místě. Po 24×15° (plná
  otáčka) je drift centroidu i bodů přesně 0.00 (probe).
- Indikace natočení: výběr propů teď v ghost vrstvě kreslí žlutou
  šipku ve směru lokální „up“ osy objektu (stejná transformace jako
  painter — rotace clockwise-positive) + popisek ve stupních. Rotace
  symetrických objektů (stromy, kameny) je tak vidět.
- Nový probe `DMMAP_ROTATE_PROBE=1` (+ `DMMAP_ROTATE_SHOT=path`):
  drift pivotu, akumulace rotace propů, screenshot indikátoru.

**Ověření:** probe-model ALL PASS, `DMMAP_PROBE=1` OK, build 0 errors.

## 2026-11-25 (8) — Devin, drag objektů: sekání + mizení objektů

**Hotovo:**
- Sekání při přesouvání: tah objektu už nespouští plný `Rebuild` každých
  90 ms (jeden rebuild stojí ~100–300 ms na reálné mapě, takže editor
  žil v permanentním rebuildu). Během tahu se nepřekresluje vůbec —
  pozici ukazuje žlutý ghost obrys — a rebuild proběhne jednou při
  puštění. Tah zrychlil z ~3–5 FPS na ~60–120 FPS (12 kroků tahu nad
  reálnou mapou: 12 000 ms → 92 ms).
- Mizení objektů: loader scény (`SceneJson.ReadProp`) tiše zahazoval
  prop kindy mimo hardcoded whitelist `Kinds.Props` — přitom editor sám
  zapisuje `house` (Smart fill), `birch`/`dark-pine`/`reeds`/`tree-stump`
  (biom species) apod. Reálná mapa uživatele: 108/126 props načteno,
  18 zahozeno. Model specifikuje otevřený slovník („any string accepted"),
  parser teď kindy nepřezkoumává. Po opravě 126/126.
- Hit-testy používají pozici z input události (`ScreenToWorld`), ne
  `GetGlobalMousePosition` — při pomalém framu se jinak klik/tah
  vyhodnotí podle pozice myši *až při zpracování eventu*, ne při stisku,
  takže tah chytil marquee/ničeho místo objektu.

**Ověření:**
- Nový probe `DMMAP_DRAG_PROBE=1` (+ `DMMAP_DRAG_LOAD=1` pro reálnou
  uloženou mapu, `DMMAP_DRAG_STEPS=1` per-step timing): počty objektů
  před/uprostřed/po tahu konzistentní, pozice domu přesná, druhý tah po
  commitu OK.
- `probe-model` ALL PASS, plný `DMMAP_PROBE=1` OK, build 0 errors.

**Známé zbytkové problémy:**
- Idle frame s velkou mapou ~35 ms (FinalVp composit běží Always kvůli
  vodě/glow) — editor se složitou mapou jede ~25–30 FPS i bez práce.
- Jeden rebuild = ~100–300 ms; cítit při každém commitu, nejen tahu.
- Během tahu se objekt nehýbe vizuálně, jen ghost obrys (architektura
  painterů neumožňuje levný inkrementální přesun).

## 2026-11-25 (7) — Devin, Smart fill hustota

**Hotovo:**
- Slider hustoty už nespouští drahý `SmartFill.Generate` pro každý
  drobný pohyb. Změny se sloučí do jednoho přepočtu po 160 ms; před
  commitem („Vyplnit“/Enter) se případný čekající přepočet vždy dokončí.
- Vedle slideru je vidět procentuální hodnota a statistiky Smart fillu
  ukazují počet nových objektů i aktuální hustotu.
- Tooltip nástroje vysvětluje, že hustota/seed se upravují nad aktivním
  náhledem oblasti.
- Probe kontroluje jak debounced cestu slideru, tak hustotu na plně
  generované krajině s existujícími kolizemi.

**Ověření:**
- Testovací oblast: hustota 15 % → 94 objektů (20 hlavních), 100 % →
  230 objektů (61 hlavních).
- Generovaná krajina: hustota 15 % → 233 objektů, 100 % → 529 objektů.
- `dotnet build --no-incremental` → 0 errors (17 známých warnings).
- `DMMAP_SMART_SHOT` i plný `DMMAP_PROBE=1` prošly.

## 2026-11-25 (6) — Devin, výkon editoru na hybridní NVIDIA

**Hotovo:**
- Hlavní zpomalení editoru byl neustálý re-render statických offscreen
  vrstev. `albedo`, `heightmap`, vodní maska a mlha teď používají
  `SubViewport.UpdateMode.Once`; `LightingStage.RefreshStatic()` je
  znovu zapne po každém `SceneView.Rebuild()`. `FinalVp` zůstává
  `Always`, protože voda a glow používají `TIME`.
- `ConfigProbe` po přepnutí biomu explicitně refreshne statické vrstvy,
  aby sweep biomů dál generoval správné výstupy.
- Náhledy nástrojů se inicializují až po nastavení looku — už se při
  startu negenerují dvakrát.

**Ověření:**
- `dotnet build --no-incremental` → 0 errors (17 známých warnings).
- NVIDIA PRIME offload: stabilní 120 FPS (před opravou cca 8 FPS).
- `DMMAP_PROBE=1` editor self-test prošel včetně move/select/fill/tokenů.
- `scenes/probe-config.tscn` prošla, sweep vygeneroval 11 PNG; biomy se
  skutečně překreslují (hashe woodland/desert se liší).

**Poznámka:** pro vynucení NVIDIA na tomto hybridním notebooku spouštět
s `__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia`.

## 2026-11-25 (5) — Devin, merge do `main` + smart fill a zkratky

**Hotovo:**
- Zmergované větve `enhance/nimblehold-98` (`a5542d1`) a
  `feat/enhance-visuals` (`2113a40`) do `main`. Konflikt v
  `SceneView.SetRocks` vyřešen tak, že se zachovala velikost ripple/foam
  podle scale a zároveň detekce všech rock definic přes `MapProp`.
- Smart fill teď používá všech 75 JSON prop definic; doplněny poslední
  dvě chybějící (`daisies`, `overgrown-ruin`) do vhodných kontextů
  v `assets/config/fill.json`.
- Klávesové zkratky editoru: Ctrl+S Smart výplň, Ctrl+Shift+S uložit,
  Ctrl+R/Ctrl+Shift+R rotace výběru, Ctrl+A výběr všeho, Enter potvrdí
  náhled výplně, šipky posouvají výběr nebo kameru, +/− zoom, Ctrl+0
  reset pohledu. V action baru je dialog „Zkratky“ s českou nápovědou.

**Ověření:**
- `dotnet build --no-incremental` → 0 errors (17 známých warnings).
- `DMMAP_PROBE=1` editor self-test prošel.
- `scenes/probe-flora.tscn` prošla pro všech 6 biomů bez chyb; woodland
  smart fill už generuje i `daisies`.

**Pozor:** `Ctrl+S` je podle přání uživatele Smart výplň; uložení je proto
`Ctrl+Shift+S` a pořád i tlačítko „Uložit“. Lokální `main` je před
`origin/main`, zatím nepushnuto.

## 2026-11-25 (4) — Claude Code (Opus 5.5), `feat/enhance-visuals`

**Hotovo:**
- Paleta editoru ukazuje prop definice z JSONu v sekci podle `category`
  (`MapEditor.PaletteTools`, `FindTool`, náhledy v `ToolThumbs`).
- Nový nástroj „Štětec vegetace“ (`src/Editor/MapEditorVegetation.cs`):
  konkrétní rostlina nebo směs podle okolí z `fill.json`.
- Ověřeno screenshotem UI (`DMMAP_VEG_SHOT`, `DMMAP_VEG_SEARCH`).

**Pro UI agenta:** zásahy do `MapEditor.cs` jsou malé (tool `veg-brush`,
`PaletteTools()` místo `Tools` v SetTool/PopulateToolCards, volání
`OnPaletteBiomeChanged`, `BuildVegSection`). Logika je v partial souboru.

## 2026-11-25 (3) — Claude Code (Opus 5.5), `feat/enhance-visuals`

**Hotovo:**
- **Flora:** nový základní druh `flora` (`src/Props/PropsFlora.cs`) se
  7 tvary (tuft, fern, flowers, reeds, mushrooms, lilypads, groundcover).
  12 rostlin jako JSON v `assets/config/props/` (tráva, kapradí, květiny,
  rákos, houby, lekníny, jetel, vřes, pouštní flora, svítivé houby…).
- **SmartFill z configu:** tabulky, velikosti, prahy kontextů a vrstvy
  (`main`, `flora`, `critters`) jsou v `assets/config/fill.json`
  (`FillRules`). Definice mimo biom scény z tabulek vypadnou automaticky.
  Nové kontexty `shallows`/`deepWater`, per-biome `biomeChance`.
  Chování vrstvy `main` odpovídá původnímu kódu (+ pár variant z props).
- `PropDef.water` / `Interactions.WaterKind` respektuje definice.
- `scenes/probe-flora.tscn`: galerie definic biomu + smart fill test
  (`DMMAP_BIOME`, `DMMAP_OUT`). Ověřeno renderem woodland/desert/alpine/cavern.
- Godot .NET cesta v AGENTS.md.

**Pro autora SmartFill (Devin):** tabulky se přesunuly z `SmartFill.cs`
do `fill.json`. Úpravy vah prosím dělej tam.

## 2026-11-25 (2) — Claude Code (Opus 5.5), `feat/enhance-visuals`

**Hotovo:**
- `assets/config/placement.json` + `Placement`: ochranné zóny z configu.
  Prop definice je přepisují (`protect`, `keepOut`). Checkbox
  „Ignorovat ochranné zóny“ v sekcích Varianta a Štětec.
- `Kinds.Biomes`/`Kinds.Moods` se berou z configu. Nový biom/mood v JSON
  už neztratí scéna při načtení.
- `assets/config/README.md`: kompletní reference všech JSON configů
  (+ odkaz v AGENTS.md, pravidlo „vizuální parametry do configu“).

## 2026-11-25 — Claude Code (Opus 5.5), větev `feat/enhance-visuals`

**Hotovo:** prop registry. Nové druhy objektů jdou přidat jako JSON do
`assets/config/props/` (schéma: `assets/config/props/README.md`).
`PropDefs` (loader + validace), `PropsStyle` (restyle všech kreslicích
volání), `SceneView.MapProp`/`PropDefs.BaseKind` řeší id definic. Glow
a vlnky kamenů ve vodě berou definice jako jejich základní druh.

**Pro UI agenta:** paleta nástrojů v editoru zatím zná jen vestavěné druhy.
Definice pro aktuální biom vrací `PropDefs.ForBiome(biomeId)` (`Id`,
`Label`, `Category`) a do scény se ukládá `kind = def.Id`.

**Pozor:** někdo otevřel projekt v Godot 4.7 **bez mono**. Ten z
`project.godot` odstranil feature `"C#"` a vygeneroval `.import` soubory
v `docs/nimblehold/`. Nic z toho jsem necommitnul; změnu v `project.godot`
nezahrnovat.

## 2026-11-25 — Claude Code (Opus 5.5)

**Hotovo:** založen `AGENTS.md` (společné instrukce), `.agents/` (handoff +
notes), `CLAUDE.md` odkazuje na `AGENTS.md`.

**Problémy:** Godot binárka není na tomto stroji na cestě uvedené
v dokumentaci (`/home/tevuori/...`) ani v PATH. Probe zatím nejde spustit.

**Další kroky:** zjistit cestu ke Godot 4.7.2 mono a pokračovat v iteracích
podle `Enhance.md` (skóre v `docs/nimblehold/scores.json`).
