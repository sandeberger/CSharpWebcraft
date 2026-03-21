#version 330 core

layout(location = 0) in vec3 aPosition;

uniform mat4 uView;
uniform mat4 uProjection;
uniform float uPointSize;

void main()
{
    vec4 viewPos = uView * vec4(aPosition, 1.0);
    gl_Position = uProjection * viewPos;
    float dist = length(viewPos.xyz);
    gl_PointSize = max(1.0, uPointSize * 80.0 / dist);
}
