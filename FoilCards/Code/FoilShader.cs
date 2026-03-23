using Godot;

namespace FoilCards;

public static class FoilShader
{
    private static Shader? _shader;

    // language=gdshader
    private const string ShaderCode = @"
shader_type canvas_item;

uniform vec2 light_angle = vec2(0.0, 0.0);
uniform float intensity : hint_range(0.0, 1.0) = 0.5;
uniform float streak_density : hint_range(2.0, 20.0) = 7.0;
uniform float scroll_speed : hint_range(0.0, 6.0) = 2.5;
uniform float noise_scale : hint_range(2.0, 30.0) = 10.0;
uniform float noise_anim_speed : hint_range(0.0, 3.0) = 0.6;
uniform float gloss_power : hint_range(4.0, 128.0) = 20.0;
uniform float gloss_strength : hint_range(0.0, 1.5) = 0.5;
uniform float tilt_strength : hint_range(0.0, 1.0) = 0.45;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i+vec2(1,0)), f.x), mix(hash(i+vec2(0,1)), hash(i+vec2(1,1)), f.x), f.y);
}
float fbm(vec2 p) { float v = 0.5*noise(p); p*=2.01; v+=0.25*noise(p); p*=2.02; v+=0.125*noise(p); return v; }
vec3 hsv_to_rgb(float h, float s, float v) {
    vec3 k = mod(vec3(5,3,1)+h*6.0, 6.0); return v - v*s*max(min(min(k,4.0-k),1.0),0.0);
}

void fragment() {
    vec2 c = UV - 0.5;
    float persp = 1.0 + c.x * light_angle.x * tilt_strength + c.y * light_angle.y * tilt_strength * 0.5;
    persp = max(persp, 0.2);
    vec2 uv = vec2(c.x / persp, c.y / persp) + 0.5;
    uv = clamp(uv, vec2(0.003), vec2(0.997));
    vec4 base_color = texture(TEXTURE, uv);
    float facing = clamp(1.0 + c.x * light_angle.x * tilt_strength * 0.5, 0.75, 1.25);
    vec2 ed = min(uv, 1.0-uv);
    float edge_fade = smoothstep(0.0, 0.015, ed.x) * smoothstep(0.0, 0.015, ed.y);

    vec2 dir = normalize(vec2(0.65, 0.35));
    float angle_shift = dot(light_angle, dir) * scroll_speed;
    vec2 nuv = uv * noise_scale + vec2(TIME*noise_anim_speed*0.3, TIME*noise_anim_speed*0.17);
    float streak = dot(uv-0.5, dir) * streak_density + angle_shift + fbm(nuv)*0.35;
    float streak_mask = pow(sin(streak*3.14159)*0.5+0.5, 0.6);
    vec3 rainbow = hsv_to_rgb(fract(streak*0.5+angle_shift*0.25), 0.65, 1.0);

    vec2 gc = light_angle*0.3+0.5;
    float gd = length(uv-gc);
    float gloss = pow(max(1.0-gd*1.8,0.0), gloss_power)*gloss_strength + pow(max(1.0-gd,0.0),3.0)*0.12;

    vec3 foil = rainbow * streak_mask + vec3(gloss);
    vec3 lit = base_color.rgb * facing;
    vec3 result = 1.0 - (1.0 - lit) * (1.0 - foil * intensity);
    result = mix(base_color.rgb, result, edge_fade);
    COLOR = vec4(result, base_color.a);
}
";

    // Simple tilt-only shader for the SubViewport output (whole card as one texture)
    private static Shader? _tiltShader;
    private const string TiltShaderCode = @"
shader_type canvas_item;
uniform float tilt_x = 0.0;
uniform float tilt_y = 0.0;
void fragment() {
    vec2 c = UV - 0.5;
    float persp = 1.0 + c.x * tilt_x + c.y * tilt_y * 0.5;
    persp = max(persp, 0.2);
    vec2 uv = vec2(c.x / persp, c.y / persp) + 0.5;
    uv = clamp(uv, vec2(0.0), vec2(1.0));
    float facing = clamp(1.0 + c.x * tilt_x * 0.4, 0.8, 1.2);
    vec4 col = texture(TEXTURE, uv);
    col.rgb *= facing;
    // Fade edges to transparent
    vec2 ed = min(uv, 1.0 - uv);
    float ef = smoothstep(0.0, 0.01, ed.x) * smoothstep(0.0, 0.01, ed.y);
    col.a *= ef;
    COLOR = col;
}
";

    public static Shader GetShader()
    {
        if (_shader == null) { _shader = new Shader(); _shader.Code = ShaderCode; }
        return _shader;
    }

    public static Shader GetTiltShader()
    {
        if (_tiltShader == null) { _tiltShader = new Shader(); _tiltShader.Code = TiltShaderCode; }
        return _tiltShader;
    }

    public static ShaderMaterial CreateMaterial()
    {
        var mat = new ShaderMaterial();
        mat.Shader = GetShader();
        mat.SetShaderParameter("light_angle", new Vector2(0f, 0f));
        mat.SetShaderParameter("intensity", 0.5f);
        mat.SetShaderParameter("streak_density", 7.0f);
        mat.SetShaderParameter("scroll_speed", 2.5f);
        mat.SetShaderParameter("noise_scale", 10.0f);
        mat.SetShaderParameter("noise_anim_speed", 0.6f);
        mat.SetShaderParameter("gloss_power", 20.0f);
        mat.SetShaderParameter("gloss_strength", 0.5f);
        mat.SetShaderParameter("tilt_strength", 0.45f);
        return mat;
    }

    public static ShaderMaterial CreateTiltMaterial()
    {
        var mat = new ShaderMaterial();
        mat.Shader = GetTiltShader();
        mat.SetShaderParameter("tilt_x", 0f);
        mat.SetShaderParameter("tilt_y", 0f);
        return mat;
    }
}
