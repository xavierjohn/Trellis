namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

/// <summary>
/// Once a response has been handed to a terminal reader or to the body-aware mapper, the extension owns
/// it: cancellation must reach the pending read, propagate, and still dispose the response.
/// </summary>
public class HttpResponseCancellationTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private static readonly Dictionary<string, Func<HttpResponseMessage, CancellationToken, Task>> s_readers = new()
    {
        ["ReadJsonAsync"] = (message, ct) =>
            Task.FromResult(Result.Ok(message)).ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, ct),
        ["ReadJsonMaybeAsync"] = (message, ct) =>
            Task.FromResult(Result.Ok(message)).ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, ct),
        ["ReadJsonOrNoneOn404Async"] = (message, ct) =>
            Task.FromResult(message).ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, ct),
    };

    public static TheoryData<string> ReaderNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in s_readers.Keys)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(ReaderNames))]
    public async Task Reader_with_a_canceled_token_throws_without_reading_the_body_and_disposes_the_response(string reader)
    {
        using var content = new PendingContent();
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK) { Content = content };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => s_readers[reader](tracker, cts.Token).WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        content.Entered.ArrivalCount.Should().Be(0, "a canceled caller must not start reading the body");
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ReaderNames))]
    public async Task Reader_canceled_while_the_body_read_is_pending_forwards_the_token_and_disposes_the_response(string reader)
    {
        using var content = new PendingContent();
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK) { Content = content };
        using var cts = new CancellationTokenSource();

        var pending = s_readers[reader](tracker, cts.Token);
        await content.Entered.WaitForArrivalAsync(s_timeout, TestContext.Current.CancellationToken);

        pending.IsCompleted.Should().BeFalse();
        tracker.Disposed.Should().BeFalse("the response stays open while its body is being read");

        await cts.CancelAsync();
        var act = () => pending.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ToResultAsync_mapper_canceled_while_pending_forwards_the_token_and_disposes_the_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.BadGateway);
        using var entered = new TestGate();
        var observedToken = CancellationToken.None;
        using var cts = new CancellationTokenSource();

        var pending = Task.FromResult<HttpResponseMessage>(tracker).ToResultAsync(
            async (_, ct) =>
            {
                observedToken = ct;
                entered.Signal();
                await Task.Delay(Timeout.Infinite, ct);
                return (Error?)null;
            },
            cts.Token);
        await entered.WaitForArrivalAsync(s_timeout, TestContext.Current.CancellationToken);

        pending.IsCompleted.Should().BeFalse();
        tracker.Disposed.Should().BeFalse("the mapper is still inspecting the response");
        observedToken.Should().Be(cts.Token);

        await cts.CancelAsync();
        var act = () => pending.WaitAsync(s_timeout, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        tracker.Disposed.Should().BeTrue();
    }

    private sealed class PendingContent : HttpContent
    {
        public TestGate Entered { get; } = new();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Entered.Signal();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Entered.Dispose();
            base.Dispose(disposing);
        }
    }
}
