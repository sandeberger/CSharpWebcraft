#version 330 core

in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uSceneTex;
uniform float uThreshold;

void main()
{
    vec3 color = texture(uSceneTex, vTexCoord).rgb;
    float brightness = dot(color, vec3(0.2126, 0.7152, 0.0722));

    // Soft knee: gradual ramp starting below threshold for smoother bloom
    float knee = uThreshold * 0.7;
    float softness = uThreshold - knee;
    float excess = brightness - knee;

    if (excess > 0.0)
    {
        float contribution;
        if (brightness < uThreshold)
        {
            // Quadratic ease-in from knee to threshold
            contribution = excess * excess / (4.0 * softness + 0.0001);
        }
        else
        {
            // Linear above threshold
            contribution = brightness - uThreshold + softness * 0.25;
        }
        FragColor = vec4(color * (contribution / brightness), 1.0);
    }
    else
    {
        FragColor = vec4(0.0, 0.0, 0.0, 1.0);
    }
}
