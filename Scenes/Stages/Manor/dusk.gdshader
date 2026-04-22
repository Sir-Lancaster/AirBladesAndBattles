shader_type canvas_item;

/// Horizon color (bottom of sky). Default: deep purple-blue
uniform vec4 horizon_color : source_color = vec4(0.2, 0.15, 0.4, 1.0);
/// Sky zenith color (top). Default: dark purple
uniform vec4 zenith_color : source_color = vec4(0.1, 0.05, 0.25, 1.0);

/// Lightning flash intensity. Higher = brighter flashes
uniform float lightning_intensity : hint_range(0.0, 2.0) = 0.8;
/// How often lightning flashes (lower = more frequent)
uniform float lightning_frequency : hint_range(0.5, 5.0) = 2.0;
/// Duration of each flash in seconds (very brief for realism)
uniform float flash_duration : hint_range(0.01, 0.5) = 0.08;

/// Cloud/atmospheric noise intensity
uniform float atmosphere_intensity : hint_range(0.0, 1.0) = 0.15;
/// Cloud movement speed
uniform float cloud_speed : hint_range(0.0, 2.0) = 0.3;

// Simple pseudo-random function
float random(float seed) {
    return fract(sin(seed) * 43758.5453);
}

// Simple noise function for clouds
float noise(vec2 st) {
    vec2 i = floor(st);
    vec2 f = fract(st);
    f = f * f * (3.0 - 2.0 * f); // smoothstep
    
    float a = random(i.x + i.y * 23.0);
    float b = random(i.x + 1.0 + i.y * 23.0);
    float c = random(i.x + (i.y + 1.0) * 23.0);
    float d = random(i.x + 1.0 + (i.y + 1.0) * 23.0);
    
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void fragment() {
    // Vertical gradient: horizon → zenith
    vec4 sky = mix(horizon_color, zenith_color, UV.y);
    
    // Atmospheric clouds/haze
    vec2 cloud_uv = UV + vec2(TIME * cloud_speed, 0.0);
    float clouds = noise(cloud_uv * 3.0);
    clouds += noise(cloud_uv * 7.0) * 0.5;
    clouds = mix(0.5, clouds, atmosphere_intensity);
    sky = mix(sky, sky * 1.2, (clouds - 0.5) * 0.3);
    
    // Lightning flash effect with 3 pulses
    // Periodic flashing based on time
    float flash_cycle = mod(TIME, lightning_frequency);
    
    // Flash is bright white when active, fades to 0
    float flash = 0.0;
    if (flash_cycle < flash_duration * 3.0) {
        // Create 3 distinct pulses
        float pulse_num = floor(flash_cycle / flash_duration);  // 0, 1, or 2
        float pulse_progress = mod(flash_cycle, flash_duration) / flash_duration;
        
        // Each pulse: bright at start, fades out
        float pulse = (1.0 - pulse_progress) * lightning_intensity;
        
        // Add slight gap between pulses for separation
        pulse *= smoothstep(0.0, 0.1, pulse_progress);
        
        flash = pulse;
    }
    
    // Add slight randomness to flash intensity
    flash *= mix(0.7, 1.0, random(floor(TIME / lightning_frequency)));
    
    // Blend flash with sky (brighten entire scene during flash)
    sky = mix(sky, vec4(1.0), flash * 0.6);
    
    COLOR = sky;
}
