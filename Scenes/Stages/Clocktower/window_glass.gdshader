shader_type canvas_item;

uniform float sheen_intensity : hint_range(0.0, 1.0) = 0.18;
uniform vec4 blue_tint : source_color = vec4(0.72, 0.88, 1.0, 1.0);

void fragment() {
    vec4 base = COLOR;

    // Subtle blue glass tint
    vec3 glass = mix(base.rgb, blue_tint.rgb, 0.2);

    // Diagonal sheen streak — soft bright stripe running top-left to bottom-right
    float d = SCREEN_UV.x - SCREEN_UV.y;
    float sheen = exp(-d * d * 620.0) * sheen_intensity;

    COLOR = vec4(glass + vec3(sheen), base.a);
}
