#version 330 core

in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uSceneTex;
uniform sampler2D uSSAOTex;
uniform sampler2D uBloomTex;
uniform float uBloomIntensity;
uniform float uSSAOStrength;

void main()
{
    vec3 scene = texture(uSceneTex, vTexCoord).rgb;
    float ssao = texture(uSSAOTex, vTexCoord).r;
    vec3 bloom = texture(uBloomTex, vTexCoord).rgb;

    // Apply SSAO (blend between full brightness and occluded)
    float ao = mix(1.0, ssao, uSSAOStrength);
    vec3 color = scene * ao;

    // Additive bloom glow
    color += bloom * uBloomIntensity;

    FragColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}
