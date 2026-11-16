using System.Buffers.Binary;

namespace Doba.App.Platform;

/// <summary>
/// The "soft click" played when a task is checked off, synthesized in code as a PCM WAV image (no binary asset).
/// </summary>
/// <remarks>
/// Two damped sine partials (a bright tick over a lower body) with a 1 ms attack ramp so the onset does not pop,
/// an exponential decay and a short linear fade-out so the tail ends exactly at zero. Peak level is well below
/// full scale so it reads as a quiet UI click, not a beep.
/// </remarks>
internal static class CompletionSound
{
    public const int SampleRate = 22_050;
    public const int DurationMs = 40;
    public const short BitsPerSample = 16;
    public const short Channels = 1;

    /// <summary>Size of the canonical RIFF/WAVE header (RIFF + fmt + data chunk headers).</summary>
    public const int HeaderSize = 44;

    private static byte[]? _wav;

    /// <summary>The completion click as a complete WAV file image, generated once and cached.</summary>
    public static byte[] Wav => _wav ??= Synthesize(SampleRate, DurationMs);

    /// <summary>Generates a mono 16-bit PCM WAV image of the click.</summary>
    public static byte[] Synthesize(int sampleRate, int durationMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMs, 5);

        var samples = (int)((long)sampleRate * durationMs / 1000);
        const int bytesPerSample = BitsPerSample / 8;
        var dataSize = samples * bytesPerSample * Channels;
        var wav = new byte[HeaderSize + dataSize];
        var span = wav.AsSpan();

        // RIFF header
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
        "WAVE"u8.CopyTo(span[8..]);

        // fmt chunk (PCM)
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // WAVE_FORMAT_PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * bytesPerSample * Channels); // byte rate
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(bytesPerSample * Channels)); // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);

        // data chunk
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);

        const double peak = 0.28;          // of full scale
        const double tickHz = 2_400;       // bright transient
        const double bodyHz = 1_150;       // softer body
        const double tickDecayMs = 3.5;
        const double bodyDecayMs = 9.0;
        const double attackMs = 1.0;
        const double fadeOutMs = 6.0;

        var pcm = span[HeaderSize..];
        for (var i = 0; i < samples; i++)
        {
            var tMs = i * 1000.0 / sampleRate;
            var t = i / (double)sampleRate;
            var tick = Math.Exp(-tMs / tickDecayMs) * Math.Sin(2 * Math.PI * tickHz * t);
            var body = Math.Exp(-tMs / bodyDecayMs) * Math.Sin(2 * Math.PI * bodyHz * t);
            var envelope = Math.Min(1.0, tMs / attackMs) * Math.Min(1.0, (durationMs - tMs) / fadeOutMs);
            var v = peak * envelope * (0.45 * tick + 0.55 * body);
            var s = (short)Math.Round(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcm[(i * bytesPerSample)..], s);
        }

        return wav;
    }
}
