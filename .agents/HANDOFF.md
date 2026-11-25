# Handoff


## 2026-11-26 — Devin: archetypový generátor terénu + generátor zřícenin

### Hotovo (commit řada končící TBD, pushnuto)

**1. Přepsaný `MapGenerator.GenerateLandscape`** — místo pár náhodných
kopců teď generuje jeden z 5 archetypů podle seedu (jen terén + voda,
bez props/cest/domovního osídlení — user chce podklad pro vlastní stavbu):

- `0` river valley — bloudící údolí flankované terasovitými hřebeny,
  řeka po dně údolí, přítoky z hřebenů
- `1` ridge spine — diagonální skalní hřeben s prudkou čelní stěnou,
  řeka v podhůří, kaskádovité potoky ze srázu
- `2` lake bowl — prstenec kopců kolem jezera s odtokovým potokem
- `3` plateau — vysoká plošina se srázovým okrajem (malý `Size` = prudký
  svah/útes), nížinná řeka, vodopádový potok, občas osamělá mesa
- `4` rolling hills — kopce v souvislých řetězcích (ne scatter), údolí
  mezi nimi, řeka v hlavním údolí, občas jezírko

Klíčové techniky: `TerracedRidge` = překrývající se protáhlé tvary
podél společné páteře (šířka klesá s výškou → soudržná terasovitá
escarpment), `Wander` = noise-driven bloudění cest/řek místo sinusek,
`Foothills` = přechodové návezí upatí, globální jemné zvlnění (výška
5-13) proti mrtvé rovině. Pozn.: údolí existují jen jako nížiny MEZI
hřebeny — terén umí jen přidávat výšku.

**2. Nový nástroj „Generátor zřícenin"** (`ruins`, kategorie Stavby) —
klik vygeneruje procedurální komplex jako jeden undo krok, všechno jsou
reálné objekty. `src/Editor/RuinGen.cs`, 4 archetypy:

- hrad — rozbitý kruhový val s mezerou brány, věže ve stavu intact/
  zřícený pašt (model 2)/sutě, základy velké haly + sloupy
- věž — pašt nebo intaktní věž, radiální fragmenty hradeb, suťové suky
- usedlost — obdélníková základová deska (floor) + BrokenWall fragmenty,
  truhla/kosti, občas vedlejší stavba
- kamenný kruh — stonecircle + sloupy, část padlých (rocks)

`BrokenWall` krájí linii na fragmenty 30-70px s mezerami, zborcené
úseky nahrazuje sutí. Seed = hash pozice + counter kliků → každý klik
jiná podoba.

**3. Proby**: `DMMAP_LAND_PROBE=<seed>` → /tmp/godot-land-<seed>.png,
`DMMAP_RUIN_PROBE=1` → /tmp/godot-ruin-{0-3}.png (záběr na každý
archetyp).

### Známé limity

- Terasovitý „pancake" vzhled je inherentní massif systému (stejné
  výšky se slévají) — vyžaduje ladění height/size kombinací.
- Některé seedy mají velké plochy klidné roviny — záměr (stavební
  prostor), ne bug. Globální zvlnění je skoro neviditelné, možná zesílit.
- Circle archetype je nejslabší — stonecircle prop je drobný.
- Zříceniny nemají UI pro výběr archetypu/velikosti — seed losuje klik.
  Drag s nástrojem negeneruje nic (záměrně, jen klik).

### Regrese ověřeny

DMMAP_PROBE=1 prochází (road-merge, brush, fill, marquee, rotate+dup,
play tokens), build 0 errors.


 log

Nejnovější záznam nahoře. Formát: `## YYYY-MM-DD — <agent>`, pak sekce
Hotovo / Rozpracováno / Problémy / Další kroky.

## 2026-11-25 (12) — Devin, interiéry + zarostlé cesty

**Hotovo:**
- **Černé místnosti opraveny** — nebyl to bug osvětlení, ale fog-of-war:
  `FogRooms` je defaultně on a editor nad interiéry kreslil 92% černý
  závoj. Teď je síla závoje kontextová: plná (0.92) jen při aktivním
  nástroji mlhy, jinak jemné ztlumení (0.22) — DM vidí, co zařizuje.
  (`uStrength` uniform v `shaders/fog.gdshader`, `SceneView.SetFogStrength`,
  napojeno v `SetTool`.)
