using Godot;

namespace FoilCards;

/// <summary>
/// Holographic foil shader with parallax UV distortion, noise-modulated rainbow
/// streaks, moving gloss highlight, and pseudo-3D perspective warp.
/// All effects are driven by the light_angle uniform (mouse position).
/// </summary>
public static class FoilShader
{
    private static Shader? _shader;

    // language=gdshader
    private const string ShaderCode = @"
shader_type canvas_item;

// Mouse position relative to card center, normalized -1..1
uniform vec2 light_angle = vec2(0.0, 0.0);
// Overall foil blend strength
uniform float intensity : hint_range(0.0, 1.0) = 0.5;
// Streak density — higher = more rainbow bands
uniform float streak_density : hint_range(2.0, 20.0) = 7.0;
// How much streaks shift when you move the mouse
uniform float scroll_speed : hint_range(0.0, 6.0) = 2.5;
// Noise scale for the organic foil pattern
uniform float noise_scale : hint_range(2.0, 30.0) = 10.0;
// Noise animation speed
uniform float noise_anim_speed : hint_range(0.0, 3.0) = 0.6;
// Gloss highlight size (higher = tighter spot)
uniform float gloss_power : hint_range(4.0, 128.0) = 20.0;
// Gloss highlight brightness
uniform float gloss_strength : hint_range(0.0, 1.5) = 0.5;
// Parallax UV shift strength (pseudo-3D)
uniform float parallax_strength : hint_range(0.0, 0.08) = 0.03;
// Perspective warp strength (pseudo-3D trapezoid distortion)
uniform float perspective_strength : hint_range(0.0, 0.35) = 0.18;

// --- Procedural noise ---
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    v += 0.50 * noise(p); p *= 2.01;
    v += 0.25 * noise(p); p *= 2.02;
    v += 0.125 * noise(p);
    return v;
}

vec3 hsv_to_rgb(float h, float s, float v) {
    vec3 k = mod(vec3(5.0, 3.0, 1.0) + h * 6.0, 6.0);
    return v - v * s * max(min(min(k, 4.0 - k), 1.0), 0.0);
}

void fragment() {
    // --- Pseudo-3D perspective warp ---
    // Distort UVs to simulate the card tilting toward the mouse.
    // Creates a subtle trapezoid warp — near side stretches, far side compresses.
    vec2 centered = UV - 0.5;
    // Perspective factor: pixels on the side the mouse is on get pushed outward
    float persp_x = 1.0 + centered.x * light_angle.x * perspective_strength;
    float persp_y = 1.0 + centered.y * light_angle.y * perspective_strength;
    vec2 warped = vec2(centered.x * persp_y, centered.y * persp_x) + 0.5;

    // Parallax shift — entire image slides slightly opposite to light angle
    warped -= light_angle * parallax_strength;

    // Clamp to avoid sampling outside the texture (fixes edge cutoff)
    warped = clamp(warped, vec2(0.005), vec2(0.995));

    vec4 base_color = texture(TEXTURE, warped);

    // Fade out at edges to prevent hard cutoffs
    float edge_fade = 1.0;
    vec2 edge_dist = min(warped, 1.0 - warped);
    edge_fade = smoothstep(0.0, 0.02, edge_dist.x) * smoothstep(0.0, 0.02, edge_dist.y);

    // --- Holographic streaks ---
    vec2 dir = normalize(vec2(0.65, 0.35));
    float view_dot = dot(warped - 0.5, dir);

    // Light angle drives streak shift
    float angle_shift = dot(light_angle, dir) * scroll_speed;

    // Noise modulation
    vec2 noise_uv = warped * noise_scale + vec2(TIME * noise_anim_speed * 0.3, TIME * noise_anim_speed * 0.17);
    float n = fbm(noise_uv) * 0.35;

    // Streak pattern
    float streak = view_dot * streak_density + angle_shift + n;
    float streak_mask = sin(streak * 3.14159) * 0.5 + 0.5;
    streak_mask = pow(streak_mask, 0.6);

    // Rainbow hue from streak position
    float hue = fract(streak * 0.5 + angle_shift * 0.25);
    vec3 rainbow = hsv_to_rgb(hue, 0.65, 1.0);

    // --- Gloss / specular ---
    vec2 gloss_center = light_angle * 0.3 + 0.5;
    float gloss_dist = length(warped - gloss_center);
    float gloss = pow(max(1.0 - gloss_dist * 1.8, 0.0), gloss_power) * gloss_strength;
    float gloss_soft = pow(max(1.0 - gloss_dist * 1.0, 0.0), 3.0) * 0.12;

    // --- Edge fresnel ---
    vec2 edge = abs(warped - 0.5) * 2.0;
    float fresnel = smoothstep(0.4, 1.0, max(edge.x, edge.y));
    float edge_boost = fresnel * length(light_angle) * 0.2;

    // --- Composite ---
    vec3 foil = rainbow * streak_mask + vec3(gloss + gloss_soft + edge_boost);

    // Screen blend
    vec3 result = 1.0 - (1.0 - base_color.rgb) * (1.0 - foil * intensity);

    // Apply edge fade
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
        mat.SetShaderParameter("parallax_strength", 0.03f);
        mat.SetShaderParameter("perspective_strength", 0.18f);
        return mat;
    }
}
