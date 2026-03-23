namespace CSharpWebcraft.Audio;

/// <summary>
/// Background music controller. Plays Echoes in a loop with volume control and toggle.
/// </summary>
public class MusicPlayer : IDisposable
{
    private readonly AudioManager _audio;
    private SoundEffect? _music;
    private float _volume = 0.5f;
    private bool _playing;

    public bool IsPlaying => _playing;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _music?.SetVolume(_volume * _audio.MasterVolume);
        }
    }

    public MusicPlayer(AudioManager audio)
    {
        _audio = audio;
    }

    public void Start()
    {
        if (!_audio.IsInitialized || !_audio.HasSound("Echoes")) return;

        _music = _audio.Play("Echoes", _volume, loop: true, fadeIn: 2f);
        if (_music != null)
        {
            _playing = true;
            Console.WriteLine("Audio: Music started.");
        }
    }

    public void Toggle()
    {
        if (!_audio.IsInitialized) return;

        if (_playing)
        {
            _music?.Stop(1f);
            _music = null;
            _playing = false;
            Console.WriteLine("Audio: Music paused.");
        }
        else
        {
            Start();
        }
    }

    public void Update(float dt)
    {
        _music?.Update(dt);

        if (_music != null && _music.IsFinished)
        {
            _music = null;
            _playing = false;
        }
    }

    public void Dispose()
    {
        _music?.Cleanup();
        _music = null;
        _playing = false;
    }
}
