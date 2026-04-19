shader_type canvas_item;

uniform vec4 color_top : source_color = vec4(0.420, 0.231, 0.0, 1.0);
uniform vec4 color_mid : source_color = vec4(0.788, 0.388, 0.008, 1.0);
uniform vec4 color_horizon : source_color = vec4(0.910, 0.518, 0.098, 1.0);
uniform vec4 color_low : source_color = vec4(0.961, 0.627, 0.188, 1.0);

void fragment() {
    float t = UV.y;
    vec4 color;

    if (t < 0.33) {
        color = mix(color_top, color_mid, t / 0.33);
    } else if (t < 0.66) {
        color = mix(color_mid, color_horizon, (t - 0.33) / 0.33);
    } else {
        color = mix(color_horizon, color_low, (t - 0.66) / 0.34);
    }

    COLOR = color;
}