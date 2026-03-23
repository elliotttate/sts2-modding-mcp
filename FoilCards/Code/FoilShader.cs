using Godot;

namespace FoilCards;

/// <summary>
/// Shaders for the 3D card effect.
///
/// TiltShader: Applied to SubViewportContainer — does perspective warp on the
/// entire card texture (frame, art, text, everything as one image).
///
/// FoilShader: Applied to the card portrait — holographic rainbow effect.
/// </summary>
public static class FoilShader
{
    // ─── Perspective tilt shader (for SubViewportContainer) ────────────────

    private static Shader? _tiltShader;

    private const string TiltCode = @"
shader_type canvas_item;
uniform float tilt_x = 0.0;
uniform float tilt_y = 0.0;

void vertex() {
    // Perspective warp — shift vertices based on their position
    // relative to card center. Creates trapezoid distortion.
    vec2 center = vec2(150.0, 211.0); // half card size
    vec2 offset = VERTEX - center;
    vec2 norm = offset / center; // -1..1

    float persp = 1.0 + norm.x * tilt_x + norm.y * tilt_y * 0.5;
    persp = max(persp, 0.2);

    VERTEX = vec2(offset.x / persp, offset.y / persp) + center;
}

void fragment() {
    vec4 col = texture(TEXTURE, UV);
    // Directional lighting — near side brighter, far side darker
    vec2 norm = UV - 0.5;
    float facing = clamp(1.0 + norm.x * tilt_x * 0.4, 0.8, 1.2);
    col.rgb *= facing;
    COLOR = col;
}
";

    public static Shader GetTiltShader()
    {
        if (_tiltShader == null)
        {
            _tiltShader = new Shader();
            _tiltShader.Code = TiltCode;
        }
        return _tiltShader;
    }

    public static ShaderMaterial CreateTiltMaterial()
    {
        var mat = new ShaderMaterial();
        mat.Shader = GetTiltShader();
        mat.SetShaderParameter("tilt_x", 0f);
        mat.SetShaderParameter("tilt_y", 0f);
        return mat;
    }

    // ─── Foil rainbow shader (for portrait TextureRect) ───────────────────

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
