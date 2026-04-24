shader_type canvas_item;

/// 1.0 = full day, 0.0 = full dusk. Drive this from KernelBadlands.cs.
uniform float day_night : hint_range(0.0, 1.0) = 1.0;

// Day palette
uniform vec4 day_top     : source_color = vec4(0.420, 0.231, 0.0,   1.0);
uniform vec4 day_mid     : source_color = vec4(0.788, 0.388, 0.008, 1.0);
uniform vec4 day_horizon : source_color = vec4(0.910, 0.518, 0.098, 1.0);
uniform vec4 day_low     : source_color = vec4(0.961, 0.627, 0.188, 1.0);

// Dusk palette
uniform vec4 dusk_top     : source_color = vec4(0.102, 0.039, 0.180, 1.0);
uniform vec4 dusk_mid     : source_color = vec4(0.239, 0.102, 0.039, 1.0);
uniform vec4 dusk_horizon : source_color = vec4(0.478, 0.180, 0.0,   1.0);
uniform vec4 dusk_low     : source_color = vec4(0.722, 0.290, 0.0,   1.0);

void fragment() {
    float t = UV.y;

    vec4 color_top     = mix(dusk_top,     day_top,     day_night);
    vec4 color_mid     = mix(dusk_mid,     day_mid,     day_night);
    vec4 color_horizon = mix(dusk_horizon, day_horizon, day_night);
    vec4 color_low     = mix(dusk_low,     day_low,     day_night);

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
