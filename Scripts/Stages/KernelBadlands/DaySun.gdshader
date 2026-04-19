shader_type canvas_item;

uniform vec4 sun_color : source_color = vec4(1.0, 0.93, 0.72, 1.0);
uniform vec4 glow_color : source_color = vec4(1.0, 0.42, 0.0, 1.0);

uniform float radius = 0.28;
uniform float glow_size = 0.15;
uniform float edge_softness = 0.03;
uniform vec2 center = vec2(0.5, 0.5);

void fragment() {
    float d = distance(UV, center);

    float sun = 1.0 - smoothstep(radius, radius + edge_softness, d);
    float glow = 1.0 - smoothstep(radius, radius + glow_size, d);
    glow *= 0.85;

    float core = 1.0 - smoothstep(0.0, radius * 0.55, d);

    vec3 color = glow_color.rgb * glow;
    color += sun_color.rgb * sun;
    color += vec3(1.0, 0.98, 0.9) * core * 0.40;

    float alpha = max(sun, glow);

    COLOR = vec4(color, alpha);
}