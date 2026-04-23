shader_type canvas_item;

/// Core brightness of the candleflame. Higher = brighter glow.
uniform float flame_brightness : hint_range(0.1, 1.0) = 0.6;
/// How fast the flame pulses. Lower = slower, more subtle.
uniform float pulse_speed : hint_range(0.1, 3.0) = 1.2;
/// How much the flame flickers. Higher = more chaotic.
uniform float flicker_amount : hint_range(0.0, 1.0) = 0.3;

/// Main candlelight color. Default: warm orange #FF9944
uniform vec4 flame_color : source_color = vec4(1.0, 0.6, 0.27, 1.0);
/// Soft outer glow color for WorldEnvironment. Default: amber #FFD700
uniform vec4 glow_color : source_color = vec4(1.0, 0.84, 0.0, 1.0);

void fragment() {
    // Distance from center of texture
    float dist = length(UV - vec2(0.5, 0.5));
    
    // Base pulsing effect from center
    float pulse = sin(TIME * pulse_speed) * 0.5 + 0.5;  // oscillates 0->1
    
    // Simple flicker using time offset
    float flicker = sin(TIME * pulse_speed * 2.3 + UV.x * 3.0) * flicker_amount;
    
    // Tight bright core (inner flame)
    float core = 1.0 - smoothstep(0.0, 0.15, dist);
    core *= (pulse * 0.7 + 0.3 + flicker);  // apply pulsing to core
    
    // Soft outer glow (for WorldEnvironment to catch)
    float glow = 1.0 - smoothstep(0.15, 0.45, dist);
    glow *= (pulse * 0.5 + 0.5);  // subtle pulse on glow
    glow *= 0.6;  // reduce intensity so it doesn't overwhelm
    
    // Combine layers
    vec4 result = vec4(0.0);
    result += flame_color * core;
    result += glow_color * glow;
    
    COLOR = result;
}