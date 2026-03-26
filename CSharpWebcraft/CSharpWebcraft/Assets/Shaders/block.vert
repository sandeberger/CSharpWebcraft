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

// Wind sway uniforms (billboard pass only)
uniform int uBillboardPass;
uniform float uTime;
uniform vec2 uWindDirection;
uniform float uWindStrength;
uniform float uGustFactor;

float hash21(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);

    // Wind sway for billboard blocks (grass, flowers, leaves, vines)
    // aNormal.y encodes sway weight (0=anchored, 1=sways)
    // Flat billboards have aNormal.x == 0, excluded by the abs check
    if (uBillboardPass > 0 && abs(aNormal.x) > 0.1)
    {
        float swayWeight = aNormal.y;

        // Per-block phase offset from world position
        vec2 blockPos = floor(worldPos.xz);
        float phaseOffset = hash21(blockPos) * 6.2831;

        // Multi-frequency sway for natural look
        float sway1 = sin(uTime * 1.8 + phaseOffset) * 0.5;
        float sway2 = sin(uTime * 3.1 + phaseOffset * 1.7) * 0.25;
        float sway3 = valueNoise(blockPos * 0.5 + uTime * 0.3) * 0.25;
        float totalSway = sway1 + sway2 + sway3;

        // Gust contribution
        float gustSway = sin(uTime * 5.0 + phaseOffset * 2.3) * uGustFactor;

        // Displacement along wind direction + some perpendicular movement
        float amplitude = (0.08 + uWindStrength * 0.15 + gustSway * 0.1) * swayWeight;
        vec2 perpDir = vec2(-uWindDirection.y, uWindDirection.x);

        worldPos.x += (uWindDirection.x * totalSway + perpDir.x * sway2 * 0.3) * amplitude;
        worldPos.z += (uWindDirection.y * totalSway + perpDir.y * sway2 * 0.3) * amplitude;
        // Slight vertical compression at max sway
        worldPos.y -= abs(totalSway) * amplitude * 0.15;
    }

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
