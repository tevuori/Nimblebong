# Config: jak vypadá mapa

Všechno, co určuje vzhled krajiny, je v JSON souborech v téhle složce.
Nový biom, světelná nálada nebo objekt na mapě znamená nový nebo upravený
JSON, bez úprav C#.

| Soubor | Co určuje | Loader |
|---|---|---|
| [`biomes/<id>.json`](#biomy-biomesidjson) | paleta krajiny, sezóny, porost, střechy, voda, cesty | `SceneConfig.LoadBiome` |
| [`moods.json`](#nálady-a-světlo-moodsjson) | slunce, stíny, ambient, denní doba | `SceneConfig.LoadMoods` |
| [`trees.json`](#stromy-treesjson) | druhy stromů, sezónní barvy listí, sníh | `SceneConfig.Trees` |
| [`props/<id>.json`](props/README.md) | vlastní objekty na mapě (varianty skal, stromů, ruin…) | `PropDefs` |
| [`placement.json`](#pokládání-objektů-placementjson) | ochranné zóny kolem táboráků, studní apod. | `Placement` |

## Společná pravidla

- **Id = název souboru** bez `.json` (`biomes/woodland.json` → `woodland`,
  `props/mossy-boulder.json` → `mossy-boulder`). Scéna ukládá jen id.
- **Barvy:** `"#rrggbb"` nebo `"#rrggbbaa"`. V prop definicích navíc
  `"@jméno"`, což je barva aktivního biomu.
- **Validace:** neznámý klíč vypíše varování (`PushWarning`) a soubor se
  načte dál. Chybějící nebo špatná hodnota vyhodí `ConfigException`
  s přesnou cestou, např. `biomes.desert.ground.stones: expected number ≥ 0`.
  Překlep se tak projeví hned, ne rozbitou mapou.
- **Ověření:** `DMMAP_PROBE=1 <godot> --path . scenes/probe-config.tscn`
  vyrenderuje každý biom a mood do `/tmp/godot-config-*.png`, zvaliduje
  všechny prop definice a skončí s exit kódem = počet chyb.
- **Determinismus:** configy nic náhodného nezavádějí. Variace
  (jitter barev, velikostí) se seeduje z pozice objektu.

## Jak se skládá výsledná paleta

```
biomes/<id>.json  palette + colors
        │
        ▼  art pack (classic | storybook | ink)   SceneConfig.ApplyPack
        ▼  sezónní grade (look: spring/summer/autumn/winter)   ApplySeasonGrade
        ▼  přepisy ze seasons.<sezóna> (ground, roads, water)
        ▼
  ResolvedBiome  →  painters (terén, voda, cesty, props)
```

Kombinace `classic` + léto bez přepisů paletu nemění. Art packy a
sezónní grade jsou zatím v C# (viz [Omezení](#omezení-todo)).

---

## Biomy (`biomes/<id>.json`)

Nový biom vytvoříš zkopírováním existujícího souboru (nejúplnější je
`woodland.json`) a přepsáním hodnot. Objeví se automaticky v seznamu
biomů a scény ho můžou používat. Všechny sekce níže jsou **povinné**,
kromě `colors` a toho, co je označené jako volitelné.

### `label` (string)
Český název pro UI, např. `"Zelené pohraničí"`.

### `palette`: sedm základních barev (všechny povinné)

| Klíč | Použití |
|---|---|
| `ground` | tráva / zem |
| `cliff` | stěny masivů a útesů |
| `water` | řeky, jezera |
| `foliage` | koruny stromů, keře |
| `stone` | kameny, zdi, mosty |
| `roof` | střechy |
| `wood` | dřevo (ploty, mola, kmeny) |

### `seasons`: které sezóny biom nabízí (aspoň jedna)

Klíče: `spring`, `summer`, `autumn`, `winter`. **První uvedená je
výchozí.** Biom bez zimy prostě `winter` nemá.

```jsonc
"winter": {
  "look": "winter",                 // povinné: jaký sezónní grade použít
  "label": "Zima",                  // volitelné
  "ground": "#dfe6e3",              // volitelné: přepis barvy země
  "scatter": { "snow": 3, "grass": 0.25 },  // násobiče: flowers, grass, leaves, snow
  "roads": { "dirt": "#b5a991", "stone": "#aab0ae", "verge": "#c3cbc7", "halo": "#ffffff40" },
  "water": { "bank": "stone", "edge": "#e8eef0" }  // bank: grass/sand/stone
}
```

Nad tím se vždycky uplatní výchozí sezónní násobiče porostu (jaro má víc
květin, podzim listí, zima sníh). Jsou v `ResolvedBiome.SeasonScatter`.

### `labels`: názvy pro UI (všechny povinné)
`trees`, `bush`, `house`, `scatter`, např. `"trees": "Listnaté stromy"`.

### `trees`
| Klíč | Hodnota |
|---|---|
| `style` | `broadleaf`, `palm`, `pine`, `mangrove`, `charred`, `mushroom` |
| `species` | **přesně 3** druhy z `trees.json` (`oak`, `spruce`, `birch`, `palm`, `acacia`, `cactus`, `snowfir`, `pine`, `bare`, `mangrove`, `willow`, `cypress`, `charred`, `stump`, `deadtree`, `mushroom`, `shroomcluster`, `coral`) |
| `summerTint` | seznam `{ "to": "#color", "amount": 0–1 }`: letní barevné varianty korun |

### `ground`: porost a detail na zemi
| Klíč | Hodnota |
|---|---|
| `dirt` | barva hlíny (povinné) |
| `dryGrass` | barva suché trávy (volitelné) |
| `grass`, `stones`, `flowers`, `patches`, `leaves`, `snow` | hustota, číslo ≥ 0 (1 = normál, 0 = vypnuto) |
| `flowerColors` | seznam barev květin |
| `extra` | doplněk: `twigs`, `ripples`, `snow`, `puddles`, `cracks`, `crystals` |
| `grassTile` | 0–1, síla ručně malované textury trávy (0 pro písek, popel, sníh, jeskyni) |

### `roofs`
| Klíč | Hodnota |
|---|---|
| `material` | `tile`, `shingle`, `slate`, `flat` |
| `chimney` | bool |
| `moss` | 0–1 |
| `extra` | `none`, `snow`, `ember-vent`, `crystal-lamps` |
| `winterSnow` | bool, sedlové střechy mají v zimě sníh |

### `water`
`bank`: styl břehu `grass` / `sand` / `stone`. `edge`: barva okraje
hladiny nebo `null`.

### `roads`
Barvy cest: `dirt` (polní), `stone` (dlážděná), `verge` (krajnice),
`halo` (světlý lem, typicky s alfou).

### `colors` (volitelné): pojmenované barvy detailů
Vždy mají hodnotu (výchozí, když chybí): `hill`, `plateauTufts`,
`bankReeds`, `causeway`, `market`, `plaza`, `cemetery`, `gate`, `flag`,
`bale`, `window`, `bed`, `bushBlossom`, `tentHighlight`, `pillarRune`.

Vypnuté, dokud je biom nenastaví (může být i `null`): `lilyPads`,
`waterDeep`, `waterShallow`, `fieldStalks`, `grave`, `statueGem`,
`stonecircle`, `pillarGem`, `graveGem`.

Všechny barvy biomu (`palette.*`, `colors.*`, `road.*`, `ground.dirt`, …)
jsou dostupné v prop definicích jako `"@jméno"`.

---

## Nálady a světlo (`moods.json`)

```jsonc
{
  "shadowless": ["label", "terrain"],          // druhy objektů bez stínu
  "suns": {                                    // pojmenované pozice slunce
    "high": { "dir": [-0.5, -0.62], "elev": 0.12 }
  },
  "channels": {                                // světelné kanály (pro UI rozpočtu světla)
    "sun": { "color": "#ffd9a0", "budget": 0.55, "intensity": 0.6 }
  },
  "moods": {
    "dusk": {
      "sun": "west",          // id z "suns"
      "shadows": true,
      "ambient": 0.4,         // spodní hranice jasu (≥ 0); menší = tmavší
      "sunColor": "#7a4526",  // barva přímého světla
      "strength": 1,          // násobič sunColor (≥ 0)
      "emissive": true        // zatím se nepoužívá (rezerva pro svítící povrchy)
    }
  }
}
```

- `dir` je směr **ke slunci** v souřadnicích mapy (y roste dolů).
- `elev` je výška slunce: **menší číslo = delší stíny** (úsvit/soumrak ~0.06,
  poledne ~0.12).
- Nový mood = nový klíč v `moods`. Scény ho můžou hned používat.

---

## Stromy (`trees.json`)

```jsonc
{
  "species": {
    "oak": { "size": 1, "shadow": 1, "deciduous": true, "evergreen": false }
  },
  "autumn": {
    "colors": [ { "to": "#c0472a", "amount": 0.45 }, null ],  // null = strom bez podzimní změny
    "evergreenTint": { "to": "#3f5f3a", "amount": 0.55 }
  },
  "spring": { "blossomChance": 0.4, "blossomColors": ["#fff6f0", "#f6b8c8"] },
  "winter": { "snowColor": "#f4f7f8", "coniferSnow": 0.7, "snowfirSnow": 0.85 }
}
```

- `size` je násobič velikosti, `shadow` násobič stínu.
- `deciduous`: na podzim se barví a v zimě je holý. `evergreen`: celý rok
  zelený (na podzim jen `evergreenTint`).
- Tvar koruny kreslí C# podle id druhu (`PropsTrees.cs`). Nový **tvar**
  stromu proto zatím vyžaduje kód. Nová **barevná varianta** existujícího
  druhu se dělá přes prop definici (`"base": "tree", "species": "oak"`).

---

## Objekty na mapě (`props/<id>.json`)

Viz [props/README.md](props/README.md). Varianta objektu (barvy,
velikost, výška stínu, biomy, ochranná zóna) postavená na existujícím
kresliči.

## Pokládání objektů (`placement.json`)

```jsonc
{
  "protection": {
    "enabled": true,        // false = ochrana vypnutá globálně
    "padding": 6,           // px navíc k poloměru zóny
    "clearOnPlace": true,   // položení chráněného objektu vyčistí okolí
    "kinds": { "campfire": 1, "tent": 0.9 },   // chráněné druhy a faktor zóny
    "except": ["label", "marker", "door", "window"]  // smí stát v zónách
  }
}
```

Poloměr zóny je `velikost/2 × faktor + padding`. Ostatní objekty se ze
zóny při pokládání odsunou. Když se odsunout nedají (okraj mapy), nepoloží
se. Ochranu jde za běhu vypnout checkboxem „Ignorovat ochranné zóny“
v editoru (`Placement.IgnoreProtection`). Platí jen do zavření aplikace.
Prop definice můžou pravidla přepsat (`protect`, `keepOut`). Jinak dědí
od svého `base`.

---

## Rychlé recepty

| Chci… | Udělám |
|---|---|
| nový biom | zkopíruju `biomes/woodland.json` → `biomes/<id>.json`, upravím barvy |
| jinou zimu v jednom biomu | `seasons.winter` v tom biomu (`ground`, `scatter`, `roads`, `water`) |
| tmavší noc | `moods.night.ambient` níž, `strength` níž |
| delší stíny při západu | `suns.west.elev` níž |
| novou variantu skály / stromu | `props/<id>.json` s `"base": "rock"` / `"tree"` |
| objekt, který se přizpůsobí biomu | v prop definici barvy jako `"@foliage"`, `"@stone"`, … |
| aby se kolem studny nesázely stromy dál | `placement.json` → `kinds.well` vyšší faktor |
| zrušit ochranné zóny | `placement.json` → `"enabled": false` |

## Omezení (TODO)

Pořád v C#, kandidáti na přesun do configu:

- art packy `storybook`/`ink` a sezónní grade (`SceneConfig.ApplyPack`,
  `ApplySeasonGrade`),
- výčty stylů (`trees.style`, `ground.extra`, `roofs.material/extra`,
  `water.bank`). Nová hodnota potřebuje kód, který ji nakreslí,
- barva magické záře podle biomu (`GlowPainter`),
- `moods.*.emissive` se načítá, ale renderer ho zatím nevyužívá,
- tvary objektů (nové tvary jen přes C# kresliče; plánované: deklarativní
  tvary v JSONu),
- poměr druhů stromů při generování lesa (`MapGenerator`, `MapEditor`),
- typy a gradient cest.
