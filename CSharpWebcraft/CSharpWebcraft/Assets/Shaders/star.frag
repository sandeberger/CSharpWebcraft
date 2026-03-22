#version 330 core

uniform float uOpacity;
uniform vec3 uStarColor;

out vec4 FragColor;

void main()
{
    // Soft circular point sprite
    vec2 coord = gl_PointCoord - vec2(0.5);
    float dist = length(coord);
    if (dist > 0.5)
        discard;
    float alpha = uOpacity * (1.0 - smoothstep(0.2, 0.5, dist));
    FragColor = vec4(uStarColor, alpha);
}
