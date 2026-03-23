using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using CSharpWebcraft.Input;
using CSharpWebcraft.Mob;
using CSharpWebcraft.Mob.Critter;
using CSharpWebcraft.Noise;
using CSharpWebcraft.Player;
using CSharpWebcraft.Weather;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Audio;

/// <summary>
/// Sound effects orchestrator. Manages all game-logic sound triggers,
/// mirroring the JavaScript SFX module.
/// </summary>
public class SfxSystem : IDisposable
{
    private readonly AudioManager _audio;
    private readonly WorldManager _world;
    private readonly SimplexNoise _noise;
    private readonly Random _random = new();

    // Active named/looping sounds (mirrors JS activeSources Map)
    private readonly Dictionary<string, SoundEffect> _activeSounds = new();

    // All one-shot sounds being tracked for fade/delay updates
    private readonly List<SoundEffect> _activeOneShots = new();

    // --- Footstep state ---
    private float _footstepTimer;
    private const float FootstepInterval = 0.4f;

    // --- Landing detection ---
    private bool _wasOnGround = true;
    private float _landCooldown;
    private const float LandCooldownTime = 0.4f;

    // --- Jump cooldown ---
    private float _jumpCooldown;
    private const float JumpCooldownTime = 0.3f;

    // --- Splash detection ---
    private bool _wasUnderwater;
    private float _splashCooldown;
    private const float SplashCooldownTime = 0.5f;

    // --- Bird ambient ---
    private float _birdTimer;
    private float _nextBirdTime;
    private static readonly HashSet<string> BirdBiomes = new() { "forest", "plains", "swamp" };

