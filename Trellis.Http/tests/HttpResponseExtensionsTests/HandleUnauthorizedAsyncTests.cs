namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System.Net;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class HandleUnauthorizedAsyncTests
{
    readonly Error.Unauthorized _unauth = new() { Detail = "token expired" };

    [Fact]
    public async Task Matching_401_returns_failure_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.HandleUnauthorizedAsync(_unauth);

        result.Should().BeFailureOfType<Error.Unauthorized>()
            .Which.Should().HaveDetail("token expired");
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_matching_status_passes_through_and_does_not_dispose(HttpStatusCode status)
    {
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.HandleUnauthorizedAsync(_unauth);

        result.Should().BeSuccess().Which.StatusCode.Should().Be(status);
        tracker.Disposed.Should().BeFalse();
        tracker.Dispose();
    }

    [Fact]
    public async Task Faulted_input_task_propagates()
    {
        var task = Task.FromException<HttpResponseMessage>(new HttpRequestException("tls error"));

        var act = async () => await task.HandleUnauthorizedAsync(_unauth);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
