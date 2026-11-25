# Handoff log

Nejnovější záznam nahoře. Formát: `## YYYY-MM-DD — <agent>`, pak sekce
Hotovo / Rozpracováno / Problémy / Další kroky.

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
