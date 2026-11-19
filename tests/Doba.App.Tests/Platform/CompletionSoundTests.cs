using System.Buffers.Binary;
using System.Text;
using Doba.App.Platform;

namespace Doba.App.Tests.Platform;

public class CompletionSoundTests
{
    [Fact]
    public void Wav_has_a_valid_PCM_RIFF_WAVE_header()
    {
        var wav = CompletionSound.Wav;

        Assert.Equal("RIFF", Ascii(wav, 0));
        Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
        Assert.Equal("WAVE", Ascii(wav, 8));
        Assert.Equal("fmt ", Ascii(wav, 12));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));    // PCM
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));    // mono
        Assert.Equal(CompletionSound.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(CompletionSound.SampleRate * 2, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28))); // byte rate
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)));    // block align
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));   // bits per sample
        Assert.Equal("data", Ascii(wav, 36));
        Assert.Equal(wav.Length - CompletionSound.HeaderSize, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
    }

    [Fact]
    public void Wav_is_about_forty_milliseconds_long()
    {
        var wav = CompletionSound.Wav;
        var dataBytes = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40));
        var durationMs = dataBytes / 2 * 1000.0 / CompletionSound.SampleRate;

        Assert.InRange(durationMs, 35, 45);
        Assert.InRange(wav.Length, 1_000, 4_000); // tiny: fine to keep pinned for the process lifetime
    }

    [Fact]
    public void Click_is_soft_audible_and_starts_and_ends_silently()
    {
        var samples = Samples(CompletionSound.Wav);
        var peak = samples.Max(s => Math.Abs((int)s));

        Assert.InRange(peak, short.MaxValue / 20, short.MaxValue / 2); // audible, but well below full scale
        Assert.Equal(0, samples[0]);                                   // attack ramp: no pop at the start
        Assert.InRange(Math.Abs((int)samples[^1]), 0, 50);              // faded out at the end

        // Energy decays: the first quarter is louder than the last quarter.
        var quarter = samples.Length / 4;
        Assert.True(Energy(samples[..quarter]) > 10 * Energy(samples[^quarter..]));
    }

    [Fact]
    public void Synthesize_honours_sample_rate_and_duration_and_is_deterministic()
    {
        var a = CompletionSound.Synthesize(44_100, 40);
        var b = CompletionSound.Synthesize(44_100, 40);

        Assert.Equal(CompletionSound.HeaderSize + 44_100 * 40 / 1000 * 2, a.Length);
        Assert.Equal(a, b);
        Assert.Same(CompletionSound.Wav, CompletionSound.Wav);
    }

    private static string Ascii(byte[] bytes, int offset) => Encoding.ASCII.GetString(bytes, offset, 4);

    private static short[] Samples(byte[] wav)
    {
        var pcm = wav.AsSpan(CompletionSound.HeaderSize);
        var samples = new short[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2)..]);
        }

        return samples;
    }

    private static double Energy(short[] samples) => samples.Sum(s => (double)s * s);
}
