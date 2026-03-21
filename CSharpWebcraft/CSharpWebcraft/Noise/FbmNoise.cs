namespace CSharpWebcraft.Noise;

public static class FbmNoise
{
    public static double Calculate3D(SimplexNoise noise, double x, double y, double z,
        double scale, int octaves, double persistence, double lacunarity)
    {
        double totalValue = 0, frequency = 1.0, amplitude = 1.0, maxValue = 0;
        for (int i = 0; i < octaves; i++)
        {
            double sX = x / scale * frequency, sY = y / scale * frequency, sZ = z / scale * frequency;
            totalValue += noise.Noise3D(sX, sY, sZ) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return maxValue == 0 ? 0 : totalValue / maxValue;
    }
}
