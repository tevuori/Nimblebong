shader_type canvas_item;
// Dark shroud over unrevealed map — uMask white = revealed.
uniform sampler2D uMask : source_color;
void fragment() {
    float m = texture(uMask, UV).r;
    COLOR = vec4(0.04, 0.05, 0.07, (1.0 - m) * 0.92);
}
