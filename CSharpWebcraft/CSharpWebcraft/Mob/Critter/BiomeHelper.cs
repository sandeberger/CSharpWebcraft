using CSharpWebcraft.Noise;

namespace CSharpWebcraft.Mob.Critter;

/// <summary>
/// Lightweight biome query for critter spawning.
/// Replicates noise sampling from TerrainGenerator without blending.
/// </summary>
public static class BiomeHelper
{
    private static readonly (string Name, double TempCenter, double MoistCenter)[] BiomeCenters =
    {
        ("desert",    0.85, 0.15),
        ("savanna",   0.75, 0.35),
        ("plains",    0.50, 0.30),
        ("forest",    0.40, 0.72),
        ("swamp",     0.58, 0.88),
        ("hills",     0.55, 0.50),
        ("mountains", 0.22, 0.55),
        ("tundra",    0.12, 0.30),
        ("valleys",   0.42, 0.55),
        ("lakes",     0.68, 0.82),
    };

    public static string GetBiomeAt(SimplexNoise noise, int wx, int wz)
    {
        double tempNoise = (noise.Noise2D(wx / 500.0, wz / 500.0) + 1) / 2;
        double moistNoise = (noise.Noise2D(wx / 400.0 + 1000, wz / 400.0 + 1000) + 1) / 2;

        string nearest = "plains";
        double minDist = double.MaxValue;

        foreach (var (name, tc, mc) in BiomeCenters)
        {
            double dt = tempNoise - tc;
            double dm = moistNoise - mc;
            double d = dt * dt + dm * dm;
            if (d < minDist)
            {
                minDist = d;
                nearest = name;
            }
        }

        return nearest;
    }
}
