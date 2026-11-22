namespace Doba.Mcp;

/// <summary>
/// The server's stdin for the MCP transport: reads can be cancelled (a console stream's reads cannot), and the end of
/// input (the client closed stdin or went away) is reported at once through <c>onEnd</c>, so calls still waiting for
/// Doba are cancelled instead of keeping the process alive until their bridge timeout.
/// </summary>
internal sealed class StdinStream(Stream inner, Action onEnd) : Stream
{
    private int _ended;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Ended(inner.Read(buffer, offset, count), count);

    public override int Read(Span<byte> buffer) => Ended(inner.Read(buffer), buffer.Length);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int read;
        try
        {
            read = await inner.ReadAsync(buffer, CancellationToken.None).AsTask().WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            read = 0; // a broken stdin pipe is the end of input too
        }

        return Ended(read, buffer.Length);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A read of 0 bytes into a non-empty buffer is the end of input.</summary>
    private int Ended(int read, int requested)
    {
        if (read == 0 && requested > 0 && Interlocked.Exchange(ref _ended, 1) == 0)
        {
            onEnd();
        }

        return read;
    }
}
