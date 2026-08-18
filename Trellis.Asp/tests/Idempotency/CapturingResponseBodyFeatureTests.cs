namespace Trellis.Asp.Tests.Idempotency;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using Trellis.Asp.Idempotency;

/// <summary>
/// Pins the contract of <see cref="CapturingResponseBodyFeature"/>: writes to <c>Stream</c> and
/// <c>BodyWriter</c> are tee'd to both the original feature stream and the in-memory capture
/// buffer; <c>SendFileAsync</c>, response trailers, and overflow past
/// <see cref="IdempotencyOptions.MaxResponseBodyBytes"/> all abort the capture but continue
/// emitting the original response to the client (no duplicate side-effect risk).
/// </summary>
public sealed class CapturingResponseBodyFeatureTests
{
    private sealed class StubBodyFeature : IHttpResponseBodyFeature
    {
        public MemoryStream Original { get; } = new();
        public int SendFileCalls { get; private set; }
        public int CompleteCalls { get; private set; }

        public Stream Stream => Original;
        public System.IO.Pipelines.PipeWriter Writer => System.IO.Pipelines.PipeWriter.Create(Original);

        public Task CompleteAsync()
        {
            CompleteCalls++;
            return Task.CompletedTask;
        }

        public void DisableBuffering() { }
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken)
        {
            SendFileCalls++;
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Stream_write_is_tee_d_to_both_original_and_capture()
    {
        var inner = new StubBodyFeature();
        var feature = new CapturingResponseBodyFeature(inner, maxBytes: 4096);

        var payload = new byte[] { 1, 2, 3, 4, 5 };
        await feature.Stream.WriteAsync(payload, TestContext.Current.CancellationToken);

        inner.Original.ToArray().Should().Equal(payload);
        feature.CaptureAborted.Should().BeFalse();
        feature.GetCapturedBytes().Should().Equal(payload);
    }

    [Fact]
    public async Task Multi_write_accumulates_into_capture_buffer()
    {
        var inner = new StubBodyFeature();
        var feature = new CapturingResponseBodyFeature(inner, maxBytes: 4096);

        await feature.Stream.WriteAsync(new byte[] { 1, 2 }, TestContext.Current.CancellationToken);
        await feature.Stream.WriteAsync(new byte[] { 3, 4, 5 }, TestContext.Current.CancellationToken);

        feature.GetCapturedBytes().Should().Equal(new byte[] { 1, 2, 3, 4, 5 });
        inner.Original.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public async Task Writes_beyond_max_bytes_abort_capture_but_still_forward_to_original()
    {
        var inner = new StubBodyFeature();
        var feature = new CapturingResponseBodyFeature(inner, maxBytes: 4);

        await feature.Stream.WriteAsync(new byte[] { 1, 2 }, TestContext.Current.CancellationToken);
        await feature.Stream.WriteAsync(new byte[] { 3, 4, 5, 6, 7 }, TestContext.Current.CancellationToken);

        feature.CaptureAborted.Should().BeTrue();
        feature.GetCapturedBytes().Should().BeNull("once capture is aborted, no snapshot is replayed");
        inner.Original.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 },
            "the client must still receive the original response bytes verbatim");
    }

    [Fact]
    public async Task SendFile_aborts_capture_and_delegates_to_inner_feature()
    {
        var inner = new StubBodyFeature();
        var feature = new CapturingResponseBodyFeature(inner, maxBytes: 1024);

        await feature.SendFileAsync("nonexistent", 0, null, TestContext.Current.CancellationToken);

        inner.SendFileCalls.Should().Be(1);
        feature.CaptureAborted.Should().BeTrue();
        feature.GetCapturedBytes().Should().BeNull();
    }

    [Fact]
    public async Task BodyWriter_writes_are_also_tee_d()
    {
        var inner = new StubBodyFeature();
        var feature = new CapturingResponseBodyFeature(inner, maxBytes: 1024);

        var span = feature.Writer.GetMemory(8);
        new byte[] { 9, 9, 9 }.CopyTo(span.Span);
        feature.Writer.Advance(3);
        await feature.Writer.FlushAsync(TestContext.Current.CancellationToken);

        feature.GetCapturedBytes().Should().Equal(new byte[] { 9, 9, 9 });
        inner.Original.ToArray().Should().Equal(new byte[] { 9, 9, 9 });
    }

    [Fact]
    public async Task CompleteAsync_flushes_buffered_BodyWriter_bytes_before_completing_inner()
    {
        // A handler may write via Response.BodyWriter.GetMemory + Advance and then call
        // Response.CompleteAsync() WITHOUT an explicit FlushAsync. The wrapper's
        // CompleteAsync must drain the cached PipeWriter so the bytes reach both the
        // underlying stream and the capture buffer before completion delegates downstream.
        var inner = new StubBodyFeature();
        var feature = new CapturingResponseBodyFeature(inner, maxBytes: 1024);

        var span = feature.Writer.GetMemory(8);
        new byte[] { 7, 7, 7 }.CopyTo(span.Span);
        feature.Writer.Advance(3);

        await feature.CompleteAsync();

        feature.GetCapturedBytes().Should().Equal(new byte[] { 7, 7, 7 });
        inner.Original.ToArray().Should().Equal(new byte[] { 7, 7, 7 });
        inner.CompleteCalls.Should().Be(1);
    }

    [Fact]
    public async Task CompleteAsync_without_BodyWriter_use_just_delegates_to_inner()
    {
        // If the handler never asked for Response.BodyWriter, there is no cached
        // PipeWriter to flush. CompleteAsync must still delegate to the inner feature
        // exactly once without throwing.
        var inner = new StubBodyFeature();
        var feature = new CapturingResponseBodyFeature(inner, maxBytes: 1024);

        await feature.CompleteAsync();

        inner.CompleteCalls.Should().Be(1);
        feature.GetCapturedBytes().Should().BeEmpty();
    }
}