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
| 01  | 80    | mottled tráva, sytost |
| 02  | 74    | terasy slabší stíny, mottle |
| 03  | 74    | rozmanitost props, stíny |
| 04  | 76    | hustota korun vs. stovky v ref |
| 05  | 78    | interiér věže řidší |
| 06  | 78    | kamínkové (ne pásové) ohrady |
| 07  | 77    | vesnice řidší než ref cluster |
| 08  | 73    | džungle řidší, stěny plošší |
| 09  | 74    | nábytek řidší, křižák menší |
| 10  | 76    | crack jemnější, most menší |

Práh ≥90 % u 9/10 naplněn není — renderer je věrný port web verze
a zbývající rozdíly jsou převážně výtvarné (hustota porostu, síla
ink linek, kompozice scén), ne chybějící primitivy. Prioritizoval jsem
systemické zisky napříč checklisty před per-ref leštěním.

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
