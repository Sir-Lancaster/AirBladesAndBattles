shader_type canvas_item;

/// 1.0 = full day (original color), 0.0 = full dusk (full glow). Drive from KernelBadlands.cs.
uniform float day_night : hint_range(0.0, 1.0) = 1.0;

/// HDR brightness at full night — set above 1.0 to trigger WorldEnvironment bloom.
uniform float glow_strength : hint_range(1.0, 10.0) = 5.0;

/// Glow colour — source_color matches TraceChaser's sRGB→linear conversion.
/// Default: #39FF14 — same as TraceChaser pulse_color.
uniform vec4 glow_color : source_color = vec4(0.224, 1.0, 0.078, 1.0);

void fragment() {
    // Use font/texture alpha so Labels render text only, Polygon2D stays solid.
    float alpha = texture(TEXTURE, UV).a;

    // Day: original polygon/vertex color. Night: neon green HDR glow.
    // COLOR.rgb here is the vertex color (compensation in C# ensures parent modulate = white).
    float night = 1.0 - day_night;
    vec3 night_rgb = glow_color.rgb * glow_strength;

    COLOR = vec4(mix(COLOR.rgb, night_rgb, night), alpha);
}
