#version 330 core

layout(location = 0) in vec3 aPosition;

uniform mat4 uView;
uniform mat4 uProjection;
uniform float uStarSize;

void main()
{
    mat4 viewNoTranslation = mat4(mat3(uView));
    vec4 pos = uProjection * viewNoTranslation * vec4(aPosition, 1.0);
    gl_Position = pos.xyww;
    gl_PointSize = uStarSize;
}
