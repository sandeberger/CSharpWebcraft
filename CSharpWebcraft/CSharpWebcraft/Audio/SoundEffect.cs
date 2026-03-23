using NAudio.Wave;

namespace CSharpWebcraft.Audio;

/// <summary>
/// A single playing sound instance. Implements ISampleProvider to be added
/// to NAudio's MixingSampleProvider. Supports volume, fade, loop, and delay.
/// </summary>
public class SoundEffect : ISampleProvider
{
    private readonly float[] _audioData;
    private int _position;
    private volatile float _volume;
    private readonly bool _looping;
    private volatile bool _stopped;

    public WaveFormat WaveFormat { get; }
    public string Name { get; }
    public bool IsFinished { get; private set; }

    // Fade state (updated from game thread via Update())
    private float _targetVolume;
    private float _currentVolume;
    private float _fadeRate;
    private bool _fadingOut;
    private float _delay;
    private bool _started;

    public SoundEffect(float[] audioData, WaveFormat format, string name,
        float volume, bool loop, float fadeIn, float delay)
    {
        _audioData = audioData;
        WaveFormat = format;
        Name = name;
        _looping = loop;
        _targetVolume = Math.Clamp(volume, 0f, 1f);
        _delay = delay;

        if (fadeIn > 0)
        {
            _currentVolume = 0f;
            _fadeRate = _targetVolume / fadeIn;
        }
        else
        {
            _currentVolume = _targetVolume;
        }

        _volume = _currentVolume;
        _started = delay <= 0;
    }

    /// <summary>
    /// Called by MixingSampleProvider on the audio thread.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        if (_stopped)
        {
            IsFinished = true;
            return 0; // Returning 0 removes us from the mixer
        }

        if (!_started)
        {
            // Output silence while waiting for delay
            Array.Clear(buffer, offset, count);
            return count;
        }

        float vol = _volume;
        int samplesWritten = 0;

        while (samplesWritten < count)
        {
            if (_position >= _audioData.Length)
            {
                if (_looping)
                {
                    _position = 0;
                }
                else
                {
                    // Fill remaining with silence
                    Array.Clear(buffer, offset + samplesWritten, count - samplesWritten);
                    IsFinished = true;
                    return samplesWritten > 0 ? count : 0;
                }
            }

            int available = _audioData.Length - _position;
            int toCopy = Math.Min(available, count - samplesWritten);

            for (int i = 0; i < toCopy; i++)
                buffer[offset + samplesWritten + i] = _audioData[_position + i] * vol;

            _position += toCopy;
            samplesWritten += toCopy;
        }

        return samplesWritten;
    }

    /// <summary>
    /// Called from the game thread to update fade/delay state.
    /// </summary>
    public void Update(float dt)
    {
        if (IsFinished || _stopped) return;

        // Handle delay
        if (!_started)
        {
            _delay -= dt;
            if (_delay <= 0)
                _started = true;
            else
                return;
        }

        // Handle fade
        if (MathF.Abs(_fadeRate) > 0.0001f)
        {
            _currentVolume += _fadeRate * dt;

            if (_fadingOut)
            {
                if (_currentVolume <= 0)
                {
                    _currentVolume = 0;
                    _stopped = true;
                    _volume = 0;
                    return;
                }
            }
            else
            {
                if ((_fadeRate > 0 && _currentVolume >= _targetVolume) ||
                    (_fadeRate < 0 && _currentVolume <= _targetVolume))
                {
                    _currentVolume = _targetVolume;
                    _fadeRate = 0;
                }
            }

            _currentVolume = Math.Clamp(_currentVolume, 0f, 1f);
            _volume = _currentVolume;
        }
    }

    public void Stop(float fadeOut = 0.5f)
    {
        if (IsFinished || _stopped) return;

        if (fadeOut > 0 && _currentVolume > 0.001f)
        {
            _fadingOut = true;
            _fadeRate = -_currentVolume / fadeOut;
        }
        else
        {
            _stopped = true;
        }
    }

    public void SetVolume(float volume)
    {
        _targetVolume = Math.Clamp(volume, 0f, 1f);
        _currentVolume = _targetVolume;
        _fadeRate = 0;
        _volume = _currentVolume;
    }

    public void SetVolumeSmooth(float volume, float duration)
    {
        if (IsFinished || _stopped) return;
        float newTarget = Math.Clamp(volume, 0f, 1f);
        if (duration > 0 && MathF.Abs(newTarget - _currentVolume) > 0.001f)
        {
            _fadeRate = (newTarget - _currentVolume) / duration;
            _targetVolume = newTarget;
            _fadingOut = false;
        }
        else
        {
            SetVolume(newTarget);
        }
    }

    public void Cleanup()
    {
        _stopped = true;
    }
}
