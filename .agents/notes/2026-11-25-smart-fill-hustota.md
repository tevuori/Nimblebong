# Smart fill — hustota (2026-11-25)

## Diagnostika

Hodnota slideru se do `SmartFill.Generate` předávala správně
(`_fillDensity.Value / 100`) a `FillLayer.Spacing(density)` ji používá.
Programová změna slideru zpustila `ValueChanged` a počty objektů se skutečně
měnily:

- testovací oblast SmartShot: 15 % → 94 objektů (20 hlavních),
  100 % → 230 objektů (61 hlavních)
- generovaná krajina s existujícími propy: 15 % → 233,
  100 % → 529

Hustota tedy nebyla algoritmicky mrtvá. Hlavní UX problém byl v tom, že
slider spouštěl kompletní context sampling synchronně pro každou drobnou
změnu. U větší scény to zbytečně blokovalo interakci a výsledek působil,
jako by se nic nedělo. Bez aktivního náhledu navíc slider logicky nic
negeneruje — hustota/seed se mají ladit až po vytáhnutí oblasti.

## Řešení

- `QueueFillRegen()` slučuje změny slideru do jednoho přepočtu po 160 ms.
- `CommitFill()` před zápisem do historie čekající přepočet flushne,
  aby se necommitl starý náhled.
- UI ukazuje procenta a statistiky obsahují hustotu i počet nových objektů.
- Tooltip popisuje správný workflow.

## Pozor

`MapGenerator.GenerateIn` (režimy Krajina/Dungeon) hustotu vůbec nepoužívá.
Pokud uživatelé čekají hustotu i tam, je to samostatný feature request —
nehledat to v `SmartFill`.
