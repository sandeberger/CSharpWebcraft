#version 330 core

uniform float uOpacity;

out vec4 FragColor;

void main()
{
    FragColor = vec4(0.63, 0.77, 1.0, uOpacity);
}
