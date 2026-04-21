namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class ReadJsonMaybeAsyncTests
{
    [Fact]
    public async Task Already_failed_result_short_circuits_with_original_error()
    {
        var error = new Error.NotFound(new ResourceRef("User", "1"));
        var task = Task.FromResult(Result.Fail<HttpResponseMessage>(error));

        var result = await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.NotFound>()
            .Which.Should().Be(error);
    }

    [Fact]
    public async Task Success_with_valid_json_returns_Some_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson { firstName = "Maya", age = 33 }, SourceGenerationContext.Default.camelcasePerson),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var maybe = result.Should().BeSuccess().Subject;
        maybe.HasValue.Should().BeTrue();
        maybe.Value.firstName.Should().Be("Maya");
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ResetContent)]
    public async Task Success_with_NoContent_or_ResetContent_returns_None_and_disposes_response(HttpStatusCode status)
    {
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeSuccess().Which.HasValue.Should().BeFalse();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Success_with_empty_body_returns_None_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeSuccess().Which.HasValue.Should().BeFalse();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Success_with_json_null_literal_returns_None_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json"),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeSuccess().Which.HasValue.Should().BeFalse();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Non_success_status_returns_Fail_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.BadGateway);
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Success_with_invalid_json_throws_JsonException_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Not JSON"),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var act = async () => await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        await act.Should().ThrowAsync<JsonException>();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson { firstName = "X", age = 1 }, SourceGenerationContext.Default.camelcasePerson),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_response_task_is_null()
    {
        Task<Result<HttpResponseMessage>> task = null!;

        var act = async () => await task.ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("response");
    }
}
