namespace RagApi.Infrastructure;

/// <summary>
/// Wraps a MemoryStream and silently ignores ReadTimeout/WriteTimeout
/// property access — required because Document Intelligence SDK internally
/// tries to set these on the stream, which MemoryStream does not support.
/// </summary>
public class TimeoutIgnoringStream : Stream
{
    private readonly Stream _inner;

    public TimeoutIgnoringStream(Stream inner) => _inner = inner;

    // Silently ignore timeout get/set instead of throwing
    public override bool CanTimeout => false;
    public override int ReadTimeout  { get => 0; set { } }
    public override int WriteTimeout { get => 0; set { } }

    // Delegate everything else to inner stream
    public override bool CanRead   => _inner.CanRead;
    public override bool CanSeek   => _inner.CanSeek;
    public override bool CanWrite  => _inner.CanWrite;
    public override long Length    => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void  Flush()                                => _inner.Flush();
    public override int   Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long  Seek(long offset, SeekOrigin origin)   => _inner.Seek(offset, origin);
    public override void  SetLength(long value)                  => _inner.SetLength(value);
    public override void  Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => _inner.ReadAsync(buffer, offset, count, ct);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _inner.ReadAsync(buffer, ct);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}