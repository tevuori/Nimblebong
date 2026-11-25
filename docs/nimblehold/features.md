# Nimblehold — feature copy a pozorované featury

Zdroje: Steam stránka (`steam/appdetails.json`), devlogy a shorts
(`videos/`, `shorts/`), crossheadstudios.com (`web/`).

## Steam copy (oficiální)

> Nimblehold is a cozy mapmaking tool for building animated tabletop
> maps. Draw it, and watch it come alive. … Light, shadow, and detail
> happen on their own.

### Build by hand
- **Wall freehand → building.** Obkreslená zeď se stane budovou;
  **oval tool** pro kulaté oválné tvary.
- **Pull building taller/shorter** — výška se táhne, stín se posouvá
  spolu s ní (stín je vázaný na výšku objektu).

### Color it your way
- **Group recolor** — výběr celé skupiny objektů a přebarvení najednou,
  nebo jen jednoho kusu zvlášť.

### Roads that find their way
- **Road → river = auto bridge** ("sand path and all"); křivka se dá
  doladit, styl mostu je vyměnitelný.

### Banks that fit your map
- **River bank styles: grass / sand / stone** — přepnutí jedním klikem.

### Hills that talk to the water
- **Hill + river = auto waterfall** — voda padá přes hranu kopce dolů do
  řeky; smazáním středu kopce vzniknou dva kopce a potok mezi nimi,
  žádný cleanup.

### Build a whole region
- **Region fill** — vyplnit vesnici domy jedním tahem; art packy mění
  styl celé oblasti (village → town s jiným packem).
- **Animated maps** — výstup žije (voda, praporky, listí).

## Z videí / devlogů (analýza framů)

- **Spline nástroje:** cesty a řeky jsou bézier splines s editovatelnými
  body a tangentami (devlog1 frame ~020: most se táhne za tangentu).
- **Auto-schody:** cesta přes hranu kopce → kamenné schodiště;
  cesta přes vodu → most.
- **Voda (devlog3):** `DeepFadeDistance` (mělká tyrkysová → hluboká
  modrozelená), `CausticsColor`, foam edge podél břehů a kolem kamenů,
  drift/proužky (`Water speed`), vodopády s pěnou u paty.
- **Terén:** kruhový štětec, každý tah = výšková hladina; `Shift+↑↓`
  mění height; cliff face preset (Stonecliff…) + top preset (Grasstop…);
  kopec lze rozřezat a přesouvat po částech.
- **4 vrstvy stínu na objekt** (short `4iSsiFTqPUs`): kontaktní stín,
  krátký drop-shadow, dlouhý projektovaný stín, ambient occlusion.
- **Prefab/sdílené skupiny** (`tlQ3w5CZ020`, `4oztueB5trU`): domy se dají
  kopírovat/ukládat/sdílet jako seskupené prefab objekty.
- **Instant encounter map** (`sm_W___0uUI`): obdélníkový fill tah →
  vygenerovaná clearing s táborákem/interiérem/hostincem, stovky stromů
  za sekundu.
- **Fill tool layers** (`J89X1iOHrQg`) / **fill regions** (`IjwegCVyXCw`).
- **Sezóny** (`0wZzYKF42oA`): podzim/zima přebarví celou mapu.
- **Art packy** (`86I86myIvr4`): 2minutetabletop asset set — jiný ink
  styl (jemnější, více "papírový"), jiná paleta.
- **House building** (`Wm_QHMALCjo`): skládání domu ze základních tvarů
  (obdélník + ovál), cut-out překryvy, pak podlahy/nábytek fill.
- **Play mode** (`playmode-*`): grid + tokeny, fog of war.
- **Light panel** (`light-panel-desert`): Light color/intensity,
  Indirect light, Sky light — globální osvětlení uživatelsky laditelné.
- **Elevation dial** (`elevation-dial*`): kruhový ovladač výšky objektu.

## UI skin (konzistentní přes všechny materiály)
- Pergamenové panely s hnědým dřevěným rámem a roztrhaným okrajem,
  patkový fantasy font.
- Dolní dock: vlevo ikony nástrojů, uprostřed kontextový "X Settings"
  panel se slidery (dřevěný knoflík + numeric box) a preset položkami
  (`> Settings`), napravo plocha swatche/thumbnails.
- Kontextová lišta úplně dole se shortcuty aktuálního nástroje.
- Color picker popover (RGB/HSV/hex + swatches).
- Dev overlay vlevo nahoře: verze + FPS.
