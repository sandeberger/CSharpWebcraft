#version 330 core

layout(location = 0) in vec3 aPosition;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec2 uOffset;
uniform float uCloudScale;

out vec2 vTexCoord;
out float vFogDistance;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vec4 viewPos = uView * worldPos;
    gl_Position = uProjection * viewPos;
    vTexCoord = worldPos.xz / uCloudScale + uOffset;
    vFogDistance = length(viewPos.xyz);
}
