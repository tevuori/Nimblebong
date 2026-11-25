#nullable enable
// Map editor — the interactive milestone: draw terrain strokes with
// the mouse, lay rivers/roads, place props, undo, pan/zoom. Czech UI.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class MapEditor : Node2D
{
    /// <summary>Input mode — how the tool turns mouse strokes into scene.</summary>
    public enum Mode
    {
        Select,
        Erase,
        Shape,
        Stroke,
        Brush,
        Prop,
        Label,
        Fog,
        Fill,
        Play,
    }

    /// <summary>One palette entry: label + category + what it commits.</summary>
    public readonly record struct ToolDef(
        string Id,
        string Label,
        string Category,
        Mode Mode,
        string Kind
    );

    /// <summary>Full tool palette — categories follow the web editor's
    /// toolCategories; labels are the Czech toolLabels.</summary>
    public static readonly ToolDef[] Tools =
    [
        new("select", "Vybrat", "Nástroje", Mode.Select, ""),
        new("erase", "Guma", "Nástroje", Mode.Erase, ""),
        new("terrain", "Kopce a srázy", "Terén a voda", Mode.Shape, Kinds.Terrain),
        new("brush", "Štětec kopců", "Terén a voda", Mode.Brush, Kinds.Terrain),
        new("river", "Řeky", "Terén a voda", Mode.Stroke, Kinds.River),
        new("pond", "Jezírka", "Terén a voda", Mode.Shape, Kinds.Pond),
        new("field", "Pole a louky", "Terén a voda", Mode.Shape, Kinds.Field),
        new("road", "Cesty", "Cesty", Mode.Stroke, Kinds.Road),
        new("wall", "Kamenné zdi", "Cesty", Mode.Stroke, Kinds.Wall),
        new("fence", "Ploty", "Cesty", Mode.Stroke, Kinds.Fence),
        new("dock", "Doky a mola", "Cesty", Mode.Prop, "dock"),
        new("house", "Budovy", "Stavby", Mode.Shape, Kinds.House),
        new("room", "Místnosti", "Stavby", Mode.Shape, Kinds.Room),
        new("floor", "Podlahy a plošiny", "Stavby", Mode.Shape, Kinds.Floor),
        new("tower", "Strážní věže", "Stavby", Mode.Prop, "tower"),
        new("stall", "Tržní stánky", "Stavby", Mode.Prop, "stall"),
        new("tent", "Stany", "Stavby", Mode.Prop, "tent"),
        new("door", "Dveře", "Interiér", Mode.Prop, "door"),
        new("window", "Okna", "Interiér", Mode.Prop, "window"),
        new("table", "Stoly", "Interiér", Mode.Prop, "table"),
        new("bed", "Postele", "Interiér", Mode.Prop, "bed"),
        new("shelf", "Knihovny a regály", "Interiér", Mode.Prop, "shelf"),
        new("rug", "Koberce", "Interiér", Mode.Prop, "rug"),
        new("fireplace", "Krby", "Interiér", Mode.Prop, "fireplace"),
        new("tree", "Stromy", "Porost a příroda", Mode.Prop, Kinds.Tree),
        new("pine", "Borovice", "Porost a příroda", Mode.Prop, "pine"),
        new("bush", "Keře", "Porost a příroda", Mode.Prop, Kinds.Bush),
        new("scatter", "Porosty", "Porost a příroda", Mode.Shape, Kinds.Scatter),
        new("rock", "Skály", "Porost a příroda", Mode.Prop, Kinds.Rock),
        new("stonecircle", "Kamenné kruhy", "Porost a příroda", Mode.Prop, "stonecircle"),
        new("crystal", "Krystaly", "Porost a příroda", Mode.Prop, "crystal"),
        new("bale", "Balíky", "Porost a příroda", Mode.Prop, "bale"),
        new("veg-brush", "Štětec vegetace", "Flora", Mode.Brush, VegKind),
        new("camp", "Tábory a skupiny", "Osídlení a dekorace", Mode.Prop, Kinds.Camp),
        new("campfire", "Ohniště", "Osídlení a dekorace", Mode.Prop, "campfire"),
        new("cart", "Vozy", "Osídlení a dekorace", Mode.Prop, "cart"),
        new("boat", "Lodě", "Osídlení a dekorace", Mode.Prop, "boat"),
        new("well", "Studny", "Osídlení a dekorace", Mode.Prop, "well"),
        new("crate", "Bedny", "Osídlení a dekorace", Mode.Prop, "crate"),
        new("barrel", "Sudy", "Osídlení a dekorace", Mode.Prop, "barrel"),
        new("fountain", "Kašny", "Osídlení a dekorace", Mode.Prop, "fountain"),
        new("lantern", "Lampy a pochodně", "Osídlení a dekorace", Mode.Prop, "lantern"),
        new("flag", "Praporky", "Osídlení a dekorace", Mode.Prop, "flag"),
        new("pillar", "Sloupy", "Osídlení a dekorace", Mode.Prop, "pillar"),
        new("statue", "Sochy", "Osídlení a dekorace", Mode.Prop, "statue"),
        new("ruin", "Zříceniny", "Osídlení a dekorace", Mode.Prop, "ruin"),
        new("grave", "Hroby", "Osídlení a dekorace", Mode.Prop, "grave"),
        new("chest", "Truhly", "Osídlení a dekorace", Mode.Prop, "chest"),
        new("bones", "Kosti a lebky", "Osídlení a dekorace", Mode.Prop, "bones"),
        new("barricade", "Zábrany", "Osídlení a dekorace", Mode.Prop, "barricade"),
        new("portal", "Magické brány", "Osídlení a dekorace", Mode.Prop, "portal"),
        new("marker", "Značky", "Mapa a hra", Mode.Prop, "marker"),
        new("fill", "Generátor výplně", "Mapa a hra", Mode.Fill, ""),
        new("play", "Hrát", "Mapa a hra", Mode.Play, ""),
        new("label", "Popisky", "Mapa a hra", Mode.Label, "label"),
        new("fog-clear", "Mlha: odhalit", "Mapa a hra", Mode.Fog, "clear"),
        new("fog-shroud", "Mlha: zahalit", "Mapa a hra", Mode.Fog, "shroud"),
    ];

    /// <summary>Card accent colors — the web toolColors palette.</summary>
    static readonly Dictionary<string, string> ToolColor = new()
    {
        ["select"] = "#8a8a8a", ["erase"] = "#8a8a8a",
        ["terrain"] = "#a4a68e", ["brush"] = "#a4a68e", ["veg-brush"] = "#6d8f46",
        ["river"] = "#298f99", ["pond"] = "#298f99", ["field"] = "#a8935a",
        ["road"] = "#d1b87c", ["wall"] = "#b7b69a", ["fence"] = "#8a6f4c",
        ["dock"] = "#a37d4c", ["house"] = "#ac6948", ["room"] = "#b8ad86",
        ["floor"] = "#a37d4c", ["tower"] = "#9a9584", ["stall"] = "#a0522d",
        ["tent"] = "#a28b61", ["door"] = "#7a5636", ["window"] = "#d8cfa8",
        ["table"] = "#9c7750", ["bed"] = "#a98a5e", ["shelf"] = "#8a6a44",
        ["rug"] = "#8a4a3c", ["fireplace"] = "#8d8378",
        ["tree"] = "#557d47", ["pine"] = "#4d6b52", ["bush"] = "#64844d",
        ["scatter"] = "#557d47", ["rock"] = "#a4a68e",
        ["stonecircle"] = "#8f8d80", ["crystal"] = "#83d4cd",
        ["bale"] = "#c9a94e", ["camp"] = "#a37d4c", ["campfire"] = "#e99742",
        ["cart"] = "#a37d4c", ["boat"] = "#a37d4c", ["well"] = "#a4a68e",
        ["crate"] = "#ac8955", ["barrel"] = "#a37d4c", ["fountain"] = "#9fb4ae",
        ["lantern"] = "#e0b45e", ["flag"] = "#b3453a", ["pillar"] = "#a8a294",
        ["statue"] = "#a4a68e", ["ruin"] = "#9a9382", ["grave"] = "#8f8d80",
        ["chest"] = "#8a6a44", ["bones"] = "#d8cdb2", ["barricade"] = "#7d6547",
        ["portal"] = "#9a86c8", ["marker"] = "#d34f3d", ["label"] = "#4a3a28",
        ["fog-clear"] = "#7f8c8d", ["fog-shroud"] = "#5a6b6f",
        ["fill"] = "#7a9a6a", ["play"] = "#d34f3d",
    };

    /// <summary>Tool descriptions shown under the name — the web
    /// toolDescriptions (scene-presets.ts), Czech.</summary>
    static readonly Dictionary<string, string> ToolDesc = new()
    {
        ["select"] = "Klikni pro výběr prvku, táhni pro přesun. Delete smaže, Esc zruší výběr. U terénu upravíš parametry posuvníky.",
        ["erase"] = "Guma — táhni přes prvky, které chceš smazat.",
        ["terrain"] = "Vymez horní hranu návrší — skalní svah se vykreslí v pásmu ven z obrysu. Kopečky ve stejné výšce se slévají v jeden masiv, různé výšky se skládají do teras. Cesta přes hranu dostane schody, řeka vodopád.",
        ["veg-brush"] = "Táhni přes mapu a štětec nasází vybranou rostlinu v trsech. Směs vybírá podle okolí — u vody rákos, ve vodě lekníny, jinde tráva a květiny (fill.json).",
        ["brush"] = "Razítkuj kopce štětcem — každá bublina přidá terén v nastavené výšce; když maluješ přes existující kopec, výška se navyšuje.",
        ["river"] = "Maluj vodní tok. Břehy, mělčina a rostliny se vytvoří automaticky; toky se při setkání slévají.",
        ["pond"] = "Klidná vodní plocha vyrytá do terénu.",
        ["field"] = "Vymez pole nebo pastvinu — brázdy a úroda se přizpůsobí prostředí.",
        ["road"] = "Nakresli cestu. Při křížení řeky se objeví most — jeho styl zvolíš volbou Most. Konec na existující cestu se přichytí a spojí.",
        ["wall"] = "Táhni a postav kamennou zeď — uzavřená smyčka se stane budovou.",
        ["fence"] = "Táhni a postav dřevěný plot.",
        ["dock"] = "Dřevěný mol nebo přístaviště u břehu.",
        ["house"] = "Nakresli půdorys. Střecha, krytina a stín vzniknou samy.",
        ["room"] = "Vymez podlahu a obvodové zdi. Místnost může být hranatá, kulatá i kreslená rukou.",
        ["floor"] = "Vymez podlahu — dřevěná prkna nebo kamenná dlažba.",
        ["tower"] = "Strážní věž s cimbuřím.",
        ["stall"] = "Tržní stánek s plachtou — u cesty se otočí pultem k ní.",
        ["tent"] = "Postav tábor; materiál se přizpůsobí prostředí.",
        ["door"] = "Klikni na zeď místnosti — zeď se přeruší a vznikne otvor s křídlem.",
        ["window"] = "Klikni na zeď místnosti — vznikne okenní otvor s rámem.",
        ["table"] = "Stůl shora — kulatý, dlouhý jídelní nebo malý čtvercový.",
        ["bed"] = "Postel s peřinou a polštářem.",
        ["shelf"] = "Knihovna, regál se zbožím nebo komoda.",
        ["rug"] = "Koberec s lemovým vzorem.",
        ["fireplace"] = "Krb s hořícím ohněm — místnost prohřeje teplým světlem.",
        ["tree"] = "Klikni pro jeden strom, táhni pro les. Druh stromu se řídí biomem a sezónou.",
        ["pine"] = "Jehličnan — klikni pro jeden, táhni pro skupinu.",
        ["bush"] = "Vegetace se mění s prostředím: keře, kaktusy, rákosí nebo houby.",
        ["scatter"] = "Vymez oblast a v ní se automaticky rozestaví vegetace, skály nebo směs. Cesty a řeky si vezmou mýtinu.",
        ["rock"] = "Rozmísti jednotlivé balvany nebo celou skupinu.",
        ["stonecircle"] = "Kruh balvanů — ohniště uvnitř rozzáří runy.",
        ["crystal"] = "Krystalové shluky; v sopečné krajině žhavé minerály.",
        ["bale"] = "Balík sena, vlny nebo zásob podle prostředí.",
        ["camp"] = "Klikni pro celou skupinu: tábor se stany a ohništěm. Rozpadne se na samostatné prvky.",
        ["campfire"] = "Ohniště s kamenným kruhem — ponořené do řeky vyhasne.",
        ["cart"] = "Dřevěný vůz; v horách dostane sněhovou pokrývku.",
        ["boat"] = "Loď ve vodě se natočí podle nejbližšího úseku toku.",
        ["well"] = "Kamenná studna s vodou a rumpálem.",
        ["crate"] = "Dřevěné bedny do tábořiště či interiéru.",
        ["barrel"] = "Klikni pro sud, táhni pro skupinu.",
        ["fountain"] = "Kamenná kašna s vodním trylkem.",
        ["lantern"] = "Lampa, žardina nebo pochodeň — pod ní se rozlije teplý kruh světla.",
        ["flag"] = "Slavnostní praporek.",
        ["pillar"] = "Sloup nebo obelisk; dva sloupy blízko sebe spojí architráv.",
        ["statue"] = "Socha nebo obelisk v materiálu prostředí.",
        ["ruin"] = "Zřícenina zdí, torzo sloupu nebo hromada sutin.",
        ["grave"] = "Náhrobek, kříž nebo hrobka — tři a víc se ohradí do hřbitova.",
        ["chest"] = "Truhla na poklady a zásoby.",
        ["bones"] = "Ostatky tvorů — žebra, lebky i kostra velké zvěře.",
        ["barricade"] = "Zátaras z kůlů a trosek na blokování cesty.",
        ["portal"] = "Stojící brána z kamene či energie.",
        ["marker"] = "Označ místa pro tokeny: klikni pro značku, táhni pro sérii. Při odeslání do DR se ze značek stanou tokeny.",
        ["label"] = "Klikni pro umístění popisku — zadáš text v dialogu.",
        ["fog-clear"] = "Odhaluj mapu — táhni přes oblasti, které mají hráči vidět.",
        ["fog-shroud"] = "Zahaluj mapu mlhou — táhni přes oblasti, které se mají skrýt.",
        ["fill"] = "Táhni obdélník přes oblast — naplní ji propy podle těch, co v ní už stojí (nebo vygeneruje krajinu/dungeon). Náhled se ukáže rovnou, tlačítko Vyplnit připíše jako jeden krok historie.",
        ["play"] = "Náhled hraní — značky se zobrazí jako herní žetony (Spawn/Hráči/Nepřítel) a dají se přetahovat jako ve hře.",
    };

    static readonly Vector2 MapSize = new(1600, 1000);

    SceneView _view = null!;
    SceneHistory _history = null!;
    GhostLayer _ghost = null!;
    PlayLayer _playLayer = null!;
    Camera2D _camera = null!;

    // Current tool + parameters.
    ToolDef _tool = Tools[2]; // terrain
    int _form; // 0 volný tah, 1 obdélník, 2 elipsa — pro plošné nástroje
    double _pHeight = 30;
    double _brushSize = 42;
    double _brushHeight = 10;
    double _pSlope = 46;
    double _pRough = 0;
    int _pModel;
    double _pRiverW = 30;
    double _pRoadW = 16;
    int _roadModel; // styl mostu: 0 dřevěný, 1 kamenný, 2 lanový

    // Active stroke recording.
    readonly List<Vector2> _stroke = [];
    bool _drawing;
    bool _panning;
    bool _spacePan;
    // Click-point drawing (web clickPoints): quick clicks accumulate
    // vertices, Enter commits them as the stroke/shape.
    readonly List<Vector2> _clickPoints = [];
    Vector2 _panFrom;

    // Selection: what the select tool picked, where a move began and a
    // snapshot to revert to (the live object is mutated during drags /
    // slider edits so the canvas updates, then reverted and committed
    // once as a single history entry).
    // Multi-selection (web `selected[]`): everything the select tool
    // picked — one item from a click, many from a marquee rect.
    readonly List<Prop> _selProps = [];
    readonly List<Stroke> _selStrokes = [];
    readonly List<Shape> _selShapes = [];
    Vector2 _selOrigin;
    bool _selMoving;
    bool _marqueeing;
    Vector2 _marqueeStart;
    Rect2 _marquee;
    // Backups for live edits (restored before the single commit).
    readonly Dictionary<Prop, Vector2> _selPosBackup = new();
    readonly Dictionary<object, List<Vector2>> _selPtsBackup = new();
    (double H, double S, double R, int M)? _selShapeBackup;

    /// <summary>Single-item accessors — valid only when exactly one of
    /// that element type is selected (sliders, variant picker).</summary>
    Prop? _selProp => _selProps.Count == 1 ? _selProps[0] : null;
    Stroke? _selStroke => _selStrokes.Count == 1 ? _selStrokes[0] : null;
    Shape? _selShape => _selShapes.Count == 1 ? _selShapes[0] : null;

    double _draftAt; // ticks when the pending live preview may run
    bool _pendingRebuild; // throttled Rebuild during selection drags
    bool _selEdited; // selection was live-edited → commit on release
    bool _syncing; // slider→selection feedback guard
    BridgeDialog? _bridge;
    HSlider _sHeight = null!;
    HSlider _sSlope = null!;
    HSlider _sRough = null!;
    OptionButton _modelOpt = null!;
    OptionButton _bridgeOpt = null!;
    VBoxContainer _terrainSection = null!;
    VBoxContainer _fogSection = null!;
    VBoxContainer _brushSection = null!;
    VBoxContainer _variantSection = null!;
    VBoxContainer _fillSection = null!;
    Label _fillStats = null!;
    OptionButton _fillMode = null!;
    HSlider _fillDensity = null!;
    LineEdit _fillSeedEdit = null!;
    Godot.Button _fillCommit = null!;
    // Pending region fill: the dragged rect, generated scene/props and
    // detected source ids — previewed until "Vyplnit" commits.
    Rect2? _fillRect;
    List<Prop> _fillProps = [];
    List<int> _fillSources = [];
    Scene? _fillScene;
    PropsPainter _fillPreview = null!;
    LineEdit _seedEdit = null!;
    OptionButton _presetOpt = null!;
    OptionButton _seasonOpt = null!;
    OptionButton _variantOpt = null!;
    /// <summary>Variant picker for prop tools: -1 auto, 0/1/2 = I/II/III.</summary>
    int _propVariant = -1;
    ToolThumbs _thumbs = null!;
    // Environment look mirrored into the thumbnail renderer.
    string _lookBiome = "woodland";
    string _lookMood = "day";
    string _lookArt = "classic";
    string? _lookSeason;
    // UI scale — quantized to 0.25 so rebuilds only happen on real jumps.
    float _ui = 1;
    int _uiBucket;
    CanvasLayer? _uiLayer;

    int ComputeUiBucket()
    {
        // Physical window height — GetViewportRect lies at _Ready
        // (default size) and reacts to camera zoom, Window.Size is
        // the real framebuffer size (contentScale==1 on Linux).
        var h = GetWindow().Size.Y;
        var b = (int)Math.Round(Math.Clamp(h / 720f, 1f, 2.6f) / 0.25f);
        if (OS.HasEnvironment("DMMAP_DEBUG_UI") && b != _dbgBucket)
        {
            _dbgBucket = b;
            GD.Print(
                $"ui-dbg: winSize={DisplayServer.WindowGetSize()} "
                    + $"winDecor={DisplayServer.WindowGetSizeWithDecorations()} "
                    + $"contentScale={GetWindow().ContentScaleFactor} "
                    + $"screenScale={DisplayServer.ScreenGetScale()} "
                    + $"screenDpi={DisplayServer.ScreenGetDpi()} "
                    + $"h={h} bucket={b}"
            );
        }
        return b;
    }

    int _dbgBucket = -1;

    int Fs(float s) => (int)Math.Round(s * _ui);
    float Us(float v) => v * _ui;

    public override void _Ready()
    {
        _uiBucket = ComputeUiBucket();
        _ui = _uiBucket * 0.25f;
        _view = SceneView.Create(MapSize);
        AddChild(_view);
        _thumbs = ToolThumbs.Create(_ui);
        AddChild(_thumbs);
        _history = new SceneHistory(_view.Scene);
        _ghost = new GhostLayer { View = _view };
        AddChild(_ghost);
        _playLayer = new PlayLayer { Visible = false };
        AddChild(_playLayer);
        _fillPreview = new PropsPainter
        {
            Modulate = new Color(1, 1, 1, 0.55f),
            Visible = false,
            BiomeCfg = _view.Biome,
        };
        AddChild(_fillPreview);
        _camera = new Camera2D { Position = MapSize / 2, Enabled = true };
        AddChild(_camera);
        BuildUi();
        _bridge = BridgeDialog.Create(
            () => _view.Stage.Snapshot().SavePngToBuffer(),
            () => _history.Current,
            () => _view.Stage.FogImage()
        );
        AddChild(_bridge);
        if (System.Environment.GetEnvironmentVariable("DMMAP_PROBE") == "1")
            CallDeferred(nameof(ProbeRun));
        var uiShot = System.Environment.GetEnvironmentVariable("DMMAP_UI_SHOT");
        if (uiShot != null)
            CallDeferred(nameof(UiShot), uiShot);
        var ghostShot = System.Environment.GetEnvironmentVariable("DMMAP_GHOST_SHOT");
        if (ghostShot != null)
            CallDeferred(nameof(GhostShot), ghostShot);
        var smartShot = System.Environment.GetEnvironmentVariable("DMMAP_SMART_SHOT");
        if (smartShot != null)
            CallDeferred(nameof(SmartShot), smartShot);
        var vegShot = System.Environment.GetEnvironmentVariable("DMMAP_VEG_SHOT");
        if (vegShot != null)
            CallDeferred(nameof(VegShot), vegShot);
    }

    /// <summary>Probe: bake a road + river + crossing wall/fence + a
    /// massif and a house, drop a tree into the river (must be nudged
    /// out), run the smart fill over the middle and dump the window.</summary>
    async void SmartShot(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Commit(scene =>
        {
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points = [new(80, 400), new(500, 420), new(1080, 480)],
                    Width = 16,
                }
            );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.River,
                    Points = [new(880, 60), new(820, 300), new(900, 640)],
                    Width = 26,
                }
            );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Wall,
                    Points = [new(620, 300), new(640, 430), new(660, 560)],
                    Width = 10,
                }
            );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Fence,
                    Points = [new(300, 340), new(300, 430), new(320, 540)],
                    Width = 6,
                }
            );
            scene.Shapes.Add(
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Terrain,
                    Height = 40,
                    Size = 40,
                    Roughness = 40,
                    Points = [new(120, 80), new(420, 60), new(470, 240), new(140, 260)],
                }
            );
            Interactions.PlaceProtected(
                scene,
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = "house",
                    X = 180,
                    Y = 330,
                    Scale = 30,
                }
            );
            // Dry-land test — a tree dropped mid-river must be nudged
            // to the shore (or refused when nothing near is dry).
            var before = scene.Props.Count;
            Interactions.PlaceProtected(
                scene,
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Tree,
                    X = 855,
                    Y = 330,
                    Scale = 30,
                }
            );
            GD.Print($"SMART water-nudge placed={scene.Props.Count > before}");
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!path.Contains("bare"))
        {
            _fillMode.Selected = 3;
            _fillRect = new Rect2(140, 260, 780, 340);
            RunFill();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"SMART generated={_fillProps.Count}");
            CommitFill();
        }
        _camera.Position = new Vector2(560, 400);
        _camera.Zoom = new Vector2(0.72f, 0.72f);
        if (_uiLayer != null)
            _uiLayer.Visible = false;
        for (var i = 0; i < 6; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    /// <summary>Probe: bake a road + river, activate the painted stroke
    /// ghost on a T-junction arm, dump the window, quit.</summary>
    async void GhostShot(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Commit(scene =>
        {
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points = [new(200, 420), new(520, 430), new(880, 500), new(1100, 560)],
                    Width = 18,
                }
            );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.River,
                    Points = [new(240, 120), new(500, 180), new(760, 140)],
                    Width = 22,
                }
            );
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _tool = Tools.First(t => t.Kind == Kinds.Road);
        _ghost.SetStroke(
            _tool,
            [new(600, 445), new(650, 380), new(700, 300), new(760, 230)],
            StrokeWidth()
        );
        _ghost.SetSnap(new Vector2(600, 445));
        for (var i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    /// <summary>Rebuild the dock + thumbnails when the window crosses a
    /// scale bucket — keeps the UI readable on big screens.</summary>
    void RebuildUi()
    {
        _ui = _uiBucket * 0.25f;
        _uiLayer?.QueueFree();
        _uiLayer = null;
        _thumbs.QueueFree();
        _thumbs = ToolThumbs.Create(_ui);
        AddChild(_thumbs);
        _toolCards.Clear();
        BuildUi();
    }

    /// <summary>UI probe: wait for thumbnails to finish, dump the whole
    /// window (dock included) to the given path, quit.</summary>
    async void UiShot(string path)
    {
        for (var i = 0; i < 600 && _thumbs.Pending; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (var i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sheet = System.Environment.GetEnvironmentVariable("DMMAP_THUMB_DUMP");
        if (sheet != null)
            _thumbs.DumpSheet(sheet);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    /// <summary>Throttled work: live massif preview while a terrain
    /// stroke is in flight, plus deferred rebuilds for selection drags.</summary>
    public override void _Process(double delta)
    {
        var now = Time.GetTicksMsec();
        if (_drawing && _tool.Kind == Kinds.Terrain && _draftAt > 0 && now >= _draftAt)
        {
            _draftAt = 0;
            UpdateDraftPreview();
        }
        if (_pendingRebuild && now - _lastRebuild >= 90)
        {
            _pendingRebuild = false;
            _lastRebuild = now;
            _view.Rebuild();
        }
        // Poll the window size — WM_SIZE_CHANGED does not fire
        // reliably (e.g. startup maximize lands silently).
        var b = ComputeUiBucket();
        if (b != _uiBucket)
        {
            _uiBucket = b;
            RebuildUi();
        }
    }

    ulong _lastRebuild;

    /// <summary>Draft massif from the in-flight stroke — band + top
    /// fills drawn translucently in the ghost layer.</summary>
    void UpdateDraftPreview()
    {
        if (_stroke.Count < 6)
            return;
        var massifs = TerrainGeometry.BuildMassifs(
            [
                new TerrainShape
                {
                    Model = _pModel,
                    Height = _pHeight,
                    Size = _pSlope,
                    Roughness = _pRough,
                    Outline = [.. Interactions.SimplifyPath(_stroke, 4)],
                },
            ]
        );
        if (massifs.Count > 0)
            _ghost.SetDraft(massifs[0]);
    }

    // Scripted self-test — exercises stroke commits, merge, props,
    // undo, and dumps the lit composite like the user would see it.
    async void ProbeRun()
    {
        // Optional art/season override for visual checks.
        var envBiome = System.Environment.GetEnvironmentVariable("DMMAP_BIOME");
        if (envBiome != null)
        {
            _lookBiome = envBiome;
            _view.SetBiome(envBiome);
        }
        var envArt = System.Environment.GetEnvironmentVariable("DMMAP_ARTPACK");
        var envSeason = System.Environment.GetEnvironmentVariable("DMMAP_SEASON");
        if (envArt != null)
        {
            _lookArt = envArt;
            _view.SetArtPack(envArt);
        }
        if (envSeason != null)
        {
            _lookSeason = envSeason;
            _view.SetSeason(envSeason);
        }
        SyncThumbLook();
        // Terrain blob via the same path a mouse stroke takes.
        var pts = new List<Vector2>();
        for (var i = 0; i < 28; i++)
        {
            var a = i / 28.0 * Mathf.Tau;
            var r = 150 * (1 + 0.15 * Math.Sin(a * 3));
            pts.Add(new Vector2(400 + (float)(Math.Cos(a) * r), 300 + (float)(Math.Sin(a) * r * 0.8)));
        }
        Commit(scene =>
            scene.Shapes = Interactions.MergeShapeInto(
                scene,
                new Shape
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Terrain,
                    Points = pts,
                    Size = 46,
                    Height = 40,
                    Roughness = 50,
                }
            )
        );
        // A river that cuts straight through the massif (waterfalls)
        // and a road crossing it (stairs) — exercises CrossingPainter.
        Commit(scene =>
        {
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.River,
                    Points =
                    [
                        new(500, -10),
                        new(470, 250),
                        new(520, 500),
                        new(600, 1020),
                    ],
                    Width = 30,
                }
            );
            // Second river crossing the first — confluence must read as
            // one unioned water body, not stacked ribbons.
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.River,
                    Points =
                    [
                        new(60, 620),
                        new(420, 600),
                        new(760, 640),
                        new(1150, 600),
                        new(1560, 650),
                    ],
                    Width = 22,
                }
            );
        });
        Commit(scene =>
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points =
                    [
                        new(180, 320),
                        new(400, 300),
                        new(620, 280),
                    ],
                    Width = 18,
                }
            )
        );
        SetTool("tree");
        PlacePropAt(new Vector2(390, 280));
        SetTool("house");
        PlacePropAt(new Vector2(800, 600));
        Undo(); // house away — verify undo rebuilds cleanly

        // Bridge: a road crossing the vertical river at ~90°.
        Commit(scene =>
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points = [new(60, 480), new(400, 470), new(760, 500), new(1000, 560)],
                    Width = 18,
                }
            )
        );
        // Stroke continuation: second segment ending at the first's end
        // must merge into a single stroke.
        var strokeCount = _view.Scene.Strokes.Count;
        Commit(scene =>
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points = [new(1010, 570), new(1200, 620), new(1400, 700)],
                    Width = 18,
                }
            )
        );
        GD.Print(
            $"road-merge: {_view.Scene.Strokes.Count} strokes after (was {strokeCount}, expect same + 0 merged)"
        );
        // Freehand regression: dense wiggly control points (like raw mouse
        // input) must still produce filled surfaces, not empty outlines.
        Commit(scene =>
        {
            var wiggle = new List<Vector2>();
            for (var i = 0; i <= 90; i++)
                wiggle.Add(
                    new Vector2(
                        60 + i * 14,
                        150 + (float)(Math.Sin(i * 0.55) * 55 + Math.Sin(i * 1.7) * 18)
                    )
                );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points = wiggle,
                    Width = 18,
                }
            );
            var wiggle2 = new List<Vector2>();
            for (var i = 0; i <= 80; i++)
                wiggle2.Add(
                    new Vector2(
                        1300 + (float)(Math.Sin(i * 0.5) * 60 + Math.Sin(i * 1.3) * 22),
                        120 + i * 10
                    )
                );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.River,
                    Points = wiggle2,
                    Width = 24,
                }
            );
        });
        // Stone + rope bridge variants further down the river.
        Commit(scene =>
        {
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points = [new(60, 700), new(500, 690), new(820, 710)],
                    Width = 18,
                    Model = 1,
                }
            );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Road,
                    Points = [new(120, 900), new(420, 880), new(700, 920)],
                    Width = 14,
                    Model = 2,
                }
            );
        });

        // Wave-A features: pond, field, wall, fence, scatter, camp.
        var pond = new List<Vector2>();
        for (var i = 0; i < 14; i++)
        {
            var a = i / 14.0 * Mathf.Tau;
            pond.Add(new Vector2(1050 + (float)Math.Cos(a) * 90, 760 + (float)Math.Sin(a) * 60));
        }
        Commit(scene =>
            scene.Shapes = Interactions.MergeShapeInto(
                scene,
                new Shape { Id = scene.AllocId(), Kind = Kinds.Pond, Points = pond }
            )
        );
        var field = new List<Vector2>();
        for (var i = 0; i < 12; i++)
        {
            var a = i / 12.0 * Mathf.Tau;
            field.Add(new Vector2(1250 + (float)Math.Cos(a) * 130, 350 + (float)Math.Sin(a) * 80));
        }
        Commit(scene =>
            scene.Shapes = Interactions.MergeShapeInto(
                scene,
                new Shape { Id = scene.AllocId(), Kind = Kinds.Field, Points = field }
            )
        );
        Commit(scene =>
        {
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Wall,
                    Points = [new(950, 500), new(1150, 480), new(1250, 560), new(1240, 700)],
                    Width = 20,
                    Model = 1,
                }
            );
            Interactions.AddStroke(
                scene,
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = Kinds.Fence,
                    Points = [new(1150, 260), new(1350, 300), new(1420, 420)],
                    Width = 10,
                }
            );
        });
        SetTool("scatter");
        BeginStroke(); // fake a scatter commit straight through Commit
        var scat = new List<Vector2>();
        for (var i = 0; i < 12; i++)
        {
            var a = i / 12.0 * Mathf.Tau;
            scat.Add(new Vector2(900 + (float)Math.Cos(a) * 110, 200 + (float)Math.Sin(a) * 70));
        }
        _stroke.Clear();
        _stroke.AddRange(scat);
        EndStroke();
        SetTool("camp");
        PlacePropAt(new Vector2(700, 750));
        // Label + marker — placed directly (PromptLabel opens a dialog).
        Commit(scene =>
        {
            scene.Props.Add(
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = "label",
                    X = 480,
                    Y = 120,
                    Scale = 16,
                    Text = "Tábor u řeky",
                }
            );
            scene.Props.Add(
                new Prop { Id = scene.AllocId(), Kind = "marker", X = 700, Y = 690, Scale = 22 }
            );
            scene.Props.Add(
                new Prop { Id = scene.AllocId(), Kind = "campfire", X = 620, Y = 700, Scale = 24 }
            );
            scene.Props.Add(
                new Prop { Id = scene.AllocId(), Kind = "lantern", X = 760, Y = 660, Scale = 22 }
            );
            scene.Props.Add(
                new Prop { Id = scene.AllocId(), Kind = "portal", X = 840, Y = 720, Scale = 26 }
            );
            scene.Props.Add(
                new Prop { Id = scene.AllocId(), Kind = "crystal", X = 560, Y = 760, Scale = 22 }
            );
            // Fog: reveal a strip across the left half, then re-shroud
            // a smaller patch inside it.
            scene.Fog.Add(
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = "clear",
                    Points = [new(80, 620), new(500, 620), new(900, 540)],
                    Width = 180,
                }
            );
            scene.Fog.Add(
                new Stroke
                {
                    Id = scene.AllocId(),
                    Kind = "shroud",
                    Points = [new(400, 560), new(560, 660)],
                    Width = 110,
                }
            );
        });
        // Brush strokes: one pass over flat ground, then a second pass
        // over the new hill must stack a terrace on top of it.
        SetTool("brush");
        _pHeight = 10;
        _stroke.Clear();
        _stroke.AddRange([new Vector2(1050, 200), new Vector2(1150, 210), new Vector2(1250, 200)]);
        _brushHeight = HeightAt(_stroke[0]) + 10;
        EndStroke();
        var hAfter = HeightAt(new Vector2(1150, 210));
        _stroke.Clear();
        _stroke.AddRange([new Vector2(1100, 195), new Vector2(1180, 215), new Vector2(1220, 205)]);
        _brushHeight = HeightAt(_stroke[1]) + 10;
        EndStroke();
        GD.Print($"brush: hill {hAfter:0} -> stack {HeightAt(new Vector2(1150, 210)):0} (expect +10)");

        // Protection: a tree inside the campfire zone must be nudged out.
        SetTool("tree");
        PlacePropAt(new Vector2(700, 750));
        for (var i = 0; i < 8; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-editor.png");
        _view.Stage.FogImage().SavePng("/tmp/godot-fog.png");
        for (var i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng("/tmp/godot-screen.png");
        GD.Print("dumped /tmp/godot-editor.png");

        // ── generated map + selection move + crossings ──
        _seedEdit.Text = "42";
        Generate();
        for (var i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-generated.png");
        GD.Print("dumped /tmp/godot-generated.png");

        // dungeon preset
        _presetOpt.Selected = 1;
        Generate();
        for (var i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-dungeon.png");
        GD.Print("dumped /tmp/godot-dungeon.png");
        _presetOpt.Selected = 0;
        Generate();
        for (var i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // ── region fill: rect over a prop cluster → sources detected,
        // new props previewed, "Vyplnit" commits in one step ──
        {
            var anchor = _view.Scene.Props.FirstOrDefault(p => p.Kind == Kinds.Tree);
            if (anchor != null)
            {
                var before = _view.Scene.Props.Count;
                _fillRect = new Rect2(
                    (float)anchor.X - 280,
                    (float)anchor.Y - 220,
                    600,
                    460
                );
                _fillMode.Selected = 0;
                RunFill();
                GD.Print(
                    $"fill: sources {_fillSources.Count}, generated {_fillProps.Count} (expect >0/>0)"
                );
                CommitFill();
                GD.Print($"fill commit: props {before} -> {_view.Scene.Props.Count}");
            }
            // Dungeon-in-region mode.
            _fillRect = new Rect2(60, 60, 500, 380);
            _fillMode.Selected = 2;
            RunFill();
            GD.Print(
                $"fill dungeon: {_fillScene?.Shapes.Count ?? 0} shapes, {_fillScene?.Props.Count ?? 0} props"
            );
            CommitFill();
            _fillMode.Selected = 0;
            for (var i = 0; i < 10; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _view.Stage.Snapshot().SavePng("/tmp/godot-fill.png");
            _view.Stage.FogImage().SavePng("/tmp/godot-fog-rooms.png");
            GD.Print("dumped /tmp/godot-fill.png");
        }

        // selection: grab the first house, move it, verify one commit
        var house = _view.Scene.Props.FirstOrDefault(p => p.Kind == Kinds.House);
        if (house != null)
        {
            var depth = _history.UndoDepth;
            BeginSelect(new Vector2((float)house.X, (float)house.Y), false);
            _selOrigin = new Vector2((float)house.X, (float)house.Y);
            MoveSelectionTo(new Vector2((float)house.X + 120, (float)house.Y));
            _selEdited = true;
            EndSelect(new Vector2((float)house.X + 120, (float)house.Y));
            GD.Print(
                $"select-move commits: {_history.UndoDepth - depth} (expect 1), "
                    + $"house at {_view.Scene.Props.First(p => p.Kind == Kinds.House).X:0}"
            );
        }
        for (var i = 0; i < 20; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _view.Stage.Snapshot().SavePng("/tmp/godot-after-move.png");
        GD.Print("dumped /tmp/godot-after-move.png");

        // ── marquee + rotate + duplicate (web selection ops) ──
        {
            ClearSelection();
            _marqueeing = true;
            _marqueeStart = Vector2.Zero;
            _marquee = new Rect2(0, 0, 1600, 1000);
            EndMarquee();
            var total = _selProps.Count + _selStrokes.Count + _selShapes.Count;
            GD.Print($"marquee: {total} selected (expect >0)");
            var counts = (
                _view.Scene.Props.Count,
                _view.Scene.Strokes.Count,
                _view.Scene.Shapes.Count
            );
            var depth = _history.UndoDepth;
            RotateSelection(Math.PI / 12);
            DuplicateSelection();
            GD.Print(
                $"rotate+dup commits: {_history.UndoDepth - depth} (expect 2), "
                    + $"props {counts.Item1}->{_view.Scene.Props.Count}, "
                    + $"strokes {counts.Item2}->{_view.Scene.Strokes.Count}, "
                    + $"shapes {counts.Item3}->{_view.Scene.Shapes.Count}"
            );
            ClearSelection();
        }

        // ── play mode: marker props → draggable token chips ──
        {
            Commit(scene =>
                scene.Props.Add(
                    new Prop { Id = scene.AllocId(), Kind = "marker", X = 800, Y = 500, Scale = 22 }
                )
            );
            SetTool("play");
            var tokens = PlayTokens();
            GD.Print($"play tokens: {tokens.Count} (expect >0)");
            if (tokens.Count > 0)
            {
                var t = tokens[0];
                var depth = _history.UndoDepth;
                BeginPlay(t.Pos);
                MoveSelectionTo(t.Pos + new Vector2(60, 40));
                _selEdited = true;
                EndSelect(t.Pos + new Vector2(60, 40));
                var moved = _view.Scene.Props.First(p => p.Kind == "marker");
                GD.Print(
                    $"token drag commits: {_history.UndoDepth - depth} (expect 1), at {moved.X:0},{moved.Y:0}"
                );
            }
            GetViewport().GetTexture().GetImage().SavePng("/tmp/godot-play.png");
            SetTool("select");
        }

        // Optional: exercise the real backend bridge end-to-end.
        if (System.Environment.GetEnvironmentVariable("DMMAP_BRIDGE_PROBE") == "1")
        {
            var res = await DrBridge.UploadMap(
                "http://127.0.0.1:3000",
                "x",
                "Godot bridge test",
                _view.Stage.Snapshot().SavePngToBuffer(),
                setLive: true,
                s => GD.Print("bridge: ", s),
                tokens:
                [
                    new Dictionary<string, object>
                    {
                        ["x"] = 400.0,
                        ["y"] = 300.0,
                        ["color"] = "#e5484d",
                        ["label"] = "test token",
                        ["radius"] = 14.0,
                        ["isVisibleForPlayers"] = true,
                    },
                ]
            );
            GD.Print($"bridge result: ok={res.Ok} mapId={res.MapId} msg={res.Message}");
        }
        GetTree().Quit();
    }

    // ── Input ────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton b:
                OnMouseButton(b);
                break;
            case InputEventMouseMotion m:
                OnMouseMotion(m);
                break;
            case InputEventKey { Pressed: true } k:
                if (k.IsActionPressed("ui_undo"))
                    Undo();
                else if (k.IsActionPressed("ui_redo"))
                    Redo();
                else if (k.Keycode is Key.Delete or Key.Backspace)
                    DeleteSelection();
                else if (k.Keycode == Key.Escape)
                {
                    if (_clickPoints.Count > 0)
                    {
                        _clickPoints.Clear();
                        _ghost.SetStroke(_tool, [], 0);
                    }
                    else
                        ClearSelection();
                }
                else if (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter)
                    CommitClickStroke();
                else if (k.Keycode == Key.D && k.CtrlPressed)
                    DuplicateSelection();
                else if (k.Keycode == Key.R && !k.CtrlPressed)
                    RotateSelection(k.ShiftPressed ? -Math.PI / 12 : Math.PI / 12);
                else if (k.Keycode == Key.V && !k.CtrlPressed)
                    SetTool("select");
                else if (k.Keycode == Key.E && !k.CtrlPressed)
                    SetTool("erase");
                else if (k.Keycode == Key.F && !k.CtrlPressed)
                    SetTool("fill");
                else if (k.Keycode == Key.H && !k.CtrlPressed)
                    SetTool("play");
                else if (k.Keycode == Key.Space)
                    _spacePan = true;
                break;
            case InputEventKey { Pressed: false } k2:
                if (k2.Keycode == Key.Space)
                    _spacePan = false;
                break;
        }
    }

    void OnMouseButton(InputEventMouseButton b)
    {
        if (b.ButtonIndex == MouseButton.WheelUp && b.Pressed)
        {
            ZoomAt(b.Position, 1.15);
        }
        else if (b.ButtonIndex == MouseButton.WheelDown && b.Pressed)
        {
            ZoomAt(b.Position, 1 / 1.15);
        }
        else if (b.ButtonIndex is MouseButton.Middle or MouseButton.Right)
        {
            _panning = b.Pressed;
            _panFrom = b.Position;
        }
        else if (b.ButtonIndex == MouseButton.Left)
        {
            if (b.Pressed)
            {
                if (_spacePan)
                {
                    _panning = true;
                    _panFrom = b.Position;
                }
                else
                    BeginStroke();
            }
            else
            {
                if (_panning)
                    _panning = false;
                else
                    EndStroke();
            }
        }
    }

    void ZoomAt(Vector2 screenPos, double factor)
    {
        // Keep the world point under the cursor fixed.
        var half = GetViewportRect().Size / 2;
        var worldAtCursor = _camera.Position + (screenPos - half) / _camera.Zoom;
        var z = Mathf.Clamp(_camera.Zoom.X * factor, 0.4f, 3f);
        _camera.Zoom = new Vector2((float)z, (float)z);
        _camera.Position = worldAtCursor - (screenPos - half) / _camera.Zoom;
    }

    void OnMouseMotion(InputEventMouseMotion m)
    {
        if (_panning)
        {
            _camera.Position -= m.Relative / _camera.Zoom;
            _panFrom = m.Position;
            return;
        }
        if (!_drawing)
            return;
        var p = GetGlobalMousePosition();
        if (_tool.Mode == Mode.Select)
        {
            if (_marqueeing)
            {
                _marquee = new Rect2(_marqueeStart, p - _marqueeStart).Abs();
                _ghost.SetMarquee(_marquee);
            }
            else if (_selMoving)
                MoveSelectionTo(p);
            return;
        }
        if (_tool.Mode == Mode.Play)
        {
            if (_selMoving)
                MoveSelectionTo(p);
            return;
        }
        if (_tool.Mode == Mode.Brush)
        {
            if (_stroke[^1].DistanceTo(p) > _brushSize * 0.45)
            {
                _stroke.Add(p);
                _ghost.SetStroke(_tool, _stroke, _brushSize);
            }
            return;
        }
        if (_tool.Mode == Mode.Fill)
        {
            if (_stroke.Count > 1)
                _stroke[1] = p;
            else
                _stroke.Add(p);
            var r = new Rect2(_stroke[0], _stroke[1] - _stroke[0]).Abs();
            _ghost.SetFill(
                [r.Position, r.Position + new Vector2(r.Size.X, 0), r.End, r.Position + new Vector2(0, r.Size.Y)],
                [],
                [],
                []
            );
            return;
        }
        if (_stroke.Count == 0 || _stroke[^1].DistanceTo(p) > 4)
        {
            _stroke.Add(p);
            _ghost.SetStroke(_tool, _tool.Mode == Mode.Shape && _form > 0 ? FormPoints(_form) : _stroke, StrokeWidth());
            _ghost.SetSnap(_tool.Mode is Mode.Stroke or Mode.Fog ? SnapTarget(_stroke[^1]) : null);
            if (_tool.Kind == Kinds.Terrain && _stroke.Count >= 6 && _draftAt == 0)
                _draftAt = Time.GetTicksMsec() + 120;
            if (_tool.Mode == Mode.Prop && _stroke.Count > 1 && _stroke[^1].DistanceTo(_stroke[^2]) > 45)
                PlacePropAt(p);
        }
    }

    /// <summary>Enter commits the click-point polyline through the same
    /// path a freehand drag takes (web commitClickStroke).</summary>
    void CommitClickStroke()
    {
        if (_clickPoints.Count < 2)
            return;
        var pts = _clickPoints.ToList();
        _clickPoints.Clear();
        _ghost.SetStroke(_tool, [], 0);
        _stroke.Clear();
        _stroke.AddRange(pts);
        _drawing = true;
        EndStroke();
    }

    double StrokeWidth() =>
        _tool.Kind switch
        {
            Kinds.River => _pRiverW,
            Kinds.Road => _pRoadW,
            Kinds.Wall => 20,
            _ when _tool.Mode == Mode.Fog => 90,
            _ => 8,
        };

    /// <summary>Bounding-box rect/ellipse polygon for shape tools
    /// when a constrained form is selected.</summary>
    List<Vector2> FormPoints(int form)
    {
        var min = _stroke[0];
        var max = _stroke[0];
        foreach (var p in _stroke)
        {
            min = min.Min(p);
            max = max.Max(p);
        }
        if (form == 1)
            return [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
        var c = (min + max) / 2;
        var r = (max - min) / 2;
        var pts = new List<Vector2>();
        for (int i = 0; i < 28; i++)
        {
            var a = i / 28.0 * Mathf.Tau;
            pts.Add(c + new Vector2((float)(r.X * Math.Cos(a)), (float)(r.Y * Math.Sin(a))));
        }
        return pts;
    }

    /// <summary>Tallest terrain-shape height containing the point
    /// (0 on flat ground) — the brush's stacking baseline.</summary>
    double HeightAt(Vector2 p)
    {
        double h = 0;
        foreach (var s in _view.Scene.Shapes)
            if (s.Kind == Kinds.Terrain && Poly.PointInPolygon(p, [.. s.Points]))
                h = Math.Max(h, s.Height);
        return h;
    }

    /// <summary>Point where the in-progress stroke would fuse with an
    /// existing same-kind stroke — endpoint concat or T-junction
    /// (mirrors MergeStrokeInto's tolerance).</summary>
    Vector2? SnapTarget(Vector2 pen)
    {
        var tol = Math.Max(24, StrokeWidth());
        foreach (var s in _view.Scene.Strokes)
        {
            if (s.Kind != _tool.Kind)
                continue;
            if (pen.DistanceTo(s.Points[0]) <= tol)
                return s.Points[0];
            if (pen.DistanceTo(s.Points[^1]) <= tol)
                return s.Points[^1];
            for (var i = 1; i < s.Points.Count; i++)
            {
                var proj = Geometry2D.GetClosestPointToSegment(pen, s.Points[i - 1], s.Points[i]);
                if (pen.DistanceTo(proj) <= tol * 0.7f)
                    return proj;
            }
        }
        return null;
    }

    void BeginStroke()
    {
        _stroke.Clear();
        _stroke.Add(GetGlobalMousePosition());
        _drawing = true;
        if (_tool.Mode == Mode.Brush)
        {
            // Height stacks: the stamp layer sits one step above the
            // tallest terrain under the cursor (flat ground → first step).
            _brushHeight = HeightAt(_stroke[0]) + Math.Max(4, _pHeight > 0 ? _pHeight : 10);
            _ghost.SetStroke(_tool, _stroke, _brushSize);
        }
        else if (_tool.Mode == Mode.Select)
            BeginSelect(_stroke[0], Input.IsKeyPressed(Key.Shift));
        else if (_tool.Mode == Mode.Play)
            BeginPlay(_stroke[0]);
        else if (_tool.Mode == Mode.Prop)
            PlacePropAt(_stroke[0]);
        else if (_tool.Mode == Mode.Label)
            PromptLabel(_stroke[0]);
        else if (_tool.Mode == Mode.Erase)
            EraseAt(_stroke[0]);
        else if (_tool.Mode == Mode.Fill)
            ClearFill();
    }

    void EndStroke()
    {
        _drawing = false;
        _draftAt = 0;
        if (_tool.Mode is Mode.Select or Mode.Play)
        {
            if (_marqueeing)
                EndMarquee();
            else
                EndSelect(GetGlobalMousePosition());
            return;
        }
        _ghost.ClearDraft();
        _ghost.SetStroke(_tool, [], 0);
        if (_stroke.Count < 2)
        {
            // A click (not a drag): with a stroke/shape tool it adds a
            // vertex — kreslení po bodech, Enter zapíše tah.
            if (_tool.Mode is Mode.Stroke or Mode.Shape)
            {
                _clickPoints.Add(_stroke[0]);
                _ghost.SetStroke(_tool, _clickPoints, StrokeWidth());
            }
            return;
        }
        _clickPoints.Clear();
        if (_tool.Mode == Mode.Fill)
        {
            if (_stroke.Count >= 2)
            {
                _fillRect = new Rect2(_stroke[0], _stroke[1] - _stroke[0]).Abs();
                RunFill();
            }
            return;
        }
        var pts = Interactions.SimplifyPath(_stroke, 2.0);
        if (_tool.Mode == Mode.Shape && _form > 0)
            pts = FormPoints(_form);
        switch (_tool.Mode)
        {
            case Mode.Brush when _tool.Kind == VegKind:
                CommitVegBrush();
                break;
            case Mode.Brush:
                Commit(scene =>
                {
                    foreach (var c in _stroke)
                    {
                        var n = 16;
                        var poly = new List<Vector2>(n);
                        var seed = c.X * 13.7 + c.Y * 7.3;
                        for (var i = 0; i < n; i++)
                        {
                            var a = i / (double)n * Math.PI * 2;
                            var r = _brushSize * (0.86 + Rng.Noise(seed + i * 3.1) * 0.24);
                            poly.Add(c + new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r)));
                        }
                        var shape = new Shape
                        {
                            Id = scene.AllocId(),
                            Kind = Kinds.Terrain,
                            Points = poly,
                            Size = _pSlope,
                            Height = _brushHeight,
                            Roughness = _pRough,
                            Model = _pModel,
                        };
                        scene.Shapes = Interactions.MergeShapeInto(scene, shape);
                    }
                });
                break;
            case Mode.Fog:
                Commit(scene =>
                    scene.Fog.Add(
                        new Stroke
                        {
                            Id = scene.AllocId(),
                            Kind = _tool.Kind,
                            Points = pts,
                            Width = StrokeWidth(),
                        }
                    )
                );
                break;
            case Mode.Stroke:
                // Snap both ends onto existing same-kind endpoints so
                // the merge joins exactly instead of leaving a jog.
                {
                    var tol = Math.Max(24, StrokeWidth());
                    foreach (var s in _view.Scene.Strokes)
                    {
                        if (s.Kind != _tool.Kind)
                            continue;
                        foreach (var end in new[] { s.Points[0], s.Points[^1] })
                        {
                            if (pts[0].DistanceTo(end) <= tol)
                                pts[0] = end;
                            if (pts[^1].DistanceTo(end) <= tol)
                                pts[^1] = end;
                        }
                    }
                }
                Commit(scene =>
                {
                    var stroke = new Stroke
                    {
                        Id = scene.AllocId(),
                        Kind = _tool.Kind,
                        Points = pts,
                        Width = StrokeWidth(),
                        Model = _tool.Kind == Kinds.Road ? _roadModel : 0,
                    };
                    Interactions.AddStroke(scene, stroke);
                });
                break;
            case Mode.Shape when _tool.Kind == Kinds.Terrain:
                if (pts.Count >= 6)
                    Commit(scene =>
                    {
                        var shape = new Shape
                        {
                            Id = scene.AllocId(),
                            Kind = Kinds.Terrain,
                            Points = pts,
                            Size = _pSlope,
                            Height = _pHeight,
                            Roughness = _pRough,
                            Model = _pModel,
                        };
                        scene.Shapes = Interactions.MergeShapeInto(scene, shape);
                    });
                break;
            case Mode.Shape when _tool.Kind is Kinds.Pond or Kinds.Field:
                if (pts.Count >= 6)
                    Commit(scene =>
                    {
                        var shape = new Shape
                        {
                            Id = scene.AllocId(),
                            Kind = _tool.Kind,
                            Points = pts,
                            Size = 24,
                        };
                        scene.Shapes = Interactions.MergeShapeInto(scene, shape);
                    });
                break;
            case Mode.Shape when _tool.Kind == Kinds.Scatter:
                if (pts.Count >= 6)
                    Commit(scene =>
                    {
                        // Fill the outline with a deterministic forest
                        // mix — same scatter as the generator uses.
                        var mix = PropScatter.ScatterMix(
                            pts,
                            46,
                            [(PropKind.Tree, 3.0), (PropKind.Pine, 1.6), (PropKind.Bush, 0.7)],
                            density: 0.92,
                            seed: pts[0].X * 0.37 + pts[0].Y * 0.53
                        );
                        foreach (var pp in mix)
                        {
                            var kind = pp.Kind switch
                            {
                                PropKind.Pine => "pine",
                                PropKind.Bush => Kinds.Bush,
                                _ => Kinds.Tree,
                            };
                            Interactions.PlaceProtected(
                                scene,
                                new Prop
                                {
                                    Id = scene.AllocId(),
                                    Kind = kind,
                                    X = pp.X,
                                    Y = pp.Y,
                                    Scale = 22 * pp.Scale,
                                    Rotation = pp.Rotation,
                                    Seed = (int)pp.Seed,
                                }
                            );
                        }
                    });
                break;
            case Mode.Shape:
                // Other area kinds (house footprint, room, floor) —
                // plain add, no terrain merge.
                if (pts.Count >= 4)
                    Commit(scene =>
                        scene.Shapes.Add(
                            new Shape
                            {
                                Id = scene.AllocId(),
                                Kind = _tool.Kind,
                                Points = pts,
                                Size = 20,
                                Height = _tool.Kind is Kinds.House or Kinds.Room ? 18 : 0,
                            }
                        )
                    );
                break;
        }
        _stroke.Clear();
    }



    // ── Selection (move / delete / param edit) ────────────────────

    void BeginSelect(Vector2 p, bool additive)
    {
        var scene = _view.Scene;
        // Same top-down priority as the eraser: prop → stroke → shape.
        var prop = scene
            .Props.OrderByDescending(pr => pr.Id)
            .FirstOrDefault(pr => new Vector2((float)pr.X, (float)pr.Y).DistanceTo(p) < 24 * (pr.Scale / 24 + 0.4));
        Stroke? stroke =
            prop == null
                ? scene.Strokes.OrderByDescending(s => s.Id).FirstOrDefault(s => StrokeHit(s, p))
                : null;
        Shape? shape =
            prop == null && stroke == null
                ? scene.Shapes.OrderByDescending(s => s.Id).FirstOrDefault(s => Poly.PointInPolygon(p, [.. s.Points]))
                : null;

        if (prop == null && stroke == null && shape == null)
        {
            // Empty ground — rubber-band select (web `marquee`).
            if (!additive)
                ClearSelection();
            _marqueeing = true;
            _marqueeStart = p;
            _marquee = new Rect2(p, Vector2.Zero);
            _ghost.SetMarquee(_marquee);
            return;
        }
        if (!additive)
            ClearSelection();
        // Shift-click toggles membership; a plain click leaves the item
        // in the (just cleared) selection.
        if (prop != null && !_selProps.Remove(prop))
            _selProps.Add(prop);
        else if (stroke != null && !_selStrokes.Remove(stroke))
            _selStrokes.Add(stroke);
        else if (shape != null && !_selShapes.Remove(shape))
            _selShapes.Add(shape);
        SnapshotBackups();
        if (_variantOpt != null && _selProp != null)
            _variantOpt.Selected = _selProp.Model + 1;
        SyncSlidersToSelection();
        if (HasSelection())
        {
            _selMoving = true;
            _selOrigin = p;
        }
        UpdateSelectionGhost();
        UpdateToolUi();
    }

    /// <summary>Snapshot every selected element's position/points so a
    /// live drag can revert before the single commit.</summary>
    void SnapshotBackups()
    {
        _selPosBackup.Clear();
        _selPtsBackup.Clear();
        foreach (var pr in _selProps)
            _selPosBackup[pr] = new Vector2((float)pr.X, (float)pr.Y);
        foreach (var st in _selStrokes)
            _selPtsBackup[st] = [.. st.Points];
        foreach (var sh in _selShapes)
            _selPtsBackup[sh] = [.. sh.Points];
        var shape = _selShape;
        _selShapeBackup =
            shape == null ? null : (shape.Height, shape.Size, shape.Roughness, shape.Model);
    }

    /// <summary>Pick every element intersecting the marquee rect.</summary>
    void EndMarquee()
    {
        _marqueeing = false;
        _ghost.SetMarquee(null);
        if (_marquee.Size.LengthSquared() < 9)
        {
            UpdateSelectionGhost();
            UpdateToolUi();
            return;
        }
        var scene = _view.Scene;
        bool InRect(Vector2 p) => _marquee.HasPoint(p);
        foreach (var pr in scene.Props)
            if (InRect(new Vector2((float)pr.X, (float)pr.Y)) && !_selProps.Contains(pr))
                _selProps.Add(pr);
        foreach (var st in scene.Strokes)
            if (st.Points.Any(InRect) && !_selStrokes.Contains(st))
                _selStrokes.Add(st);
        foreach (var sh in scene.Shapes)
        {
            if (_selShapes.Contains(sh))
                continue;
            if (
                sh.Points.Any(InRect)
                || Poly.PointInPolygon(_marquee.Position, [.. sh.Points])
                || Poly.PointInPolygon(_marquee.End, [.. sh.Points])
            )
                _selShapes.Add(sh);
        }
        SnapshotBackups();
        SyncSlidersToSelection();
        UpdateSelectionGhost();
        UpdateToolUi();
    }

    bool HasSelection() =>
        _selProps.Count > 0 || _selStrokes.Count > 0 || _selShapes.Count > 0;

    /// <summary>Play mode: grab the marker token under the cursor and
    /// live-drag it like a select-move (web hitToken + gesture).</summary>
    void BeginPlay(Vector2 p)
    {
        var tokens = PlayTokens();
        for (var i = tokens.Count - 1; i >= 0; i--)
            if (tokens[i].Pos.DistanceTo(p) <= tokens[i].R)
            {
                ClearSelection();
                _selProps.Add(tokens[i].Prop);
                SnapshotBackups();
                _selMoving = true;
                _selOrigin = p;
                RefreshPlayTokens();
                return;
            }
        ClearSelection();
        RefreshPlayTokens();
    }

    /// <summary>Marker props → DR-style tokens (web tokensForScene):
    /// deterministic label = markerTokenLabels[model] + running number.</summary>
    List<(Prop Prop, Vector2 Pos, float R, string Text)> PlayTokens()
    {
        var tokens = new List<(Prop, Vector2, float, string)>();
        var counter = 0;
        foreach (var pr in _view.Scene.Props)
        {
            if (pr.Kind != "marker")
                continue;
            counter++;
            var model = pr.Model < 0 ? 0 : Math.Min(2, pr.Model);
            var label = $"{MarkerTokenLabels[model]} {counter}";
            // chipText — first letter of the label + its number.
            var chip = $"{label[0]}{counter}";
            tokens.Add((pr, new Vector2((float)pr.X, (float)pr.Y), (float)(pr.Scale * 0.9), chip));
        }
        return tokens;
    }

    static readonly string[] MarkerTokenLabels = ["Spawn", "Hráči", "Nepřítel"];

    void RefreshPlayTokens()
    {
        var selected = _selProp;
        _playLayer.SetTokens(
            PlayTokens()
                .Select(t => (t.Pos, t.R, t.Prop == selected, t.Text))
                .ToList()
        );
    }

    /// <summary>Live-move the selected element by the drag delta
    /// (mutated in place; history entry is written on release).</summary>
    void MoveSelectionTo(Vector2 p)
    {
        var delta = p - _selOrigin;
        foreach (var (pr, pos) in _selPosBackup)
        {
            pr.X = pos.X + delta.X;
            pr.Y = pos.Y + delta.Y;
        }
        foreach (var (el, pts) in _selPtsBackup)
        {
            var target = el is Stroke st ? st.Points : ((Shape)el).Points;
            for (var i = 0; i < target.Count && i < pts.Count; i++)
                target[i] = pts[i] + delta;
        }
        _selEdited = delta.LengthSquared() > 1;
        UpdateSelectionGhost();
        if (_tool.Mode == Mode.Play)
            RefreshPlayTokens();
        _pendingRebuild = true;
    }

    /// <summary>Revert the live mutation and write one history entry
    /// with the final offset.</summary>
    void EndSelect(Vector2 endPos)
    {
        _selMoving = false;
        if (!_selEdited)
            return;
        _selEdited = false;
        var delta = endPos - _selOrigin;
        // Restore the pre-drag values — Commit() re-applies the delta.
        foreach (var (pr, pos) in _selPosBackup)
        {
            pr.X = pos.X;
            pr.Y = pos.Y;
        }
        foreach (var (el, pts) in _selPtsBackup)
        {
            var target = el is Stroke st ? st.Points : ((Shape)el).Points;
            for (var i = 0; i < target.Count && i < pts.Count; i++)
                target[i] = pts[i];
        }
        if (delta.LengthSquared() < 1)
        {
            _view.Rebuild();
            return;
        }
        var propIds = _selProps.Select(pr => pr.Id).ToHashSet();
        var strokeIds = _selStrokes.Select(st => st.Id).ToHashSet();
        var shapeIds = _selShapes.Select(sh => sh.Id).ToHashSet();
        Commit(scene =>
        {
            foreach (var pr in scene.Props.Where(q => propIds.Contains(q.Id)))
            {
                pr.X += delta.X;
                pr.Y += delta.Y;
            }
            foreach (var st in scene.Strokes.Where(q => strokeIds.Contains(q.Id)))
            for (var i = 0; i < st.Points.Count; i++)
                st.Points[i] += delta;
            foreach (var sh in scene.Shapes.Where(q => shapeIds.Contains(q.Id)))
            for (var i = 0; i < sh.Points.Count; i++)
                sh.Points[i] += delta;
        });
        RefreshSelection();
    }

    /// <summary>Commit replaces the scene object — re-resolve the
    /// selection to the matching element in the new scene.</summary>
    void RefreshSelection()
    {
        // Commit replaces the scene object — re-resolve every selected
        // element in the new scene.
        var scene = _view.Scene;
        for (var i = _selProps.Count - 1; i >= 0; i--)
            _selProps[i] = scene.Props.FirstOrDefault(q => q.Id == _selProps[i].Id) ?? _selProps[i];
        for (var i = _selStrokes.Count - 1; i >= 0; i--)
            _selStrokes[i] = scene.Strokes.FirstOrDefault(q => q.Id == _selStrokes[i].Id) ?? _selStrokes[i];
        for (var i = _selShapes.Count - 1; i >= 0; i--)
            _selShapes[i] = scene.Shapes.FirstOrDefault(q => q.Id == _selShapes[i].Id) ?? _selShapes[i];
        SnapshotBackups();
        UpdateSelectionGhost();
    }

    void DeleteSelection()
    {
        if (!HasSelection())
            return;
        var propIds = _selProps.Select(pr => pr.Id).ToHashSet();
        var strokeIds = _selStrokes.Select(st => st.Id).ToHashSet();
        var shapeIds = _selShapes.Select(sh => sh.Id).ToHashSet();
        Commit(scene =>
        {
            scene.Props.RemoveAll(q => propIds.Contains(q.Id));
            scene.Strokes.RemoveAll(q => strokeIds.Contains(q.Id));
            scene.Shapes.RemoveAll(q => shapeIds.Contains(q.Id));
        });
        ClearSelection();
    }

    /// <summary>Rotate every selected element around its own center
    /// (web `rotateBy`): props spin in place, outlines rotate around
    /// their bbox center, and `Angle` follows so house roofs and
    /// oriented footprints stay aligned.</summary>
    void RotateSelection(double delta)
    {
        if (!HasSelection())
            return;
        var propIds = _selProps.Select(pr => pr.Id).ToHashSet();
        var strokeIds = _selStrokes.Select(st => st.Id).ToHashSet();
        var shapeIds = _selShapes.Select(sh => sh.Id).ToHashSet();
        static Vector2 Rotate(Vector2 p, double a, Vector2 c)
        {
            var d = p - c;
            return new Vector2(
                (float)(c.X + d.X * Math.Cos(a) - d.Y * Math.Sin(a)),
                (float)(c.Y + d.X * Math.Sin(a) + d.Y * Math.Cos(a))
            );
        }
        static Vector2 BBoxCenter(List<Vector2> pts)
        {
            var min = pts[0];
            var max = pts[0];
            foreach (var q in pts)
            {
                min = new Vector2(Math.Min(min.X, q.X), Math.Min(min.Y, q.Y));
                max = new Vector2(Math.Max(max.X, q.X), Math.Max(max.Y, q.Y));
            }
            return (min + max) / 2;
        }
        Commit(scene =>
        {
            foreach (var pr in scene.Props.Where(q => propIds.Contains(q.Id)))
                pr.Rotation += delta;
            foreach (var el in scene.Strokes.Where(q => strokeIds.Contains(q.Id)).Cast<object>()
                .Concat(scene.Shapes.Where(q => shapeIds.Contains(q.Id))))
            {
                var pts = el is Stroke st ? st.Points : ((Shape)el).Points;
                var c = BBoxCenter(pts);
                for (var i = 0; i < pts.Count; i++)
                    pts[i] = Rotate(pts[i], delta, c);
                if (el is Shape sh)
                    sh.Angle += delta;
            }
        });
        RefreshSelection();
    }

    /// <summary>Duplicate the selection (web Ctrl+D) — clones get new
    /// ids, are committed in one step and become the new selection.</summary>
    void DuplicateSelection()
    {
        if (!HasSelection())
            return;
        var props = _selProps.ToList();
        var strokes = _selStrokes.ToList();
        var shapes = _selShapes.ToList();
        List<long>? newIds = null;
        Commit(scene =>
        {
            foreach (var pr in props)
            {
                var copy = pr.DeepClone();
                copy.Id = scene.AllocId();
                scene.Props.Add(copy);
                (newIds ??= []).Add(copy.Id);
            }
            foreach (var st in strokes)
            {
                var copy = st.DeepClone();
                copy.Id = scene.AllocId();
                scene.Strokes.Add(copy);
                (newIds ??= []).Add(copy.Id);
            }
            foreach (var sh in shapes)
            {
                var copy = sh.DeepClone();
                copy.Id = scene.AllocId();
                scene.Shapes.Add(copy);
                (newIds ??= []).Add(copy.Id);
            }
        });
        ClearSelection();
        var scene2 = _view.Scene;
        foreach (var id in newIds ?? [])
        {
            if (scene2.Props.FirstOrDefault(q => q.Id == id) is { } pr)
                _selProps.Add(pr);
            else if (scene2.Strokes.FirstOrDefault(q => q.Id == id) is { } st)
                _selStrokes.Add(st);
            else if (scene2.Shapes.FirstOrDefault(q => q.Id == id) is { } sh)
                _selShapes.Add(sh);
        }
        SnapshotBackups();
        UpdateSelectionGhost();
        UpdateToolUi();
    }

    void ClearSelection()
    {
        _selProps.Clear();
        _selStrokes.Clear();
        _selShapes.Clear();
        if (_variantSection != null)
            _variantSection.Visible = _tool.Mode == Mode.Prop;
        _selPosBackup.Clear();
        _selPtsBackup.Clear();
        _selShapeBackup = null;
        _selMoving = false;
        _selEdited = false;
        _marqueeing = false;
        _ghost.SetSelection([], [], []);
        _ghost.SetMarquee(null);
    }

    /// <summary>Sliders write into the selected terrain shape live;
    /// the change commits when the drag ends (one undo step).</summary>
    void OnTerrainSlider(double h, double s, double r, int m, bool commit)
    {
        (_pHeight, _pSlope, _pRough, _pModel) = (h, s, r, m);
        if (_selShape is not { Kind: Kinds.Terrain } shape)
            return;
        if (commit)
        {
            // Revert live values then write one history entry.
            if (_selShapeBackup is { } b)
                (shape.Height, shape.Size, shape.Roughness, shape.Model) = b;
            var id = shape.Id;
            Commit(scene =>
            {
                if (scene.Shapes.FirstOrDefault(q => q.Id == id) is { } sh)
                    (sh.Height, sh.Size, sh.Roughness, sh.Model) = (h, s, r, m);
            });
            RefreshSelection();
        }
        else
        {
            (shape.Height, shape.Size, shape.Roughness, shape.Model) = (h, s, r, m);
            _pendingRebuild = true;
        }
    }

    void UpdateSelectionGhost()
    {
        _ghost.SetSelection(
            _selProps.Select(pr => new Vector2((float)pr.X, (float)pr.Y)).ToList(),
            _selStrokes.Select(st => st.Points.ToArray()).ToList(),
            _selShapes.Select(sh => sh.Points.ToArray()).ToList()
        );
    }

    /// <summary>Clicking a prop tool on an existing prop cycles its
    /// variant (seed++) instead of stacking a new prop on top.</summary>
    void PlacePropAt(Vector2 p)
    {
        var existing = _view
            .Scene.Props.OrderByDescending(pr => pr.Id)
            .FirstOrDefault(pr =>
                new Vector2((float)pr.X, (float)pr.Y).DistanceTo(p) < CycleRadius(_tool.Kind)
            );
        if (existing != null)
        {
            var id = existing.Id;
            Commit(scene =>
            {
                if (scene.Props.FirstOrDefault(q => q.Id == id) is { } pr)
                    pr.Seed++;
            });
            return;
        }
        var kind = _tool.Kind;
        Commit(scene =>
            Interactions.PlaceProtected(
                scene,
                new Prop
                {
                    Id = scene.AllocId(),
                    Kind = kind,
                    X = p.X,
                    Y = p.Y,
                    Scale = PropToolScale(kind),
                    Rotation = 0,
                    Seed = (int)(p.X * 0.37 + p.Y * 0.53),
                    Model = _propVariant,
                }
            )
        );
    }

    // ── Region fill ("Generátor výplně") ─────────────────────────

    /// <summary>Regenerate the pending fill for the dragged rect with
    /// the current mode/density/seed — same rect, new preview.</summary>
    void RunFill()
    {
        if (_fillRect is not { } r)
            return;
        _fillScene = null;
        _fillProps = [];
        _fillSources = [];
        var region = new List<Vector2>
        {
            r.Position,
            r.Position + new Vector2(r.Size.X, 0),
            r.End,
            r.Position + new Vector2(0, r.Size.Y),
        };
        var seed = int.TryParse(_fillSeedEdit.Text, out var sd) ? Math.Abs(sd) : 42;
        var outlines = new List<Vector2[]>();
        var lines = new List<Vector2[]>();
        switch (_fillMode.Selected)
        {
            case 1: // Krajina
            case 2: // Dungeon
            {
                _fillScene = MapGenerator.GenerateIn(seed, r, _fillMode.Selected == 2);
                outlines = _fillScene.Shapes.Select(sh => sh.Points.ToArray()).ToList();
                lines = _fillScene.Strokes.Select(st => st.Points.ToArray()).ToList();
                _fillProps = _fillScene.Props;
                _fillStats.Text = $"Nových {_fillScene.Shapes.Count + _fillScene.Strokes.Count + _fillProps.Count}";
                break;
            }
            case 3: // Smart — context-aware scatter, no source samples
            {
                var res = SmartFill.Generate(
                    region,
                    _view.Scene,
                    seed,
                    _fillDensity.Value / 100
                );
                _fillProps = res.Props;
                _fillSources = res.SourceIds;
                _fillStats.Text = $"Nových {_fillProps.Count} · kostka = jiná varianta";
                break;
            }
            default:
            {
                var res = MapFill.Generate(
                    region,
                    _view.Scene,
                    seed,
                    _fillDensity.Value / 100
                );
                _fillProps = res.Props;
                _fillSources = res.SourceIds;
                _fillStats.Text = $"Zdrojů {_fillSources.Count} · Nových {_fillProps.Count}";
                break;
            }
        }
        _fillPreview.SetProps(
            _fillProps.Select(pr =>
                new PropPlacement(
                    SceneView.MapProp(pr.Kind),
                    pr.X,
                    pr.Y,
                    pr.Scale / 24.0,
                    pr.Rotation,
                    pr.Seed
                ).WithDef(pr.Kind)
            )
        );
        _fillPreview.Visible = true;
        _fillCommit.Disabled =
            _fillProps.Count == 0 && (_fillScene?.Shapes.Count ?? 0) == 0;
        _ghost.SetFill(region, _fillSources.Select(id =>
        {
            var pr = _view.Scene.Props.FirstOrDefault(q => q.Id == id);
            return pr != null ? new Vector2((float)pr.X, (float)pr.Y) : Vector2.Zero;
        }).Where(q => q != Vector2.Zero).ToList(), outlines, lines);
    }

    /// <summary>Commit the previewed fill as one undo step.</summary>
    void CommitFill()
    {
        if (_fillScene is { } gen)
            Commit(scene => MapGenerator.MergeInto(scene, gen));
        else if (_fillProps.Count > 0)
            Commit(scene =>
            {
                foreach (var pr in _fillProps)
                {
                    pr.Id = scene.AllocId();
                    scene.Props.Add(pr);
                }
            });
        ClearFill();
    }

    void ClearFill()
    {
        _fillRect = null;
        _fillProps = [];
        _fillSources = [];
        _fillScene = null;
        if (_fillPreview != null)
            _fillPreview.Visible = false;
        _ghost?.SetFill([], [], [], []);
    }

    /// <summary>Small text prompt — on OK places a "label" prop with
    /// the entered caption at `pos`.</summary>
    void PromptLabel(Vector2 pos)
    {
        var dlg = new ConfirmationDialog
        {
            Title = "Popisek",
            Theme = new Theme { DefaultFontSize = Fs(13) },
        };
        var edit = new LineEdit { PlaceholderText = "Text popisku…", CustomMinimumSize = new Vector2(Us(220), 0) };
        dlg.AddChild(edit);
        dlg.RegisterTextEnter(edit);
        dlg.Confirmed += () =>
        {
            var text = edit.Text.Trim();
            if (text.Length > 0)
                Commit(scene =>
                    scene.Props.Add(
                        new Prop
                        {
                            Id = scene.AllocId(),
                            Kind = "label",
                            X = pos.X,
                            Y = pos.Y,
                            Scale = 16,
                            Text = text,
                        }
                    )
                );
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
        edit.GrabFocus();
    }

    void EraseAt(Vector2 p)
    {
        Commit(scene =>
        {
            // Topmost first: props, then strokes, then shapes.
            var prop = scene
                .Props.OrderByDescending(pr => pr.Id)
                .FirstOrDefault(pr => new Vector2((float)pr.X, (float)pr.Y).DistanceTo(p) < 24 * (pr.Scale / 24 + 0.4));
            if (prop != null)
            {
                scene.Props.Remove(prop);
                return;
            }
            var stroke = scene
                .Strokes.OrderByDescending(s => s.Id)
                .FirstOrDefault(s => StrokeHit(s, p));
            if (stroke != null)
            {
                scene.Strokes.Remove(stroke);
                return;
            }
            var shape = scene
                .Shapes.OrderByDescending(s => s.Id)
                .FirstOrDefault(s => Poly.PointInPolygon(p, [.. s.Points]));
            if (shape != null)
                scene.Shapes.Remove(shape);
        });
    }

    static bool StrokeHit(Stroke s, Vector2 p)
    {
        for (var i = 0; i + 1 < s.Points.Count; i++)
            if (Interactions.DistanceToSegment(p, s.Points[i], s.Points[i + 1]) < s.Width / 2 + 6)
                return true;
        return false;
    }

    // ── History ──────────────────────────────────────────────────

    void Commit(Action<Scene> mutate)
    {
        var next = _view.Scene.DeepClone();
        mutate(next);
        _history.Commit(next);
        _view.Scene = _history.Current;
        _view.Rebuild();
    }

    void Undo()
    {
        if (_history.Undo() is { } s)
        {
            _view.Scene = s;
            _view.Rebuild();
        }
    }

    void Redo()
    {
        if (_history.Redo() is { } s)
        {
            _view.Scene = s;
            _view.Rebuild();
        }
    }

    // ── Persistence ──────────────────────────────────────────────

    void Save()
    {
        using var f = FileAccess.Open("user://dmmap-scene.json", FileAccess.ModeFlags.Write);
        f.StoreString(SceneJson.Serialize(_view.Scene));
        GD.Print("saved ", ProjectSettings.GlobalizePath("user://dmmap-scene.json"));
    }

    void Load()
    {
        var path = "user://dmmap-scene.json";
        if (!FileAccess.FileExists(path))
            return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var warnings = new List<string>();
        if (SceneJson.TryParse(f.GetAsText(), out var scene, warnings))
        {
            _history.Commit(scene!);
            _view.Scene = _history.Current;
            _view.Rebuild();
        }
        foreach (var err in warnings)
            GD.PushWarning(err);
    }

    void ExportPng()
    {
        var img = _view.Stage.Snapshot();
        img.SavePng("user://dmmap-export.png");
        GD.Print("exported ", ProjectSettings.GlobalizePath("user://dmmap-export.png"));
    }

    /// <summary>Seed → whole generated scene as one undo-able commit.</summary>
    void Generate()
    {
        var seed = int.TryParse(_seedEdit.Text, out var s)
            ? s
            : _seedEdit.Text.GetHashCode();
        var gen =
            _presetOpt.Selected == 1
                ? MapGenerator.GenerateDungeon(seed, MapSize)
                : MapGenerator.GenerateLandscape(seed, MapSize);
        ClearSelection();
        _history.Commit(gen);
        _view.Scene = _history.Current;
        _view.Rebuild();
    }

    /// <summary>Sliders reflect the selected terrain shape's params.</summary>
    void SyncSlidersToSelection()
    {
        if (_selShape is not { Kind: Kinds.Terrain } sh)
            return;
        _syncing = true;
        _sHeight.Value = sh.Height;
        _sSlope.Value = sh.Size;
        _sRough.Value = sh.Roughness;
        _modelOpt.Selected = Math.Clamp(sh.Model, 0, 2);
        _syncing = false;
    }

    // ── Tool palette ────────────────────────────────────────────────

    VBoxContainer _toolLib = null!;
    readonly Dictionary<string, Button> _toolCards = new();
    Label _toolName = null!;
    Label _toolDesc = null!;
    OptionButton _formOpt = null!;
    VBoxContainer _waterSection = null!;
    VBoxContainer _roadSection = null!;

    void SetTool(string id)
    {
        foreach (var t in PaletteTools())
            if (t.Id == id)
            {
                _tool = t;
                ClearSelection();
                _playLayer.Visible = t.Mode == Mode.Play;
                if (_playLayer.Visible)
                    RefreshPlayTokens();
                UpdateToolUi();
                return;
            }
    }

    /// <summary>Show only the parameter controls relevant to the
    /// active tool (terrain sliders, road model/width, river width) and
    /// refresh the name/description/card highlight in the dock.</summary>
    void UpdateToolUi()
    {
        if (_toolName != null)
            _toolName.Text = _tool.Label;
        if (_toolDesc != null)
        {
            _toolDesc.Text = ToolDesc.GetValueOrDefault(_tool.Id, "");
            _toolDesc.Visible = _toolDesc.Text.Length > 0;
        }
        if (_terrainSection != null)
            _terrainSection.Visible = _tool.Kind == Kinds.Terrain;
        if (_waterSection != null)
            _waterSection.Visible =
                _tool.Kind == Kinds.River || _tool.Kind == Kinds.Pond;
        if (_roadSection != null)
            _roadSection.Visible = _tool.Kind == Kinds.Road;
        if (_formOpt != null)
            _formOpt.Visible = _tool.Mode == Mode.Shape;
        if (_fogSection != null)
            _fogSection.Visible = _tool.Mode == Mode.Fog;
        if (_brushSection != null)
            _brushSection.Visible = _tool.Mode == Mode.Brush;
        if (_vegSection != null)
            _vegSection.Visible = _tool.Kind == VegKind;
        if (_fillSection != null)
            _fillSection.Visible = _tool.Mode == Mode.Fill;
        if (_tool.Mode != Mode.Fill)
            ClearFill();
        if (_variantSection != null)
            _variantSection.Visible =
                _tool.Mode == Mode.Prop || (_tool.Mode == Mode.Select && _selProp != null);
        foreach (var kv in _toolCards)
            kv.Value.SetPressedNoSignal(kv.Key == _tool.Id);
    }

    /// <summary>Mirror the current environment selections into the
    /// thumbnail renderer — re-renders card icons on every change.</summary>
    void SyncThumbLook() => _thumbs.SetLook(_lookBiome, _lookMood, _lookArt, _lookSeason);

    /// <summary>Categorized tool browser — web-style card grid grouped
    /// by category, filtered by the search box (diacritics-folded).</summary>
    void PopulateToolCards(string needle)
    {
        _toolNeedle = needle;
        needle = Fold(needle.Trim());
        foreach (var c in _toolLib.GetChildren())
            c.QueueFree();
        _toolCards.Clear();
        var palette = PaletteTools().ToList();
        foreach (var cat in palette.Select(t => t.Category).Distinct())
        {
            var items = palette
                .Where(t => t.Category == cat)
                .Where(t => needle == "" || Fold(t.Label + " " + t.Id).Contains(needle))
                .ToList();
            if (items.Count == 0)
                continue;
            var head = new Label { Text = cat };
            head.AddThemeFontSizeOverride("font_size", Fs(11));
            head.AddThemeColorOverride("font_color", new Color(0.62f, 0.59f, 0.53f));
            _toolLib.AddChild(head);
            var flow = new HFlowContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            flow.AddThemeConstantOverride("h_separation", Fs(5));
            flow.AddThemeConstantOverride("v_separation", Fs(5));
            _toolLib.AddChild(flow);
            foreach (var t in items)
                flow.AddChild(ToolCard(t));
        }
        UpdateToolUi();
    }

    /// <summary>Web-style card: a live-rendered thumbnail of the tool's
    /// output on top, the tool name under it, tool color as the accent
    /// strip. The thumbnail streams in from ToolThumbs once rendered.</summary>
    Button ToolCard(ToolDef t)
    {
        var col = Palette.Hex(ToolColor.GetValueOrDefault(t.Id, "#9a9584"));
        var b = new Button
        {
            ToggleMode = true,
            TooltipText = ToolDesc.GetValueOrDefault(t.Id, ""),
            CustomMinimumSize = new Vector2(Us(114), Us(102)),
        };
        b.AddThemeStyleboxOverride("normal", CardStyle(col, 0.45f));
        b.AddThemeStyleboxOverride("hover", CardStyle(col, 0.8f, lift: true));
        b.AddThemeStyleboxOverride("pressed", CardStyle(col, 1.0f, pressed: true));
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        v.AddThemeConstantOverride("separation", Fs(3));
        v.Alignment = BoxContainer.AlignmentMode.Center;
        b.AddChild(v);
        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(Us(104), Us(65)),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var frameStyle = new StyleBoxFlat { BgColor = new Color(0.09f, 0.08f, 0.07f) };
        frameStyle.SetCornerRadiusAll(Fs(3));
        frame.AddThemeStyleboxOverride("panel", frameStyle);
        var tex = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(tex);
        v.AddChild(frame);
        var label = new Label
        {
            Text = t.Label,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", Fs(11));
        label.AddThemeColorOverride("font_color", new Color(0.88f, 0.85f, 0.79f));
        v.AddChild(label);
        b.Pressed += () => SetTool(t.Id);
        _toolCards[t.Id] = b;
        _thumbs.Register(t.Id, tex);
        return b;
    }

    /// <summary>Card chrome — dark tile with a tool-color accent strip
    /// along the bottom edge; alpha rises hover → pressed.</summary>
    StyleBoxFlat CardStyle(
        Color bar,
        float accent,
        bool lift = false,
        bool pressed = false
    )
    {
        var s = new StyleBoxFlat
        {
            BgColor = pressed
                ? new Color(0.32f, 0.28f, 0.20f)
                : lift
                    ? new Color(0.20f, 0.19f, 0.16f)
                    : new Color(0.15f, 0.14f, 0.12f),
            BorderColor = bar with { A = accent },
        };
        s.SetCornerRadiusAll(Fs(4));
        s.SetBorderWidthAll(0);
        s.BorderWidthBottom = Fs(3);
        s.SetContentMarginAll(Us(4));
        return s;
    }

    /// <summary>Lowercase + strip Czech diacritics for search.</summary>
    static string Fold(string s) =>
        s.ToLowerInvariant()
            .Replace("á", "a")
            .Replace("č", "c")
            .Replace("ď", "d")
            .Replace("é", "e")
            .Replace("ě", "e")
            .Replace("í", "i")
            .Replace("ň", "n")
            .Replace("ó", "o")
            .Replace("ř", "r")
            .Replace("š", "s")
            .Replace("ť", "t")
            .Replace("ú", "u")
            .Replace("ů", "u")
            .Replace("ý", "y")
            .Replace("ž", "z");

    // ── UI ───────────────────────────────────────────────────────

    void BuildUi()
    {
        var layer = new CanvasLayer();
        _uiLayer = layer;
        AddChild(layer);
        // Shared scaled font for every control under the dock/actions —
        // OptionButtons, LineEdits, CheckBoxes, slider labels, tooltips.
        var uiTheme = new Theme { DefaultFontSize = Fs(13) };

        // ── top-right action bar ─────────────────────────────────
        var actions = new HBoxContainer
        {
            AnchorLeft = 1,
            AnchorRight = 1,
            OffsetLeft = -Us(700),
            OffsetRight = -Us(8),
            OffsetTop = Us(8),
            Alignment = BoxContainer.AlignmentMode.End,
            Theme = uiTheme,
        };
        actions.AddThemeConstantOverride("separation", Fs(6));
        layer.AddChild(actions);
        ActionButton(actions, "Zpět", Undo);
        ActionButton(actions, "Uložit", Save);
        ActionButton(actions, "Načíst", Load);
        ActionButton(actions, "Export PNG", ExportPng);
        ActionButton(actions, "Odeslat do DR", () => _bridge?.PopupCentered());
        ActionButton(actions, "Nový", () =>
            Commit(scene =>
            {
                scene.Shapes.Clear();
                scene.Strokes.Clear();
                scene.Props.Clear();
            })
        );

        // ── bottom dock: settings column + tool library ──────────
        var dock = new PanelContainer
        {
            AnchorTop = 1,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetTop = -Us(244),
            Theme = uiTheme,
        };
        var dockStyle = new StyleBoxFlat { BgColor = new Color(0.11f, 0.10f, 0.09f, 0.96f) };
        dockStyle.SetContentMarginAll(Us(10));
        dock.AddThemeStyleboxOverride("panel", dockStyle);
        layer.AddChild(dock);
        var row = new HBoxContainer();
        dock.AddChild(row);

        // ── settings column (tool name, description, params) ─────
        var settings = new VBoxContainer { CustomMinimumSize = new Vector2(Us(300), 0) };
        row.AddChild(settings);
        _toolName = new Label();
        _toolName.AddThemeFontSizeOverride("font_size", Fs(16));
        settings.AddChild(_toolName);
        _toolDesc = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _toolDesc.AddThemeFontSizeOverride("font_size", Fs(11));
        _toolDesc.AddThemeColorOverride("font_color", new Color(0.72f, 0.69f, 0.62f));
        settings.AddChild(_toolDesc);
        var paramScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        settings.AddChild(paramScroll);
        var box = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        paramScroll.AddChild(box);

        _formOpt = new OptionButton();
        _formOpt.AddItem("Volný tah");
        _formOpt.AddItem("Obdélník");
        _formOpt.AddItem("Elipsa");
        _formOpt.ItemSelected += i => _form = (int)i;
        box.AddChild(_formOpt);

        _terrainSection = new VBoxContainer();
        box.AddChild(_terrainSection);
        _sHeight = TerrainSlider(_terrainSection, "Výška", 0, 100, _pHeight, v => _pHeight = v);
        _sSlope = TerrainSlider(_terrainSection, "Šířka svahu", 10, 90, _pSlope, v => _pSlope = v);
        _sRough = TerrainSlider(_terrainSection, "Nerovnosti", 0, 100, _pRough, v => _pRough = v);
        _modelOpt = new OptionButton();
        foreach (var name in new[] { "Skalnatý", "Hlínitý", "Hřeben" })
            _modelOpt.AddItem(name);
        _modelOpt.Selected = _pModel;
        _modelOpt.ItemSelected += i =>
        {
            _pModel = (int)i;
            OnTerrainSlider(_pHeight, _pSlope, _pRough, _pModel, commit: true);
        };
        _terrainSection.AddChild(_modelOpt);

        _brushSection = new VBoxContainer { Visible = false };
        box.AddChild(_brushSection);
        Slider(_brushSection, "Velikost štětce", 16, 90, _brushSize, v => _brushSize = v);
        BuildVegSection(box);

        _variantSection = new VBoxContainer();
        box.AddChild(_variantSection);
        _variantOpt = new OptionButton();
        _variantOpt.AddItem("Varianta: automatická");
        _variantOpt.AddItem("Varianta I");
        _variantOpt.AddItem("Varianta II");
        _variantOpt.AddItem("Varianta III");
        _variantOpt.ItemSelected += i =>
        {
            if (_selProp != null && i > 0)
            {
                var model = (int)i - 1;
                Commit(scene =>
                {
                    var pr = scene.Props.FirstOrDefault(q => q.Id == _selProp.Id);
                    if (pr != null)
                        pr.Model = model;
                });
            }
            _propVariant = (int)i - 1;
        };
        _variantSection.AddChild(_variantOpt);

        // Protection override (assets/config/placement.json) — the prop
        // and brush tools both place under it, so each gets a synced copy.
        var ignoreBoxes = new List<CheckBox>();
        foreach (var section in new[] { _variantSection, _brushSection })
        {
            var cb = new CheckBox
            {
                Text = "Ignorovat ochranné zóny",
                ButtonPressed = Placement.IgnoreProtection,
                TooltipText = "Objekty se položí přesně tam, kam klikneš — nic se neodsouvá ani nemaže kolem táboráků, studní apod.",
            };
            cb.Toggled += on =>
            {
                Placement.IgnoreProtection = on;
                foreach (var other in ignoreBoxes)
                    other.SetPressedNoSignal(on);
            };
            ignoreBoxes.Add(cb);
            section.AddChild(cb);
        }

        _waterSection = new VBoxContainer();
        box.AddChild(_waterSection);
        Slider(_waterSection, "Šířka řeky", 10, 60, _pRiverW, v => _pRiverW = v);

        _roadSection = new VBoxContainer();
        box.AddChild(_roadSection);
        Slider(_roadSection, "Šířka cesty", 8, 40, _pRoadW, v => _pRoadW = v);
        _bridgeOpt = new OptionButton();
        foreach (var name in new[] { "Most: dřevěný", "Most: kamenný", "Most: lanový" })
            _bridgeOpt.AddItem(name);
        _bridgeOpt.ItemSelected += i => _roadModel = (int)i;
        _roadSection.AddChild(_bridgeOpt);

        _fogSection = new VBoxContainer();
        box.AddChild(_fogSection);
        var shroudAll = new Button { Text = "Zahalit celou mapu" };
        shroudAll.Pressed += () => Commit(scene => scene.FogCoverAll = true);
        _fogSection.AddChild(shroudAll);
        var revealAll = new Button { Text = "Odkrýt celou mapu" };
        revealAll.Pressed += () =>
            Commit(scene =>
            {
                scene.FogCoverAll = false;
                scene.Fog.RemoveAll(t => t.Kind == "shroud");
            });
        _fogSection.AddChild(revealAll);
        var fogRooms = new CheckBox
        {
            Text = "Zahalit interiéry",
            ButtonPressed = true,
            TooltipText = "Půdorysy místností se pečou do masky mlhy — hráči je nevidí, dokud je neodhalíš.",
        };
        fogRooms.Toggled += on => Commit(scene => scene.FogRooms = on);
        _fogSection.AddChild(fogRooms);

        _fillSection = new VBoxContainer { Visible = false };
        box.AddChild(_fillSection);
        _fillMode = new OptionButton();
        _fillMode.AddItem("Výplň podle vzorků");
        _fillMode.AddItem("Krajina");
        _fillMode.AddItem("Dungeon");
        _fillMode.AddItem("Smart");
        _fillMode.ItemSelected += _ => RunFill();
        _fillSection.AddChild(_fillMode);
        _fillStats = new Label();
        _fillStats.AddThemeFontSizeOverride("font_size", Fs(11));
        _fillSection.AddChild(_fillStats);
        var drow = new HBoxContainer();
        _fillDensity = new HSlider { MinValue = 15, MaxValue = 100, Value = 55, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fillDensity.ValueChanged += _ => RunFill();
        drow.AddChild(new Label { Text = "Hustota" });
        drow.AddChild(_fillDensity);
        _fillSection.AddChild(drow);
        var srow = new HBoxContainer();
        _fillSeedEdit = new LineEdit { Text = "42", PlaceholderText = "seed", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fillSeedEdit.TextSubmitted += _ => RunFill();
        srow.AddChild(_fillSeedEdit);
        var dice = new Godot.Button { Text = "🎲" };
        dice.Pressed += () =>
        {
            _fillSeedEdit.Text = ((int)(Time.GetTicksMsec() % 100000)).ToString();
            RunFill();
        };
        srow.AddChild(dice);
        _fillSection.AddChild(srow);
        var frow = new HBoxContainer();
        _fillCommit = new Godot.Button
        {
            Text = "Vyplnit",
            Disabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _fillCommit.Pressed += CommitFill;
        frow.AddChild(_fillCommit);
        var cancel = new Godot.Button { Text = "Zrušit" };
        cancel.Pressed += ClearFill;
        frow.AddChild(cancel);
        _fillSection.AddChild(frow);

        row.AddChild(new VSeparator());

        // ── tool library: search + selects + card grid ───────────
        var library = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(library);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Fs(6));
        library.AddChild(head);

        var search = new LineEdit
        {
            PlaceholderText = "Hledat nástroj…",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        search.TextChanged += _ => PopulateToolCards(search.Text);
        head.AddChild(search);

        var biome = new OptionButton();
        foreach (var id in SceneConfig.BiomeIds())
            biome.AddItem(id);
        biome.Selected = Math.Max(0, SceneConfig.BiomeIds().IndexOf("woodland"));
        biome.ItemSelected += i =>
        {
            _lookBiome = biome.GetItemText((int)i);
            _view.SetBiome(_lookBiome);
            OnPaletteBiomeChanged();
            RebuildSeasonOptions();
            _lookSeason = null;
            SyncThumbLook();
        };
        head.AddChild(biome);

        var mood = new OptionButton();
        foreach (var id in SceneConfig.MoodIds())
            mood.AddItem(id);
        mood.Selected = Math.Max(0, SceneConfig.MoodIds().IndexOf("day"));
        mood.ItemSelected += i =>
        {
            _lookMood = mood.GetItemText((int)i);
            _view.SetMood(_lookMood);
            SyncThumbLook();
        };
        head.AddChild(mood);

        var artPack = new OptionButton();
        foreach (var id in new[] { "classic", "storybook", "ink" })
            artPack.AddItem(id);
        artPack.ItemSelected += i =>
        {
            _lookArt = artPack.GetItemText((int)i);
            _view.SetArtPack(_lookArt);
            SyncThumbLook();
        };
        head.AddChild(artPack);

        _seasonOpt = new OptionButton();
        _seasonOpt.ItemSelected += i =>
        {
            _lookSeason = i == 0 ? null : _seasonOpt.GetItemText((int)i);
            _view.SetSeason(_lookSeason);
            SyncThumbLook();
        };
        head.AddChild(_seasonOpt);
        RebuildSeasonOptions();

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        library.AddChild(scroll);
        _toolLib = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_toolLib);
        PopulateToolCards("");

        var genRow = new HBoxContainer();
        genRow.AddThemeConstantOverride("separation", Fs(6));
        library.AddChild(genRow);
        _presetOpt = new OptionButton();
        _presetOpt.AddItem("Krajina");
        _presetOpt.AddItem("Dungeon");
        genRow.AddChild(_presetOpt);
        _seedEdit = new LineEdit
        {
            Text = "42",
            PlaceholderText = "seed",
            CustomMinimumSize = new Vector2(Us(70), 0),
        };
        genRow.AddChild(_seedEdit);
        var gen = new Godot.Button
        {
            Text = "🎲 Terén",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        gen.Pressed += Generate;
        genRow.AddChild(gen);

        UpdateToolUi();
        SyncThumbLook();

        void RebuildSeasonOptions()
        {
            _seasonOpt.Clear();
            _seasonOpt.AddItem("sezóna: výchozí");
            foreach (var id in _view.Biome.Seasons)
                _seasonOpt.AddItem(id);
            _seasonOpt.Selected = 0;
        }
    }

    void ActionButton(HBoxContainer box, string text, Action act)
    {
        var b = new Button { Text = text };
        var s = new StyleBoxFlat { BgColor = new Color(0.16f, 0.15f, 0.13f, 0.9f) };
        s.SetCornerRadiusAll(Fs(4));
        s.SetContentMarginAll(Us(6));
        b.AddThemeStyleboxOverride("normal", s);
        b.AddThemeFontSizeOverride("font_size", Fs(12));
        b.Pressed += act.Invoke;
        box.AddChild(b);
    }

    void Slider(VBoxContainer box, string label, double min, double max, double value, Action<double> set)
    {
        var l = new Label();
        var s = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Value = value,
            CustomMinimumSize = new Vector2(0, Us(18)),
        };
        void Sync() => l.Text = $"{label}: {s.Value:0}";
        s.ValueChanged += v =>
        {
            set(v);
            Sync();
        };
        Sync();
        box.AddChild(l);
        box.AddChild(s);
    }

    /// <summary>Terrain slider: live-writes into the selected shape
    /// while dragging, single commit when the drag ends.</summary>
    HSlider TerrainSlider(VBoxContainer box, string label, double min, double max, double value, Action<double> set)
    {
        var l = new Label();
        var s = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Value = value,
            CustomMinimumSize = new Vector2(0, Us(18)),
        };
        void Sync() => l.Text = $"{label}: {s.Value:0}";
        s.ValueChanged += v =>
        {
            if (_syncing)
            {
                Sync();
                return;
            }
            set(v);
            Sync();
            OnTerrainSlider(_pHeight, _pSlope, _pRough, _pModel, commit: false);
        };
        s.DragEnded += changed =>
        {
            if (changed && !_syncing)
                OnTerrainSlider(_pHeight, _pSlope, _pRough, _pModel, commit: true);
        };
        Sync();
        box.AddChild(l);
        box.AddChild(s);
        return s;
    }

    static void Button(VBoxContainer box, string label, Action action)
    {
        var b = new Godot.Button { Text = label };
        b.Pressed += () => action();
        box.AddChild(b);
    }
}

/// <summary>Ghost preview — in-progress stroke, live massif draft
/// and the selection highlight, drawn in the main viewport above
/// the baked composite.</summary>
public partial class GhostLayer : Node2D
{
    /// <summary>View reference for the painted stroke preview — its
    /// Scene instance is swapped on every commit, so always read
    /// View.Scene live.</summary>
    public SceneView? View;

    MapEditor.Mode _mode;
    string _kind = "";
    List<Vector2> _pts = [];
    double _width;
    Massif? _draft;
    WaterPainter? _strokePaint;
    List<Vector2> _selProps = [];
    List<Vector2[]> _selLines = [];
    List<Vector2[]> _selOutlines = [];
    Rect2? _marquee;
    Vector2? _snap;
    List<Vector2> _fillRect = [];
    List<Vector2> _fillSrc = [];
    List<Vector2[]> _fillPolys = [];
    List<Vector2[]> _fillLines = [];

    /// <summary>Pending region fill: the rect outline, detected source
    /// positions (green "detected" glow) and ghosted generated geometry.</summary>
    public void SetFill(
        List<Vector2> rect,
        List<Vector2> sources,
        List<Vector2[]> polys,
        List<Vector2[]> lines
    )
    {
        _fillRect = rect;
        _fillSrc = sources;
        _fillPolys = polys;
        _fillLines = lines;
        QueueRedraw();
    }

    public void SetSnap(Vector2? p)
    {
        _snap = p;
        QueueRedraw();
    }

    public void SetStroke(MapEditor.ToolDef tool, List<Vector2> pts, double width)
    {
        (_mode, _kind, _pts, _width) = (tool.Mode, tool.Kind, [.. pts], width);
        UpdateStrokePaint();
        QueueRedraw();
    }

    /// <summary>Painted stroke preview — like the web draft: run the
    /// in-progress road/river through the real WaterPainter over the
    /// baked map, merged exactly as the commit would merge it. The
    /// host's repainted pixels are identical, so junctions read clean
    /// and nothing dims the existing stroke.</summary>
    void UpdateStrokePaint()
    {
        _strokePaint ??= new WaterPainter { Visible = false };
        if (_strokePaint.GetParent() == null)
            AddChild(_strokePaint);
        _strokePaint.ClearRuns();
        _strokePaint.Visible = false;
        var preview = PreviewStroke();
        if (preview is not { } st)
            return;
        var biome = View!.Biome;
        _strokePaint.Water = biome.Water;
        _strokePaint.RoadDirt = biome.At("road.dirt");
        _strokePaint.RoadVerge = biome.At("road.verge");
        _strokePaint.RoadHalo = biome.At("road.halo");
        if (st.Kind == Kinds.Road)
            _strokePaint.AddRoad(st.Points, st.Width);
        else
            _strokePaint.AddRiver(st.Points, st.Width);
        _strokePaint.Visible = true;
    }

    /// <summary>The stroke the commit would store — merged into the
    /// touching same-kind host (endpoint concat or T-junction arm),
    /// else the draft alone. Returns the single changed entry.</summary>
    Stroke? PreviewStroke()
    {
        if (
            View == null
            || _mode != MapEditor.Mode.Stroke
            || _pts.Count < 2
            || _kind is not (Kinds.Road or Kinds.River)
        )
            return null;
        var scene = View.Scene;
        // Mirror the commit path exactly: endpoint snap to same-kind
        // ends, simplify, then merge — the preview is what gets stored.
        var pts = Interactions.SimplifyPath(_pts);
        var tol = Math.Max(24, _width);
        foreach (var s in scene.Strokes)
        {
            if (s.Kind != _kind)
                continue;
            if (pts[0].DistanceTo(s.Points[0]) <= tol)
                pts[0] = s.Points[0];
            if (pts[0].DistanceTo(s.Points[^1]) <= tol)
                pts[0] = s.Points[^1];
            if (pts[^1].DistanceTo(s.Points[0]) <= tol)
                pts[^1] = s.Points[0];
            if (pts[^1].DistanceTo(s.Points[^1]) <= tol)
                pts[^1] = s.Points[^1];
        }
        var added = new Stroke
        {
            Kind = _kind,
            Points = pts,
            Width = _width,
        };
        var merged = Interactions.MergeStrokeInto(scene.Strokes, added);
        return merged.FirstOrDefault(m =>
            !scene.Strokes.Any(s => s.Points.SequenceEqual(m.Points))
        );
    }

    public void Clear()
    {
        _pts = [];
        _draft = null;
        _snap = null;
        UpdateStrokePaint();
        QueueRedraw();
    }

    /// <summary>Translucent massif ghost while a terrain stroke is
    /// being drawn — band + top fills at half alpha.</summary>
    public void SetDraft(Massif m)
    {
        _draft = m;
        QueueRedraw();
    }

    public void ClearDraft()
    {
        _draft = null;
        QueueRedraw();
    }

    public void SetSelection(List<Vector2> props, List<Vector2[]> lines, List<Vector2[]> outlines)
    {
        (_selProps, _selLines, _selOutlines) = (props, lines, outlines);
        QueueRedraw();
    }

    public void SetMarquee(Rect2? rect)
    {
        _marquee = rect;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // selection highlight
        var hi = new Color(1f, 0.85f, 0.25f, 0.9f);
        foreach (var o in _selOutlines)
            if (o.Length > 2)
                DrawPolyline([.. o, o[0]], hi, 2.5f);
        foreach (var l in _selLines)
            if (l.Length > 1)
                DrawPolyline(l, hi, 3);
        foreach (var pp in _selProps)
            DrawArc(pp, 20, 0, Mathf.Tau, 24, hi, 2.5f);

        // marquee rubber band
        if (_marquee is { } mq)
        {
            DrawRect(mq, new Color(1f, 0.85f, 0.25f, 0.12f));
            DrawRect(mq, hi, false, 1.5f);
        }

        // terrain draft — the massif the stroke would create
        if (_draft is { } m)
        {
            var band = new Color(0.71f, 0.69f, 0.54f, 0.45f);
            var top = new Color(0.49f, 0.65f, 0.32f, 0.5f);
            foreach (var poly in m.Band)
                if (Geometry2D.TriangulatePolygon(poly).Length > 0)
                    DrawColoredPolygon(poly, band);
            foreach (var poly in m.Tops)
                if (Geometry2D.TriangulatePolygon(poly).Length > 0)
                    DrawColoredPolygon(poly, top);
            foreach (var poly in m.Tops)
                DrawPolyline([.. poly, poly[0]], new Color(0.2f, 0.18f, 0.1f, 0.8f), 2);
        }

        // pending fill — region rect, source glow, ghosted geometry
        if (_fillRect.Count >= 3)
            DrawPolyline(
                [.. _fillRect, _fillRect[0]],
                new Color(0.35f, 0.85f, 0.45f, 0.9f),
                2f
            );
        foreach (var sp in _fillSrc)
            DrawArc(sp, 14, 0, Mathf.Tau, 20, new Color(0.3f, 0.9f, 0.45f, 0.8f), 2.5f);
        foreach (var poly in _fillPolys)
            if (poly.Length >= 3)
            {
                DrawColoredPolygon(poly, new Color(0.35f, 0.85f, 0.45f, 0.12f));
                DrawPolyline([.. poly, poly[0]], new Color(0.35f, 0.85f, 0.45f, 0.6f), 1.5f);
            }
        foreach (var line in _fillLines)
            if (line.Length >= 2)
                DrawPolyline(line, new Color(0.35f, 0.85f, 0.45f, 0.6f), 3f);

        // the raw stroke on top
        if (_pts.Count < 2)
            return;
        if (_snap is { } sn)
            DrawArc(sn, 9, 0, Mathf.Tau, 20, new Color(1f, 0.9f, 0.4f, 0.9f), 2f);
        var pts = _pts.ToArray();
        if (_mode == MapEditor.Mode.Shape)
        {
            DrawPolyline([.. pts, pts[0]], new Color(0.2f, 0.18f, 0.1f, 0.7f), 2);
            return;
        }
        if (_mode == MapEditor.Mode.Fog)
        {
            var fc =
                _kind == "clear" ? new Color(0.18f, 0.8f, 0.44f, 0.4f) : new Color(0.9f, 0.3f, 0.24f, 0.4f);
            DrawPolyline(pts, fc, (float)_width);
            return;
        }
        if (_mode == MapEditor.Mode.Brush)
        {
            var veg = _kind == MapEditor.VegKind;
            var fill = veg ? new Color(0.45f, 0.66f, 0.36f, 0.35f) : new Color(0.71f, 0.69f, 0.54f, 0.4f);
            var rim = veg ? new Color(0.12f, 0.24f, 0.1f, 0.7f) : new Color(0.2f, 0.18f, 0.1f, 0.7f);
            foreach (var c in _pts)
            {
                DrawCircle(c, (float)_width, fill);
                DrawArc(c, (float)_width, 0, Mathf.Tau, 24, rim, 1.5f);
            }
            return;
        }
        // Road/river drafts paint through the real renderer instead
        // (the translucent ribbon dimmed everything underneath).
        if (_mode == MapEditor.Mode.Stroke && _strokePaint?.Visible == true)
            return;
        var col = _kind switch
        {
            Kinds.Wall => new Color(0.72f, 0.71f, 0.6f, 0.5f),
            Kinds.Fence => new Color(0.43f, 0.34f, 0.24f, 0.5f),
            Kinds.River => new Color(0.16f, 0.56f, 0.6f, 0.5f),
            _ => new Color(0.82f, 0.72f, 0.49f, 0.5f),
        };
        DrawPolyline(pts, col, (float)_width);
    }
}

/// <summary>Play-mode overlay — DR-style token chips over the map
/// (web renderPlayOverlay/renderTokenChip): parchment ring, colored
/// core, dark rim, inner ring and a bold chip label. Drawn in world
/// space under the camera; tokens are draggable via the editor's
/// select-move machinery.</summary>
public partial class PlayLayer : Node2D
{
    List<(Vector2 Pos, float R, bool Selected, string Text)> _tokens = [];

    public void SetTokens(List<(Vector2 Pos, float R, bool Selected, string Text)> tokens)
    {
        _tokens = tokens;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var paper = new Color(0.957f, 0.906f, 0.741f);
        var ink = new Color(0.184f, 0.165f, 0.118f);
        var core = Palette.Hex("#d34f3d");
        foreach (var (pos, r, selected, text) in _tokens)
        {
            // Drop shadow, parchment body, colored core, rims.
            DrawCircle(pos + new Vector2(r * 0.12f, r * 0.2f), r, new Color(0.08f, 0.13f, 0.1f, 0.4f));
            DrawCircle(pos, r, paper);
            DrawCircle(pos, r * 0.8f, core);
            DrawArc(pos, r * 0.8f, 0, Mathf.Tau, 32, ink, Mathf.Max(1, r * 0.08f));
            DrawArc(pos, r * 0.58f, 0, Mathf.Tau, 32, ink with { A = 0.35f }, Mathf.Max(0.8f, r * 0.05f));
            var size = Mathf.Max(9, (int)(r * 0.55));
            var font = ThemeDB.FallbackFont;
            var advance = font.GetStringSize(text, fontSize: size).X;
            var tp = pos + new Vector2(-advance / 2, size * 0.36f);
            DrawString(font, tp, text, fontSize: size, modulate: paper);
            if (selected)
                DrawArc(pos, r + Mathf.Max(2, r * 0.1f), 0, Mathf.Tau, 32, new Color(1f, 0.96f, 0.79f), Mathf.Max(2, r * 0.12f));
        }
    }
}
