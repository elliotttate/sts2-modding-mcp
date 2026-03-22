using Godot;

namespace FoilCards;

/// <summary>
/// Creates and caches the holographic foil shader material.
/// The shader simulates light catching a foil surface — rainbow bands shift
/// as the light_angle uniform changes (driven by mouse position in the patch).
/// </summary>
public static class FoilShader
{
    private static Shader? _shader;

    // language=gdshader
    private const string ShaderCode = @"
shader_type canvas_item;

// Normalized mouse position relative to card center (-1..1, -1..1)
uniform vec2 light_angle = vec2(0.0, 0.0);
// Overall foil intensity (0 = invisible, 1 = full)
uniform float intensity : hint_range(0.0, 1.0) = 0.7;
// How many rainbow bands are visible
uniform float band_density : hint_range(1.0, 12.0) = 5.0;
// Speed of the subtle shimmer animation
uniform float shimmer_speed : hint_range(0.0, 4.0) = 1.2;
// Specular highlight tightness
uniform float specular_power : hint_range(2.0, 64.0) = 16.0;
// Specular highlight brightness
uniform float specular_strength : hint_range(0.0, 1.0) = 0.4;

vec3 hsv_to_rgb(float h, float s, float v) {
    vec3 k = mod(vec3(5.0, 3.0, 1.0) + h * 6.0, 6.0);
    return v - v * s * max(min(min(k, 4.0 - k), 1.0), 0.0);
}

void fragment() {
    vec4 base = texture(TEXTURE, UV);

    // Diagonal gradient driven by UV + light angle
    float diag = (UV.x + UV.y) * 0.5;
    float angle_offset = dot(light_angle, vec2(0.7, 0.3));

    // Animated shimmer
    float shimmer = sin(TIME * shimmer_speed + diag * 20.0) * 0.02;

    // Rainbow hue cycles along the diagonal, shifts with mouse position
    float hue = fract(diag * band_density + angle_offset * 2.0 + shimmer);
    vec3 rainbow = hsv_to_rgb(hue, 0.6, 1.0);

    // Fresnel-like edge brightening: stronger at card edges
    vec2 center_offset = UV - 0.5;
    float edge_dist = length(center_offset) * 1.4;
    float fresnel = smoothstep(0.1, 0.9, edge_dist);

    // Specular highlight — a soft bright spot that follows the mouse
    vec2 spec_pos = (light_angle * 0.5 + 0.5); // map -1..1 to 0..1
    float spec_dist = length(UV - spec_pos);
    float specular = pow(max(1.0 - spec_dist * 2.5, 0.0), specular_power) * specular_strength;

    // Combine: rainbow foil tinted by fresnel, plus specular highlight
    vec3 foil = rainbow * (0.5 + fresnel * 0.5) + vec3(specular);

    // Blend foil over the base texture using screen blend mode
    vec3 result = 1.0 - (1.0 - base.rgb) * (1.0 - foil * intensity);

    // Add a subtle extra brightness from the specular
    result += vec3(specular * intensity);

    COLOR = vec4(result, base.a);
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
        mat.SetShaderParameter("intensity", 0.7f);
        mat.SetShaderParameter("band_density", 5.0f);
        mat.SetShaderParameter("shimmer_speed", 1.2f);
        mat.SetShaderParameter("specular_power", 16.0f);
        mat.SetShaderParameter("specular_strength", 0.4f);
        return mat;
    }
}
