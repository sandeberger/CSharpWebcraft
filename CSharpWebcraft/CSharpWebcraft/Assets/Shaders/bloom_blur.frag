#version 330 core

in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uInputTex;
uniform int uHorizontal;

// 9-tap Gaussian kernel (sigma ~1.5)
const float weights[5] = float[](0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);

void main()
{
    vec2 texelSize = 1.0 / vec2(textureSize(uInputTex, 0));
    vec3 result = texture(uInputTex, vTexCoord).rgb * weights[0];

    vec2 dir = (uHorizontal == 1) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);

    for (int i = 1; i < 5; i++)
    {
        vec2 offset = dir * float(i) * texelSize;
        result += texture(uInputTex, vTexCoord + offset).rgb * weights[i];
        result += texture(uInputTex, vTexCoord - offset).rgb * weights[i];
    }

    FragColor = vec4(result, 1.0);
}
