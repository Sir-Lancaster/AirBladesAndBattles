shader_type canvas_item;

// ── Tunable parameters ────────────────────────────────────────────────────────
uniform float speed          : hint_range(0.1, 3.0)  = 0.5;
uniform float blob_size      : hint_range(0.02, 0.3) = 0.10;
uniform vec4  color_bg        : source_color = vec4(0.02, 0.04, 0.01, 1.0); // near-black dark green
uniform vec4  color_lava      : source_color = vec4(0.04, 0.82, 0.14, 1.0); // vivid green
uniform vec4  color_highlight : source_color = vec4(0.55, 1.00, 0.60, 1.0); // bright green-white core
uniform vec4  color_glow      : source_color = vec4(0.00, 0.30, 0.04, 1.0); // dim ambient glow

// ── Metaball field contribution for one blob ──────────────────────────────────
// Standard formula: r² / d².  High where close to center, falls off quickly.
float blob(vec2 uv, vec2 center, float r) {
    vec2 d = uv - center;
    return (r * r) / (dot(d, d) + 0.0001);
}

void fragment() {
    // Aspect-correct UV so blobs appear circular instead of elliptical.
    // SCREEN_PIXEL_SIZE = 1/viewport_size, so .y/.x = width/height.
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2  uv     = vec2(UV.x * aspect, UV.y);
    float t      = TIME * speed;
    float r      = blob_size;

    // ── 8 lava-lamp blobs ─────────────────────────────────────────────────────
    // Each blob oscillates vertically (±0.40 of screen height) at a unique phase
    // and speed to prevent synchronisation, with a small horizontal drift.
    float f = 0.0;
    f += blob(uv, vec2((0.10 + 0.04 * sin(t * 0.37 + 0.00)) * aspect, 0.5 + 0.40 * sin(t * 0.41 + 0.00)), r * 1.20);
    f += blob(uv, vec2((0.25 + 0.05 * cos(t * 0.29 + 1.20)) * aspect, 0.5 + 0.42 * cos(t * 0.38 + 1.50)), r * 1.00);
    f += blob(uv, vec2((0.40 + 0.04 * sin(t * 0.33 + 2.40)) * aspect, 0.5 + 0.40 * sin(t * 0.43 + 0.80)), r * 1.30);
    f += blob(uv, vec2((0.55 + 0.05 * cos(t * 0.27 + 0.60)) * aspect, 0.5 + 0.42 * cos(t * 0.36 + 2.00)), r * 1.10);
    f += blob(uv, vec2((0.68 + 0.04 * sin(t * 0.35 + 3.10)) * aspect, 0.5 + 0.40 * cos(t * 0.44 + 1.00)), r * 1.00);
    f += blob(uv, vec2((0.82 + 0.03 * cos(t * 0.31 + 1.80)) * aspect, 0.5 + 0.42 * sin(t * 0.39 + 0.30)), r * 0.90);
    f += blob(uv, vec2((0.20 + 0.04 * sin(t * 0.39 + 2.00)) * aspect, 0.5 + 0.38 * cos(t * 0.45 + 1.70)), r * 1.10);
    f += blob(uv, vec2((0.47 + 0.05 * cos(t * 0.32 + 0.90)) * aspect, 0.5 + 0.41 * sin(t * 0.40 + 2.80)), r * 1.20);

    // ── Map field value to colour layers ──────────────────────────────────────
    // lava  — the blob surface (sharp-ish edge where blobs begin/end)
    // core  — bright centre of each blob (simulates light through hot wax)
    // glow  — soft ambient halo that surrounds but doesn't overlap blobs
    float lava = smoothstep(0.85, 1.20, f);
    float core = smoothstep(2.00, 5.00, f);
    float glow = smoothstep(0.20, 0.85, f) * (1.0 - lava);

    vec3 col = color_bg.rgb;
    col += color_glow.rgb * glow;
    col  = mix(col, color_lava.rgb, lava);
    col  = mix(col, color_highlight.rgb, core);

    COLOR = vec4(col, 1.0);
}
