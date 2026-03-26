#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aColor;
layout(location = 2) in float aRotation;
layout(location = 3) in float aLife;

out vec3 vColor;
out float vRotation;
out float vLife;

uniform mat4 uView;
uniform mat4 uProjection;
uniform float uPointSize;

void main()
{
    vec4 viewPos = uView * vec4(aPosition, 1.0);
    gl_Position = uProjection * viewPos;
    float dist = length(viewPos.xyz);
    gl_PointSize = max(1.0, uPointSize * 120.0 / dist);
    vColor = aColor;
    vRotation = aRotation;
    vLife = aLife;
}
