shader_type canvas_item;
// Dark shroud over unrevealed map — uMask white = revealed.
uniform sampler2D uMask : source_color;
// Veil opacity — full strength while the fog tool is active (editing
// player visibility), a faint dim otherwise so the DM can still see
// and furnish shrouded interiors.
uniform float uStrength = 0.92;
void fragment() {
    float m = texture(uMask, UV).r;
    COLOR = vec4(0.04, 0.05, 0.07, (1.0 - m) * uStrength);
}
