// Per-frame final combine — full-res albedo multiplied by the baked
// (half-res, upscaled) light, plus everything cheap and/or animated:
// water waves + glints (the only TIME-dependent part), paper grain and
// the parchment edge. A few texture reads and noise ops per pixel —
// no raymarch, so this can afford to run every frame.
shader_type canvas_item;

uniform sampler2D uLight : filter_linear;
uniform sampler2D uWater : filter_linear;
uniform vec2 uMapSize = vec2(1600.0, 1000.0);

void fragment() {
    vec2 texel = 1.0 / uMapSize;
    vec3 lit = texture(TEXTURE, UV).rgb * texture(uLight, UV).rgb;
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
