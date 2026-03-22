#version 330 core

in vec2 vTexCoord;
out float FragColor;

uniform sampler2D uDepthTex;
uniform sampler2D uNoiseTex;
uniform mat4 uProjection;
uniform mat4 uInvProjection;
uniform vec2 uNoiseScale;
uniform vec3 uSamples[16];
uniform float uRadius;
uniform float uBias;
uniform float uPower;

vec3 viewPosFromDepth(vec2 uv, float depth)
{
    vec4 ndc = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = uInvProjection * ndc;
    return viewPos.xyz / viewPos.w;
}

void main()
{
    float depth = texture(uDepthTex, vTexCoord).r;

    // Skip sky (at far plane)
    if (depth >= 0.9999)
    {
        FragColor = 1.0;
        return;
    }

    vec3 fragPos = viewPosFromDepth(vTexCoord, depth);

    // Reconstruct view-space normal from depth derivatives
    vec3 posDdx = dFdx(fragPos);
    vec3 posDdy = dFdy(fragPos);
    vec3 normal = normalize(cross(posDdx, posDdy));

    // Random rotation vector from tiled noise texture
    vec3 randomVec = normalize(texture(uNoiseTex, vTexCoord * uNoiseScale).xyz);

    // Gramm-Schmidt to build TBN (tangent/bitangent/normal) matrix
    vec3 tangent = normalize(randomVec - normal * dot(randomVec, normal));
    vec3 bitangent = cross(normal, tangent);
    mat3 TBN = mat3(tangent, bitangent, normal);

    float occlusion = 0.0;
    for (int i = 0; i < 16; i++)
    {
        // Orient hemisphere sample along surface normal
        vec3 samplePos = fragPos + TBN * uSamples[i] * uRadius;

        // Project sample to screen space
        vec4 proj = uProjection * vec4(samplePos, 1.0);
        vec2 sampleUV = clamp(proj.xy / proj.w * 0.5 + 0.5, 0.001, 0.999);

        // Read actual geometry depth at sample's screen position
        float sampleDepth = texture(uDepthTex, sampleUV).r;
        float sampleZ = viewPosFromDepth(sampleUV, sampleDepth).z;

        // Range check: only count occlusion from nearby geometry
        float rangeCheck = smoothstep(0.0, 1.0, uRadius / (abs(fragPos.z - sampleZ) + 0.001));

        // If actual surface is closer to camera than sample -> sample is occluded
        occlusion += step(samplePos.z + uBias, sampleZ) * rangeCheck;
    }

    FragColor = pow(1.0 - occlusion / 16.0, uPower);
}
