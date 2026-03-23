using OpenTK.Mathematics;
using MH = CSharpWebcraft.Utils.MathHelper;

namespace CSharpWebcraft.Weather;

public enum WeatherType { Clear, Rain, Storm }
public enum WeatherPhase { Idle, Approaching, Active, Clearing }

public class WeatherSystem
{
    // Current interpolated state
    public float Gloom { get; private set; }
    public float Precipitation { get; private set; }
    public float AmbientScale { get; private set; } = 1f;
    public float DirectionalScale { get; private set; } = 1f;
    public float FogDensityOffset { get; private set; }
    public float CloudOpacityBoost { get; private set; }
    public float StarVisibility { get; private set; } = 1f;
    public Vector3 SkyTint { get; private set; }
    public Vector3 CloudTint { get; private set; } = Vector3.One;
    public Vector3 FogTint { get; private set; } = Vector3.One;
    public float LightningStrength { get; private set; }
    public bool IsStormActive => _current == WeatherType.Storm && _phase == WeatherPhase.Active;

    public WeatherType CurrentWeather => _current;
    public WeatherPhase Phase => _phase;

    // Audio event: fires with thunder delay in seconds
    public Action<float>? OnLightning;

    // State
    private WeatherType _current = WeatherType.Clear;
    private WeatherType _target = WeatherType.Clear;
    private WeatherPhase _phase = WeatherPhase.Idle;

    // Transition
    private float _transitionTimer;
    private float _transitionDuration;
    private WeatherPreset _startPreset;
    private WeatherPreset _targetPreset;

    // Auto-weather
    private float _autoTimer;
    private float _nextEventTime;
    private float _activeTimer;
    private float _activeDuration;
    private readonly Random _random = new();

    // Lightning
    private float _lightningCooldown;

    private const float APPROACH_DURATION = 12f;
    private const float CLEAR_DURATION = 10f;
    private const float PRECIP_START = 0.35f;

    // Presets
    private static readonly WeatherPreset Clear = new(0, 0, 1f, 1f, 0, 1f, 0,
        0x000000, 0xFFFFFF, 0xFFFFFF);
    private static readonly WeatherPreset RainPreset = new(0.6f, 0.7f, 0.78f, 0.68f, 0.01f, 0.35f, 0.18f,
        0x404C5A, 0x6F7C8F, 0x596579);
    private static readonly WeatherPreset StormPreset = new(0.85f, 1f, 0.62f, 0.45f, 0.018f, 0.15f, 0.28f,
        0x27303A, 0x4A5260, 0x39414D);

    public WeatherSystem()
    {
        _startPreset = Clear;
        _targetPreset = Clear;
        _nextEventTime = RandomRange(60f, 150f);
    }

    public void Update(float dt)
    {
        switch (_phase)
        {
            case WeatherPhase.Idle:
                UpdateIdle(dt);
                break;
            case WeatherPhase.Approaching:
                UpdateTransition(dt, APPROACH_DURATION);
                if (_transitionTimer >= _transitionDuration)
                {
                    _phase = WeatherPhase.Active;
                    _activeTimer = 0;
                    _activeDuration = RandomRange(45f, 90f);
                    _lightningCooldown = RandomRange(4f, 9f);
                }
                break;
            case WeatherPhase.Active:
                UpdateActive(dt);
                break;
            case WeatherPhase.Clearing:
                UpdateTransition(dt, CLEAR_DURATION);
                if (_transitionTimer >= _transitionDuration)
                {
                    _current = WeatherType.Clear;
                    _phase = WeatherPhase.Idle;
                    _autoTimer = 0;
                    _nextEventTime = RandomRange(60f, 150f);
                    SnapToPreset(Clear);
                }
                break;
        }

        // Lightning decay
        if (_current == WeatherType.Storm && _phase == WeatherPhase.Active)
        {
            _lightningCooldown -= dt;
            if (_lightningCooldown <= 0)
            {
                LightningStrength = 0.85f + (float)_random.NextDouble() * 0.15f;
                _lightningCooldown = RandomRange(4f, 9f);

                float thunderDelay = 0.3f + (float)_random.NextDouble() * 1.2f;
                OnLightning?.Invoke(thunderDelay);
            }
        }
        LightningStrength = MathF.Max(0, LightningStrength - dt * 4f);
    }

    public void RequestWeather(WeatherType type)
    {
        if (type == WeatherType.Clear)
        {
            if (_phase == WeatherPhase.Active || _phase == WeatherPhase.Approaching)
            {
                _target = WeatherType.Clear;
                _startPreset = CaptureCurrentState();
                _targetPreset = Clear;
                _transitionTimer = 0;
                _transitionDuration = CLEAR_DURATION;
                _phase = WeatherPhase.Clearing;
            }
        }
        else
        {
            _target = type;
            _current = type;
            _startPreset = CaptureCurrentState();
            _targetPreset = type == WeatherType.Rain ? RainPreset : StormPreset;
            _transitionTimer = 0;
            _transitionDuration = APPROACH_DURATION;
            _phase = WeatherPhase.Approaching;
        }
    }

