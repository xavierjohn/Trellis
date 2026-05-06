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
    // 405 (Method Not Allowed) is omitted here: it requires the Allow header to be present
    // (per RFC 9110 §15.5.6) and falls through to InternalServerError when absent. Header-aware
    // behavior is covered by Default_405_preserves_Allow_header_in_typed_error and
    // Default_405_with_no_Allow_header_falls_through_to_InternalServerError.
    [InlineData((int)HttpStatusCode.NotAcceptable, typeof(Error.NotAcceptable))]
    [InlineData((int)HttpStatusCode.Conflict, typeof(Error.Conflict))]
    [InlineData((int)HttpStatusCode.Gone, typeof(Error.Gone))]
    [InlineData((int)HttpStatusCode.PreconditionFailed, typeof(Error.PreconditionFailed))]
    [InlineData((int)HttpStatusCode.RequestEntityTooLarge, typeof(Error.ContentTooLarge))]
    [InlineData((int)HttpStatusCode.UnsupportedMediaType, typeof(Error.UnsupportedMediaType))]
    // 416 (Range Not Satisfiable) is omitted here: it requires the Content-Range header to be
    // present (per RFC 9110 §15.5.17) and falls through to InternalServerError when absent.
    // Header-aware behavior is covered by Default_416_preserves_Content_Range_* and
    // Default_416_with_no_Content_Range_header_falls_through_to_InternalServerError.
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

    #region Inspection finding i-H2 — header-aware status mapping

    [Fact]
    public async Task Default_405_preserves_Allow_header_in_typed_error()
    {
        // Inspection finding i-H2: Error.MethodNotAllowed.Allow drives the wire-level
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

        var err = result.Should().BeFailureOfType<Error.MethodNotAllowed>().Subject;
        err.Allow.Items.Should().Equal("GET", "HEAD");
    }

    [Fact]
    public async Task Default_416_preserves_Content_Range_complete_length_in_typed_error()
    {
        // Inspection finding i-H2: Error.RangeNotSatisfiable.CompleteLength comes from
        // the upstream Content-Range: */<size> header.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        tracker.Content!.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(length: 9999);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.RangeNotSatisfiable>().Subject;
        err.CompleteLength.Should().Be(9999);
    }

    [Fact]
    public async Task Default_429_preserves_Retry_After_seconds_in_typed_error()
    {
        // Inspection finding i-H2: Retry-After is part of the typed error.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        tracker.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.TooManyRequests>().Subject;
        err.RetryAfter.Should().NotBeNull();
        err.RetryAfter!.IsDelaySeconds.Should().BeTrue();
        err.RetryAfter.DelaySeconds.Should().Be(60);
    }

    [Fact]
    public async Task Default_503_preserves_Retry_After_date_in_typed_error()
    {
        var when = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        tracker.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(when);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.ServiceUnavailable>().Subject;
        err.RetryAfter.Should().NotBeNull();
        err.RetryAfter!.IsDelaySeconds.Should().BeFalse();
        err.RetryAfter.Date.Should().Be(when);
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

        result.Should().BeFailureOfType<Error.InternalServerError>();
    }

    [Fact]
    public async Task Default_429_with_no_Retry_After_header_keeps_RetryAfter_null()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.TooManyRequests>().Subject;
        err.RetryAfter.Should().BeNull();
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

        result.Should().BeFailureOfType<Error.InternalServerError>();
    }

    [Fact]
    public async Task Default_429_with_negative_Retry_After_treats_header_as_absent()
    {
        // Inspection finding (GPT-5.5 pre-commit review): a malformed negative Retry-After
        // would have caused RetryAfterValue.FromSeconds to throw ArgumentOutOfRangeException
        // *inside* MapStatusToError, leaking the response. ExtractRetryAfter now treats
        // negative deltas as absent (null).
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.TooManyRequests);
        // Synthesize a malformed (negative) delta — adversarial / buggy upstream pattern.
        tracker.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(-30));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.TooManyRequests>().Subject;
        err.RetryAfter.Should().BeNull("malformed (negative) Retry-After must not crash the strict-default mapper");
    }

    [Fact]
    public async Task Default_401_preserves_WWW_Authenticate_schemes_in_typed_error()
    {
        // Inspection finding (GPT-5.5 pre-commit review): 401 had no header preservation
        // even though Error.Unauthorized.Challenges round-trips through ASP's response writer.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "realm=\"api\""));
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Basic"));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.Unauthorized>().Subject;
        err.Challenges.Length.Should().Be(2);
        err.Challenges.Items[0].Scheme.Should().Be("Bearer");
        err.Challenges.Items[1].Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task Default_401_preserves_WWW_Authenticate_parameters_in_typed_error()
    {
        // Copilot PR-comment finding: ExtractAuthChallenges previously dropped the auth
        // parameters and produced bare-scheme challenges. ASP's ResponseFailureWriter then
        // re-emitted Bearer with no realm/error/etc. — losing important auth semantics.
        // The mapper now does a best-effort parse of the parameter string.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", "realm=\"api\", error=\"invalid_token\", error_description=\"The access token expired\""));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.Unauthorized>().Subject;
        err.Challenges.Length.Should().Be(1);
        var challenge = err.Challenges.Items[0];
        challenge.Scheme.Should().Be("Bearer");
        challenge.Params.Should().NotBeNull();
        challenge.Params!["realm"].Should().Be("api");
        challenge.Params["error"].Should().Be("invalid_token");
        challenge.Params["error_description"].Should().Be("The access token expired");
    }

    [Fact]
    public async Task Default_401_token68_challenge_degrades_to_scheme_only()
    {
        // Documented limitation: AuthChallenge has no slot for the RFC 7235 token68
        // form (e.g. "Negotiate <base64-token>"), so a token68-form challenge captures
        // only the scheme on round-trip. Callers needing token68 support must use
        // ToResultAsync(statusMap) or the body-aware overload. This test pins the
        // documented behavior so future changes don't silently regress callers.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Negotiate", "TlRMTVNTUAABAAAA"));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.Unauthorized>().Subject;
        err.Challenges.Length.Should().Be(1);
        err.Challenges.Items[0].Scheme.Should().Be("Negotiate");
        err.Challenges.Items[0].Params.Should().BeNull("token68 is not parsed; AuthChallenge has no slot for it");
    }

    [Fact]
    public async Task Default_401_quoted_string_with_escaped_chars_unescapes_correctly()
    {
        // Coverage for UnescapeQuotedPair: RFC 9110 §5.6.4 quoted-pair forms (\" and \\) must
        // be unescaped in the parsed Params dictionary so a downstream consumer sees the
        // original characters, not the on-the-wire escape sequences.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", "realm=\"my \\\"quoted\\\" realm\", error=\"path \\\\ with backslash\""));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.Unauthorized>().Subject;
        err.Challenges.Length.Should().Be(1);
        err.Challenges.Items[0].Params.Should().NotBeNull();
        err.Challenges.Items[0].Params!["realm"].Should().Be("my \"quoted\" realm");
        err.Challenges.Items[0].Params!["error"].Should().Be("path \\ with backslash");
    }

    [Fact]
    public async Task Default_401_unparseable_parameter_falls_back_to_scheme_only()
    {
        // Coverage for the BuildChallenge fallback: when the parameter string contains no
        // recognizable name=value pairs (e.g. just garbage), the regex matches nothing,
        // builder.Count stays at 0, and we return scheme-only rather than an empty-params
        // challenge.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        tracker.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", "@@@ no equals here @@@"));
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.Unauthorized>().Subject;
        err.Challenges.Length.Should().Be(1);
        err.Challenges.Items[0].Scheme.Should().Be("Bearer");
        err.Challenges.Items[0].Params.Should().BeNull("parser matched no name=value pairs; falls back to scheme-only");
    }

    [Fact]
    public async Task Default_416_preserves_Content_Range_unit_in_typed_error()
    {
        // Copilot PR-comment finding: Error.RangeNotSatisfiable.Unit drives the wire-level
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

        var err = result.Should().BeFailureOfType<Error.RangeNotSatisfiable>().Subject;
        err.CompleteLength.Should().Be(50);
        err.Unit.Should().Be("items");
    }

    [Fact]
    public async Task Default_401_with_no_WWW_Authenticate_header_keeps_challenges_empty()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.Unauthorized);
        var task = Task.FromResult<HttpResponseMessage>(tracker);

        var result = await task.ToResultAsync();

        var err = result.Should().BeFailureOfType<Error.Unauthorized>().Subject;
        err.Challenges.IsEmpty.Should().BeTrue();
    }

    #endregion
}