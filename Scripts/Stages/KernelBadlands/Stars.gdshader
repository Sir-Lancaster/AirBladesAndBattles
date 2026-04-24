shader_type canvas_item;

/// 1.0 = full day (stars hidden), 0.0 = full dusk (stars fully visible).
uniform float day_night : hint_range(0.0, 1.0) = 1.0;

/// Controls arm length and core size. Tune in Inspector.
uniform float star_radius : hint_range(0.001, 0.03) = 0.004;

/// Match this to the ColorRect's pixel dimensions so the core is circular.
uniform vec2 rect_size = vec2(2241.0, 914.0);

/// How many stars to render. Max 1200.
uniform int star_count : hint_range(10, 1200) = 200;

float hash(float n) {
    return fract(sin(n) * 43758.5453123);
}

void fragment() {
    float aspect    = rect_size.x / rect_size.y;
    vec2  uv_aspect = vec2(UV.x * aspect, UV.y);

    float vertical_fade = 1.0 - smoothstep(0.33, 0.66, UV.y);

    vec3  out_color = vec3(0.0);
    float out_alpha = 0.0;

    for (int i = 0; i < 1200; i++) {
        if (i >= star_count) break;

        float fi = float(i);

        // Per-star radius biased toward small: most ~0.001, few reach star_radius.
        float r_frac    = 0.25 + pow(hash(fi * 13.71), 2.5) * 0.75;
        float s_radius  = star_radius * r_frac;

        float arm_len_px = s_radius * rect_size.y * 1.2;
        float arm_w_px   = max(s_radius * rect_size.y * 0.015, 2.0);

        vec2  pos        = vec2(hash(fi * 1.17), hash(fi * 2.31 + 0.4) * 0.7);
        vec2  pos_aspect = vec2(pos.x * aspect, pos.y);

        float twinkle_rate = 0.8 + hash(fi * 3.73) * 2.5;
        float twinkle_offs = hash(fi * 5.19) * 6.28318;

        // Aspect-corrected delta for circular core only.
        vec2  delta = uv_aspect - pos_aspect;
        float dist  = length(delta);

        // Raw pixel-space delta for arms — consistent across all positions.
        float dx_px = abs(UV.x - pos.x) * rect_size.x;
        float dy_px = abs(UV.y - pos.y) * rect_size.y;

        float core_r = s_radius * aspect * 0.03;

        if (dist < (arm_len_px / rect_size.y) * aspect * 1.3) {
            float twinkle = 0.55 + 0.45 * sin(TIME * twinkle_rate + twinkle_offs);

            float core  = (1.0 - smoothstep(0.0, core_r, dist)) * twinkle;

            // Cardinal arms — pixel-space, symmetric on all screens
            float h_arm = (1.0 - smoothstep(0.0, arm_len_px, dx_px))
                        * (1.0 - smoothstep(0.0, arm_w_px,   dy_px));
            float v_arm = (1.0 - smoothstep(0.0, arm_w_px,   dx_px))
                        * (1.0 - smoothstep(0.0, arm_len_px, dy_px));

            // Diagonal arms — 60% length, half width
            float diag_w_px   = max(arm_w_px * 0.5, 1.0);
            float diag_len_px = arm_len_px * 0.6;
            float diag_dist   = abs(dx_px - dy_px) * 0.707;
            float diag_extent = (dx_px + dy_px) * 0.707;
            float diag_arm    = (1.0 - smoothstep(0.0, diag_len_px, diag_extent))
                              * (1.0 - smoothstep(0.0, diag_w_px,   diag_dist));

            float spike     = max(max(h_arm, v_arm), diag_arm) * 0.5 * twinkle;
            float intensity = clamp(core + spike, 0.0, 1.0);

            bool is_green = hash(fi * 11.37) > 0.08;
            vec3 color    = is_green
                ? vec3(0.0,  1.0,   0.533)  // #00FF88
                : vec3(1.0,  0.961, 0.878); // #FFF5E0

            if (intensity > out_alpha) {
                out_alpha = intensity;
                out_color = color;
            }
        }
    }

    COLOR = vec4(out_color, out_alpha * vertical_fade * (1.0 - day_night));
}