- **Nové interiérové props: `chair` (židle/stolička/lavice, 3 modely) a
  `candle` (svíčka s teplou glovou aurou v GlowPainteru)** — kresliče v
  `PropsInterior.cs`, enum + mapping + paleta ("Židle a lavice", "Svíčky").
- **Procedurální zařizování místností** (`src/Editor/RoomFurnish.cs`):
  nově nakreslená místnost se automaticky zařídí skutečnými props
  (koberec, stůl, židle čelem ke stolu, postele/skříně/krb u zdí,
  truhly/bedny/sudy v rozích, svíčky). Deterministické ze shape id,
  jeden undo krok za všechno, drzé předměty se nepřekrývají (rejection
  sampling + per-kind poloměry), dodržuje swing zónu dveří. Checkbox
  "Automaticky zařídit" v nástroji místností (default on). Baked decor
  se v zařízených místnostech přeskočí (FurnitureKinds rozšířeny).
- **Zarostlost cest** — nový slider v nástroji cesty (0–100, platí pro
  nové tahy). `Stroke.Wear` (0..1) se serializuje do JSON (`wear`).
  Vizuálně v `WaterPainter`: verge/halo slábnou, povrch se v
  noise-skládaných skvrnách probíjí zemí/trávou v barvě biomu
  (`RoadGround`), při vysokém wear povrch skoro mizí — pěšina splývá
  s podkladem jako v Nimblehold referencích. Probe `DMMAP_WEAR_PROBE=1`.
- Probes: `DMMAP_ROOM_PROBE=1` (místnost + furnish), regrese: main probe,
  probe-interiors, probe-water — všechny pass. Build 0 errors.

**Problémy / pozor:**
- Crash z předchozí zprávy (rotace o 195°) je **nedeterministický engine
  race**: coredump ukazuje SIGSEGV v .NET finalizer vlákně při teardownu
  nativních objektů — známá třída Godot mono bugů (godotengine/godot#104263,
  #106494, #109711). Rotace samotná je bezpečná (probe rotuje všechny
  objekty na mapě o 195° bez pádu, i s vynuceným GC). Spouští ji GC timing
  při churnu objektů; nelze spolehlivě reprodukovat ani opravit z userlandu
  — riziko klesá s menším churnem (fingerprint skip už pomáhá).
- Furnish: wall items se uchycují hlavně na nejdelší zdi; občas bed
  nevyjde (inset test) — v1 akceptovatelné, props jsou reálné a uživatel
  je má přesouvat.
- Zarostlost platí jen pro nové tahy (per-stroke). Apply na existující =
  budoucí rozšíření (slider v select tool?).

## 2026-11-25 (11) — Devin, fingerprint skip painterů

**Hotovo:**
- `SceneView.Rebuild` už nepřetváří všechny paintery při každém commitu.
  Paintery jsou rozdělené do dvou fingerprinted skupin:
  - **geo** (`areas`, `areaH`, `surfaces`, `water`, `crossings`, `fortify`,
    `bridges`, `waterH`) — fingerprint: shapes + strokes + rock props
    (foam) + look ids (biome/artpack/season/mood).
  - **props** (`interiors`, `interiorsH`, `propsPainter`, `propsH`) —
    fingerprint: shapes + všechny props + look ids. Strokes záměrně ne —
    editace cest/řek tak přeskočí nejdražší painter (props) úplně.
  - Z-order v AlbedoVp/HeightVp je teď explicitní `ZIndex` (massif 0,
    areas 10, surfaces 11, interiors 20, water 30–33, props 40), nikdy
    tree order — jinak by skipnutý painter změnil pořadí vrstev.
  - Fingerprinty hashují obsah, ne id (prohození id bez změny geometrie
    = žádný repaint). Kontrola pokrytí: Shape/Stroke/Prop pole všechna
    v hash; water mask/glow/fog jsou persistent in-place nodes
    (bez fingerprintu, levné).
