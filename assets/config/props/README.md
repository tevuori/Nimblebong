# Prop definice (`assets/config/props/*.json`)

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
                                    //   shelf, rug, fireplace
  "category": "nature",             // volitelné, skupina pro paletu nástrojů
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
  "adjust": { "hue": 0.0, "saturation": 1.0, "value": 1.0 }  // HSV: hue v otáčkách ±1
}
```

Barvy jsou `"#rrggbb"`, `"#rrggbbaa"`, nebo `"@jméno"`, což je barva
aktivního biomu (`@foliage`, `@stone`, `@roof`, `@ground`, `@water`, …).
Takový prop se pak přizpůsobí biomu i sezóně.

Neznámý klíč vypíše varování a špatná hodnota vyhodí `ConfigException`
s přesnou cestou (`props.mossy-boulder.tint.amount: expected number in [0, 1]`).
