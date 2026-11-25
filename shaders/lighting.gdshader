// Lighting composite — albedo (TEXTURE) lit by a heightmap: normals
// from the elevation gradient drive directional diffuse, and a raymarch
// toward the sun produces projected soft shadows. The same approach the
// Nimblehold devs describe (heightmap + raymarched shadow pass).
shader_type canvas_item;

uniform sampler2D uHeight : filter_linear;
uniform sampler2D uWater : filter_linear;
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
    vec3 lit = albedo.rgb * (uAmbient * (1.0 - occ * 0.45) + uSunColor * diff * (1.0 - occ * 0.85));
    // Animated water: slow wave tint drift + sparse moving glints,
    // confined to the water mask (uWater alpha = coverage).
    float w = texture(uWater, UV).a;
    if (w > 0.01) {
        vec2 wp = UV / texel;
        float wave = sin(wp.x * 0.11 + TIME * 1.3) * sin(wp.y * 0.09 - TIME * 0.9);
        lit = mix(lit, lit * 1.07 + vec3(0.01, 0.025, 0.03), w * (0.45 + 0.3 * wave));
        float g = sin(wp.x * 0.31 + wp.y * 0.17 + TIME * 1.9)
                * sin(wp.y * 0.37 - wp.x * 0.13 - TIME * 1.4);
        float glint = smoothstep(0.93, 1.0, g);
        lit += vec3(0.9, 0.95, 1.0) * glint * 0.1 * w;
    }
    // Paper grain — fine two-scale value noise multiplying the sheet,
    // like pigment catching on paper (web paperGrain, ~0.085 strength).
    vec2 gp = UV / texel;
    float g1 = fract(sin(dot(floor(gp / 3.0), vec2(127.1, 311.7))) * 43758.5453);
    float g2 = fract(sin(dot(floor(gp / 7.0) + 13.7, vec2(269.5, 183.3))) * 43758.5453);
    lit *= 0.915 + (g1 * 0.6 + g2 * 0.4) * 0.085;
    // Parchment edge — a sepia wash creeping in from the borders like
    // pigment pooling at the sheet's edge (web parchmentEdge).
    float ex = min(UV.x, 1.0 - UV.x) / texel.x;
    float ey = min(UV.y, 1.0 - UV.y) / texel.y;
    float wash = smoothstep(46.0, 0.0, min(ex, ey));
    lit = mix(lit, vec3(0.40, 0.30, 0.16), wash * 0.22);
    COLOR = vec4(lit, 1.0);
    }
}
