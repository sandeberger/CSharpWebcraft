#version 330 core

in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uSceneTex;
uniform sampler2D uSSAOTex;
uniform sampler2D uBloomTex;
uniform float uBloomIntensity;
uniform float uSSAOStrength;

// Underwater distortion
uniform int uUnderwater;
uniform float uTime;

// ACES filmic tone mapping (Narkowicz 2015)
vec3 ACESFilm(vec3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

// Hash for procedural noise
float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// Smooth value noise
float vnoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Multi-octave turbidity noise
float turbidity(vec2 p, float t)
{
    float v = 0.0;
    v += vnoise(p * 3.0 + vec2(t * 0.3, t * 0.2)) * 0.5;
    v += vnoise(p * 6.0 + vec2(-t * 0.2, t * 0.35)) * 0.25;
    v += vnoise(p * 12.0 + vec2(t * 0.15, -t * 0.25)) * 0.125;
    return v;
}

void main()
{
    vec2 uv = vTexCoord;

    // Underwater wavy distortion
    if (uUnderwater > 0)
    {
        float distortStrength = 0.004;
        float freq = 12.0;
        float speed = 2.5;
        uv.x += sin(uv.y * freq + uTime * speed) * distortStrength;
        uv.y += cos(uv.x * freq * 0.8 + uTime * speed * 0.7) * distortStrength * 0.7;
        uv = clamp(uv, 0.0, 1.0);
    }

    vec3 scene = texture(uSceneTex, uv).rgb;
    float ssao = texture(uSSAOTex, uv).r;
    vec3 bloom = texture(uBloomTex, uv).rgb;

    // Apply SSAO (blend between full brightness and occluded)
    float ao = mix(1.0, ssao, uSSAOStrength);
    vec3 color = scene * ao;

    // Additive bloom glow
    color += bloom * uBloomIntensity;

    // Underwater effects
    if (uUnderwater > 0)
    {
        // Suspended particle turbidity (murky water)
        float murk = turbidity(vTexCoord * 4.0, uTime);
        vec3 murkColor = vec3(0.12, 0.30, 0.28);
        color = mix(color, murkColor, murk * 0.25);

        // Drifting sediment wisps (larger, slower clouds)
        float wisps = vnoise(vTexCoord * 1.5 + vec2(uTime * 0.08, uTime * 0.05));
        wisps = smoothstep(0.4, 0.7, wisps);
        vec3 wispColor = vec3(0.18, 0.38, 0.35);
        color = mix(color, wispColor, wisps * 0.15);

        // Blue-green tint
        vec3 waterTint = vec3(0.15, 0.45, 0.55);
        color = mix(color, waterTint, 0.15);

        // Vignette (darker at edges for depth feeling)
        float dist = length(vTexCoord - 0.5) * 1.4;
        float vignette = 1.0 - dist * dist * 0.5;
        color *= vignette;

        // Caustic shimmer (light patterns)
        float caustic1 = sin(vTexCoord.x * 40.0 + uTime * 1.5) * sin(vTexCoord.y * 30.0 + uTime * 1.2);
        float caustic2 = sin(vTexCoord.x * 25.0 - uTime * 1.0) * sin(vTexCoord.y * 35.0 - uTime * 0.8);
        float caustic = (caustic1 + caustic2) * 0.02;
        color += vec3(caustic * 0.5, caustic * 0.8, caustic);
    }

    FragColor = vec4(ACESFilm(color), 1.0);
}
