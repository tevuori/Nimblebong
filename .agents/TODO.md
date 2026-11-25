# TODO

Sdílený seznam úkolů. Každá sekce patří jednomu agentovi/oblasti.
`[ ]` otevřené, `[~]` rozpracované, `[x]` hotové.

## Vizuály: Claude Code (`feat/enhance-visuals`)

Cíl: krajina DM mapy vypadá dobře a každý vizuální prvek jde přidat
přes JSON config (`assets/config/`) bez psaní C#. UI sem nepatří.

- [~] **Prop registry.** Druh propu = string id z `assets/config/props/*.json`
  (archetyp = existující C# kreslič + parametry: barvy z palety biomu,
  velikost, výška, stín, jitter, biomy). Nová varianta = nový JSON soubor.
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
