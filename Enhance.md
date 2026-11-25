# Enhance.md — Napodobit Nimblehold

## Autonomous Execution Rules
- NEVER pause to ask clarifying questions or request confirmation from the user.
- If requirements are ambiguous, make the most pragmatic, standard architectural decision, document your assumption in `DECISIONS.md`, and continue working.
- Do NOT stop after planning. Execute the full implementation end-to-end.
- You are only done when all acceptance criteria are met and all tests/linters pass with exit code 0.

Dodatečné pravidlo pro tento úkol: práci smíš přerušit a zeptat se uživatele
jen tehdy, když ti chybí assety, které se nepodařilo získat z Nimblehold
materiálů. Jinak pracuj plně autonomně (bypass privilege).

---

## Cíl

Přizpůsobit a vylepšit Godot editor (`/home/tevuori/Documents/DnD/DMmap-Godot`)
tak, aby **vypadal a reagoval co nejvěrněji jako Nimblehold** (Crosshead /
Hexcode) — cozy kreslený mapmaking tool pro tabletop mapy. Nejde o 1:1 kopii —
jde o to, aby renderer, nástroje, interakce a celkový pocit (ink + watercolor +
světla/stíny + živá reaktivita prvků) odpovídaly referenčním materiálům.

Iteruj dokud **nejsi spokojen alespoň na 90 % u 9 z 10 kontrolních obrázků**.

---

## Fáze 1 — Sběr materiálů

Vytvoř `docs/nimblehold/` a nasbírej do něj všechno:

### 1a. Steam stránka

`https://store.steampowered.com/app/4693100/Nimblehold/?beta=1`

- Stáhni všechny screenshoty a trailer(y) — asset URL jsou v HTML stránky
  (`shared.akamai.steamstatic.com/store_item_assets/steam/apps/4693100/...`),
  případně přes Steam API `store.steampowered.com/api/appdetails?appids=4693100`.
- Zapiš si feature copy do `docs/nimblehold/features.md` — ze stránky už teď
  vím: wall freehand → building, oval tool, pull building taller/shorter
  (stín se posouvá), group recolor, road→river = auto bridge (vyměnitelný
  styl), river bank styles (grass/sand/stone), hill+river = auto waterfall,
  region fill (village), art packs, animated maps.

### 1b. YouTube shorts — Crosshead Studios

`https://www.youtube.com/@CrossheadStudios/shorts`

- `yt-dlp` je na `~/.local/bin/yt-dlp`, `ffmpeg` na PATH.
- Playlist listing:
  `yt-dlp --flat-playlist --print "%(id)s %(title)s" "https://www.youtube.com/@CrossheadStudios/shorts"`
- Známá videa k Nimblehold (z listingu): `J89X1iOHrQg` (fill tool layers),
  `IjwegCVyXCw` (fill tool regions), `sm_W___0uUI` (instant encounter map),
  `UW4iCH-aiNQ` (jungle biome), `WlGzTlluyEg` (tavern battlemap),
  `CUkYIoxg8CI` (artists/sharing), `0wZzYKF42oA` (seasons),
  `86I86myIvr4` (2minutetabletop assets), `8_tayU0ui_4` (river tool),
  `Wm_QHMALCjo` (house building tool), `tlQ3w5CZ020` (copy/save/share houses),
  `ac0gLfwax4g` (region map assets), `4oztueB5trU` (prefab/grouped items),
  `4iSsiFTqPUs` (4 shadow layers per object), `d0jTed-2aI4` (building tests).
- Stáhni shorts (`yt-dlp -f "bv*+ba/b" -o "docs/nimblehold/videos/%(id)s.%(ext)s" <url>`),
  nebo aspoň nejlepší quality; z každého vyextrahuj klíčové snímky
  (`ffmpeg -i v.mp4 -vf fps=1 frames/vid-%03d.png`) a vyber ty reprezentativní.

### 1c. Další zdroje

- Crosshead web / itch.io / Patreon / Instagram / TikTok (linkuje Steam page).
- Nimblehold Discord/news, devlogy.
- Web search "Nimblehold mapmaking tool" pro články/preview.

### 1d. Výstup fáze 1

`docs/nimblehold/`:
- `videos/`, `frames/`, `screenshots/` — surový materiál
- `features.md` — seznam featur z copy + videí
- `analysis.md` — detailní rozbor: paleta barev (odeber pixel hodnoty přes
  PIL), ink outline styl, watercolor fills, heightmap-lighting look, stíny
  (4 vrstvy na objekt!), UI layout, tool workflow krok za krokem, interakce
  (co se stane když X potká Y), animace (voda, tráva, praporky?).

