#version 330 core

in vec3 vPosition;

out vec4 FragColor;

uniform vec3 uTopColor;
uniform vec3 uBottomColor;
uniform vec3 uSunDirection;
uniform float uSunGlow;
uniform vec3 uMoonDirection;
uniform float uMoonGlow;

void main()
{
    vec3 dir = normalize(vPosition);
    float cosTheta = dir.y;

    // Non-linear gradient: more sky color concentrated at zenith
    float t = pow(cosTheta * 0.5 + 0.5, 1.5);
    vec3 skyColor = mix(uBottomColor, uTopColor, t);

    // Horizon glow: subtle warm tint near the horizon
    float horizonBlend = pow(1.0 - abs(cosTheta), 4.0);
    vec3 horizonTint = mix(uBottomColor, uTopColor * vec3(1.2, 0.95, 0.8), 0.3);
    skyColor = mix(skyColor, horizonTint, horizonBlend * 0.5);

    // Sun glow
    float sunDot = max(dot(dir, uSunDirection), 0.0);
    float sunGlow = pow(sunDot, 32.0) * uSunGlow;
    // Wider, softer halo around sun
    float sunHalo = pow(sunDot, 8.0) * uSunGlow * 0.15;
    skyColor += vec3(1.0, 0.9, 0.7) * sunGlow;
    skyColor += vec3(1.0, 0.85, 0.6) * sunHalo;

    // Moon glow
    float moonDot = max(dot(dir, uMoonDirection), 0.0);
    float moonCore = pow(moonDot, 48.0) * uMoonGlow;
    float moonHalo = pow(moonDot, 10.0) * uMoonGlow * 0.10;
    skyColor += vec3(0.8, 0.85, 1.0) * moonCore;
    skyColor += vec3(0.7, 0.8, 1.0) * moonHalo;

    FragColor = vec4(skyColor, 1.0);
}
