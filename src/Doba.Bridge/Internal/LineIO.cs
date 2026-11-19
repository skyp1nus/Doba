using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Doba.Bridge.Internal;

/// <summary>Outcome of <see cref="LineIO.ReadLineAsync"/>.</summary>
internal enum LineStatus
{
    /// <summary>A line (without its '\n') was read.</summary>
    Line,

    /// <summary>The other side closed before sending anything.</summary>
    Empty,

    /// <summary>The other side closed in the middle of a line.</summary>
    Partial,

    /// <summary>More than the allowed bytes came before a '\n'.</summary>
    TooLong,
}

/// <summary>The bridge's framing: one UTF-8 JSON message per line ('\n'), at most
/// <see cref="BridgeHost.MaxMessageBytes"/> bytes.</summary>
internal static class LineIO
{
    private const int ChunkBytes = 16 * 1024;

    /// <summary>JSON on one line; non-ASCII text unescaped (it is read by a model, see <see cref="TaskJson"/>).</summary>
    private static readonly JsonWriterOptions s_writerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>Reads up to the first '\n' (anything after it is ignored: one message per connection).</summary>
    public static async Task<(LineStatus Status, byte[] Line)> ReadLineAsync(Stream stream, int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(ChunkBytes);
        while (true)
        {
            var memory = buffer.GetMemory(ChunkBytes);
            var read = await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.WrittenCount == 0 ? (LineStatus.Empty, []) : (LineStatus.Partial, buffer.WrittenSpan.ToArray());
            }

            var newline = memory.Span[..read].IndexOf((byte)'\n');
            if (newline >= 0)
            {
                if (buffer.WrittenCount + newline > maxBytes)
                {
                    return (LineStatus.TooLong, []);
                }

                buffer.Advance(newline);
                return (LineStatus.Line, buffer.WrittenSpan.ToArray());
            }

            buffer.Advance(read);
            if (buffer.WrittenCount > maxBytes)
            {
                return (LineStatus.TooLong, []);
            }
        }
    }

    /// <summary><paramref name="value"/> as one JSON line (with its '\n').</summary>
    public static byte[] ToLine<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, s_writerOptions))
        {
            JsonSerializer.Serialize(writer, value, typeInfo);
        }

        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }
}
