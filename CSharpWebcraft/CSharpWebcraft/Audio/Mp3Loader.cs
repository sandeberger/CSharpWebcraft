using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CSharpWebcraft.Audio;

/// <summary>
/// Decoded audio data cached in memory for instant playback.
/// All sounds are normalized to 44100 Hz, stereo, 32-bit float.
/// </summary>
public class CachedSound
{
    public float[] AudioData { get; }
    public WaveFormat WaveFormat { get; }

    private CachedSound(float[] data, WaveFormat format)
    {
        AudioData = data;
        WaveFormat = format;
    }

    public static CachedSound LoadFromFile(string filePath, WaveFormat targetFormat)
    {
        using var reader = new AudioFileReader(filePath);
        ISampleProvider chain = reader;

        // Resample if needed
        if (reader.WaveFormat.SampleRate != targetFormat.SampleRate)
            chain = new WdlResamplingSampleProvider(chain, targetFormat.SampleRate);

        // Convert mono to stereo if needed
        if (chain.WaveFormat.Channels == 1 && targetFormat.Channels == 2)
            chain = new MonoToStereoSampleProvider(chain);

        // Read all samples
        var samples = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = chain.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                samples.Add(buffer[i]);
        }

        return new CachedSound(samples.ToArray(), targetFormat);
    }
}
