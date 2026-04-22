shader_type canvas_item;

// --- Storm sky colors ---
uniform vec4 sky_top_color : source_color = vec4(0.04, 0.04, 0.08, 1.0);
uniform vec4 sky_horizon_color : source_color = vec4(0.12, 0.10, 0.18, 1.0);

// --- Storm clouds ---
uniform vec4 cloud_dark_color : source_color = vec4(0.06, 0.06, 0.10, 1.0);
uniform vec4 cloud_light_color : source_color = vec4(0.28, 0.26, 0.34, 1.0);
uniform float cloud_speed = 0.04;
uniform float cloud_scale = 2.8;
uniform float storm_intensity : hint_range(0.0, 1.0) = 0.75;

// --- Lightning ---
uniform float lightning_frequency : hint_range(0.0, 1.0) = 0.3;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    // Quintic interpolation — eliminates slope discontinuities at grid edges.
    vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return mix(
        mix(hash(i),                    hash(i + vec2(1.0, 0.0)), u.x),
        mix(hash(i + vec2(0.0, 1.0)),   hash(i + vec2(1.0, 1.0)), u.x),
        u.y
    );
}

// This is the key fix for the grid problem.
// A rotation matrix rotates a vec2 by a fixed angle (~37 degrees here).
// Between every fBm octave we rotate the coordinate system, so each octave
// samples the grid at a different angle. The grid axes never line up across
// octaves, so the structure cancels out instead of reinforcing itself.
// mat2 is a 2x2 matrix — multiplying it by a vec2 rotates that vector.
const mat2 ROTATE = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));

float fbm(vec2 p) {
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 6; i++) {
        value += amplitude * noise(p);
        p = ROTATE * p * 2.0;   // rotate AND scale — two birds, one stone
        amplitude *= 0.5;
    }
    return value;
}

void fragment() {
    vec2 uv = UV;

    // --- Storm sky gradient ---
    vec4 sky = mix(sky_top_color, sky_horizon_color, pow(uv.y, 0.6));

    // --- Storm clouds ---
    // Two fbm passes scrolling at slightly different speeds and angles
    // give the layered, churning look of a real storm front.
    vec2 scroll = vec2(TIME * cloud_speed, TIME * cloud_speed * 0.3);
    vec2 p1 = uv * cloud_scale + scroll;
    vec2 p2 = uv * cloud_scale * 1.7 + scroll * 1.3 + vec2(4.7, 2.3);

    float clouds = fbm(p1) * 0.6 + fbm(p2) * 0.4;

    // Remap the noise: values above the midpoint become bright cloud breaks,
    // values below become dark cloud mass. storm_intensity pushes more of the
    // sky into the dark range, making the storm feel heavier.
    clouds = smoothstep(0.35 - storm_intensity * 0.2, 0.75, clouds);

    // Clouds thin out toward the bottom (near the horizon) — lighter there
    // so the horizon isn't completely black.
    float horizon_fade = 1.0 - smoothstep(0.55, 1.0, uv.y);
    clouds *= horizon_fade;

    vec4 cloud_color = mix(cloud_dark_color, cloud_light_color, clouds);
    sky = mix(sky, cloud_color, 0.92);

    // --- Lightning glow ---
    // sin(TIME) oscillates -1 to 1 every ~6 seconds.
    // Raising it to a high power (32) collapses most of the wave to near-zero,
    // leaving only a brief sharp spike near the peak — the "flash" moment.
    // We add a second sin at a prime-ish frequency so flashes feel irregular.
    float flash1 = pow(max(0.0, sin(TIME * 1.7 * lightning_frequency)), 32.0);
    float flash2 = pow(max(0.0, sin(TIME * 2.3 * lightning_frequency + 1.5)), 48.0);
    float lightning = (flash1 + flash2 * 0.6) * storm_intensity;

    // The flash brightens the whole sky slightly and tints it blue-white.
    vec4 lightning_color = vec4(0.75, 0.80, 1.0, 1.0);
    sky = mix(sky, lightning_color, lightning * 0.35);

    COLOR = sky;
}
