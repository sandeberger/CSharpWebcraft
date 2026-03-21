#version 330 core

in vec3 vPosition;

out vec4 FragColor;

uniform vec3 uTopColor;
uniform vec3 uBottomColor;
uniform vec3 uSunDirection;
uniform float uSunGlow;

void main()
{
    vec3 dir = normalize(vPosition);
    float t = dir.y * 0.5 + 0.5;
    vec3 skyColor = mix(uBottomColor, uTopColor, t);

    float sunDot = max(dot(dir, uSunDirection), 0.0);
    float sunGlow = pow(sunDot, 32.0) * uSunGlow;
    skyColor += vec3(1.0, 0.9, 0.7) * sunGlow;

    FragColor = vec4(skyColor, 1.0);
}
