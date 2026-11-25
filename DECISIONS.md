# DECISIONS.md — Nimblehold parity (Enhance.md)

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
