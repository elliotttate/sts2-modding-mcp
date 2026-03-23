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

// 3D perspective tilt — simulates card rotating on its vertical/horizontal axis
// Higher values = more dramatic tilt effect
uniform float tilt_strength : hint_range(0.0, 1.0) = 0.45;

// --- Noise ---
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i); float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0)); float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.5 * noise(p); p *= 2.01;
    v += 0.25 * noise(p); p *= 2.02;
    v += 0.125 * noise(p);
    return v;
}

vec3 hsv_to_rgb(float h, float s, float v) {
    vec3 k = mod(vec3(5.0, 3.0, 1.0) + h * 6.0, 6.0);
    return v - v * s * max(min(min(k, 4.0 - k), 1.0), 0.0);
}

void fragment() {
    // ===== 3D PERSPECTIVE TILT =====
    // Simulates the card turning on its axis like a physical card.
    // Maps light_angle to a perspective projection that compresses
    // one side and expands the other.

    vec2 centered = UV - 0.5;

    // Perspective division — this is the key to the 3D look.
    // When light_angle.x > 0, the right side compresses (farther away)
    // and the left side expands (closer). Like looking at a card edge-on.
    float tilt_x = light_angle.x * tilt_strength;
    float tilt_y = light_angle.y * tilt_strength * 0.5; // less vertical tilt

    // Perspective factor per pixel — varies across the card width/height
    // This creates the trapezoid distortion that reads as 3D rotation
    float persp = 1.0 + centered.x * tilt_x + centered.y * tilt_y;

    // Prevent division by zero or negative values
    persp = max(persp, 0.2);

    // Apply perspective division — farther side gets compressed UVs
    vec2 uv = vec2(centered.x / persp, centered.y / persp) + 0.5;

    // Clamp and fade edges
    uv = clamp(uv, vec2(0.003), vec2(0.997));
    vec4 base_color = texture(TEXTURE, uv);

    float edge_fade = 1.0;
    vec2 ed = min(uv, 1.0 - uv);
    edge_fade = smoothstep(0.0, 0.015, ed.x) * smoothstep(0.0, 0.015, ed.y);

    // ===== Lighting simulation for 3D tilt =====
    // Darken the side that's turning away, brighten the side turning toward viewer
    float facing = 1.0 + centered.x * tilt_x * 0.6;
    facing = clamp(facing, 0.7, 1.3);

    // ===== HOLOGRAPHIC STREAKS =====
    vec2 dir = normalize(vec2(0.65, 0.35));
    float view_dot = dot(uv - 0.5, dir);
    float angle_shift = dot(light_angle, dir) * scroll_speed;

    vec2 noise_uv = uv * noise_scale + vec2(TIME * noise_anim_speed * 0.3, TIME * noise_anim_speed * 0.17);
    float n = fbm(noise_uv) * 0.35;

    float streak = view_dot * streak_density + angle_shift + n;
    float streak_mask = sin(streak * 3.14159) * 0.5 + 0.5;
    streak_mask = pow(streak_mask, 0.6);

    float hue = fract(streak * 0.5 + angle_shift * 0.25);
    vec3 rainbow = hsv_to_rgb(hue, 0.65, 1.0);

    // ===== GLOSS =====
    vec2 gloss_center = light_angle * 0.3 + 0.5;
    float gloss_dist = length(uv - gloss_center);
    float gloss = pow(max(1.0 - gloss_dist * 1.8, 0.0), gloss_power) * gloss_strength;
    float gloss_soft = pow(max(1.0 - gloss_dist * 1.0, 0.0), 3.0) * 0.12;

    // ===== COMPOSITE =====
    vec3 foil = rainbow * streak_mask + vec3(gloss + gloss_soft);

    // Apply 3D lighting to base color
    vec3 lit_base = base_color.rgb * facing;

    // Screen blend foil over lit base
    vec3 result = 1.0 - (1.0 - lit_base) * (1.0 - foil * intensity);

    result = mix(base_color.rgb, result, edge_fade);

    COLOR = vec4(result, base_color.a);
}
";

    public static Shader GetShader()
    {
        if (_shader == null)
        {
            _shader = new Shader();
            _shader.Code = ShaderCode;
        }
        return _shader;
    }

    // Separate tilt shader for the whole card (applied to CardContainer)
    private static Shader? _tiltShader;
    private const string TiltShaderCode = @"
shader_type canvas_item;
uniform float tilt_x = 0.0;
uniform float tilt_y = 0.0;
void fragment() {
    vec2 c = UV - 0.5;
    float persp = 1.0 + c.x * tilt_x + c.y * tilt_y * 0.5;
    persp = max(persp, 0.15);
    vec2 uv = vec2(c.x / persp, c.y / persp) + 0.5;
    uv = clamp(uv, vec2(0.0), vec2(1.0));
    float facing = clamp(1.0 + c.x * tilt_x * 0.5, 0.7, 1.3);
    vec4 col = texture(TEXTURE, uv);
    col.rgb *= facing;
    COLOR = col;
}
";

    public static Shader GetTiltShader()
    {
        if (_tiltShader == null)
        {
            _tiltShader = new Shader();
            _tiltShader.Code = TiltShaderCode;
        }
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
}
