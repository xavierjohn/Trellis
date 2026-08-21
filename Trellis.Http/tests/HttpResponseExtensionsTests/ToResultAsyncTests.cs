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
    private static THttpError AssertTransportFault<THttpError>(Result<HttpResponseMessage> result)
        where THttpError : HttpError =>
        result.Should().BeFailureOfType<Error.TransportFault>()
            .Which.Fault.Should().BeOfType<THttpError>().Subject;

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
    [InlineData((int)HttpStatusCode.BadRequest, typeof(Error.InvalidInput), null)]
    [InlineData((int)HttpStatusCode.Unauthorized, typeof(Error.AuthenticationRequired), null)]
    [InlineData((int)HttpStatusCode.Forbidden, typeof(Error.Forbidden), null)]
    [InlineData((int)HttpStatusCode.NotFound, typeof(Error.NotFound), null)]
    // 405 (Method Not Allowed) is omitted here: it requires the Allow header to be present
    // (per RFC 9110 §15.5.6) and falls through to InternalServerError when absent. Header-aware
    // behavior is covered by Default_405_preserves_Allow_header_in_typed_error and
    // Default_405_with_no_Allow_header_falls_through_to_InternalServerError.
    [InlineData((int)HttpStatusCode.NotAcceptable, typeof(Error.TransportFault), typeof(HttpError.NotAcceptable))]
    [InlineData((int)HttpStatusCode.Conflict, typeof(Error.Conflict), null)]
    [InlineData((int)HttpStatusCode.Gone, typeof(Error.Gone), null)]
    [InlineData((int)HttpStatusCode.PreconditionFailed, typeof(Error.TransportFault), typeof(HttpError.PreconditionFailed))]
    [InlineData((int)HttpStatusCode.RequestEntityTooLarge, typeof(Error.TransportFault), typeof(HttpError.ContentTooLarge))]
    [InlineData((int)HttpStatusCode.UnsupportedMediaType, typeof(Error.TransportFault), typeof(HttpError.UnsupportedMediaType))]
    // 416 (Range Not Satisfiable) is omitted here: it requires the Content-Range header to be
    // present (per RFC 9110 §15.5.17) and falls through to InternalServerError when absent.
    // Header-aware behavior is covered by Default_416_preserves_Content_Range_* and
    // Default_416_with_no_Content_Range_header_falls_through_to_InternalServerError.
    [InlineData((int)HttpStatusCode.UnprocessableEntity, typeof(Error.InvalidInput), null)]
    [InlineData(428, typeof(Error.TransportFault), typeof(HttpError.PreconditionRequired))]
    [InlineData(429, typeof(Error.RateLimited), null)]
    [InlineData((int)HttpStatusCode.NotImplemented, typeof(Error.Unexpected), null)]
    [InlineData((int)HttpStatusCode.ServiceUnavailable, typeof(Error.Unavailable), null)]
    public async Task Default_null_status_map_returns_typed_failure_for_known_non_success_statuses(
        int statusCode,
        Type errorType,
        Type? transportFaultType)
    {
        var status = (HttpStatusCode)statusCode;
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailure();
        if (transportFaultType is null)
        {
            result.Error.Should().BeOfType(errorType);
        }
        else
        {
            var error = result.Should().BeFailureOfType<Error.TransportFault>().Subject;
            error.Fault.Should().BeOfType(transportFaultType);
        }

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

        result.Should().BeFailureOfType<Error.Unexpected>()
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
        var error = new Error.Unexpected("F1") { Detail = "503" };

        var result = await task.ToResultAsync(_ => error);

        result.Should().BeFailureOfType<Error.Unexpected>()
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
        var error = new Error.Unexpected("F2") { Detail = "bad request body" };

        var result = await task.ToResultAsync(
            (_, _) => Task.FromResult<Error?>(error),
            CancellationToken.None);

        result.Should().BeFailureOfType<Error.Unexpected>()
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
    public async Task Body_aware_overload_throws_ArgumentNullException_when_mapper_is_null_and_disposes_response()
    {
        // Round-6 PR finding: round-4 fixed Handle*Async to await BEFORE the null-error
        // guard so the in-flight HttpResponseMessage is owned and disposed even on the
        // programmer's null-arg path. The body-aware ToResultAsync(mapper, ct) overload
        // had the same shape — ArgumentNullException.ThrowIfNull(mapper) running BEFORE
        // the await let the awaited message leak. This test pins the corrected ordering:
        // await first, then null-check + dispose + throw.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.NotFound);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var act = async () => await task.ToResultAsync(
            (Func<HttpResponseMessage, CancellationToken, Task<Error?>>)null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("mapper");
        tracker.Disposed.Should().BeTrue("body-aware overload's disposal contract must hold even on the null-mapper path");
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

    #region Inspection finding i-H2 — header-aware status mapping

    [Fact]
    public async Task Default_405_preserves_Allow_header_in_typed_error()
    {
        // Inspection finding i-H2: HttpError.MethodNotAllowed.Allow drives the wire-level
        // Allow response header in ASP. The strict default mapper must extract the
        // upstream Allow header rather than producing an empty list.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.MethodNotAllowed)
        {
            Content = new StringContent(string.Empty),
        };
        // Allow lives on Content.Headers per HttpContentHeaders.
        tracker.Content!.Headers.Allow.Add("GET");
        tracker.Content.Headers.Allow.Add("HEAD");
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = AssertTransportFault<HttpError.MethodNotAllowed>(result);
        err.Allow.Items.Should().Equal("GET", "HEAD");
    }

    [Fact]
    public async Task Default_416_preserves_Content_Range_complete_length_in_typed_error()
    {
        // Inspection finding i-H2: HttpError.RangeNotSatisfiable.CompleteLength comes from
        // the upstream Content-Range: */<size> header.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        tracker.Content!.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(length: 9999);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = AssertTransportFault<HttpError.RangeNotSatisfiable>(result);
        err.CompleteLength.Should().Be(9999);
    }

    [Fact]
    public async Task Default_429_with_Retry_After_header_preserves_RetryAdvice_After()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        tracker.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var rate = result.Should().BeFailureOfType<Error.RateLimited>().Subject;
        rate.Retry.Should().NotBeNull();
        rate.Retry!.Value.After.Should().Be(TimeSpan.FromSeconds(60));
        rate.Retry.Value.At.Should().BeNull();
    }

    [Fact]
    public async Task Default_503_with_Retry_After_header_preserves_RetryAdvice_At()
    {
        var when = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        tracker.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(when);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var unavailable = result.Should().BeFailureOfType<Error.Unavailable>().Subject;
        unavailable.Retry.Should().NotBeNull();
        unavailable.Retry!.Value.At.Should().Be(when);
        unavailable.Retry.Value.After.Should().BeNull();
    }

    [Fact]
    public async Task Default_501_maps_to_Unexpected_with_not_implemented_reason_code()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.NotImplemented);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var unexpected = result.Should().BeFailureOfType<Error.Unexpected>().Subject;
        unexpected.ReasonCode.Should().Be(FaultCodes.NotImplemented);
    }

    [Fact]
    public async Task Default_405_with_no_Allow_header_falls_through_to_InternalServerError()
    {
        // Inspection finding (PR #462 round 4): RFC 9110 §15.5.6 says a 405 response MUST
        // include the Allow header. When upstream is non-conforming and omits it, the
        // strict default mapper falls through to InternalServerError rather than
        // synthesizing a typed `new Error.MethodNotAllowed` with an empty array — that
        // empty-array shape would produce a misleading wire-level `Allow:` header on
        // round-trip through ASP.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.Unexpected>();
    }

    [Fact]
    public async Task Default_429_with_no_Retry_After_header_returns_TooManyRequests()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.RateLimited>();
    }

    [Fact]
    public async Task Default_416_with_no_Content_Range_header_falls_through_to_InternalServerError()
    {
        // Inspection finding (PR #462 round 4): RFC 9110 §15.5.17 says a 416 response
        // SHOULD include Content-Range. When upstream omits it, the strict default
        // mapper falls through to InternalServerError rather than synthesizing a typed
        // `new Error.RangeNotSatisfiable` with a zero length — that zero-length shape
        // would produce a misleading `Content-Range: bytes */0` wire header on
        // round-trip through ASP.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.Unexpected>();
    }

    [Fact]
    public async Task Default_429_with_negative_Retry_After_still_returns_TooManyRequests()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        tracker.Headers.TryAddWithoutValidation("Retry-After", "-30");
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.RateLimited>();
    }

    [Fact]
    public async Task Default_429_with_Retry_After_seconds_overflowing_int_still_returns_TooManyRequests()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        tracker.Headers.TryAddWithoutValidation("Retry-After", "9999999999");
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.RateLimited>();
    }

    [Fact]
    public async Task Default_401_with_WWW_Authenticate_header_returns_Unauthorized()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "realm=\"api\""));
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Basic"));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.AuthenticationRequired>();
    }

    [Fact]
    public async Task Default_416_preserves_Content_Range_unit_in_typed_error()
    {
        // Copilot PR-comment finding: HttpError.RangeNotSatisfiable.Unit drives the wire-level
        // Content-Range unit when ASP renders the error. The mapper must preserve the upstream
        // unit (e.g. "items") rather than hard-coding "bytes".
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        tracker.Content!.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(length: 50)
        {
            Unit = "items",
        };
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = AssertTransportFault<HttpError.RangeNotSatisfiable>(result);
        err.CompleteLength.Should().Be(50);
        err.Unit.Should().Be("items");
    }

    [Fact]
    public async Task Default_401_with_no_WWW_Authenticate_header_returns_Unauthorized()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.AuthenticationRequired>();
    }

    // -------- RFC-strict defensive cases (gaps in initial i-H2 coverage) --------
    //
    // The following tests pin behavior that the original i-H2 implementation either
    // didn't address explicitly or got wrong (and was fixed in a later round). Each test
    // cites the RFC clause it locks in. They live here rather than in a parallel "Rfc_*"
    // region because they're behaviorally peers of the i-H2 tests above; the difference
    // is purely citation-style.

    [Fact]
    public async Task Default_416_with_Content_Range_zero_length_produces_typed_error_for_empty_resource()
    {
        // RFC 9110 §15.5.17: a 416 response SHOULD include Content-Range. `bytes */0` is the
        // legitimate form for an empty resource. Mapping must produce a typed
        // HttpError.RangeNotSatisfiable(0) so it round-trips (mapper → typed error → ASP
        // renderer → wire) without losing the zero-length signal.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        tracker.Content!.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0L);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = AssertTransportFault<HttpError.RangeNotSatisfiable>(result);
        err.CompleteLength.Should().Be(0);
        err.Unit.Should().Be("bytes");
    }

    [Fact]
    public async Task Default_416_with_range_form_but_no_length_falls_through_to_InternalServerError()
    {
        // RFC 9110 §14.4: the unsatisfied-range form `bytes 0-99/*` (no complete length)
        // is permitted by the grammar but conveys no length we can attach to a typed
        // RangeNotSatisfiable. The mapper falls through to InternalServerError rather
        // than synthesizing a (long, string) Error.RangeNotSatisfiable from incomplete
        // information.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        tracker.Content!.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0L, 99L);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.Unexpected>();
    }

    [Fact]
    public async Task Default_405_Allow_header_present_but_empty_value_falls_through()
    {
        // RFC 9110 §15.5.6: a 405 response MUST generate an Allow header. An empty list
        // (literal `Allow:` on the wire) is non-conformant. The mapper still falls through
        // to InternalServerError rather than synthesizing MethodNotAllowed(empty) — which
        // would round-trip as a malformed empty Allow on the wire.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.MethodNotAllowed)
        {
            Content = new StringContent(string.Empty),
        };
        // Add a literal empty Allow header (Contains("Allow") == true, Allow.Count == 0)
        // to faithfully represent the wire shape "Allow:" — distinct from "header was
        // never sent". The mapper treats both the same, but the test name is about the
        // empty-value case so we pin the wire shape explicitly.
        tracker.Content!.Headers.TryAddWithoutValidation("Allow", string.Empty);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.Unexpected>();
    }

    [Fact]
    public async Task Default_429_zero_Retry_After_delta_still_returns_TooManyRequests()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        tracker.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        result.Should().BeFailureOfType<Error.RateLimited>();
    }

    #endregion
}