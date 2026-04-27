namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class ToResultAsyncTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Default_null_status_map_returns_Ok_for_success_status_codes(HttpStatusCode status)
    {
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeSuccess().Which.StatusCode.Should().Be(status);
        tracker.Disposed.Should().BeFalse();
        tracker.Dispose();
    }

    [Theory]
    [InlineData((int)HttpStatusCode.BadRequest, typeof(Error.BadRequest))]
    [InlineData((int)HttpStatusCode.Unauthorized, typeof(Error.Unauthorized))]
    [InlineData((int)HttpStatusCode.Forbidden, typeof(Error.Forbidden))]
    [InlineData((int)HttpStatusCode.NotFound, typeof(Error.NotFound))]
    [InlineData((int)HttpStatusCode.MethodNotAllowed, typeof(Error.MethodNotAllowed))]
    [InlineData((int)HttpStatusCode.NotAcceptable, typeof(Error.NotAcceptable))]
    [InlineData((int)HttpStatusCode.Conflict, typeof(Error.Conflict))]
    [InlineData((int)HttpStatusCode.Gone, typeof(Error.Gone))]
    [InlineData((int)HttpStatusCode.PreconditionFailed, typeof(Error.PreconditionFailed))]
    [InlineData((int)HttpStatusCode.RequestEntityTooLarge, typeof(Error.ContentTooLarge))]
    [InlineData((int)HttpStatusCode.UnsupportedMediaType, typeof(Error.UnsupportedMediaType))]
    [InlineData((int)HttpStatusCode.RequestedRangeNotSatisfiable, typeof(Error.RangeNotSatisfiable))]
    [InlineData((int)HttpStatusCode.UnprocessableEntity, typeof(Error.UnprocessableContent))]
    [InlineData(428, typeof(Error.PreconditionRequired))]
    [InlineData(429, typeof(Error.TooManyRequests))]
    [InlineData((int)HttpStatusCode.NotImplemented, typeof(Error.NotImplemented))]
    [InlineData((int)HttpStatusCode.ServiceUnavailable, typeof(Error.ServiceUnavailable))]
    public async Task Default_null_status_map_returns_typed_failure_for_known_non_success_statuses(
        int statusCode,
        Type errorType)
    {
        var status = (HttpStatusCode)statusCode;
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailure();
        result.Error.Should().BeOfType(errorType);
        result.Error!.Detail.Should().Contain(((int)status).ToString(CultureInfo.InvariantCulture));
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Default_null_status_map_returns_InternalServerError_for_unknown_status()
    {
        const HttpStatusCode status = (HttpStatusCode)599;
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.InternalServerError>()
            .Which.Detail.Should().Contain("599");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Status_map_returning_null_returns_Ok_and_does_not_dispose()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync(_ => null);

        result.Should().BeSuccess();
        tracker.Disposed.Should().BeFalse();
        tracker.Dispose();
    }

    [Fact]
    public async Task Status_map_returning_error_returns_Fail_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var task = Task.FromResult<HttpResponseMessage>(tracker);
        var error = new Error.InternalServerError("F1") { Detail = "503" };

        var result = await task.ToResultAsync(_ => error);

        result.Should().BeFailureOfType<Error.InternalServerError>()
            .Which.Should().HaveDetail("503");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Status_map_not_invoked_when_input_task_already_cancelled()
    {
        var invoked = false;
        var task = Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));

        var act = async () => await task.ToResultAsync(_ => { invoked = true; return null; });

        await act.Should().ThrowAsync<TaskCanceledException>();
        invoked.Should().BeFalse();
    }

    // ----- Body-aware overload -----

    [Fact]
    public async Task Body_aware_mapper_not_invoked_for_success_status()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var task = Task.FromResult<HttpResponseMessage>(response);
        var invoked = false;

        var result = await task.ToResultAsync(
            (_, _) => { invoked = true; return Task.FromResult<Error?>(null); },
            CancellationToken.None);

        result.Should().BeSuccess();
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task Body_aware_mapper_returning_null_returns_Ok()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.BadGateway);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync(
            (_, _) => Task.FromResult<Error?>(null),
            CancellationToken.None);

        result.Should().BeSuccess().Which.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        tracker.Disposed.Should().BeFalse();
        tracker.Dispose();
    }

    [Fact]
    public async Task Body_aware_mapper_returning_error_returns_Fail_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.BadRequest);
        var task = Task.FromResult<HttpResponseMessage>(tracker);
        var error = new Error.InternalServerError("F2") { Detail = "bad request body" };

        var result = await task.ToResultAsync(
            (_, _) => Task.FromResult<Error?>(error),
            CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>()
            .Which.Should().HaveDetail("bad request body");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Body_aware_mapper_receives_response_and_supplied_cancellation_token()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway);
        var task = Task.FromResult<HttpResponseMessage>(response);
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        HttpResponseMessage? observedResponse = null;
        CancellationToken observedToken = default;

        var result = await task.ToResultAsync(
            (r, c) => { observedResponse = r; observedToken = c; return Task.FromResult<Error?>(null); },
            ct);

        result.Should().BeSuccess();
        observedResponse.Should().BeSameAs(response);
        observedToken.Should().Be(ct);
    }

    [Fact]
    public async Task Body_aware_overload_propagates_cancellation()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway);
        var task = Task.FromResult<HttpResponseMessage>(response);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await task.ToResultAsync(
            (_, c) => Task.FromException<Error?>(new OperationCanceledException(c)),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ----- Null-task and exception-disposal guards -----

    [Fact]
    public async Task Throws_ArgumentNullException_when_response_task_is_null()
    {
        Task<HttpResponseMessage> task = null!;

        var act = async () => await task.ToResultAsync();

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("response");
    }

    [Fact]
    public async Task Body_aware_overload_throws_ArgumentNullException_when_response_task_is_null()
    {
        Task<HttpResponseMessage> task = null!;

        var act = async () => await task.ToResultAsync(
            (_, _) => Task.FromResult<Error?>(null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("response");
    }

    [Fact]
    public async Task Status_map_throwing_disposes_response_before_propagating()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var act = async () => await task.ToResultAsync(_ => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Body_aware_mapper_throwing_disposes_response_before_propagating()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.BadRequest);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var act = async () => await task.ToResultAsync(
            (_, _) => throw new InvalidOperationException("mapper-failed"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("mapper-failed");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Body_aware_mapper_async_failure_disposes_response_before_propagating()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.BadRequest);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var act = async () => await task.ToResultAsync(
            (_, _) => Task.FromException<Error?>(new InvalidOperationException("async-fail")),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("async-fail");
        tracker.Disposed.Should().BeTrue();
    }
}