    private void UpdateIdle(float dt)
    {
        // Relax toward clear
        RelaxToward(Clear, dt * 0.5f);

        // Auto-weather scheduling
        _autoTimer += dt;
        if (_autoTimer >= _nextEventTime)
        {
            if (_random.NextDouble() < 0.2)
            {
                var type = _random.NextDouble() < 0.67 ? WeatherType.Rain : WeatherType.Storm;
                RequestWeather(type);
            }
            else
            {
                _autoTimer = 0;
                _nextEventTime = RandomRange(60f, 150f);
            }
        }
    }

    private void UpdateActive(float dt)
    {
        var target = _current == WeatherType.Rain ? RainPreset : StormPreset;
        RelaxToward(target, dt * 0.5f);

        _activeTimer += dt;
        if (_activeTimer >= _activeDuration)
            RequestWeather(WeatherType.Clear);
    }

    private void UpdateTransition(float dt, float duration)
    {
        _transitionTimer += dt;
        _transitionDuration = duration;
        float progress = MathF.Min(_transitionTimer / duration, 1f);
        float eased = Smoothstep(progress);

        Gloom = MH.Lerp(_startPreset.Gloom, _targetPreset.Gloom, eased);
        AmbientScale = MH.Lerp(_startPreset.AmbientScale, _targetPreset.AmbientScale, eased);
        DirectionalScale = MH.Lerp(_startPreset.DirectionalScale, _targetPreset.DirectionalScale, eased);
        FogDensityOffset = MH.Lerp(_startPreset.FogDensityOffset, _targetPreset.FogDensityOffset, eased);
        CloudOpacityBoost = MH.Lerp(_startPreset.CloudOpacityBoost, _targetPreset.CloudOpacityBoost, eased);
        StarVisibility = MH.Lerp(_startPreset.StarVisibility, _targetPreset.StarVisibility, eased);

        SkyTint = MH.LerpColor(_startPreset.SkyTintV, _targetPreset.SkyTintV, eased);
        CloudTint = MH.LerpColor(_startPreset.CloudTintV, _targetPreset.CloudTintV, eased);
        FogTint = MH.LerpColor(_startPreset.FogTintV, _targetPreset.FogTintV, eased);

        // Precipitation starts later in the transition
        float precipProgress = MathF.Max(0, (progress - PRECIP_START) / (1f - PRECIP_START));
        Precipitation = MH.Lerp(_startPreset.Precipitation, _targetPreset.Precipitation, Smoothstep(precipProgress));
    }

    private void RelaxToward(WeatherPreset target, float t)
    {
        t = MathF.Min(t, 0.25f);
        Gloom = MH.Lerp(Gloom, target.Gloom, t);
        Precipitation = MH.Lerp(Precipitation, target.Precipitation, t);
        AmbientScale = MH.Lerp(AmbientScale, target.AmbientScale, t);
        DirectionalScale = MH.Lerp(DirectionalScale, target.DirectionalScale, t);
        FogDensityOffset = MH.Lerp(FogDensityOffset, target.FogDensityOffset, t);
        CloudOpacityBoost = MH.Lerp(CloudOpacityBoost, target.CloudOpacityBoost, t);
        StarVisibility = MH.Lerp(StarVisibility, target.StarVisibility, t);
        SkyTint = MH.LerpColor(SkyTint, target.SkyTintV, t);
        CloudTint = MH.LerpColor(CloudTint, target.CloudTintV, t);
        FogTint = MH.LerpColor(FogTint, target.FogTintV, t);
    }

    private void SnapToPreset(WeatherPreset p)
    {
        Gloom = p.Gloom; Precipitation = p.Precipitation;
        AmbientScale = p.AmbientScale; DirectionalScale = p.DirectionalScale;
        FogDensityOffset = p.FogDensityOffset; CloudOpacityBoost = p.CloudOpacityBoost;
        StarVisibility = p.StarVisibility;
        SkyTint = p.SkyTintV; CloudTint = p.CloudTintV; FogTint = p.FogTintV;
    }

    private WeatherPreset CaptureCurrentState() => new(
        Gloom, Precipitation, AmbientScale, DirectionalScale,
        FogDensityOffset, StarVisibility, CloudOpacityBoost,
        SkyTint, CloudTint, FogTint);

    private float RandomRange(float min, float max) => min + (float)_random.NextDouble() * (max - min);
    private static float Smoothstep(float t) => t * t * (3f - 2f * t);

    private readonly record struct WeatherPreset(
        float Gloom, float Precipitation, float AmbientScale, float DirectionalScale,
        float FogDensityOffset, float StarVisibility, float CloudOpacityBoost,
        Vector3 SkyTintV, Vector3 CloudTintV, Vector3 FogTintV)
    {
        public WeatherPreset(float gloom, float precipitation, float ambientScale, float directionalScale,
            float fogDensityOffset, float starVisibility, float cloudOpacityBoost,
            uint skyTint, uint cloudTint, uint fogTint)
            : this(gloom, precipitation, ambientScale, directionalScale,
                fogDensityOffset, starVisibility, cloudOpacityBoost,
                MH.ColorFromHex(skyTint), MH.ColorFromHex(cloudTint), MH.ColorFromHex(fogTint))
        {
        }
    }
}
