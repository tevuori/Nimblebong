# Prop definice (`assets/config/props/*.json`)

Každá definice platná v aktuálním biomu se **sama objeví v paletě nástrojů
editoru** v sekci podle `category`, i s živým náhledem. Kliknutím se pak
pokládá jako kterýkoli jiný objekt.

Každý soubor = jeden druh objektu na mapě. **Id = název souboru** (bez
`.json`) a scéna ho ukládá do `prop.kind`. Definice znovu použije
vestavěný C# kreslič (`base`) a přebarví/přeškáluje ho, takže na novou
variantu stačí nový JSON bez C#.

```jsonc
{
  "label": "Mechový balvan",        // povinné, česky (pro UI)
  "base": "rock",                   // povinné, vestavěný druh: tree, pine, rock, bush, house,
                                    //   tent, campfire, well, ruin, grave, bones, statue,
                                    //   stonecircle, crystal, bale, crate, barrel, tower, stall,
                                    //   cart, flag, barricade, lantern, fountain, pillar, marker,
                                    //   dock, boat, chest, portal, door, window, table, bed,
                                    //   shelf, rug, fireplace, butterfly, bird, flora
  "category": "Porost a příroda",   // sekce palety nástrojů v editoru (název sekce;
                                    //   existující sekce se sloučí, nová se vytvoří; chybí = „Další“)
  "biomes": ["woodland", "swamp"],  // volitelné, prázdné/chybí = všechny biomy
  "scale": 1.25,                    // násobič velikosti (default 1)
  "footprint": 1.0,                 // násobič poloměru výškového hrbolu (default 1)
  "height": 1.2,                    // násobič výšky hrbolu, delší stín (default 1)
  "species": "birch",               // výchozí druh stromu (jen tree), viz trees.json
  "model": 2,                       // výchozí varianta (-1 = podle seedu)
  "palette": {                      // přepis barevných slotů kresliče
    "stone": "#8c9278"              //   foliage, foliageWarm, foliageCool, pine, stone,
  },                                //   wood, wall, roof, ink
  "tint": { "color": "@foliage", "amount": 0.22 },   // přimíchá barvu do všeho (0–1)
  "adjust": { "hue": 0.0, "saturation": 1.0, "value": 1.0 }, // HSV: hue v otáčkách ±1
  "protect": 1.2,                   // ochranná zóna (faktor), 0 = žádná; chybí = podle base
  "keepOut": false,                 // musí stát mimo cizí zóny? chybí = podle base
  "water": true                     // smí stát ve vodě (rákos, lekníny); chybí = podle base
}
```

Barvy jsou `"#rrggbb"`, `"#rrggbbaa"`, nebo `"@jméno"`, což je barva
aktivního biomu (`@foliage`, `@stone`, `@roof`, `@ground`, `@water`, …).
Takový prop se pak přizpůsobí biomu i sezóně.

Neznámý klíč vypíše varování a špatná hodnota vyhodí `ConfigException`
s přesnou cestou (`props.mossy-boulder.tint.amount: expected number in [0, 1]`).

## Ochranné zóny (`assets/config/placement.json`)

Chráněné objekty (táborák, studna, …) mají kolem sebe kruh o poloměru
`velikost/2 × faktor + padding`. Ostatní objekty se z něj při pokládání
odsunou. Položení chráněného objektu (`clearOnPlace`) vyčistí kruh od
stromů, keřů apod. Druhy v `except` smí stát kdekoli.

Definice bez `protect`/`keepOut` dědí pravidla svého `base`, takže
varianta táboráku je chráněná automaticky.

**Vypnutí:** `"enabled": false` v `placement.json` vypne ochranu globálně.
Za běhu to jde přes checkbox „Ignorovat ochranné zóny“ v editoru
(`Placement.IgnoreProtection`).

## Flora: rostliny (`"base": "flora"`)

Rostliny kreslí jeden parametrický kreslič (`src/Props/PropsFlora.cs`) se
sedmi tvary při pohledu shora. Konkrétní druh rostliny je jen JSON
s blokem `flora`, který je pro `base: "flora"` povinný:

