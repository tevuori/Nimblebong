# DECISIONS.md — Nimblehold parity (Enhance.md)

## Redesign palety nástrojů — živé náhledy místo textových tlačítek

- Nový `src/Editor/ToolThumbs.cs`: skrytý mini `SceneView` (120×76, stejné
  rozměry jako web `previewShapes` canvas) renderuje pro každý nástroj
  jeho skutečný výstup — port web `previewShape` (rect/line/point
  patterns + `previewSizes` škály propů).
- Karty (`MapEditor.ToolCard`) = thumbnail nahoře + label, akcent barvou
  nástroje na spodní hraně — web card layout.
- Ikony se renderují asynchronně (~2 framy/ikona), cachují se per-look
  a pře-renderují při změně biomu/naladění/art packu/sezóny — jako web.
- Speciální náhledy: `fill` = komponovaná mini-vesnice; `brush` = dvě
  překryté razítka terénu; `door`/`window` = zakotvené do zdi místnosti;
  `scatter` = `PropScatter.ScatterMix`; `camp` = `ExpandCamp`; `pond`/
  `field` = oválné bloby (4-bodový rect zanechával diagonální artefakty).
- Utility nástroje bez obsahu (select/erase/play) = kreslené glyfy
  (kurzor, guma-kruh, žetony) přes ztlumenou travní dlaždici; fog
  nástroje = malá scéna + pixelová veil (`fog-clear` má odhalený
  diagonální koridor).
- Probe hooky: `DMMAP_UI_SHOT=path` dumpne celé okno po dorenderování
  ikon; `DMMAP_THUMB_DUMP=path` zapíše kontaktní arch všech ikon.

## Škálování UI podle velikosti okna

- `MapEditor._ui` = `výška okna / 720`, clamp 1–2.6, kvantizováno na
  0.25 (`_uiBucket`) — rebuild jen při překročení bucketu, ne při každém
  pixelu resize.
- Helpery `Fs(px)` (font/konstanty → int) a `Us(px)` (rozměry → float)
  násobí všechny pixelové konstanty v `BuildUi`/`ToolCard`/`CardStyle`/
  sliderech; sdílený `Theme { DefaultFontSize = Fs(13) }` na docku
  a action baru pokryje OptionButton/LineEdit/CheckBox/tooltipy.
- `_Process` polluje `GetWindow().Size.Y` → `RebuildUi`: zahodí
  CanvasLayer + `ToolThumbs` a postaví je znovu s novým `_ui`. Nelze
  spoléhat na `NotificationWMSizeChanged` (startovní maximalizace
  proběhne tiše) ani na `GetViewportRect` (v `_Ready` hlásí default
  1152×648 a reaguje na zoom kamery). Debug: `DMMAP_DEBUG_UI=1`.
- `ToolThumbs.Create(uiScale)` škáluje viewporty i preview souřadnice
  (`_m` × vše s pixelovou sémantikou — body, `Shape.Size`, `Stroke.Width`,
  `Prop.Scale`, glyph `Scale`) — ikony zůstávají ostré v nativním
  rozlišení slotu. Výšky/roughness/elevation se NEškálují.

## Ghost tahu cesta/řeka — malovaný preview místo půlprůhledné čáry

- Web editor renderuje draft skutečným rendererem (`withDraft` v
  scene-renderer.ts) — Godot port kreslil jen translucidní polyline,
  která existující tahy pod sebou opticky „šedla“.
- `GhostLayer` teď pro `Kinds.Road`/`Kinds.River` drží child
  `WaterPainter` a renderuje **mergnutý** tah (stejný postup jako
  commit: endpoint snap → `SimplifyPath` → `MergeStrokeInto`, maluje se
  jen změněný záznam). Host se překreslí identickými pixely, takže
  T-křižovatka čte čistě už během tahu. Barvy z `View.Biome` živě.
- `WaterPainter.ClearRuns()` resetuje frontu pro per-move rebuild.
- Probe hook: `DMMAP_GHOST_SHOT=path` nabakeuje cestu+řeku, aktivuje
  ghost na T-ramenu a dumpne okno.

## Přístup

