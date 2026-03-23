using Godot;

namespace FoilCards;

/// <summary>
/// Holographic foil rainbow shader for card portraits.
/// Applied to the Portrait TextureRect — adds rainbow streaks and gloss
/// that shift based on the light_angle uniform (driven by mouse position).
/// 3D tilt is handled separately via Scale.X on CardContainer (in bridge).
/// </summary>
public static class FoilShader
{
    private static Shader? _foilShader;

    private const string FoilCode = @"
shader_type canvas_item;
uniform vec2 light_angle = vec2(0.0, 0.0);
uniform float intensity : hint_range(0.0, 1.0) = 0.6;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); f = f*f*(3.0-2.0*f);
    return mix(mix(hash(i), hash(i+vec2(1,0)), f.x), mix(hash(i+vec2(0,1)), hash(i+vec2(1,1)), f.x), f.y);
}
vec3 hsv_to_rgb(float h, float s, float v) {
    vec3 k = mod(vec3(5,3,1)+h*6.0, 6.0); return v - v*s*max(min(min(k,4.0-k),1.0),0.0);
}

void fragment() {
    vec4 base_color = texture(TEXTURE, UV);
    vec2 dir = normalize(vec2(0.65, 0.35));
    float angle_shift = dot(light_angle, dir) * 2.5;
    vec2 nuv = UV * 10.0 + vec2(TIME*0.2, TIME*0.1);
    float n = noise(nuv) * 0.35;
    float streak = dot(UV-0.5, dir) * 7.0 + angle_shift + n;
    float mask = pow(sin(streak*3.14159)*0.5+0.5, 0.6);
    vec3 rainbow = hsv_to_rgb(fract(streak*0.5+angle_shift*0.25), 0.65, 1.0);
    vec2 gc = light_angle*0.3+0.5;
    float gloss = pow(max(1.0-length(UV-gc)*1.8,0.0), 20.0)*0.5;
    vec3 foil = rainbow * mask + vec3(gloss);
    vec3 result = 1.0 - (1.0 - base_color.rgb) * (1.0 - foil * intensity);
    COLOR = vec4(result, base_color.a);
}
";

    public static Shader GetFoilShader()
    {
        if (_foilShader == null)
        {
            _foilShader = new Shader();
            _foilShader.Code = FoilCode;
        }
        return _foilShader;
    }

    public static ShaderMaterial CreateFoilMaterial()
    {
        var mat = new ShaderMaterial();
        mat.Shader = GetFoilShader();
        mat.SetShaderParameter("light_angle", new Vector2(0f, 0f));
        mat.SetShaderParameter("intensity", 0.6f);
        return mat;
    }
}
