#version 330 core

in vec2 vTexCoord;
in vec4 vColor;

uniform sampler2D uTexture;
uniform int uUseTexture;

out vec4 FragColor;

void main()
{
    if (uUseTexture == 1)
    {
        vec4 texColor = texture(uTexture, vTexCoord);
        if (texColor.a < 0.01) discard;
        FragColor = texColor * vColor;
    }
    else
    {
        FragColor = vColor;
    }
}
