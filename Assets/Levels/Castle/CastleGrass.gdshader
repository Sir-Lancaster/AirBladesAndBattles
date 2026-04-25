shader_type canvas_item;

// --- Colors tuned for the stormy castle atmosphere ---
// Keep greens dark and desaturated so they read correctly under the blue-gray sky.
uniform vec4 grass_shadow  : source_color = vec4(0.07, 0.13, 0.05, 1.0);
uniform vec4 grass_base    : source_color = vec4(0.13, 0.21, 0.09, 1.0);
uniform vec4 grass_highlight : source_color = vec4(0.19, 0.29, 0.13, 1.0);

uniform float wind_speed    = 1.2;
uniform float wind_strength = 0.6;
// Scales local pixel coords down to a noise-friendly range.
// Local coords here span ~7000 x 1500 px, so 0.008 puts one noise cell ≈ 125 px.
uniform float noise_scale   = 0.008;
uniform float ambient_light : hint_range(0.0, 1.0) = 0.6;

varying vec2 local_pos;

void vertex() {
    local_pos = VERTEX;
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    // Quintic — no slope kinks at grid edges.
    vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return mix(
        mix(hash(i),               hash(i + vec2(1.0, 0.0)), u.x),
        mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x),
        u.y
    );
}

// Two octaves is plenty for background fill — keeps it cheap.
float fbm(vec2 p) {
    return noise(p) * 0.6 + noise(p * 2.1 + vec2(1.7, 9.2)) * 0.4;
}

void fragment() {
    // Wind scrolls the noise horizontally. The vertical component is much slower
    // so the pattern churns rather than just sliding in one direction.
    vec2 wind = vec2(TIME * wind_speed * 0.05, TIME * wind_speed * 0.012);

    vec2 p = local_pos * noise_scale;
    float n1 = fbm(p + wind);
    float n2 = fbm(p * 1.8 + wind * 1.3 + vec2(4.7, 2.3));

    // Large-scale patch variation + finer detail layer.
    float pattern = n1 * 0.65 + n2 * 0.35;

    // Wind ripple: a sine wave that moves across the hill surface.
    // Amplitude is modulated by n1 so it follows the natural clump pattern.
    float ripple = sin(local_pos.x * 0.012 + TIME * wind_speed) * 0.5 + 0.5;
    pattern = mix(pattern, ripple, 0.15 * wind_strength * n1);

    vec4 color = mix(grass_shadow, grass_highlight, pattern);

    // Darken slightly at the very top of the hill (y ≈ 79) where the castle
    // ground casts a shadow. Polygon top edge is near y=79, bottom near y=1600.
    float top_shadow = smoothstep(79.0, 350.0, local_pos.y);
    color.rgb = mix(color.rgb * 0.72, color.rgb, top_shadow);

    COLOR = vec4(color.rgb * ambient_light, color.a);
}
