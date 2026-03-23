using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CSharpWebcraft.Audio;

/// <summary>
/// Central audio system using NAudio. Manages WaveOut device, mixer,
/// and cached sound buffers.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private WaveOutEvent? _output;
    private MixingSampleProvider? _mixer;
    private readonly Dictionary<string, CachedSound> _sounds = new();
    private bool _initialized;

    // Standard format for all sounds
    private static readonly WaveFormat OutputFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    public bool IsInitialized => _initialized;
    public float MasterVolume { get; set; } = 0.5f;

    public void Init()
    {
        try
        {
            _mixer = new MixingSampleProvider(OutputFormat)
            {
                ReadFully = true // Output silence when no inputs (prevents stopping)
            };

            _output = new WaveOutEvent
            {
                DesiredLatency = 100
            };
            _output.Init(_mixer);
            _output.Play();

            _initialized = true;
            Console.WriteLine("Audio: NAudio initialized (WaveOut).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio: Initialization failed: {ex.Message}");
            _output?.Dispose();
            _output = null;
            _mixer = null;
        }
    }

    public void LoadAllSounds()
    {
        if (!_initialized) return;

        string basePath = AppContext.BaseDirectory;

        // SFX sounds
        string[] sfxNames =
        {
            "bird_chirp", "footstep_grass", "footstep_stone", "footstep_sand",
            "jump", "land", "splash", "thunder", "rain_light",
            "hurt", "spider_idle", "slime_idle"
        };

        foreach (string name in sfxNames)
        {
            string path = Path.Combine(basePath, "Assets", "Sound", name + ".mp3");
            LoadSound(name, path);
        }

        // Music
        string musicPath = Path.Combine(basePath, "Assets", "Music", "Echoes.mp3");
        LoadSound("Echoes", musicPath);
    }

    public bool LoadSound(string name, string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Audio: Sound file not found: {filePath}");
            return false;
        }

        try
        {
            var cached = CachedSound.LoadFromFile(filePath, OutputFormat);
            _sounds[name] = cached;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio: Failed to load {name}: {ex.Message}");
            return false;
        }
    }

    public bool HasSound(string name) => _sounds.ContainsKey(name);

    /// <summary>
    /// Play a cached sound. Returns the SoundEffect for control, or null if unavailable.
    /// </summary>
    public SoundEffect? Play(string name, float volume = 1f, bool loop = false,
        float fadeIn = 0f, float delay = 0f)
    {
        if (!_initialized || _mixer == null) return null;
        if (!_sounds.TryGetValue(name, out var cached)) return null;

        float effectiveVolume = volume * MasterVolume;
        var sfx = new SoundEffect(cached.AudioData, OutputFormat, name,
            effectiveVolume, loop, fadeIn, delay);
        _mixer.AddMixerInput(sfx);
        return sfx;
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _mixer = null;
        _sounds.Clear();
        _initialized = false;
        Console.WriteLine("Audio: Disposed.");
    }
}
