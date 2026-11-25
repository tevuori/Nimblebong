// Lighting composite — albedo (TEXTURE) lit by a heightmap: normals
// from the elevation gradient drive directional diffuse, and a raymarch
// toward the sun produces projected soft shadows. The same approach the
// Nimblehold devs describe (heightmap + raymarched shadow pass).
shader_type canvas_item;

uniform sampler2D uHeight : filter_linear;
uniform float uBaseline = 0.16;
uniform vec2 uSunDir = vec2(-0.5, -0.62); // toward the sun (canvas coords, y down)
uniform float uSunElev = 0.12;            // height units per px travelled
uniform float uScale = 60.0;              // gray 0..1 -> world px of relief
uniform float uShadows = 1.0;
uniform float uAmbient = 0.66;
uniform vec3 uSunColor = vec3(0.42, 0.38, 0.30);
uniform int uSteps = 40;
uniform float uStepPx = 3.0;
uniform int uDebug = 0;

float heightAt(vec2 uv, vec2 texel) {
    return (texture(uHeight, uv).r - uBaseline) * uScale;
}

void fragment() {
    if (uDebug == 1) {
        COLOR = vec4(texture(uHeight, UV).rrr, 1.0);
    } else if (uDebug == 2) {
        float h0 = heightAt(UV, TEXTURE_PIXEL_SIZE);
        float o = 0.0;
        vec2 st = normalize(uSunDir) * TEXTURE_PIXEL_SIZE * uStepPx;
        float sl = uSunElev / max(length(uSunDir), 1e-4);
        for (int i = 1; i <= uSteps; i++) {
            vec2 p = UV + st * float(i);
            if (p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0) break;
            float hs = heightAt(p, TEXTURE_PIXEL_SIZE);
            float over = hs - (h0 + float(i) * uStepPx * sl);
            o = max(o, clamp(over * 0.06, 0.0, 1.0) * (1.0 - float(i) / float(uSteps)));
            if (o > 0.985) break;
        }
        COLOR = vec4(vec3(o), 1.0);
    } else {
    vec4 albedo = texture(TEXTURE, UV);
    vec2 texel = TEXTURE_PIXEL_SIZE;
    float h = heightAt(UV, texel);
    float hx = heightAt(UV + vec2(texel.x, 0.0), texel)
             - heightAt(UV - vec2(texel.x, 0.0), texel);
    float hy = heightAt(UV + vec2(0.0, texel.y), texel)
             - heightAt(UV - vec2(0.0, texel.y), texel);
    vec3 n = normalize(vec3(-hx * 0.9, -hy * 0.9, 1.0));
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
            float hs = heightAt(p, texel);
            float over = hs - (h + float(i) * uStepPx * slope);
            float hit = clamp(over * 0.06, 0.0, 1.0);
            float fade = 1.0 - float(i) / float(uSteps);
            occ = max(occ, hit * fade);
            if (occ > 0.985) break;
        }
    }
    COLOR = vec4(albedo.rgb * (uAmbient * (1.0 - occ * 0.35) + uSunColor * diff * (1.0 - occ * 0.85)), 1.0);
    }
}