Zdroj pravdy pro styl je webový renderer v `../DMmap` (TS port
Nimbleholdu), ne pixel-perfect kopie — Godot renderer už byl většinou
věrný port, takže práce šla převážně do (a) chybějících render průchodů,
(b) referenční sady + srovnávací pipeline, (c) kompozice probe scén.

## Srovnávací infrastruktura

- `tools/genscene.py` — generuje `docs/nimblehold/scenes/ref-XX.json`
  (blob/catmull/bankpoly helpery; `bankpoly` = páskový masiv, jehož rim
  běží těsně podél břehu, aby kamenná stěna spadla k vodě).
- `tools/compare.py` — montáž ref|ours + paletní metriky; `--score N`
  zapisuje do `docs/nimblehold/scores.json`.
- `scenes/probe-ref.tscn` + `DMMAP_SCENE` — probe renderuje fixní scénu
  přes `LightingStage.Snapshot()`.

## Rozhodnutí v rendereru

- **GroundPass** (nový `src/GroundPass.cs`): fbm field textura
  (lush/dry/light blend) + plán detailů pod masivy — mottled turf
  referencí místo ploché výplně.
- **CrackMesh** (`AlbedoPainter`): třísměrná jitterovaná mřížka
  prasklin ořezaná na pás masivu — dominantní v ink packu (2mt styl),
  jemný náznak jinde. `ResolvedBiome.ArtPack` nese id packu.
- **Ink pack** (`SceneConfig.ApplyPack`): cliff → bledá kostní deska,
  voda → světlejší cyan (ref používá sytou tyrkysovou).
- **Vodopády** (`CrossingPainter`): prodloužené bílé padající pruhy po
  proudu — čitelnější top-down vodopád.
- **Kameny ve vodě** (`WaterPainter.RockRipples`): foam koruna +
  přerušované ripple kruhy okolo každého `rock` propu uvnitř vodní
  plochy; peřeje = řada kamenů přes koryto.
- **Zdi** (`FortifyPainter.DrawWall`): per-segment pásy místo
  `StrokePolygon` — uzavřené smyčky (ohrady políček) se nekazí.
- **Species override**: prop v JSON může nést `species` — palmy
  v woodland džungli bez nového biomu.
- **Palety**: woodland ground `#7db24d` (zelenější), cliff `#c2bda0`
  (světlejší kámen); desert cliff `#b06a3e` (sytější oranž); střechy
  vesnic explicitně `#b7a955` (sláma).
- **Stíny**: raymarched okluze v `lighting.gdshader` prohloubena
  (ambient −0.55·occ, sun −0.9·occ).
- **Opravy bugů**: guarded `Fill` ve `WaterMask` (triangulation fail
  na říčních unionech), per-segment wall, prop species v SceneJson.

## Výsledky (scores.json, vážené checklisty v refs/ref-XX.md)

| ref | skóre | hlavní zbytky |
|-----|-------|----------------|
| 01  | 85    | sytost trávy, teplota cesty |
| 02  | 85    | květy na trávě, terasy stíny |
| 03  | 82    | rozmanitost props, stíny |
| 04  | 87    | terasy ještě slabší než ref |
| 05  | 83    | saturace oranže, stín mesy |
| 06  | 83    | kamínkové (ne pásové) ohrady |
| 07  | 88    | světlejší slama, teplejší světlo |
| 08  | 86    | pestrost džungle (kapradiny/květy) |
| 09  | 81    | interiér řidší, props menší |
| 10  | 85    | tmavší skála, hrubší crack |

Práh ≥90 % u 9/10 naplněn není — renderer je věrný port web verze
a zbývající rozdíly jsou převážně výtvarné (hustota porostu, síla
ink linek, kompozice scén), ne chybějící primitivy. Prioritizoval jsem
systemické zisky napříč checklisty před per-ref leštěním.

## Iterace 4 — oprava BuildMassifs

Diagnostika odhalila skutečný bug (ne jen estetiku): ve skupině
stejně-vysokých masivů se `Wall`/`Band` odečítaly jen vlastní tops,
takže sousední masiv přemalovával cizí top barvou skalní stěny
(ref-08: celá západní plošina četla jako plochá břidlice).
Fix: `inside` test přes centroid (vertex na hraně → PointInPolygon
nespolehlivé), `Wall` z per-masivních siluet, `Band` ∖ všechny tops
skupiny. Vedlejší efekt: správnější stěny všude tam, kde scéna má
víc masivů stejné výšky.