    // --- Rain ambient ---
    private bool _rainActive;

    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!_enabled) StopAll();
        }
    }

    public SfxSystem(AudioManager audio, WorldManager world, SimplexNoise noise)
    {
        _audio = audio;
        _world = world;
        _noise = noise;
        _nextBirdTime = 8f + (float)_random.NextDouble() * 17f;
    }

    // --- Core playback API ---

    public SoundEffect? Play(string name, float volume = 1f, bool loop = false,
        float fadeIn = 0f, float delay = 0f)
    {
        if (!_enabled || !_audio.IsInitialized) return null;

        var sfx = _audio.Play(name, volume, loop, fadeIn, delay);
        if (sfx == null) return null;

        if (loop)
        {
            // Stop previous loop of same name
            Stop(name, 0.1f);
            _activeSounds[name] = sfx;
        }
        else
        {
            _activeOneShots.Add(sfx);
        }

        return sfx;
    }

    public void Stop(string name, float fadeOut = 0.5f)
    {
        if (_activeSounds.TryGetValue(name, out var sfx))
        {
            sfx.Stop(fadeOut);
            _activeSounds.Remove(name);
        }
    }

    public void PlayDistanced(string name, Vector3 soundPos, Vector3 listenerPos,
        float volume = 0.5f)
    {
        float dx = listenerPos.X - soundPos.X;
        float dy = listenerPos.Y - soundPos.Y;
        float dz = listenerPos.Z - soundPos.Z;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        if (dist > 20f) return;

        // Linear fade: full volume at 5 blocks, silent at 20 blocks
        float distVolume = MathF.Max(0f, 1f - (dist - 5f) / 15f);
        Play(name, volume: volume * distVolume);
    }

    // --- Direct trigger methods (called from events) ---

    public void PlayJump()
    {
        if (_jumpCooldown <= 0)
        {
            Play("jump", volume: 0.4f);
            _jumpCooldown = JumpCooldownTime;
        }
    }

    public void PlayHurt() => Play("hurt", volume: 0.6f);
    public void PlayBlockBreak() => Play("footstep_stone", volume: 0.6f);
    public void PlayBlockPlace() => Play("footstep_grass", volume: 0.5f);

    public void PlayThunder(float delay)
    {
        float vol = 0.6f + (float)_random.NextDouble() * 0.3f;
        Play("thunder", volume: vol, delay: delay);
    }

    // --- Per-frame update ---

    public void Update(float dt, Vector3 cameraPos, PlayerPhysics physics,
        InputManager input, float gameHour, WeatherSystem weather,
        IReadOnlyList<MobBase> mobs)
    {
        if (!_enabled || !_audio.IsInitialized) return;

        // Tick cooldowns
        _landCooldown = MathF.Max(0, _landCooldown - dt);
        _jumpCooldown = MathF.Max(0, _jumpCooldown - dt);
        _splashCooldown = MathF.Max(0, _splashCooldown - dt);

        UpdateFootsteps(dt, cameraPos, physics, input);
        UpdateLanding(physics);
        UpdateSplash(physics);
        UpdateBirds(dt, cameraPos, gameHour, physics);
        UpdateRain(dt, weather);
        UpdateMobSounds(dt, mobs, cameraPos);
        UpdateActiveSounds(dt);
    }

    // --- Footsteps ---

    private void UpdateFootsteps(float dt, Vector3 cameraPos, PlayerPhysics physics, InputManager input)
    {
        bool isMoving = input.IsKeyDown(Keys.W) || input.IsKeyDown(Keys.A) ||
                        input.IsKeyDown(Keys.S) || input.IsKeyDown(Keys.D);

        if (!isMoving || !physics.IsOnGround || physics.IsUnderwater)
        {
            _footstepTimer = FootstepInterval * 0.8f;
            return;
        }

        _footstepTimer += dt;
        if (_footstepTimer >= FootstepInterval)
        {
            _footstepTimer = 0;

            // Determine block under feet
            int feetY = (int)MathF.Floor(cameraPos.Y - 1.8f);
            byte blockType = _world.GetBlockAt(
                (int)MathF.Floor(cameraPos.X),
                feetY,
                (int)MathF.Floor(cameraPos.Z));

            string soundName = blockType switch
            {
                5 => "footstep_sand",                    // Sand
                3 or 17 or 23 => "footstep_stone",      // Stone, Sandstone, MossyStone
                _ => "footstep_grass"
            };

            float vol = 0.5f + (float)_random.NextDouble() * 0.2f;
            Play(soundName, volume: vol);
        }
    }

    // --- Landing ---

    private void UpdateLanding(PlayerPhysics physics)
    {
        if (physics.IsOnGround && !_wasOnGround && !physics.IsUnderwater && _landCooldown <= 0)
        {
            Play("land", volume: 0.4f);
            _landCooldown = LandCooldownTime;
        }
        _wasOnGround = physics.IsOnGround;
    }

    // --- Splash ---

    private void UpdateSplash(PlayerPhysics physics)
    {
        if (physics.IsUnderwater && !_wasUnderwater && _splashCooldown <= 0)
        {
            Play("splash", volume: 0.5f);
            _splashCooldown = SplashCooldownTime;
        }
        _wasUnderwater = physics.IsUnderwater;
    }

    // --- Bird ambient ---

    private void UpdateBirds(float dt, Vector3 cameraPos, float gameHour, PlayerPhysics physics)
    {
        if (gameHour < 6f || gameHour > 19f) return;
        if (physics.IsUnderwater) return;

        _birdTimer += dt;
        if (_birdTimer < _nextBirdTime) return;

        _birdTimer = 0;
        _nextBirdTime = 8f + (float)_random.NextDouble() * 17f;

        // Check biome
        string biome = BiomeHelper.GetBiomeAt(_noise,
            (int)MathF.Floor(cameraPos.X),
            (int)MathF.Floor(cameraPos.Z));
        if (!BirdBiomes.Contains(biome)) return;

        float vol = 0.15f + (float)_random.NextDouble() * 0.15f;
        Play("bird_chirp", volume: vol);
    }

    // --- Rain ambient ---

    private void UpdateRain(float dt, WeatherSystem weather)
    {
        float intensity = weather.Precipitation;

        if (intensity > 0.2f && !_rainActive)
        {
            _rainActive = true;
            Play("rain_light", volume: intensity * 0.6f, loop: true, fadeIn: 2f);
        }
        else if (intensity < 0.1f && _rainActive)
        {
            _rainActive = false;
            Stop("rain_light", fadeOut: 3f);
        }
        else if (_rainActive)
        {
            // Adjust rain volume based on intensity
            if (_activeSounds.TryGetValue("rain_light", out var rain))
                rain.SetVolumeSmooth(intensity * 0.6f, 0.5f);
        }
    }

    // --- Mob idle sounds ---

    private void UpdateMobSounds(float dt, IReadOnlyList<MobBase> mobs, Vector3 cameraPos)
    {
        for (int i = 0; i < mobs.Count; i++)
        {
            var mob = mobs[i];
            if (mob.IdleSoundName == null || !mob.IsAlive) continue;

            mob.IdleSoundTimer -= dt;
            if (mob.IdleSoundTimer <= 0)
            {
                mob.IdleSoundTimer = 5f + (float)_random.NextDouble() * 10f;
                PlayDistanced(mob.IdleSoundName, mob.Position, cameraPos, volume: 0.4f);
            }
        }
    }

    // --- Update active sounds (tick fades/delays, clean up finished) ---

    private void UpdateActiveSounds(float dt)
    {
        // Update named/looping sounds and remove finished ones
        var finishedKeys = new List<string>();
        foreach (var kvp in _activeSounds)
        {
            kvp.Value.Update(dt);
            if (kvp.Value.IsFinished)
                finishedKeys.Add(kvp.Key);
        }
        foreach (var key in finishedKeys)
            _activeSounds.Remove(key);

        // Update and clean up one-shots
        for (int i = _activeOneShots.Count - 1; i >= 0; i--)
        {
            _activeOneShots[i].Update(dt);
            if (_activeOneShots[i].IsFinished)
                _activeOneShots.RemoveAt(i);
        }
    }

    private void StopAll()
    {
        foreach (var kvp in _activeSounds)
            kvp.Value.Stop(0.2f);
        _activeSounds.Clear();

        foreach (var sfx in _activeOneShots)
            sfx.Cleanup();
        _activeOneShots.Clear();

        _rainActive = false;
    }

    public void Dispose()
    {
        StopAll();
    }
}
