#version 330 core

in vec3 vColor;
in float vRotation;
in float vLife;

out vec4 FragColor;

void main()
{
    // Rotate point coord around center
    vec2 pc = gl_PointCoord - 0.5;
    float c = cos(vRotation), s = sin(vRotation);
    vec2 rotated = vec2(c * pc.x - s * pc.y, s * pc.x + c * pc.y);

    // Leaf shape: elongated diamond
    float shape = abs(rotated.x) * 1.5 + abs(rotated.y);
    if (shape > 0.4) discard;

    // Fade out as life approaches 1.0
    float alpha = 1.0 - smoothstep(0.7, 1.0, vLife);
    alpha *= (1.0 - shape * 2.0); // softer edges

    FragColor = vec4(vColor, alpha * 0.85);
}
