#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUV;
layout(location = 2) in float aWaveSeed;

out vec2 vUV;
out float vWaveSeed;
out float vElevation;

uniform mat4 uView;
uniform mat4 uProjection;
uniform float uTime;

void main()
{
    // Animate the curtain: vertical ripple along the band
    vec3 pos = aPosition;
    float wave = sin(aWaveSeed * 6.2831 + uTime * 0.8) * 0.02;
    pos += normalize(pos) * wave;

    vUV = aUV;
    vWaveSeed = aWaveSeed;
    vElevation = asin(clamp(normalize(pos).y, -1.0, 1.0));

    // Sky dome projection: strip translation, pin to far plane
    mat4 viewNoTranslation = mat4(mat3(uView));
    vec4 clipPos = uProjection * viewNoTranslation * vec4(pos, 1.0);
    gl_Position = clipPos.xyww;
}