---

## Fáze 2 — Referenční kontrolní sada

Vyber z materiálů **10 referenčních obrázků** → `docs/nimblehold/refs/ref-01.png` …
`ref-10.png`. Každý musí reprezentovat ověřitelný aspekt, pro který umíme
vyrobit ekvivalentní render. Doporučené pokrytí (uprav podle reálného materiálu):

1. Celková krajina s kopcem, řekou a cestou (základní look)
2. Detail stromů a porostu
3. Stěna masivu / terasovitý kopec
4. Řeka + břehy + most přes cestu
5. Vodopád (kopec × řeka)
6. Dům / skupina domů (roofline, stín, zdi)
7. Vesnice / cluster budov
8. Interiér nebo dungeon oblast
9. Interakce tahů (soutok řek / křižovatka / napojení cest)
10. Fill tool výplň regionu / art pack nebo sezóna

Ke každému `refs/ref-XX.md`: co obrázek ukazuje, klíčové vizuální prvky,
konkrétní checklist (barvy, hrany, stíny, textura, kompozice) — podle něj se
hodnotí shoda.

---

## Fáze 3 — Srovnávací pipeline

- Postav scénu v našem editoru, která co nejvěrněji odpovídá každému
  referenčnímu obrázku (stejná kompozice/motiv).
- Renderuj přes `DMMAP_PROBE=1` probe (`LightingStage.Snapshot()` → PNG),
  případně rozšiř probe o `DMMAP_SCENE=<json>` načtení fixní scény.
- `tools/compare.py` (nový, PIL): naše vs. ref vedle sebe + metriky
  (SSIM nebo perceptual diff není cíl — pomůcka; hodnocení = vizuální checklist
  z `refs/ref-XX.md` + paleta/hrany/stíny). Procentuální skóre 0–100.

---

## Fáze 4 — Iterační smyčka

Pro každý z 10 obrázků, dokud skóre ≥ 90 %:

1. Render → porovnej s referencí → zapiš konkrétní rozdíly.
2. Oprav renderer/nástroje/config tak, aby rozdíly zmizely
   (paleta, ink outline, watercolour fills, stíny, textury, interakce).
3. `dotnet build` musí projít s 0 errors; probe kroky nesmí regresovat.
4. Commit po každé smysluplné iteraci (zpráva = co se měnilo a proč).

Mezitím implementuj chybějící **interakce a nástroje** pozorované ve videích —
např. z popisu Steam stránky: oval/rect tvary pro domy, tah domu na výšku
(stín se přizpůsobí), group recolor, volba stylu břehu řeky (grass/sand/stone),
prefab/sdílené skupiny objektů, 4-vrstvé stíny objektů, instant encounter
generator. Co už máme: mosty, vodopády, soutoky, sezóny, art packy, fill,
multi-select, region dungeon — u těch seřaď vzhled, ne existenci.

---

## Akceptační kritéria

- [ ] `docs/nimblehold/` obsahuje stažené materiály + `analysis.md` + `features.md`
- [ ] `docs/nimblehold/refs/ref-01..10.png` + per-image checklisty
- [ ] ≥ 9 z 10 obrázků splňuje vlastní checklist na ≥ 90 %
- [ ] `dotnet build` → 0 errors; `DMMAP_PROBE=1` probe projde bez ERROR
- [ ] Výsledky + rozhodnutí zdokumentovaná v `DECISIONS.md`

## Poznámky k prostředí

- Godot binary: `/home/tevuori/Tools/godot/Godot_v4.7.2-stable_mono_linux.x86_64`
  (probe BEZ `--headless`, dummy renderer nemá texture readback).
- Probe env: `DMMAP_PROBE=1`, `DMMAP_BIOME`, `DMMAP_SEASON`, `DMMAP_ARTPACK`,
  `DMMAP_BRIDGE_PROBE=1` (jen s běžícím backendem).
- `dotnet build` občas tiše skipne → ověř timestamp DLL nebo `--no-incremental`.
- Web reference (pokročilejší port Nimblehold-stylu, zdroj featur):
  `/home/tevuori/Documents/DnD/DMmap` (TS codebase, náš hlavní zdroj pravdy
  pro sémantiku — renderer.js comments už Nimblehold přístup dokumentují).
