// Lighting stage — three SubViewports feeding the shader composite:
// albedo (the painted map), heightmap (elevation raster), and a final
// composite that bakes the lit result and can be read back to PNG.
using Godot;

namespace DmMap;

public partial class LightingStage : Node2D
{
    public SubViewport AlbedoVp = null!;
    public SubViewport HeightVp = null!;
    public SubViewport FinalVp = null!;
    public SubViewport FogVp = null!;
    public SubViewport WaterVp = null!;
    /// <summary>Statically baked lighting (shadows/diffuse/grain) —
    /// re-rendered once per scene edit, not per frame.</summary>
    public SubViewport LitVp = null!;
    public AlbedoPainter Albedo = null!;
    public HeightmapPainter Height = null!;
    public FogMask Fog = null!;
    public WaterMask Water = null!;
    public GlowPainter Glow = null!;
    TextureRect _composite = null!;

    public static LightingStage Create(Vector2 mapSize)
    {
        var stage = new LightingStage();
        stage.AlbedoVp = NewViewport(mapSize);
        stage.HeightVp = NewViewport(mapSize);
        stage.FinalVp = NewViewport(mapSize, SubViewport.UpdateMode.Always);
        stage.FogVp = NewViewport(mapSize);
        stage.FogVp.TransparentBg = true;
        stage.Fog = new FogMask { MapSize = mapSize };
        stage.FogVp.AddChild(stage.Fog);
        stage.WaterVp = NewViewport(mapSize);
        stage.WaterVp.TransparentBg = true;
        stage.Water = new WaterMask { MapSize = mapSize };
        stage.WaterVp.AddChild(stage.Water);
        stage.Albedo = new AlbedoPainter { MapSize = mapSize };
        stage.Height = new HeightmapPainter { MapSize = mapSize };
        stage.AlbedoVp.AddChild(stage.Albedo);
        stage.HeightVp.AddChild(stage.Height);
        // Static bake: the expensive part of the lighting composite
        // (40-step shadow raymarch per pixel, normal diffuse) depends
        // only on scene data, so it renders once per edit into LitVp
        // instead of every frame — at HALF resolution, since the light
        // field is smooth and the full-res detail returns when the
        // per-frame pass multiplies it with the albedo.
        stage.LitVp = NewViewport(mapSize / 2);
        var bakeShader = GD.Load<Shader>("res://shaders/lighting_bake.gdshader");
        var bakeMaterial = new ShaderMaterial { Shader = bakeShader };
        var bake = new ColorRect
        {
            Material = bakeMaterial,
            Size = mapSize / 2,
        };
        bakeMaterial.SetShaderParameter("uHeight", stage.HeightVp.GetTexture());
        bakeMaterial.SetShaderParameter("uMapSize", mapSize);
        if (System.Environment.GetEnvironmentVariable("DMMAP_RAYSTEPS") is { } rs
            && int.TryParse(rs, out var rsn))
            bakeMaterial.SetShaderParameter("uSteps", rsn);
        stage.LitVp.AddChild(bake);
        // Per-frame pass: albedo x baked light + water animation, grain
        // and parchment — a few texture reads per pixel, no raymarch.
        var shader = GD.Load<Shader>("res://shaders/water_overlay.gdshader");
        var material = new ShaderMaterial { Shader = shader };
        stage._composite = new TextureRect
        {
            Texture = stage.AlbedoVp.GetTexture(),
            Material = material,
            Size = mapSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        material.SetShaderParameter("uLight", stage.LitVp.GetTexture());
        material.SetShaderParameter("uWater", stage.WaterVp.GetTexture());
        material.SetShaderParameter("uMapSize", mapSize);
        stage.FinalVp.AddChild(stage._composite);
        stage.Glow = new GlowPainter();
        stage.FinalVp.AddChild(stage.Glow);
        stage.AddChild(stage.AlbedoVp);
        stage.AddChild(stage.HeightVp);
        stage.AddChild(stage.FogVp);
        stage.AddChild(stage.WaterVp);
        stage.AddChild(stage.LitVp);
        stage.AddChild(stage.FinalVp);
        // Show the baked result on screen too.
        stage.AddChild(
            new TextureRect
            {
                Texture = stage.FinalVp.GetTexture(),
                Size = mapSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            }
        );
        return stage;
    }

    static SubViewport NewViewport(
        Vector2 size,
        SubViewport.UpdateMode updateMode = SubViewport.UpdateMode.Once
    ) =>
        new()
        {
            Size = (Vector2I)size,
            TransparentBg = false,
            RenderTargetUpdateMode = updateMode,
            HandleInputLocally = false,
            GuiDisableInput = true,
        };

    /// <summary>Repaint the static inputs once after a scene rebuild.
    /// Final stays continuous because water/glow animate with TIME.
    /// The lighting bake is deferred two frames: SubViewport render
    /// order within a frame is not guaranteed, and the bake must sample
    /// the ALREADY refreshed albedo/height rasters — baking in the same
    /// frame could capture the pre-edit textures permanently.</summary>
    public void RefreshStatic()
    {
        AlbedoVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        HeightVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        FogVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        WaterVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _litBakeIn = 2;
    }

    int _litBakeIn = 2; // first bake shortly after startup too

    public override void _Process(double delta)
    {
        // Perf experiment kill-switches (probes only).
        if (System.Environment.GetEnvironmentVariable("DMMAP_FINAL_ONCE") == "1"
            && FinalVp.RenderTargetUpdateMode == SubViewport.UpdateMode.Always)
            FinalVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        if (_litBakeIn <= 0)
            return;
        _litBakeIn--;
        if (_litBakeIn == 0)
            LitVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    /// <summary>Set the sun uniforms on the composite shader.</summary>
    public void SetSun(Vector2 dir, float elev, bool shadows)
    {
        var mat = (ShaderMaterial)((ColorRect)LitVp.GetChild(0)).Material;
        mat.SetShaderParameter("uSunDir", dir);
        mat.SetShaderParameter("uSunElev", elev);
        mat.SetShaderParameter("uShadows", shadows ? 1.0f : 0.0f);
        // Sun parameters are baked in — rebake.
        _litBakeIn = 1;
    }

    /// <summary>Current lit pixels — for probes and exports.</summary>
    public Image Snapshot() => FinalVp.GetTexture().GetImage();

    /// <summary>Grayscale fog mask (white = revealed) — bridge input.</summary>
    public Image FogImage() => FogVp.GetTexture().GetImage();

    /// <summary>The bake material — probes read sun uniforms and flip
    /// the debug visualizations (heightmap / raw shadow raycast).</summary>
    public Material DebugMaterial() => ((ColorRect)LitVp.GetChild(0)).Material;
}
