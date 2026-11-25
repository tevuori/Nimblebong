# TODO

Sdílený seznam úkolů. Každá sekce patří jednomu agentovi/oblasti.
`[ ]` otevřené, `[~]` rozpracované, `[x]` hotové.

## Vizuály: Claude Code (`feat/enhance-visuals`)

Cíl: krajina DM mapy vypadá dobře a každý vizuální prvek jde přidat
přes JSON config (`assets/config/`) bez psaní C#. UI sem nepatří.

- [x] **Prop registry.** `assets/config/props/*.json` (schéma v README tam).
  Definice = vestavěný kreslič (`base`) + paleta/tint/HSV/scale/výška.
- [x] Prop registry: vizuálně ověřeno (`scenes/probe-flora.tscn`).
- [x] **Flora + smart fill z configu** (`fill.json`, `base: "flora"`).
- [ ] Flora: sezónní vzhled (podzimní hnědá tráva, zimní holé trsy), vítr/animace.
- [ ] Biomy: scatter lesů/porostu z JSONu (váhy prop id per biom místo
  natvrdo `(Tree 3, Pine 1.6, Bush 0.7)` v MapGenerator/MapEditor).
- [ ] **Deklarativní tvary.** Prop složený čistě z JSON vrstev (blob,
  elipsa, polygon, ink obrys, stín). Úplně nové objekty bez kódu.
- [ ] **Terén přes config.** Povrchové textury, styly břehů a útesů a
  ground scatter jako pluginy.
- [ ] **Gradient a typy cest (zatím nedělat).** Cesty jsou teď všude
  stejné. Rozlišit je (polní cesta / dlážděná silnice / stezka / ...)
  a dát jim gradient (střed vyjetý, okraje do trávy, přechod do okolí),
  vše konfigurovatelné v JSONu.
- [ ] Ladění vzhledu proti `docs/nimblehold/refs` (skóre ≥ 90 %).
  Blokováno: na stroji chybí Godot 4.7.2 mono pro probe rendery.