- Měření (reálná mapa, čistý běh):
  - no-op commit: ~310 ms → **41 ms** (nic se nepřetváří, jen fog/mood)
  - prop-move / stroke-edit: ~200 ms (předtím ~310 ms vždy)
  - terrain-edit: ~460 ms (vše repaints — massifs + obě skupiny, inherentní)
- Nový probe `DMMAP_COMMIT_PROBE=1` v MapEditor.cs — měří commit cost
  podle typu editace (prop/stroke/terrain) + screenshot parity.
- Regrese: DMMAP_PROBE plný pass, drag probe pass (counts OK, drag 96 ms).

**Problémy / pozor:**
- Měření je v prostředí hodně hlučná: občas se objeví frame s časem
  přesně ~1000 ms (throttling artefakt — compositor/occluded window,
  NE kód; v editoru žádný vteřinový Timer neexistuje). Měřit vždy čistě,
  jeden Godot proces naráz, se settle framy.
- Zbylých ~200 ms commitu = re-kreace canvas items skupiny (~115 ms)
  + re-raster statických viewportů (~80 ms). Raster je per-viewport —
  viewport zrasterizuje všechny viditelné canvas items bez ohledu na to,
  co se změnilo, takže další zrychlení vyžaduje **per-prop canvas items**
  v PropsPainteru (každý prop vlastní Node2D child, z-index podle y,
  přesun = update jednoho childu). To je velký refactor PropsPainteru —
  ale i tak se raster static viewports při commitu nevyhne, takže strop
  výhry je nejistý. Zvážit, jestli ~200 ms hitch při puštění myši stojí
  za tu složitost.

## 2026-11-25 (10) — Devin, globální rendering optimalizace

**Hotovo:**
- Hloubková analýza framů nad reálnou mapou uživatele (16 shapes /
  13 strokes / 126 props). Pozor: měření bez settle framí je značně
  hlučná (shader warmup, lazy kompilace) — všechna čísla níž jsou
  ustálená.
- Idle frame 35 ms → **8,3 ms (120 FPS)**. Příčina: composite shader
  dělal 40-krokový raymarch stínů na každý pixel každého framu
  (1600×1000×40 ≈ 64M samplů/frame), přitom stíny jsou statické.
  Rozdělení composite:
  - `lighting_bake.gdshader` — diffuse + raymarch stíny, pouze světelný
    multiplier, render Once po editaci do LitVp na **polovičním
    rozlišení** (světlo je hladké, upscale neviditelný) = 4× méně
    raymarch práce na editaci.
  - `water_overlay.gdshader` — per-frame: albedo × light + animovaná
    voda + grain + parchment. Pár texture reads, žádný raymarch.
  - Bake je deferred o 2 framy (`_litBakeIn` v LightingStage) — render
    order subviewportů ve frame není garantován a bake nesmí zachytit
    pre-edit textury (jinak trvale zastaralé osvětlení).
  - `DMMAP_NO_STATIC_REFRESH` env v SceneView už není potřeba, ale
    zůstává pro experimenty; `DMMAP_FINAL_ONCE` kill-switch v LightingStage.
- Prop drag (tah s nástrojem props) commitoval každých 45 px — každý
  commit = full repaint. Teď se pozice sbírají do ghost trailu (žlutá
  kolečka) a při puštění je **jeden** commit. Klikání beze změny.
- ARCHITECTURE.md pipeline diagram aktualizován.

**Ověření:** vizuální parita screenshotů před/po (stíny, grain, voda,
parchment identické), probe-model ALL PASS, probe-config biomy OK,
SMART probe OK, plný DMMAP_PROBE OK, build 0 errors.

**Zbývá (dokumentované, neudělané):**
- Commit frame ~310 ms i po optimalizacích — dominuje re-kreace painter
  canvas items (tisíce draw primitiv znovu submitnutých do rendering
  serveru), ne rasterizace (~85 ms) ani bake. Skutečná oprava =
  perzistentní per-objekt canvas items nebo finger-print skip painterů
  + explicitní z-index (velký refactor, viz note).
- Idle 8,3 ms = vsync cap, ne bottlneck.

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
