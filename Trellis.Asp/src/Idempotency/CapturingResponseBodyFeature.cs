namespace Trellis.Asp.Idempotency;

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Decorator over an <see cref="IHttpResponseBodyFeature"/> that tees writes into a
/// bounded in-memory capture buffer while still forwarding every byte to the original
/// response stream. The capture is used by the idempotency middleware to record a replayable
/// response snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Writes go to <c>both</c> the original feature stream and a <see cref="MemoryStream"/>
/// capture. If the capture exceeds <c>maxBytes</c>, capture is aborted and only the original
/// stream continues to receive bytes — the client is never affected by capture failure.
/// </para>
/// <para>
/// <c>SendFileAsync</c> calls abort capture (the file content does not flow through the wrapped
/// stream). When capture is aborted <see cref="GetCapturedBytes"/> returns <see langword="null"/>
/// and the middleware skips recording a snapshot for this request.
/// </para>
/// </remarks>
public sealed class CapturingResponseBodyFeature : IHttpResponseBodyFeature, IDisposable
{
    private readonly IHttpResponseBodyFeature _inner;
    private readonly MemoryStream _capture;
    private readonly TeeStream _teeStream;
    private PipeWriter? _cachedWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapturingResponseBodyFeature"/> class.
    /// </summary>
    /// <param name="inner">The wrapped response body feature.</param>
    /// <param name="maxBytes">The maximum number of bytes to retain in the capture buffer.</param>
    public CapturingResponseBodyFeature(IHttpResponseBodyFeature inner, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
        }

        _inner = inner;
        _capture = new MemoryStream();
        _teeStream = new TeeStream(inner.Stream, _capture, maxBytes, this);
    }

    /// <inheritdoc/>
    public Stream Stream => _teeStream;

    /// <inheritdoc/>
    public PipeWriter Writer => _cachedWriter ??= PipeWriter.Create(_teeStream, new StreamPipeWriterOptions(leaveOpen: true));

    /// <summary>
    /// Gets a value indicating whether the capture has been aborted (overflow, SendFile, or trailers).
    /// </summary>
    public bool CaptureAborted { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Flushes the cached <see cref="PipeWriter"/> first (if it was ever requested) so any
    /// bytes the handler wrote via <c>Response.BodyWriter.GetMemory()</c> /
    /// <c>Advance()</c> without an explicit <c>FlushAsync</c> drain through the underlying
    /// tee stream — landing in both the client response and the capture buffer — before
    /// completion delegates to the inner feature.
    /// </remarks>
    public async Task CompleteAsync()
    {
        if (_cachedWriter is not null)
        {
            await _cachedWriter.FlushAsync().ConfigureAwait(false);
        }

        await _inner.CompleteAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void DisableBuffering() => _inner.DisableBuffering();

    /// <inheritdoc/>
    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken)
    {
        CaptureAborted = true;
        return _inner.SendFileAsync(path, offset, count, cancellationToken);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default) => _inner.StartAsync(cancellationToken);

    /// <summary>
    /// Returns the captured bytes if capture is intact, otherwise <see langword="null"/>.
    /// </summary>
    public byte[]? GetCapturedBytes() => CaptureAborted ? null : _capture.ToArray();

    /// <summary>
    /// Flushes the cached <see cref="PipeWriter"/> (if the handler ever requested it) so any
    /// bytes the handler wrote via <c>Response.BodyWriter</c> without calling
    /// <c>FlushAsync</c> drain through the underlying tee stream — landing in both the client
    /// response and the in-memory capture buffer — before a caller reads
    /// <see cref="GetCapturedBytes"/>. Safe to call when the writer was never requested.
    /// </summary>
    /// <param name="cancellationToken">A bounded token that aborts the flush if it stalls.</param>
    public async ValueTask FlushCachedWriterAsync(CancellationToken cancellationToken)
    {
        if (_cachedWriter is null)
        {
            return;
        }

        await _cachedWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks capture as aborted from an external observer (for example, when the middleware
    /// detects that response trailers were written or that <c>HasStarted</c> was set before
    /// the wrapper was installed).
    /// </summary>
    public void AbortCapture() => CaptureAborted = true;

    /// <inheritdoc/>
    public void Dispose()
    {
        _cachedWriter?.Complete();
        _capture.Dispose();
    }

    private sealed class TeeStream : Stream
    {
        private readonly Stream _original;
        private readonly MemoryStream _capture;
        private readonly long _maxBytes;
        private readonly CapturingResponseBodyFeature _owner;

        public TeeStream(Stream original, MemoryStream capture, long maxBytes, CapturingResponseBodyFeature owner)
        {
            _original = original;
            _capture = capture;
            _maxBytes = maxBytes;
            _owner = owner;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _original.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _original.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _original.Write(buffer, offset, count);
            TryCapture(buffer.AsSpan(offset, count));
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _original.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            TryCapture(buffer.AsSpan(offset, count));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _original.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            TryCapture(buffer.Span);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _original.Write(buffer);
            TryCapture(buffer);
        }

        private void TryCapture(ReadOnlySpan<byte> buffer)
        {
            if (_owner.CaptureAborted)
            {
                return;
            }

            if (_capture.Length + buffer.Length > _maxBytes)
            {
                _owner.AbortCapture();
                return;
            }

            _capture.Write(buffer);
        }
    }
}