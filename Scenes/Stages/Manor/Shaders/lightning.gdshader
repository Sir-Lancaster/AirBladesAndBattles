shader_type canvas_item;

uniform float chaser_progress  : hint_range(0.0, 1.0) = 0.0;
// day_night locked to 1.0 (full day) while perfecting the shader

/// Tight bright core of the travelling pulse.
uniform float pulse_width      : hint_range(0.01, 0.4) = 0.10;
/// Wider soft aura around the pulse core — sells the glow-outside effect.
uniform float pulse_aura_width : hint_range(0.05, 0.8) = 0.35;

/// Resting color of the trace. Default: #00FF88
uniform vec4 base_color  : source_color = vec4(0.0,   1.0,   0.533, 1.0);
/// Color of the travelling pulse. Default: #39FF14
uniform vec4 pulse_color : source_color = vec4(0.224, 1.0,   0.078, 1.0);

void fragment() {
    float dist = abs(UV.x - chaser_progress);
    dist = min(dist, 1.0 - dist); // wrap cleanly at both ends

    // Tight bright core
    float core = 1.0 - smoothstep(0.0, pulse_width, dist);
    // Wider dim aura — bleeds outward to be caught by WorldEnvironment Glow
    float aura = (1.0 - smoothstep(pulse_width, pulse_aura_width, dist)) * 0.35;

    float base_brightness = 0.3; // day value — restore mix(0.08, 0.3, day_night) later

    vec4 result = base_color * base_brightness;  // resting trace
    result += pulse_color * core;                // bright pulse core
    result += pulse_color * aura;                // soft glow halo

    COLOR = result;
}
