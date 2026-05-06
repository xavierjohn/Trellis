namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System.Net;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class HandleNotFoundAsyncTests
{
    readonly Error.NotFound _notFound = new(new ResourceRef("User", "42")) { Detail = "User 42 not found" };

    [Fact]
    public async Task Matching_404_returns_failure_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.NotFound);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.HandleNotFoundAsync(_notFound);

        result.Should().BeFailureOfType<Error.NotFound>()
            .Which.Should().HaveDetail("User 42 not found");
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_matching_status_passes_through_and_does_not_dispose(HttpStatusCode status)
    {
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.HandleNotFoundAsync(_notFound);

        result.Should().BeSuccess().Which.StatusCode.Should().Be(status);
        tracker.Disposed.Should().BeFalse();
        tracker.Dispose();
    }

    [Fact]
    public async Task Faulted_input_task_propagates()
    {
        var task = Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));

        var act = async () => await task.HandleNotFoundAsync(_notFound);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_response_task_is_null()
    {
        Task<HttpResponseMessage> task = null!;

        var act = async () => await task.HandleNotFoundAsync(_notFound);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("response");
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_error_is_null()
    {
        // Inspection finding M-H1: error parameter must be null-guarded fail-fast,
        // not deferred to the matched-status path or to Result.Fail's internal guard.
        var task = Task.FromResult<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));

        var act = async () => await task.HandleNotFoundAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("error");
    }

    [Fact]
    public async Task Null_error_disposes_response_before_throwing()
    {
        // Inspection finding (PR #462 round 4): the null-`error` guard previously fired
        // BEFORE awaiting the response task, leaking the in-flight HttpResponseMessage.
        // The guard must run AFTER the await and dispose the message before throwing.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var act = async () => await task.HandleNotFoundAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
        tracker.Disposed.Should().BeTrue("HandleNotFoundAsync's disposal contract must hold even on the null-error throw path");
    }
}