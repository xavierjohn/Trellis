namespace Trellis.Http;

using System.Buffers;
using System.IO;
using System.Net.Http;

internal sealed class PooledResponseBody : Stream
{
    private const int MaxPooledLength = 1024 * 1024;

    private byte[] _buffer = [];
    private int _length;
    private bool _rented;
    private bool _released;

    internal ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    // Finish reading before parsing so truncated data still surfaces as a transport failure.
    internal Task CopyFromAsync(HttpContent content, CancellationToken ct) =>
        content.Headers.ContentLength > int.MaxValue
            ? CopyWithPlatformLimitAsync(content, ct)
            : content.CopyToAsync(this, ct);

    private async Task CopyWithPlatformLimitAsync(HttpContent content, CancellationToken ct)
    {
        // Let HttpContent enforce its limit only if the body hasn't already been buffered.
        await content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
        await content.CopyToAsync(this, ct).ConfigureAwait(false);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_released;
    public override long Length => _length;
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_released, this);
        if (buffer.Length > Array.MaxLength - _length)
            throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, "The HTTP response body is too large to buffer.");

        var required = _length + buffer.Length;
        if (required > _buffer.Length)
        {
            var capacity = Math.Max(required, (int)Math.Min((long)_buffer.Length * 2, Array.MaxLength));
            var rent = capacity <= MaxPooledLength;
            var next = rent ? ArrayPool<byte>.Shared.Rent(capacity) : GC.AllocateUninitializedArray<byte>(capacity);
            WrittenSpan.CopyTo(next);
            ReturnBuffer();
            _buffer = next;
            _rented = rent;
        }

        buffer.CopyTo(_buffer.AsSpan(_length));
        _length = required;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void WriteByte(byte value) => Write(new ReadOnlySpan<byte>(in value));
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            throw new InvalidOperationException("Only the response reader can release the body buffer.");

        base.Dispose(disposing);
    }

    internal void Release()
    {
        if (_released)
            return;

        _released = true;
        ReturnBuffer();
        _buffer = [];
        _length = 0;
        _rented = false;
    }

    private void ReturnBuffer()
    {
        if (!_rented)
            return;

        _buffer.AsSpan(0, _length).Clear();
        ArrayPool<byte>.Shared.Return(_buffer);
    }
}
