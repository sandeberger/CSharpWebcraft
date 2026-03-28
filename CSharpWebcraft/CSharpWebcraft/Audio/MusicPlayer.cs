namespace CSharpWebcraft.Audio;

/// <summary>
/// Background music controller. Plays Echoes in a loop with volume control and toggle.
/// </summary>
public class MusicPlayer : IDisposable
{
    private readonly AudioManager _audio;
    private SoundEffect? _music;
    private SoundEffect? _fadingOut;
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
            // Keep reference so fade-out Update() keeps running
            _fadingOut?.Cleanup();
            _fadingOut = _music;
            _fadingOut?.Stop(1f);
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

        // Keep updating the fading-out track until it finishes
        if (_fadingOut != null)
        {
            _fadingOut.Update(dt);
            if (_fadingOut.IsFinished)
            {
                _fadingOut.Cleanup();
                _fadingOut = null;
            }
        }

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
        _fadingOut?.Cleanup();
        _fadingOut = null;
        _playing = false;
    }
}