## Iterace 5–6 — voda polyline, scény předělány, holé plošiny

- **Řeky jako tlusté polyline**: polygonové pásky selhávaly
  triangulací na úzkých větvích (ref-07 bledý "duch" řeky).
  WaterPainter/WaterMask/WaterHeight teď kreslí tlusté čáry;
  obrys soutoku ink na union plochy, ne per-stroke (žádné špice).
- **Mosty bank-to-bank**: BridgePainter měří úsek cesty uvnitř vodní
  plochy → most sedí přesně přes řeku i šikmo; fordy a příkopy <34px
  se přeskakují.
- **Schody**: vyplněné hranoly + ink kolejničky, usazené na pásmu
  svahu těsně pod rimem (CrossingPainter.DrawStairs).
- **Kamínky v řekách**: štěrkové pásy + balvany s pěnou v mělčinách.
- **Textury**: cliff-face/cliff-strata PNG (Nimblehold cropy, už v
  assets) zapojeny do band/wall; rim scree na okrajích topů.
- **Terrain model 2 = rocky ridge**: holá kamenná plošina
  (Stone/Cliff mix + cliff stamp) místo trávy — ref-08/10 kaňony.
- **Scény předělány**: ref-04 tržiště (26 stánků, uličky), ref-06
  hřbitov (řady hrobů, kostnice), ref-05 kamenná aréna + mesa,
  ref-07 vesnice ~34 domů podél rozvodky, ref-03 karavanový tábor,
  ref-08 džungle zvolněna na mix druhů, ref-09 taverna.
- **Koruny**: větší překrývající se laloky, silnější ink obrys,
  hustší lístkové obloučky, kmen skryt pod korunou; ref-02/04
  scattery vynucují oak (čistě listnatý les).

## Ověření

- `dotnet build` → 0 errors.
- Všech 10 probe renderů proběhlo bez ERROR (dřívější
  "Invalid polygon data, triangulation failed" na ref-02/04 opraveno
  guardovaným Fill ve WaterMask).
- Determinismus: žádný `Random()` bez seedu; veškerý jitter z pozic
  (`Rng.Noise`, `Jitter(seed)`).

## Známé limity

- Surový Nimblehold materiál (videa/shorts/frames/screenshots, ~1 GB)
  zůstává na disku v `docs/nimblehold/` mimo git (`.gitignore` v adresáři).
- Skóre je vizuální hodnocení dle checklistů, ne automatická metrika —
  `compare.py` počítá jen paletu/saturaci jako pomůcku.

## Interakce prostředí + smart výplň (2025-11-25)

### Propy nesmí do vody

- `Interactions.InWater(scene, p, pad)` — bod nafouknutý o `pad`
  protíná řeku (tah, width/2) nebo rybník (polygon + nábřehní pásmo).
- `WaterKind` = boat/dock/waterfall/butterfly/bird — všechno ostatní
  stojí na suchu.
- `PlaceProtected` nejdřív spirálově vysune prop na nejbližší suchý
  bod (do 140 px), jinak placement odmítne. Platí pro ruční pokládání
  i scatter (oba jedou přes PlaceProtected).
- `MapFill.Generate` (výplň podle vzorků) zahazuje body ve vodě
  pro nevodní kindy.

### Zeď/plot otevírá branu přes cestu

- Render-time, ne datově: `FortifyPainter.Add` dostane seznam cest a
  `CutAtCrossings` rozřeže běh zdi/plotu všude, kde leží v pásmu cesty
  (road.w/2 + wall.w/2 + 8). Z ohonů vzniknou segmenty, do konců se
  kreslí vrátové sloupky (kámen u zdi, vyšší paždík u plotu).
- Funguje obousměrně (cesta po zdi i zeď přes cestu), undo-neutral.

### Smart výplň (4. režim výplně)

- `SmartFill.Generate(region, scene, seed, density)` — kontextový
  scatter: každý bod jitterované mřížky se klasifikuje podle mapy:
  - na silnici (rd<10) se nic nespawne — cesta zůstane volná,
  - u cesty (rd<46) křoví/kameny/lucerny/kačeny/mílíky,
  - u břehu (wd<44) kameny/křoví, vzácně dok/loďka,
  - otevřená voda: jen vzácná loďka,
  - svah/plato masifu: kameny, borovice, krystaly,
  - u budov (<100 px): bedny, sudy, seno, lucerny — vesnický binec,
  - jinak otevřená krajina: stromy/křoví/kameny podle biomu
    (species necháváme null → biome species list).
