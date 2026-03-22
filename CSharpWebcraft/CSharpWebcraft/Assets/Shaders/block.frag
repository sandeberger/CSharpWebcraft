#version 330 core

in vec3 vColor;
in vec2 vTexCoord;
in vec3 vNormal;
in float vFogDistance;
in vec3 vWorldPos;
in float vSkyBri;
in float vBlockBri;
in float vAO;

out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec3 uFogColor;
uniform float uFogDensity;
uniform float uAlphaTest;
uniform float uSkyMultiplier;
uniform float uFogHeightStart;
uniform float uFogHeightEnd;
uniform vec3 uFogColorBottom;

void main()
{
    vec4 texColor = texture(uTexture, vTexCoord);

    if (texColor.a < uAlphaTest)
        discard;

    // Combine sky brightness (affected by time of day) with block brightness
    float bri = max(vSkyBri * uSkyMultiplier, vBlockBri);

    // Apply ambient occlusion
    bri *= vAO;

    // Apply minimum brightness (ensures some visibility even in total darkness)
    bri = 0.05 + 0.95 * bri;

    vec3 color = texColor.rgb * vColor * bri;

    // Height-based exponential-squared fog
    float heightFactor = 1.0 - smoothstep(uFogHeightStart, uFogHeightEnd, vWorldPos.y);
    float density = uFogDensity * (0.3 + 0.7 * heightFactor);
    float fogFactor = exp(-density * density * vFogDistance * vFogDistance);
    fogFactor = clamp(fogFactor, 0.0, 1.0);

    // Blend fog color by fragment height (warmer near ground)
    float colorBlend = smoothstep(uFogHeightStart - 20.0, uFogHeightEnd, vWorldPos.y);
    vec3 finalFogColor = mix(uFogColorBottom, uFogColor, colorBlend);
    color = mix(finalFogColor, color, fogFactor);

    FragColor = vec4(color, texColor.a);
}
