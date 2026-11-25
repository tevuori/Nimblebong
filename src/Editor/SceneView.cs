// Scene view — turns a Model.Scene into the painted world: rebuilds
// all painters from scene data on every commit. Owns the LightingStage
// and the per-layer painter nodes; callers mutate the Scene, call
// Rebuild(), done.
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public partial class SceneView : Node2D
{
    public LightingStage Stage = null!;
    public Scene Scene = new();
    public ResolvedBiome Biome = null!;
    public ResolvedMood Mood = null!;
    /// <summary>The massifs built during the last Rebuild — used by the
    /// editor for selection hit-testing and live previews.</summary>
    public List<Massif> Massifs = [];

    // Per-layer painters, rebuilt from scratch each commit — split
    // into two fingerprinted groups so a commit that only touches one
    // input class skips re-creating (and re-submitting) the other
    // group's canvas items entirely. Z-order inside the viewports is
    // explicit (ZIndex), never tree order, so a skipped painter keeps
    // its correct layer position.
    readonly List<Node> _geoLayers = [];
    readonly List<Node> _propLayers = [];
    int _geoFp = -1;
    int _propFp = -1;
    /// <summary>Fog veil material — strength is context-dependent:
    /// full while editing fog, faint otherwise so shrouded rooms stay
    /// editable.</summary>
    public ShaderMaterial? FogMaterial;

    public void SetFogStrength(double strength)
    {
        FogMaterial?.SetShaderParameter("uStrength", (float)strength);
    }

    // Massif cache — props/strokes rebuilds skip the expensive clipper
    // when the terrain shapes didn't change.
    List<TerrainShape> _lastShapes = [];
    bool _massifsValid;

    public static SceneView Create(Vector2 mapSize)
    {
        var view = new SceneView { Stage = LightingStage.Create(mapSize) };
        view.AddChild(view.Stage);
        // Fog-of-war overlay — dark veil over unrevealed regions,
        // drawn above the lit map but below the editor's ghost layer.
        var fogMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/fog.gdshader") };
        fogMat.SetShaderParameter("uMask", view.Stage.FogVp.GetTexture());
        fogMat.SetShaderParameter("uStrength", 0.22f);
        view.FogMaterial = fogMat;
        view.AddChild(
            new TextureRect
            {
                Texture = view.Stage.FogVp.GetTexture(),
                Material = fogMat,
                Size = mapSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }
        );
        view.Scene.Width = mapSize.X;
        view.Scene.Height = mapSize.Y;
        view.Scene.Biome = "woodland";
        view.Biome = SceneConfig.ResolveArt(
            SceneConfig.LoadBiome("woodland"), null, null);
        view.Mood = SceneConfig.LoadMood("day");
        view.Rebuild();
        return view;
    }

    public void SetBiome(string id)
    {
        Scene.Biome = id;
        Biome = SceneConfig.ResolveArt(
            SceneConfig.LoadBiome(id), Scene.ArtPack, Scene.Season);
        _massifsValid = false;
        Rebuild();
    }

    public void SetArtPack(string id)
    {
        Scene.ArtPack = id;
        ReResolveBiome();
    }

    public void SetSeason(string? id)
    {
        Scene.Season = id;
        ReResolveBiome();
    }

    void ReResolveBiome()
    {
        Biome = SceneConfig.ResolveArt(
            SceneConfig.LoadBiome(Scene.Biome ?? "woodland"),
            Scene.ArtPack,
            Scene.Season
        );
        _massifsValid = false;
        Rebuild();
    }

    public void SetMood(string id)
    {
        Mood = SceneConfig.LoadMood(id);
        Scene.Mood = id;
        Rebuild();
    }

    /// <summary>Repaint everything from the current Scene data.</summary>
    public void Rebuild()
    {
        var shapes = Scene
            .Shapes.Where(s => s.Kind == Kinds.Terrain)
            .Select(s => new TerrainShape
            {
                Model = s.Model,
                Height = s.Height,
                Size = s.Size,
                Roughness = s.Roughness,
                Outline = [.. s.Points],
            })
            .ToList();
        if (!_massifsValid || !ShapesEqual(_lastShapes, shapes))
        {
            Massifs = TerrainGeometry.BuildMassifs(shapes);
            _lastShapes = shapes;
            _massifsValid = true;
            var albedo = Stage.Albedo;
            albedo.Biome = Biome;
            albedo.Ground = Biome.Ground;
            albedo.Cliff = Biome.Cliff;
            albedo.Stone = Biome.Stone;
            albedo.GrassTile = Biome.GrassTile;
            albedo.Winter = Biome.SeasonLook == "winter";
            albedo.SetMassifs(Massifs);
            Stage.Height.SetMassifs(Massifs);
        }
        var massifs = Massifs;

        // Fingerprinted groups — recreating a painter means thousands
        // of canvas draw commands re-submitted to the rendering server,
        // which dominates the per-commit cost. Inputs that did not
        // change keep their existing canvas items byte-for-byte.
        Fingerprints(out var geoFp, out var propFp);
        var geoDirty = geoFp != _geoFp;
        var propDirty = propFp != _propFp;

        if (geoDirty)
        {
            foreach (var n in _geoLayers)
                n.QueueFree();
            _geoLayers.Clear();

            // Area fills — ponds and fields under everything else.
            var areas = new AreaPainter { Water = Biome.Water, Ground = Biome.Ground, ZIndex = 10 };
            var areaH = new AreaHeight { ZIndex = 10 };
            var fortify = new FortifyPainter { Stone = Biome.Stone, Wood = Biome.Wood, ZIndex = 32 };
            foreach (var sh in Scene.Shapes)
            {
                if (sh.Kind is Kinds.Pond or Kinds.Field)
                {
                    areas.Add(sh.Kind, sh.Points);
                    if (sh.Kind == Kinds.Pond)
                        areaH.AddPond(sh.Points);
                }
            }
            AddGeo(Stage.AlbedoVp, areas);
            AddGeo(Stage.HeightVp, areaH);
            // Ground surfaces (marsh, scree, dunes…) — JSON-defined washes
            // over ponds/fields, under everything else.
            var surfaces = new SurfacePainter { Biome = Biome, ZIndex = 11 };
            foreach (var sh in Scene.Shapes)
                if (SurfaceDefs.ForKind(sh.Kind) is { } sdef)
                    surfaces.Add(sdef, sh.Points);
            AddGeo(Stage.AlbedoVp, surfaces);

            var water = new WaterPainter
            {
                Water = Biome.Water,
                RoadDirt = Biome.At("road.dirt"),
                RoadVerge = Biome.At("road.verge"),
                RoadHalo = Biome.At("road.halo"),
                RoadGround = Biome.Ground,
                ZIndex = 30,
            };
            // Natural bank treatment (TS sceneConfig.banks). The art/season
            // override picks grass/sand/stone; the ink sheets read their
            // canyon channels as rocky cuts, not lime lawn edging.
            var bank = Biome.WaterBank ?? (Biome.ArtPack == "ink" ? "stone" : "grass");
            (water.BankVerge, water.BankEdge) = bank switch
            {
                "stone" => (Palette.Hex("#57594d"), Palette.Hex("#8d8b76")),
                "sand" => (Palette.Hex("#8f7c54"), Palette.Hex("#e6cf9a")),
                _ => (Palette.Hex("#647750"), Palette.Hex("#b4c985")),
            };
            var waterH = new WaterHeight { ZIndex = 30 };
            var crossings = new CrossingPainter { WaterLight = Biome.Water, ZIndex = 31 };
            crossings.SetMassifs(massifs);
            // Walls/fences open a gated gap wherever they cross a road.
            var gateCuts = Scene
                .Strokes.Where(s => s.Kind == Kinds.Road)
                .Select(s => ((IReadOnlyList<Vector2>)s.Points, s.Width))
                .ToList();
            foreach (var st in Scene.Strokes)
            {
                if (st.Kind == Kinds.River)
                {
                    water.AddRiver(st.Points, st.Width);
                    waterH.AddRiver(st.Points, st.Width);
                    crossings.AddStroke(st.Kind, st.Points, st.Width);
                }
                else if (st.Kind == Kinds.Road)
                {
                    water.AddRoad(st.Points, st.Width, st.Wear);
                    crossings.AddStroke(st.Kind, st.Points, st.Width);
                }
                else if (st.Kind is Kinds.Wall or Kinds.Fence)
                {
                    fortify.Add(st.Kind, st.Points, st.Width, st.Model, gateCuts);
                }
            }
            water.SetRocks(
                Scene.Props.Where(p => MapProp(p.Kind) == PropKind.Rock).Select(p => (
                    new Vector2((float)p.X, (float)p.Y),
                    p.Scale
                ))
            );
            var bridges = new BridgePainter { Stone = Biome.Stone, ArtPack = Biome.ArtPack, ZIndex = 33 };
            bridges.SetStrokes(Scene.Strokes);
            AddGeo(Stage.AlbedoVp, water);
            AddGeo(Stage.AlbedoVp, crossings);
            AddGeo(Stage.HeightVp, waterH);
            AddGeo(Stage.AlbedoVp, fortify);
            AddGeo(Stage.AlbedoVp, bridges);
            _geoFp = geoFp;
        }

        if (propDirty)
        {
            foreach (var n in _propLayers)
                n.QueueFree();
            _propLayers.Clear();

            // Buildings & interiors — house/room/floor footprints with
            // roofs, wall rings and door/window openings carved from props.
            var interiors = new InteriorPainter { Biome = Biome, ZIndex = 20 };
            var interiorsH = new InteriorHeight { ZIndex = 20 };
            interiorsH.SetMassifs(massifs);
            foreach (var sh in Scene.Shapes)
                if (sh.Kind is Kinds.House or Kinds.Room or Kinds.Floor)
                {
                    interiors.Add(sh, Scene.Props);
                    interiorsH.Add(sh);
                }
            AddProp(Stage.AlbedoVp, interiors);
            AddProp(Stage.HeightVp, interiorsH);

            var anchored = interiors.AnchoredPropIds;
            var props = Scene
                .Props.Where(p => !anchored.Contains(p.Id))
                .Select(p =>
                    new PropPlacement(
                        MapProp(p.Kind),
                        p.X,
                        p.Y,
                        p.Scale / 24.0,
                        p.Rotation,
                        p.Seed
                    )
                    {
                        Model = p.Model,
                        Species = p.Species,
                        Text = p.Text,
                    }.WithDef(p.Kind)
                )
                .Concat(SurfacePainter.ScatterProps(Scene, Biome.Id))
                .ToList();
            var propsPainter = new PropsPainter
            {
                BiomeCfg = Biome,
                Foliage = Biome.Foliage,
                Stone = Biome.Stone,
                Roof = Biome.Roof,
                Wood = Biome.Wood,
                ZIndex = 40,
            };
            AddProp(Stage.AlbedoVp, propsPainter);
            propsPainter.SetProps(props);
            var propsH = new PropsHeight { ZIndex = 40 };
            AddProp(Stage.HeightVp, propsH);
            propsH.SetMassifs(massifs);
            propsH.SetProps(props);
            _propFp = propFp;
        }

        // Cheap in-place persistent layers — masks and glow update
        // inside their existing nodes, no canvas re-creation.
        Stage.Water.SetWater(Scene);
        Stage.Glow.SetGlow(Scene, Biome.Id);
        Stage.Fog.Strokes = Scene.Fog;
        Stage.Fog.CoverAll = Scene.FogCoverAll;
        Stage.Fog.Rooms = Scene.FogRooms
            ? Scene
                .Shapes.Where(sh => sh.Kind == Kinds.Room)
                .Select(sh => sh.Points.ToArray())
                .ToList()
            : [];
        Stage.Fog.QueueRedraw();

        Mood.ApplyTo(Stage);
        if (
            (geoDirty || propDirty)
            && System.Environment.GetEnvironmentVariable("DMMAP_NO_STATIC_REFRESH") != "1"
        )
            Stage.RefreshStatic();
    }

    // Z-order inside the static viewports is explicit — never tree
    // order — because a fingerprint-skipped painter keeps its node
    // while others are re-created and appended. Matches the original
    // creation order: massif painters (persistent, z=0) < areas (10)
    // < surfaces (11) < interiors (20) < water (30) < crossings (31)
    // < fortify (32) < bridges (33) < props (40).
    void AddGeo(SubViewport vp, Node2D painter)
    {
        vp.AddChild(painter);
        _geoLayers.Add(painter);
    }

    void AddProp(SubViewport vp, Node2D painter)
    {
        vp.AddChild(painter);
        _propLayers.Add(painter);
    }

    /// <summary>Content hashes of everything the two painter groups
    /// read. geo = strokes + shapes + rock props (water foam) + the
    /// look ids; props = shapes + all props + look ids. Strokes are
    /// excluded from props — no prop rendering depends on them — so
    /// road/river editing skips the most expensive painter entirely.
    /// Ids are deliberately excluded: output depends on content, not
    /// identity.</summary>
    void Fingerprints(out int geo, out int props)
    {
        geo = 17;
        props = 17;
        foreach (var id in new[] { Scene.Biome, Scene.ArtPack, Scene.Season, Scene.Mood })
        {
            Mix(ref geo, id?.GetHashCode() ?? 0);
            Mix(ref props, id?.GetHashCode() ?? 0);
        }
        foreach (var sh in Scene.Shapes)
        {
            MixShape(ref geo, sh);
            MixShape(ref props, sh);
        }
        foreach (var st in Scene.Strokes)
        {
            Mix(ref geo, st.Kind.GetHashCode());
            Mix(ref geo, st.Width.GetHashCode());
            Mix(ref geo, st.Model);
            foreach (var pt in st.Points)
                MixV(ref geo, pt);
        }
        foreach (var pr in Scene.Props)
        {
            MixProp(ref props, pr);
            if (MapProp(pr.Kind) == PropKind.Rock)
            {
                MixD(ref geo, pr.X);
                MixD(ref geo, pr.Y);
                MixD(ref geo, pr.Scale);
            }
        }
    }

    static void Mix(ref int h, int v) => h = h * 31 + v;
    static void MixD(ref int h, double d) => Mix(ref h, d.GetHashCode());
    static void MixV(ref int h, Vector2 v)
    {
        Mix(ref h, v.X.GetHashCode());
        Mix(ref h, v.Y.GetHashCode());
    }

    static void MixShape(ref int h, Shape sh)
    {
        Mix(ref h, sh.Kind.GetHashCode());
        MixD(ref h, sh.Size);
        MixD(ref h, sh.Height);
        MixD(ref h, sh.Roughness);
        Mix(ref h, sh.Model);
        MixD(ref h, sh.Angle);
        Mix(ref h, sh.Color?.GetHashCode() ?? 0);
        foreach (var pt in sh.Points)
            MixV(ref h, pt);
    }

    static void MixProp(ref int h, Prop pr)
    {
        Mix(ref h, pr.Kind.GetHashCode());
        MixD(ref h, pr.X);
        MixD(ref h, pr.Y);
        MixD(ref h, pr.Scale);
        MixD(ref h, pr.Rotation);
        Mix(ref h, pr.Seed);
        Mix(ref h, pr.Model);
        Mix(ref h, pr.Species?.GetHashCode() ?? 0);
        Mix(ref h, pr.Text?.GetHashCode() ?? 0);
    }

    static bool ShapesEqual(List<TerrainShape> a, List<TerrainShape> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            var (x, y) = (a[i], b[i]);
            if (
                x.Model != y.Model
                || x.Height != y.Height
                || x.Size != y.Size
                || x.Roughness != y.Roughness
                || !x.Outline.SequenceEqual(y.Outline)
            )
                return false;
        }
        return true;
    }

    static readonly Dictionary<string, PropKind> PropKinds = new()
    {
        ["tree"] = PropKind.Tree,
        ["pine"] = PropKind.Pine,
        ["rock"] = PropKind.Rock,
        ["bush"] = PropKind.Bush,
        ["house"] = PropKind.House,
        ["tent"] = PropKind.Tent,
        ["campfire"] = PropKind.Campfire,
        ["well"] = PropKind.Well,
        ["ruin"] = PropKind.Ruin,
        ["grave"] = PropKind.Grave,
        ["bones"] = PropKind.Bones,
        ["statue"] = PropKind.Statue,
        ["stonecircle"] = PropKind.StoneCircle,
        ["crystal"] = PropKind.Crystal,
        ["bale"] = PropKind.Bale,
        ["crate"] = PropKind.Crate,
        ["barrel"] = PropKind.Barrel,
        ["tower"] = PropKind.Tower,
        ["stall"] = PropKind.Stall,
        ["cart"] = PropKind.Cart,
        ["flag"] = PropKind.Flag,
        ["barricade"] = PropKind.Barricade,
        ["lantern"] = PropKind.Lantern,
        ["fountain"] = PropKind.Fountain,
        ["pillar"] = PropKind.Pillar,
        ["marker"] = PropKind.Marker,
        ["dock"] = PropKind.Dock,
        ["boat"] = PropKind.Boat,
        ["chest"] = PropKind.Chest,
        ["portal"] = PropKind.Portal,
        ["door"] = PropKind.Door,
        ["window"] = PropKind.Window,
        ["table"] = PropKind.Table,
        ["bed"] = PropKind.Bed,
        ["shelf"] = PropKind.Shelf,
        ["rug"] = PropKind.Rug,
        ["fireplace"] = PropKind.Fireplace,
        ["chair"] = PropKind.Chair,
        ["candle"] = PropKind.Candle,
        ["label"] = PropKind.Label,
        ["butterfly"] = PropKind.Butterfly,
        ["bird"] = PropKind.Bird,
        ["flora"] = PropKind.Flora,
        ["detail"] = PropKind.Detail,
    };

    /// <summary>Built-in kind names only (JSON definitions excluded).</summary>
    public static bool TryMapBuiltinProp(string kind, out PropKind k) =>
        PropKinds.TryGetValue(kind, out k);

    /// <summary>Scene kind → drawer: built-in name, else the base of a
    /// JSON prop definition (assets/config/props), else a marker.</summary>
    public static PropKind MapProp(string kind) =>
        PropKinds.TryGetValue(kind, out var k) ? k
        : PropDefs.TryGet(kind, out var def) ? def.Base
        : PropKind.Marker;
}