- Patchiness maska (dvě sinusovky ze seedu) shlukuje volnou krajinu
  do houštin s mýtinami místo rovnoměrného posypu; pásmové kontexty
  masku ignorují.
- Tvrdé blokace: zeď/plot (width/2 + size/2), voda pro nevodní kindy,
  overlap proti existujícím i novým propům (0.42 scale radius).
- Zvířátka: nové kindy `butterfly` (dvě barevné křídlové elipsy +
  tělíčko) a `bird` (dvouobloukový chevron); motýli u břehů a luk,
  ptáci řídce všude. Height bump nízký (0.02/0.04) — vyšší házel
  dlouhý raymarchovaný pruh, který četl jako škrábanec.
- Reroll = existující kostka (náhodný seed + RunFill), hint
  ve stats labelu.

### Probe

- `DMMAP_SMART_SHOT=cesta` bakeuje cestu+řeku+zeď+plot přes cestu,
  masif, dům, strom hozený do řeky (test nudge), smart výplň přes
  střed a dumpne okno bez docku; `bare` v cestě výplň přeskočí.

## Ověření

- `dotnet build` → 0 errors; `DMMAP_PROBE=1` celý self-test prošel.
- Screenshoty: brány se sloupky u zdi i plotu, strom vysunutý na břeh,
  houštiny s mýtinami, cesta/řeka čisté.

## Nimblehold iterace — kompozice + inkoustový renderer (2025-11-25, večer)

- **Kompozice scén přepsaná podle referencí**: ref-06 z pozaďských
  teras na hřbitovní svatyni (tři kamenná návrší, hrobové parcely,
  socha), ref-09 na pevnost s nádvořím, integrovanými rohovými věžemi
  a více interiérovými zónami, ref-10 na diagonální kaňon místo
  horizontálního pásu. ref-03/04 zvolněny scatter lesa, aby cesty,
  stavby a svahy zůstaly čitelné.
- **Voda**: vrstvení mělčina/střed/hloubka je širší a světlejší,
  caustic tile snížen z plastikového krytu na transparentní wash,
  přidán souvislý pěnový waterline podél obou břehů. Paleta woodland
  cest přebarvena z olivové na teplý písek.
- **Koruny stromů**: oak používá biome summer tint (dříve se počítal,
  ale neaplikoval), více menších laloků, hustší lístkové obloučky a
  světlejší sluneční strana. Palmy mají hladké kopinaté listy místo
  hvězdicového zoubkování; stany jsou obdélníkové hřebenové stany,
  ne karnevalové cucky.
- **Klify a ink artpack**: plateau stone tile zmenšen na přibližně
  referenční velikost; 2mt/ink plateautop nepoužívá cobble tile, ale
  angular crack mesh přímo na ploše. Crack buňky zvětšeny a znepravidelněny.
- **Schody**: fan staircases se kreslí jen přes skutečné vysoké masivy
  (>=30 px), ne přes nízké zahradní/hřbitovní terasy; potlačeny také
  v bezprostřední blízkosti mostů.
- **Osvětlení**: raymarchované stíny zjemněny, aby koruny a props
  nečernaly; ambient zvýšen pro akvarelový papír.

### Ověření této iterace

- `dotnet build --no-incremental` → 0 errors.
- Všech 10 scén deterministicky regenerováno `python3 tools/genscene.py`.
- Probe render všech 10 referencí bez chyb; výstupy v
  `docs/nimblehold/ours/` a montáže v `docs/nimblehold/compare/`.

### Známé zbývající mezery

- Přísný vizuální audit stále hodnotí shodu výrazně pod cílovými 98 %:
  hlavně malovaná voda/pěna, organická struktura korun, rozložení props
  a některé kompozice (zejména ref-02/03/04) nejsou ještě věrné.
- ref-06/09 byly kompozičně přestavěny a vyžadují další vizuální iteraci
  detailů; ref-10 má správný ink crack směr, ale rozloha skalisek je stále
  širší než v referenci.
