shader_type canvas_item;

// --- Uniforms (the knobs you can tweak in the Godot Inspector) ---
uniform vec4 brick_color : source_color = vec4(0.45, 0.22, 0.15, 1.0);
uniform vec4 mortar_color : source_color = vec4(0.75, 0.72, 0.68, 1.0);
uniform float brick_width = 80.0;
uniform float brick_height = 64.0;
uniform float mortar_thickness = 4.0;
uniform float ambient_light : hint_range(0.0, 1.0) = 1.0;

// A varying carries a value from vertex() to fragment().
// We use it to pass the local pixel position, because UV is unreliable
// on a Polygon2D with no texture (Godot doesn't auto-generate UVs).
varying vec2 local_pos;

void vertex() {
    // VERTEX is the polygon vertex position in local pixel space.
    // The GPU interpolates this across the triangle, so every fragment
    // receives its own exact pixel position — no polygon_size math needed.
    local_pos = VERTEX;
}

void fragment() {
    vec2 px = local_pos;

    // Which row of bricks are we in?
    float row = floor(px.y / brick_height);

    // Every other row, shift bricks sideways by half a brick width.
    // This is what makes a real brick stagger pattern.
    float offset = mod(row, 2.0) * (brick_width / 2.0);

    // Which column of bricks are we in (accounting for the row offset)?
    float col = floor((px.x + offset) / brick_width);

    // Where are we inside the current brick cell, in pixels?
    vec2 brick_local = vec2(
        mod(px.x + offset, brick_width),
        mod(px.y, brick_height)
    );

    // Signed distance to the mortar edge (positive = inside brick, negative = in mortar).
    // fwidth gives the screen-space size of one pixel in local-space units, so the
    // smoothstep window stays exactly one pixel wide regardless of zoom level.
    vec2 fw = fwidth(brick_local);
    float edge_x = brick_width - mortar_thickness;
    float edge_y = brick_height - mortar_thickness;
    float mortar_x = smoothstep(edge_x - fw.x, edge_x + fw.x, brick_local.x);
    float mortar_y = smoothstep(edge_y - fw.y, edge_y + fw.y, brick_local.y);
    float mortar_factor = max(mortar_x, mortar_y);

    // Subtle per-brick variation so they don't look copy-pasted.
    // Standard hash trick: large prime multiply + fract gives pseudo-random floats.
    float variation = fract(sin(col * 127.1 + row * 311.7) * 43758.5453) * 0.08 - 0.04;
    vec4 base_color = mix(brick_color + vec4(vec3(variation), 0.0), mortar_color, mortar_factor);

    COLOR = vec4(base_color.rgb * ambient_light, base_color.a);
}
