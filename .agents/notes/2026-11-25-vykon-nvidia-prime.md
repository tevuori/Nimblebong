# Výkon editoru na hybridní NVIDIA PRIME (2026-11-25)

## Pozorování

Notebook má Wayland/GNOME, integrované AMD Radeon 780M a dedikované
NVIDIA RTX 3050 Laptop. NVIDIA ovladač byl nainstalovaný a načtený
(`nvidia-smi` fungovalo, `nvidia_drm.modeset=Y`). Bez PRIME env proměnných
Godot vybral AMD; s vynuceným offloadem běžel na NVIDIA, ale editor byl
subjektivně ještě pomalejší.

Naměřeno před opravou (`--print-fps`):

- NVIDIA offload: cca 8 FPS / 125 ms na frame
- AMD iGPU: cca 15–18 FPS / 55–77 ms na frame

Nativní `--display-driver wayland` na NVIDIA byl podobný nebo horší
(cca 5–10 FPS), takže hlavní příčinou nebylo jen Xwayland kopírování.

## Skutečný bottleneck

`LightingStage` měl všech pět SubViewportů (`AlbedoVp`, `HeightVp`,
`FinalVp`, `FogVp`, `WaterVp`) v `UpdateMode.Always`. Albedo/height/fog/water
jsou ale statické mezi rebuildy scény, takže se zbytečně rasterizovaly
každý frame. Na hybridní NVIDIA se driver overhead a synchronizace projevily
víc než na Mesa/AMD.

Po přepnutí statických vrstev na `UpdateMode.Once` a refreshi jen při
`SceneView.Rebuild()`:

- NVIDIA offload: stabilní 120 FPS / 8.33 ms (cap monitoru/vsync)
- editor probe stále prochází, vizuál je stejný
- `FinalVp` musí zůstat `Always`, protože lighting shader používá `TIME`
  pro vodu a `GlowPainter` se překresluje každý frame

## Vedlejší zjištění

Miniatury nástrojů se při startu dříve generovaly dvakrát: karty se
registrovaly před prvním `SyncThumbLook()`. Po přesunutí `SyncThumbLook()`
před `PopulateToolCards("")` se generují jen jednou.

Testovála se i disková cache miniatur, ale byla zavržena: po opravě
viewportů už není potřeba a validní invalidace by musela zahrnout C#,
configy, shadery i textury, což je zbytečně křehké.

## Spuštění na NVIDIA

```bash
__NV_PRIME_RENDER_OFFLOAD=1 \
__GLX_VENDOR_LIBRARY_NAME=nvidia \
~/Tools/godot/Godot_v4.7.2-stable_mono_linux.x86_64 --path .
```

Ověřený výstup:

```text
OpenGL API 3.3.0 NVIDIA 620.70.06 - Compatibility -
Using Device: NVIDIA - NVIDIA GeForce RTX 3050 6GB Laptop GPU
```
