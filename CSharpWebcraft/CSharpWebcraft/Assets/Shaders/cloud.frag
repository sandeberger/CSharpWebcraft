#version 330 core

in vec2 vTexCoord;
in float vFogDistance;

uniform sampler2D uTexture;
uniform vec3 uCloudColor;
uniform float uCloudOpacity;
uniform vec3 uFogColor;
uniform float uFogDensity;

out vec4 FragColor;

void main()
{
    vec4 texColor = texture(uTexture, vTexCoord);
    float alpha = texColor.a * uCloudOpacity;
    if (alpha < 0.01) discard;

    vec3 color = uCloudColor * texColor.rgb;

    float fogFactor = clamp(exp(-uFogDensity * vFogDistance), 0.0, 1.0);
    color = mix(uFogColor, color, fogFactor);

    FragColor = vec4(color, alpha);
}
