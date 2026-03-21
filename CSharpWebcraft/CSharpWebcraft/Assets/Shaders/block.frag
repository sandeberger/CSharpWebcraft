#version 330 core

in vec3 vColor;
in vec2 vTexCoord;
in vec3 vNormal;
in float vFogDistance;
in vec3 vWorldPos;

out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec3 uFogColor;
uniform float uFogDensity;
uniform float uAlphaTest;

void main()
{
    vec4 texColor = texture(uTexture, vTexCoord);

    if (texColor.a < uAlphaTest)
        discard;

    vec3 color = texColor.rgb * vColor;

    // Exponential fog
    float fogFactor = exp(-uFogDensity * vFogDistance);
    fogFactor = clamp(fogFactor, 0.0, 1.0);
    color = mix(uFogColor, color, fogFactor);

    FragColor = vec4(color, texColor.a);
}
