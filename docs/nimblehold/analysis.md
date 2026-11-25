# Nimblehold — vizuální analýza

Kompilace z `nimblehold-analysis/ANALYZA.md` (devlogy), Steam screenshotů
a shorts framů (`frames/`). Pixel hodnoty odebrané přes PIL z ref
obrázků.

## Art direction

Top-down, ručně kreslený **watercolor + ink** styl. Každý tvar má
inkonturu (tmavě hnědá až černá linka s mírnou nepravidelností), výplně
jsou akvarelové s viditelným žíháním — žádné ploché barvy. Jemný
papírový grain přes celou mapu, okraje mají sepia vinětaci.

## Paleta (odebrané hodnoty)

| prvek | hodnoty |
|---|---|
| voda hluboká | `#0d5b68`, `#0f5c6c`, `#0c2f35` |
| voda střed/edge | `#128477`, `#1ba396`, `#28845d` |
| tráva světlá | `#5f993b`, `#689f3b`, `#8fa660` |
| tráva tmavá skvrna | `#356c36`, `#356e3e`, `#1e3918` |
| tráva suchá | `#a79c4e`, `#b2a65a`, `#aca656` |
| cliff face | `#a8ab80`, `#b8b692`, `#abab84`, `#caae7b` |
| cliff stín | `#1f3120`, `#2e4e31`, `#455b43` |
| strom koruna | `#689f3b` světlo → `#356e3e` → `#1f4023` stín |
| střecha slaměná | `#b7a955`, `#c7a661` |
| dřevo/most | `#a17a3f`, `#a37738` |
| cesta | `#a1a252` světlá hlína, hrany se rozpadají do trávy |
| poušť: písek | `#e1a65f`, `#e2b36b`; canyon `#855d38`, `#94633a` |
| ink | `#090a03` až tmavě hnědá `#463720` |

Ink linka je ~2–3 px na 1920px renderu, vždy teplá (hnědá, ne černá).

## Terén / cliff faces

- Každá výšková hladina = obkreslená ploška. Pod spodní (jih/jihovýchod)
  hranou visí **cliff face**: světlá kamenito-písková plocha
  (`#a8ab80`–`#caae7b`) s ink prasklinami a šrafami orientovanými dolů,
  plus kontaktní stín vržený na nižší vrstvu.
- Face není konturový "elevation lines" styl — je to souvislá malovaná
  stěna s několika vodorovnými strata linkami a prasklinami.
- Břehy masivů mají kamenný "cobble" detail jen podél paty, ne všude.
- Terasy (terraced-fields ref): každá hladina má kamennou ohradu
  (řada kamínků podél hrany) + stín do nižší hladiny.

## Voda

- Hluboká sytá tyrkysová/modrozelená (`#0d5b68`) uprostřed koryta,
  světlejší (`#1ba396`) u břehu → mělká hrana, případně písková či
  travnatá čára "bank" podle stylu břehu.
- **Foam edge**: bílá pěnová čára kolem břehů, kamenů a pod vodopády.
- Vlnkované jiskry/caustics v mělčinách, klidný drift uprostřed.
- Kameny ve vodě mají ripple kroužky.
- Řeka ve skalním korytě (gorge refs) má ještě světlou "rapid" část —
  pěna táhnoucí se po proudu.

## Stromy a porost

- Koruna = **velký scalloped blob**: překrývající se kruhové obláčky
  vytvářejí vlnitý obrys, vnitřek má 2–3 tóny (světlý střed `#689f3b`,
  tmavší kraj `#356e3e`, nejtmavší ink podtrh dole `#1f4023`).
- Ink obrys kopíruje scallopy; dole pod korunou krátký difúzní stín
  + viditelný kmínek u roztroušených stromů.
- Palmy v desert/jungle: paprsčité listy s ink obrysem.
- Keře = menší verze téhož, 1–2 tóny.

## Objekty / props

- Vše má ink obrys + vlastní akvarelovou výplň + **4 vrstvy stínu**:
  kontaktní tmavá elipsa, krátký drop shadow (sun direction), dlouhý
  projektovaný stín (heightmap raymarch), AO v zákrytech.
- Vozy/stany/táboráky: dřevo `#a17a3f`, plátno béž/červenobílé proužky.
- Domy: zdi béž/kámen, střechy sláma `#b7a955` nebo červená taška,
  komínek, dveře/okna vyryté ink linkami; stín budovy odpovídá výšce.

## Interiéry

- Podlahy: dřevěná prkna (teplá hnědá, ink spáry) nebo kamenná dlažba
  (světlá, ink mřížka); koberec s ornamentem.
- Zdi tlusté s ink obrysem a crenellation detaily nahoře;
  round tower = kruhová místnost s kamenným obvodem.
- Nábytek: stoly, postele, police, barely — vše ink, stíny ke SV/Z.

## UI (pro úplnost — renderer porovnáváme bez UI)

Pergamenové panely, dřevěný rám, patkový font; dolní dock
[nástroje | Settings panel | swatch/thumbnail plocha]; kontextová
shortcut lišta; dev overlay verze+FPS vlevo nahoře.

## Animace / život

Voda driftuje a třpytí se, listí se vlní ("Wind warp strength"),
praporky vlají, táborák bliká — mapa "žije" i mimo export.

## Rozdíly oproti našemu rendereru (startovní stav)

1. Tráva u nás téměř plochá s jemným specklem; Nimblehold má výrazné
   **mottled patches** (světlé/tmavé/suché bloby ~40–100 px).
2. Naše cliff face = soustava terrace ringů (contour look);
   Nimblehold = **světlá souvislá face s ink prasklinami** + silný
   kontaktní stín.
3. Naše stromy jsou malé ploché kruhy; Nimblehold koruny jsou
   **velké scalloped multi-tone bloby** (~2–3× větší než naše default).
4. Voda u nás světlejší cyan; Nimblehold **hluboká sytá teal** s výraznou
   foam hranou a bank čarou.
5. Nimblehold props (carts, tents, campfire, crates) jsou detailnější
   ilustrace; naše zjednodušené glyphy.
6. Interiéry: Nimblehold dřevěná prkna / kamenná dlažba + vybavení;
   naše zatím zjednodušené podlahy.
