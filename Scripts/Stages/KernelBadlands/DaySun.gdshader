shader_type canvas_item;

/// 1.0 = full day, 0.0 = full dusk. Must match DaySky.gdshader.
uniform float day_night : hint_range(0.0, 1.0) = 1.0;

// Day colors
uniform vec4 day_sun_color  : source_color = vec4(1.0,   0.93,  0.72,  1.0);
uniform vec4 day_glow_color : source_color = vec4(1.0,   0.42,  0.0,   1.0);
uniform vec4 day_core_color : source_color = vec4(1.0,   0.98,  0.9,   1.0);

// Dusk colors
uniform vec4 dusk_sun_color  : source_color = vec4(1.0,   0.271, 0.0,   1.0); // #FF4500
uniform vec4 dusk_glow_color : source_color = vec4(1.0,   0.420, 0.0,   1.0); // #FF6B00
uniform vec4 dusk_core_color : source_color = vec4(0.722, 0.290, 0.0,   1.0); // #B84A00

uniform float radius        = 0.28;
uniform float glow_size     = 0.15;
uniform float edge_softness = 0.03;
uniform vec2  center        = vec2(0.5, 0.5);

void fragment() {
    // Scale shrinks 18% toward dusk (1.0 day → 0.82 dusk).
    float sun_scale = mix(0.82, 1.0, day_night);
    float r         = radius * sun_scale;

    float d = distance(UV, center);

    float sun  = 1.0 - smoothstep(r, r + edge_softness, d);
    float glow = (1.0 - smoothstep(r, r + glow_size, d)) * 0.85;
    float core = 1.0 - smoothstep(0.0, r * 0.55, d);

    vec4 sun_col  = mix(dusk_sun_color,  day_sun_color,  day_night);
    vec4 glow_col = mix(dusk_glow_color, day_glow_color, day_night);
    vec4 core_col = mix(dusk_core_color, day_core_color, day_night);

    vec3 color = glow_col.rgb * glow;
    color += sun_col.rgb * sun;
    color += core_col.rgb * core * 0.40;

    float alpha = max(sun, glow);

    COLOR = vec4(color, alpha);
}
