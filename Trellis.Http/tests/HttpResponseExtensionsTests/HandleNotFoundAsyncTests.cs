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
}
