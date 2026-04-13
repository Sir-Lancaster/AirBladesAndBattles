/*
Shader from Godot Shaders - the free shader library.
godotshaders.com/shader/pixelate

This shader is under CC0 license. Feel free to use, improve and 
change this shader according to your needs and consider sharing 
the modified result to godotshaders.com.
*/

shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform float pixel_size = 5;

void fragment()
{
    vec2 viewport_size = 1.0 / SCREEN_PIXEL_SIZE;
    vec2 screen_px = SCREEN_UV * viewport_size;
    vec2 snapped_px = (floor(screen_px / pixel_size) + 0.5) * pixel_size;
    vec2 snapped_uv = snapped_px / viewport_size;

    COLOR = texture(screen_texture, snapped_uv);
}
