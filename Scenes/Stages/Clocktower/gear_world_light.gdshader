shader_type canvas_item;

// World-distance lighting — brightness depends only on world position relative
// to the three lights, so adjacent gears agree at shared edges.
uniform vec2  light1_pos = vec2(291.0, 560.0);
uniform vec2  light2_pos = vec2(789.0, 380.0);
uniform vec2  light3_pos = vec2(1267.0, 550.0);
uniform float falloff : hint_range(1.0, 3000.0) = 700.0;
uniform float ambient : hint_range(0.0, 1.0) = 0.05;

varying vec2 world_pos;
varying vec4 vertex_color;

void vertex() {
    world_pos    = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    vertex_color = COLOR;
}

float light_contrib(vec2 light_pos) {
    return clamp(1.0 - distance(world_pos, light_pos) / falloff, 0.0, 1.0);
}

void fragment() {
    vec4  base       = texture(TEXTURE, UV) * vertex_color;
    float contrib    = light_contrib(light1_pos)
                     + light_contrib(light2_pos)
                     + light_contrib(light3_pos);
    float brightness = ambient + (1.0 - ambient) * clamp(contrib, 0.0, 1.5);
    COLOR = vec4(base.rgb * brightness, base.a);
}
