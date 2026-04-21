shader_type canvas_item;

uniform vec2 light1_pos = vec2(291.0, 560.0);
uniform vec2 light2_pos = vec2(789.0, 380.0);
uniform vec2 light3_pos = vec2(1267.0, 550.0);
uniform float falloff : hint_range(1.0, 3000.0) = 700.0;
uniform float ambient : hint_range(0.0, 1.0) = 0.03;
uniform float radius : hint_range(10.0, 500.0) = 186.0;

varying vec2 world_pos;
varying vec2 world_offset;
varying vec4 vertex_color;

void vertex() {
    vec2 wp = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
    vec2 world_center = MODEL_MATRIX[3].xy;
    float world_scale = length(MODEL_MATRIX[0].xy);
    world_pos = wp;
    world_offset = (wp - world_center) / (radius * world_scale);
    vertex_color = COLOR;
}

float light_contrib(vec2 light_pos, vec3 normal) {
    vec2 to_light = light_pos - world_pos;
    vec3 l = normalize(vec3(normalize(to_light), 0.5));
    float diffuse = max(0.0, dot(normal, l));
    float dist_factor = clamp(1.0 - distance(world_pos, light_pos) / falloff, 0.0, 1.0);
    return diffuse * dist_factor;
}

void fragment() {
    vec4 base = texture(TEXTURE, UV) * vertex_color;

    vec2 n2d = clamp(world_offset, -1.0, 1.0);
    float z = sqrt(max(0.0, 1.0 - dot(n2d, n2d)));
    vec3 normal = normalize(vec3(n2d, z));

    float contrib = light_contrib(light1_pos, normal)
                  + light_contrib(light2_pos, normal)
                  + light_contrib(light3_pos, normal);

    float brightness = ambient + (1.0 - ambient) * clamp(contrib, 0.0, 1.5);

    COLOR = vec4(base.rgb * brightness, base.a);
}
