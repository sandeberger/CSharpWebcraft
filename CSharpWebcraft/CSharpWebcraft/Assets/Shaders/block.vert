#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec3 aColor;
layout(location = 3) in vec2 aTexCoord;
layout(location = 4) in float aSkyBri;
layout(location = 5) in float aBlockBri;
layout(location = 6) in float aAO;

out vec3 vColor;
out vec2 vTexCoord;
out vec3 vNormal;
out float vFogDistance;
out vec3 vWorldPos;
out float vSkyBri;
out float vBlockBri;
out float vAO;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vec4 viewPos = uView * worldPos;
    gl_Position = uProjection * viewPos;

    vColor = aColor;
    vTexCoord = aTexCoord;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vFogDistance = length(viewPos.xyz);
    vWorldPos = worldPos.xyz;
    vSkyBri = aSkyBri;
    vBlockBri = aBlockBri;
    vAO = aAO;
}
