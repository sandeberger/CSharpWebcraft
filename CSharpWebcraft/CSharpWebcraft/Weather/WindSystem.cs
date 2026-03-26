using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Noise;

namespace CSharpWebcraft.Weather;

public class WindSystem
{
    public Vector2 WindDirection { get; private set; } = new(1, 0);
    public float WindStrength { get; private set; }
    public float GustFactor { get; private set; }

    private readonly SimplexNoise _noise;
    private float _time;

    public WindSystem(SimplexNoise noise)
    {
        _noise = noise;
    }

    public void Update(float dt, WeatherSystem weather)
    {
        _time += dt;

        // Wind angle rotates slowly via noise
        float angle = (float)_noise.Noise2D(_time * GameConfig.WIND_DIRECTION_CHANGE_SPEED, 100.0) * MathF.PI * 2f;
        WindDirection = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

        // Base strength from separate noise domain
        float baseStrength = ((float)_noise.Noise2D(_time * 0.03, 200.0) + 1f) * 0.5f;
        baseStrength = baseStrength * 0.25f + GameConfig.WIND_BASE_STRENGTH;

        // Weather boost
        float weatherBoost = weather.Precipitation;
        if (weather.IsStormActive) weatherBoost = MathF.Max(weatherBoost, 0.8f);
        WindStrength = MathF.Min(baseStrength + weatherBoost * GameConfig.WIND_STORM_BOOST, 1.0f);

        // Gusts: higher frequency noise with shorter peaks
        GustFactor = MathF.Max(0, (float)_noise.Noise2D(_time * GameConfig.WIND_GUST_FREQUENCY, 300.0)) * WindStrength;
    }
}
