namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System.Net;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class HandleConflictAsyncTests
{
    readonly Error.Conflict _conflict = new(new ResourceRef("Order", "7"), "duplicate_key") { Detail = "Order 7 already exists" };

    [Fact]
    public async Task Matching_409_returns_failure_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Conflict);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.HandleConflictAsync(_conflict);

        result.Should().BeFailureOfType<Error.Conflict>()
            .Which.Should().HaveDetail("Order 7 already exists");
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_matching_status_passes_through_and_does_not_dispose(HttpStatusCode status)
    {
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.HandleConflictAsync(_conflict);

        result.Should().BeSuccess().Which.StatusCode.Should().Be(status);
        tracker.Disposed.Should().BeFalse();
        tracker.Dispose();
    }

    [Fact]
    public async Task Faulted_input_task_propagates()
    {
        var task = Task.FromException<HttpResponseMessage>(new HttpRequestException("dns failure"));

        var act = async () => await task.HandleConflictAsync(_conflict);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_response_task_is_null()
    {
        Task<HttpResponseMessage> task = null!;

        var act = async () => await task.HandleConflictAsync(_conflict);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("response");
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_error_is_null()
    {
        // Inspection finding M-H1.
        var task = Task.FromResult<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));

        var act = async () => await task.HandleConflictAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("error");
    }
}