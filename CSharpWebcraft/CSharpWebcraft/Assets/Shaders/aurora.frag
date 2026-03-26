#version 330 core

in vec2 vUV;
in float vWaveSeed;
in float vElevation;

out vec4 FragColor;

uniform float uTime;
uniform float uIntensity;

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm2(vec2 p)
{
    float v = 0.0;
    v += 0.5 * noise(p); p *= 2.0;
    v += 0.25 * noise(p);
    return v;
}

float fbm3(vec2 p)
{
    float v = 0.0;
    v += 0.5 * noise(p); p *= 2.0;
    v += 0.25 * noise(p); p *= 2.0;
    v += 0.125 * noise(p);
    return v;
}

float fbm4(vec2 p)
{
    float v = 0.0;
    v += 0.5 * noise(p); p *= 2.0;
    v += 0.25 * noise(p); p *= 2.0;
    v += 0.125 * noise(p); p *= 2.0;
    v += 0.0625 * noise(p);
    return v;
}

void main()
{
    vec2 noiseCoord = vec2(vUV.x * 4.0 + uTime * 0.05, vUV.y * 2.0);
    float density = fbm4(noiseCoord);

    float largeDensity = fbm3(vec2(vUV.x * 1.5 + uTime * 0.02, vUV.y * 0.8 + uTime * 0.01));
    density *= smoothstep(0.25, 0.55, largeDensity);

    float verticalFade = pow(1.0 - vUV.y, 1.5);
    density *= verticalFade;

    density = smoothstep(0.08, 0.40, density);

    float edgeFade = smoothstep(0.0, 0.18, vUV.x) * smoothstep(1.0, 0.82, vUV.x);
    density *= edgeFade;

    float vEdgeFade = smoothstep(0.0, 0.08, vUV.y) * smoothstep(1.0, 0.85, vUV.y);
    density *= vEdgeFade;

    vec3 greenBase = vec3(0.1, 0.9, 0.3);
    vec3 purpleTop = vec3(0.6, 0.1, 0.8);
    vec3 pinkAccent = vec3(0.9, 0.3, 0.5);

    float colorT = pow(vUV.y, 0.8);
    vec3 auroraColor = mix(greenBase, purpleTop, colorT);

    float pinkMask = fbm2(vec2(vUV.x * 3.0 + uTime * 0.08, vUV.y + uTime * 0.03));
    auroraColor = mix(auroraColor, pinkAccent, pinkMask * 0.3 * (1.0 - colorT));

    float emission = 1.0 + 0.2 * density;
    vec3 finalColor = auroraColor * emission;

    float al = density * uIntensity;

    float elevFade = smoothstep(0.15, 0.45, vElevation) * smoothstep(1.5, 0.85, vElevation);
    al *= elevFade;

    if (al < 0.003) discard;

    FragColor = vec4(finalColor, al);
}
