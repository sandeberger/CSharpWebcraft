#version 330 core

in vec3 vColor;
in vec2 vTexCoord;
in vec3 vNormal;
in float vFogDistance;
in vec3 vWorldPos;
in float vSkyBri;
in float vBlockBri;
in float vAO;

out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec3 uFogColor;
uniform float uFogDensity;
uniform float uAlphaTest;
uniform float uSkyMultiplier;
uniform float uFogHeightStart;
uniform float uFogHeightEnd;
uniform vec3 uFogColorBottom;

// Water PBR uniforms
uniform int uWaterPass;
uniform float uTime;
uniform vec3 uCameraPos;
uniform vec3 uSunDirection;

// --- Water helper functions ---
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

float fbm(vec2 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 3; i++)
    {
        value += amplitude * noise(p);
        p *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}

void main()
{
    vec4 texColor = texture(uTexture, vTexCoord);

    if (texColor.a < uAlphaTest)
        discard;

    // Combine sky brightness (affected by time of day) with block brightness
    float bri = max(vSkyBri * uSkyMultiplier, vBlockBri);

    // Apply ambient occlusion - reduced on emissive blocks to preserve HDR bloom
    float ao = vAO;
    if (vBlockBri > 1.0)
        ao = mix(ao, 1.0, 0.85);
    bri *= ao;

    // Apply minimum brightness (ensures some visibility even in total darkness)
    bri = 0.05 + 0.95 * bri;

    vec3 color = texColor.rgb * vColor * bri;

    // --- Water PBR effects ---
    if (uWaterPass > 0)
    {
        vec3 viewDir = normalize(uCameraPos - vWorldPos);

        // How much this face points upward (wave animation on top surfaces only)
        float upFacing = max(dot(vNormal, vec3(0.0, 1.0, 0.0)), 0.0);

        // Animated wave normals (two scrolling noise layers)
        vec2 waveUV1 = vWorldPos.xz * 0.8 + uTime * vec2(0.04, 0.025);
        vec2 waveUV2 = vWorldPos.xz * 0.4 + uTime * vec2(-0.025, 0.035);
        float wave1 = fbm(waveUV1);
        float wave2 = fbm(waveUV2);

        // Perturbed normal from waves (blend with face normal based on upFacing)
        float waveStrength = 0.2 * upFacing;
        vec3 waterNormal = normalize(vec3(
            (wave1 - 0.5) * waveStrength,
            1.0,
            (wave2 - 0.5) * waveStrength
        ));
        vec3 surfaceNormal = mix(vNormal, waterNormal, upFacing);

        // Fresnel effect (Schlick approximation) - water IOR ~1.33, F0 ~ 0.02
        float cosTheta = max(dot(viewDir, surfaceNormal), 0.0);
        float F0 = 0.02;
        float fresnel = F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);

        // Sky reflection color (use fog color as sky proxy, slightly brightened)
        vec3 skyReflection = uFogColor * 1.3;

        // Blend water color with sky reflection based on Fresnel
        color = mix(color, skyReflection, fresnel * 0.6);

        // Specular sun highlight (only when sun is above horizon)
        if (uSunDirection.y > 0.05)
        {
            vec3 halfDir = normalize(uSunDirection + viewDir);
            float specAngle = max(dot(surfaceNormal, halfDir), 0.0);
            float specular = pow(specAngle, 256.0) * 2.5;
            vec3 sunColor = vec3(1.0, 0.95, 0.8);
            color += sunColor * specular * uSkyMultiplier;
        }

        // Subtle wave shimmer (caustic-like brightness variation)
        float shimmer = 0.95 + 0.1 * (wave1 + wave2) * upFacing;
        color *= shimmer;
    }

    // Height-based exponential-squared fog
    float heightFactor = 1.0 - smoothstep(uFogHeightStart, uFogHeightEnd, vWorldPos.y);
    float density = uFogDensity * (0.3 + 0.7 * heightFactor);
    float fogFactor = exp(-density * density * vFogDistance * vFogDistance);
    fogFactor = clamp(fogFactor, 0.0, 1.0);

    // Blend fog color by fragment height (warmer near ground)
    float colorBlend = smoothstep(uFogHeightStart - 20.0, uFogHeightEnd, vWorldPos.y);
    vec3 finalFogColor = mix(uFogColorBottom, uFogColor, colorBlend);
    color = mix(finalFogColor, color, fogFactor);

    FragColor = vec4(color, texColor.a);
}
