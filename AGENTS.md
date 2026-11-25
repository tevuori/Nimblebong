# AGENTS.md

Společné instrukce pro **všechny AI agenty** pracující na tomhle repu
(Claude Code, Codex, Cursor, Copilot, Gemini, …). Tohle je jediný zdroj
pravdy. Nástrojově specifické soubory (`CLAUDE.md` apod.) sem jen odkazují
a nemají obsahovat nic, co by tu chybělo.

## Projekt

DMmap-Godot: 2.5D editor tabletop map ve stylu Nimbleholdu
(terrain → heightfield → GPU lighting). Godot 4.7.2 mono, C# (`net8.0`),
renderer `gl_compatibility`. Dokumentace a UI jsou česky.

| Co | Kde |
|---|---|
| Architektura, render pipeline, pravidla | `ARCHITECTURE.md` (přečti před zásahem do painterů/editoru) |
| Aktuální úkol: Nimblehold parita | `Enhance.md` |
| **Config vzhledu (biomy, moody, stromy, props, pokládání)** | `assets/config/README.md` (reference všech JSON) |
| Reference, checklisty, skóre | `docs/nimblehold/` (`refs/ref-XX.{png,md}`, `scores.json`, `analysis.md`, `features.md`) |
| Zdrojáky | `src/` (painters v rootu, `Editor/`, `Bridge/`, `Props/`, `Model/`, `Config/`) |
| Probe scény | `scenes/probe-*.tscn` |
| Nástroje | `tools/compare.py`, `tools/genscene.py` |
| Rozhodnutí + předpoklady | `DECISIONS.md` (vytvoř, pokud chybí) |

## Build a ověření

```
dotnet build                   # 0 errors je povinné; občas tiše skipne → --no-incremental
DMMAP_PROBE=1 <godot> --path . [scenes/probe-x.tscn]   # dumpne /tmp/godot-*.png
python3 tools/compare.py ...   # naše vs. ref vedle sebe + skóre
```

- Probe **nikdy** s `--headless`: dummy rasterizer nemá texture readback.
- Probe env: `DMMAP_PROBE`, `DMMAP_BIOME`, `DMMAP_SEASON`, `DMMAP_ARTPACK`,
  `DMMAP_BRIDGE_PROBE` (jen s běžícím backendem na :3000).
- Probe scénu předej jako poziční argument a **neměň** `run/main_scene`.

## Pravidla kódu (podrobněji `ARCHITECTURE.md`)

- Deterministika: žádné `Random`/`GD.Rand*`. Seeduj z pozic přes `Rng.Noise`.
- Polygony kresli vždy přes `Fill(poly, color)`. Self-intersecting čisti
  přes `Geometry2D.MergePolygons(p, p)`.
- Nový vizuální prvek = nový painter soubor, integrace přes `LightingStage`.
  Painters nesahají na cizí vrstvy.
- Vizuální parametry patří do JSON configu (`assets/config/`), ne do
  C# konstant. Nový config klíč zdokumentuj v `assets/config/README.md`.
- Editor změny jdou přes `SceneHistory` (undo). Tah/slider = živá mutace
  a pak jeden commit.
- Nepřidávej `.uid`/`.import` ručně, generuje je Godot.

## Spolupráce více agentů

Na repu pracuje víc AI (a lidé) střídavě, někdy i souběžně. Proto:

1. **Začni stavem repa:** `git status`, `git log -10`, přečti
   `.agents/HANDOFF.md`. Necommitnuté změny, které nejsou tvoje, nepřepisuj
   ani nerevertuj. Zeptej se, nebo pracuj okolo nich.
2. **Malé, samostatné commity** s popisnou zprávou (co a proč). Každý commit
   musí projít `dotnet build`. Nikdy `push --force`, `reset --hard` ani
   rebase cizí práce.
3. **Na konci session aktualizuj `.agents/HANDOFF.md`:** co jsi udělal, co
   je rozpracované, co nefunguje a co dál. Nejnovější záznam patří nahoru
   a obsahuje datum + jméno agenta.
4. **Trvalé poznatky** (diagnostiky, iterační logy, proč něco nefunguje)
   ukládej do `.agents/notes/YYYY-MM-DD-tema.md`, ne do soukromých složek
   jednotlivých nástrojů. Ostatní agenti je jinak neuvidí.
5. **Rozhodnutí a předpoklady** zapisuj do `DECISIONS.md`.
6. **Souběžná práce:** jeden soubor/painter = jeden agent naráz. Pokud
   potřebuješ izolaci, pracuj v git worktree nebo na větvi a merguj až po
   buildu a probe.
7. **Soukromé složky nástrojů** (`.claude/`, `.codex/`, `.cursor/`, …)
   slouží jen pro konfiguraci daného nástroje. Ostatní agenti je nemusí číst.

## Prostředí

Cesty v `ARCHITECTURE.md`/`Enhance.md` (`/home/tevuori/...`) pocházejí
z jiného stroje. Na aktuálním stroji neexistují, proto si cestu ke Godot
4.7.2 mono binárce ověř. Webová TS reference (`DMmap`) a Nimblehold analýza
tu nemusí být k dispozici. Jako zdroj pravdy pak slouží `docs/nimblehold/`.