```jsonc
{
  "label": "Luční květiny",
  "base": "flora",
  "category": "flora",
  "biomes": ["woodland", "alpine"],
  "scale": 0.65,
  "height": 0,                      // ploché rostliny (lekníny) bez stínu
  "flora": {
    "form": "flowers",              // tvar, viz tabulka
    "count": [5, 8],                // počet stébel/listů/hlav… (číslo nebo [min, max])
    "radius": 8,                    // rozptyl trsu (lokální px, prop 24 px ≈ 12)
    "length": [5, 9],               // délka stébel/listů (číslo nebo [min, max])
    "width": 1.2,                   // tloušťka stébel a žeber
    "leaf": "@foliage",             // barva listí ("#hex" nebo "@barvaBiomu")
    "leafDark": "#3e6233",          // tmavší tón; chybí = leaf ztmavené
    "accents": ["#f2d14b", "#d9534f"], // květy, klobouky, palice rákosu
    "accentSize": 1.7,              // velikost akcentu
    "accentChance": 0.6,            // podíl stébel/listů s akcentem (0–1)
    "ink": 0.3                      // síla ink obrysu (0–1)
  }
}
```

| `form` | Vzhled | Akcenty |
|---|---|---|
| `tuft` | trs stébel trávy | občas kvítek na špičce |
| `fern` | růžice kapradí s lístky | — |
| `flowers` | růžice listů + pětilisté květy | barvy květů |
| `reeds` | stébla nakloněná větrem | palice orobince |
| `mushrooms` | shluk klobouků s ink lemem | barva klobouku, bílé tečky |
| `lilypads` | vroubkované listy na vodě | květ leknínu |
| `groundcover` | nízké překrývající se skvrny (jetel, vřes) | drobné kvítky |
| `flowerpatch` | pole 15–25 kytek v nepravidelném trsu + tráva | viz `heads` |

**Květy (`heads`):** pro `flowers` a `flowerpatch` seznam druhů květů,
každé stéblo si vybere jeden: `simple`, `dandelion`, `poppy`, `daisy`,
`bluebell`, `cornflower`, `lavender`, `sunflower`, `tulip`, `clover`.
Výchozí barvy a velikost každého druhu jsou v `assets/config/flowers.json`.
`accents` je přepíše. Další klíče: `underlay` (0–1, tráva pod polem),
`puffChance` (0–1, podíl odkvetlých pampelišek).

```jsonc
{ "label": "Vlčí máky", "base": "flora", "category": "Květiny",
  "flora": { "form": "flowerpatch", "count": [16, 24], "radius": 16, "heads": ["poppy"] } }
```

## Detaily (`"base": "detail"`)

Mapové detaily a krytí pro DM. Kreslí je `src/Props/PropsDetail.cs`
a blok `detail` má stejné klíče jako `flora`. `leaf` je hlavní materiál
(kůra, kámen, bahno), `leafDark` jeho stín a `accents` doplňky (mech,
plody, svíčky, žhavé uhlíky, včely).

| `form` | Co | Důležité klíče |
|---|---|---|
| `log` | padlý kmen s letokruhy, mech = accents | `length`, `width` (průměr) |
| `stump` | pařez s kořeny | `radius` |
| `bramble` | ostružiní / trní, plody = accents | `radius`, `count`, `length` |
| `rubble` | suť, kameny | `radius`, `count`, `length` (velikost kamene) |
| `cairn` | mohyla z kamenů | `radius`, `count` (počet pater) |
| `signpost` | rozcestník | `length`, `width`, `count` (počet šipek) |
| `shrine` | svatyně, svíčky = accents | `radius`, `count` |
| `tracks` | stopy zvěře | `length`, `count`, `width`, `accentSize` |
| `puddle` | louže / bahniště, voda = accents | `radius` |
| `carcass` | kostra / mršina | `length`, `width` |
| `anthill` | mraveniště | `radius`, `count` (mravenci) |
| `beehive` | úl, včely = accents | `radius`, `count` |
| `stalagmite` | krápníky (i ledové) | `count`, `length`, `radius` |
| `web` | pavučina, pavouk = accents | `radius`, `count` (paprsky) |

Výškový hrbol (stín) flory i detailů se odvozuje z jejich velikosti.
`height` ho násobí a `0` znamená bez stínu (louže, stopy, lekníny).

Barvy `@foliage`/`@ground` se mění se sezónou a art packem biomu.

**V editoru:** rostliny jsou v sekci palety „Flora“ (klik = jeden trs).
Nástroj **Štětec vegetace** sází vybranou rostlinu v trsech podél tahu
myší. Volba „Směs (podle okolí)“ vybírá z vrstvy `flora` ve `fill.json`
podle místa: ve vodě `shallows`, u vody `shore`, jinde `land`. Velikost
štětce a hustota se nastavují v panelu. Štětec respektuje existující
objekty, vodu i ochranné zóny (včetně checkboxu „Ignorovat ochranné zóny“).
Zobrazit všechny definice biomu jako galerii + test smart fillu:
`DMMAP_BIOME=<biom> <godot> --path . scenes/probe-flora.tscn` →
`/tmp/godot-flora-<biom>.png`.
