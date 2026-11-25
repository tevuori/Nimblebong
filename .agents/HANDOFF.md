# Handoff log

Nejnovější záznam nahoře. Formát: `## YYYY-MM-DD — <agent>`, pak sekce
Hotovo / Rozpracováno / Problémy / Další kroky.

## 2026-11-25 (3) — Claude Code (Opus 5.5), `feat/enhance-visuals`

**Hotovo:**
- **Flora:** nový základní druh `flora` (`src/Props/PropsFlora.cs`) se
  7 tvary (tuft, fern, flowers, reeds, mushrooms, lilypads, groundcover).
  12 rostlin jako JSON v `assets/config/props/` (tráva, kapradí, květiny,
  rákos, houby, lekníny, jetel, vřes, pouštní flora, svítivé houby…).
- **SmartFill z configu:** tabulky, velikosti, prahy kontextů a vrstvy
  (`main`, `flora`, `critters`) jsou v `assets/config/fill.json`
  (`FillRules`). Definice mimo biom scény z tabulek vypadnou automaticky.
  Nové kontexty `shallows`/`deepWater`, per-biome `biomeChance`.
  Chování vrstvy `main` odpovídá původnímu kódu (+ pár variant z props).
- `PropDef.water` / `Interactions.WaterKind` respektuje definice.
- `scenes/probe-flora.tscn`: galerie definic biomu + smart fill test
  (`DMMAP_BIOME`, `DMMAP_OUT`). Ověřeno renderem woodland/desert/alpine/cavern.
- Godot .NET cesta v AGENTS.md.

**Pro autora SmartFill (Devin):** tabulky se přesunuly z `SmartFill.cs`
do `fill.json`. Úpravy vah prosím dělej tam.

## 2026-11-25 (2) — Claude Code (Opus 5.5), `feat/enhance-visuals`

**Hotovo:**
- `assets/config/placement.json` + `Placement`: ochranné zóny z configu.
  Prop definice je přepisují (`protect`, `keepOut`). Checkbox
  „Ignorovat ochranné zóny“ v sekcích Varianta a Štětec.
- `Kinds.Biomes`/`Kinds.Moods` se berou z configu. Nový biom/mood v JSON
  už neztratí scéna při načtení.
- `assets/config/README.md`: kompletní reference všech JSON configů
  (+ odkaz v AGENTS.md, pravidlo „vizuální parametry do configu“).

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
