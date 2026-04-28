namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

/// <summary>
/// Tests for the 404-to-Maybe terminal JSON helper.
/// </summary>
public class ReadJsonOrNoneOn404AsyncTests
{
    [Fact]
    public async Task NotFound_returns_None_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.NotFound);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeSuccess().Which.HasValue.Should().BeFalse();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Success_with_valid_json_returns_Some_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson { firstName = "Ada", age = 36 }, SourceGenerationContext.Default.camelcasePerson),
        };
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var maybe = result.Should().BeSuccess().Subject;
        maybe.TryGetValue(out var person).Should().BeTrue();
        person.Should().NotBeNull();
        person!.firstName.Should().Be("Ada");
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ResetContent)]
    public async Task NoContent_or_ResetContent_returns_None_and_disposes_response(HttpStatusCode status)
    {
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeSuccess().Which.HasValue.Should().BeFalse();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_body_returns_None_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeSuccess().Which.HasValue.Should().BeFalse();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Non_not_found_failure_returns_strict_status_error_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Conflict);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.Conflict>()
            .Which.Detail.Should().Contain("409");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_non_success_status_returns_InternalServerError_and_disposes_response()
    {
        const HttpStatusCode status = (HttpStatusCode)599;
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>()
            .Which.Detail.Should().Contain("599");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_response_task_is_null()
    {
        Task<HttpResponseMessage> task = null!;

        var act = async () => await task.ReadJsonOrNoneOn404Async(SourceGenerationContext.Default.camelcasePerson);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("response");
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_json_metadata_is_null()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var task = Task.FromResult(response);

        var act = async () => await task.ReadJsonOrNoneOn404Async<camelcasePerson>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("jsonTypeInfo");
    }
}