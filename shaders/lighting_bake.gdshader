// Static lighting bake — light multiplier only, no albedo: normals
// diffuse + raymarched soft shadows, rendered once per scene edit at
// HALF resolution (the heightfield is smooth, so the upscaled light is
// visually identical) and multiplied with the full-res albedo in the
// per-frame pass. Halving the resolution quarters the raymarch cost
// of every scene edit; keeping albedo out of the bake preserves the
// full-res art detail and the crisp paper grain.
shader_type canvas_item;

uniform sampler2D uHeight : filter_linear;
uniform vec2 uMapSize = vec2(1600.0, 1000.0);
uniform float uBaseline = 0.16;
uniform vec2 uSunDir = vec2(-0.5, -0.62); // toward the sun (canvas coords, y down)
uniform float uSunElev = 0.12;            // height units per px travelled
uniform float uScale = 60.0;              // gray 0..1 -> world px of relief
uniform float uShadows = 1.0;
uniform float uAmbient = 0.72;
uniform vec3 uSunColor = vec3(0.42, 0.38, 0.30);
uniform int uSteps = 40;
uniform float uStepPx = 3.0;
uniform int uDebug = 0;

float heightAt(vec2 uv) {
    return (texture(uHeight, uv).r - uBaseline) * uScale;
}

void fragment() {
    // World-pixel size in UV units — the heightmap is map-sized, so its
    // texel is exactly one world pixel regardless of bake resolution.
    vec2 texel = 1.0 / uMapSize;
    if (uDebug == 1) {
        COLOR = vec4(texture(uHeight, UV).rrr, 1.0);
    } else if (uDebug == 2) {
        float h0 = heightAt(UV);
        float o = 0.0;
        vec2 st = normalize(uSunDir) * texel * uStepPx;
        float sl = uSunElev / max(length(uSunDir), 1e-4);
        for (int i = 1; i <= uSteps; i++) {
            vec2 p = UV + st * float(i);
            if (p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0) break;
            float hs = heightAt(p);
            float over = hs - (h0 + float(i) * uStepPx * sl);
            o = max(o, clamp(over * 0.06, 0.0, 1.0) * (1.0 - float(i) / float(uSteps)));
            if (o > 0.985) break;
        }
        COLOR = vec4(vec3(o), 1.0);
    } else {
    float h = heightAt(UV);
    // Sample the height gradient over a small footprint. One-pixel
    // central differences turn the raster's quantized elevation rings
    // into alternating light/dark contour stripes; a 9 px footprint
    // integrates the same ramp as a continuous slope instead.
    vec2 hs = texel * 4.5;
    float hx = heightAt(UV + vec2(hs.x, 0.0))
             - heightAt(UV - vec2(hs.x, 0.0));
    float hy = heightAt(UV + vec2(0.0, hs.y))
             - heightAt(UV - vec2(0.0, hs.y));
    vec3 n = normalize(vec3(-hx * 0.2, -hy * 0.2, 1.0));
    vec3 L = normalize(vec3(uSunDir * 2.0, 1.2));
    float diff = clamp(dot(n, L), 0.0, 1.0);
    float occ = 0.0;
    if (uShadows > 0.5) {
        float travel = length(uSunDir);
        vec2 stepUv = normalize(uSunDir) * texel * uStepPx;
        float slope = uSunElev / max(travel, 1e-4);
        for (int i = 1; i <= uSteps; i++) {
            vec2 p = UV + stepUv * float(i);
            if (p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0) break;
            float hs2 = heightAt(p);
            float over = hs2 - (h + float(i) * uStepPx * slope);
            float hit = clamp(over * 0.06, 0.0, 1.0);
            float fade = 1.0 - float(i) / float(uSteps);
            occ = max(occ, hit * fade);
            if (occ > 0.985) break;
        }
    }
    vec3 light = vec3(uAmbient * (1.0 - occ * 0.5)) + uSunColor * diff * (1.0 - occ * 0.78);
    COLOR = vec4(light, 1.0);
    }
}
