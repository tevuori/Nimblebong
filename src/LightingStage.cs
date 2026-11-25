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
        var shader = GD.Load<Shader>("res://shaders/lighting.gdshader");
        var material = new ShaderMaterial { Shader = shader };
        stage._composite = new TextureRect
        {
            Texture = stage.AlbedoVp.GetTexture(),
            Material = material,
            Size = mapSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        material.SetShaderParameter("uHeight", stage.HeightVp.GetTexture());
        material.SetShaderParameter("uWater", stage.WaterVp.GetTexture());
        stage.FinalVp.AddChild(stage._composite);
        stage.Glow = new GlowPainter();
        stage.FinalVp.AddChild(stage.Glow);
        stage.AddChild(stage.AlbedoVp);
        stage.AddChild(stage.HeightVp);
        stage.AddChild(stage.FinalVp);
        stage.AddChild(stage.FogVp);
        stage.AddChild(stage.WaterVp);
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
    /// Final stays continuous because water/glow animate with TIME.</summary>
    public void RefreshStatic()
    {
        AlbedoVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        HeightVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        FogVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        WaterVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    /// <summary>Set the sun uniforms on the composite shader.</summary>
    public void SetSun(Vector2 dir, float elev, bool shadows)
    {
        var mat = (ShaderMaterial)_composite.Material;
        mat.SetShaderParameter("uSunDir", dir);
        mat.SetShaderParameter("uSunElev", elev);
        mat.SetShaderParameter("uShadows", shadows ? 1.0f : 0.0f);
    }

    /// <summary>Current lit pixels — for probes and exports.</summary>
    public Image Snapshot() => FinalVp.GetTexture().GetImage();

    /// <summary>Grayscale fog mask (white = revealed) — bridge input.</summary>
    public Image FogImage() => FogVp.GetTexture().GetImage();

    public Material DebugMaterial() => _composite.Material;
}
