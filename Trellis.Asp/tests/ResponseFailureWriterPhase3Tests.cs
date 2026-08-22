namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Phase 3 boundary translation tests: Retry-After emission from <see cref="RetryAdvice"/>,
/// <see cref="Error.Aggregate"/> rendering as RFC 9457 <c>errors[]</c> extension with
/// worst-status outer, Conflict + If-Match override → 412/precondition-failed,
/// AuthenticationRequired.Scheme verbatim, HttpError payload projection to extensions,
/// and outer wire-kind round-trip stability.
/// </summary>
public sealed class ResponseFailureWriterPhase3Tests
{
    private static DefaultHttpContext NewContext(string? ifMatch = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        if (ifMatch is not null)
            ctx.Request.Headers["If-Match"] = ifMatch;
        return ctx;
    }

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    private sealed record T(int Id);

    private static async Task<JsonDocument> ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    // ----------------- Retry-After -----------------

    [Fact]
    public async Task RateLimited_with_After_emits_RetryAfter_delta_seconds()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.RateLimited(new RetryAdvice(After: TimeSpan.FromSeconds(30))));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(429);
        ctx.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public async Task RateLimited_with_At_emits_RetryAfter_HttpDate()
    {
        var ctx = NewContext();
        var at = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var r = Result.Fail<T>(new Error.RateLimited(new RetryAdvice(At: at)));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Headers["Retry-After"].ToString().Should().Be("Tue, 01 Jan 2030 00:00:00 GMT");
    }

    [Fact]
    public async Task RateLimited_with_both_After_and_At_prefers_After()
    {
        var ctx = NewContext();
        var retry = new RetryAdvice(After: TimeSpan.FromSeconds(60), At: DateTimeOffset.UtcNow.AddDays(1));
        var r = Result.Fail<T>(new Error.RateLimited(retry));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Headers["Retry-After"].ToString().Should().Be("60");
    }

    [Fact]
    public async Task Unavailable_with_After_emits_RetryAfter_delta_seconds()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.Unavailable(Retry: new RetryAdvice(After: TimeSpan.FromSeconds(45))));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(503);
        ctx.Response.Headers["Retry-After"].ToString().Should().Be("45");
    }

    // ----------------- Aggregate -----------------

    [Fact]
    public async Task Aggregate_worst_status_5xx_beats_4xx()
    {
        var ctx = NewContext();
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "1")),
            new Error.Unexpected("boom"));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Aggregate_worst_status_tie_breaks_to_highest_numeric()
    {
        var ctx = NewContext();
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "1")),
            new Error.Conflict(ResourceRef.For("Item", "1"), "duplicate_key"));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Aggregate_renders_problems_extension_with_per_child_problem_details()
    {
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "format", null, "must be email"));
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "42")),
            new Error.InvalidInput(fields));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("kind").GetString().Should().Be("multi");
        var problems = body.RootElement.GetProperty("problems");
        problems.GetArrayLength().Should().Be(2);

        var first = problems[0];

        // `type` is a URI reference per RFC 9457, not the kind slug. That it matches the URI the
        // same status yields at the root is asserted in ResponseFailureWriterProblemObjectTests;
        // here we only pin the shape.
        first.GetProperty("type").GetString().Should().StartWith("https://");
        first.GetProperty("status").GetInt32().Should().Be(404);
        first.GetProperty("kind").GetString().Should().Be("not-found");

        // NotFound carries no reason code of its own, so its kind is no longer restated as one.
        first.GetProperty("code").GetString().Should().Be("error.unspecified");

        var second = problems[1];
        second.GetProperty("type").GetString().Should().StartWith("https://");
        second.GetProperty("status").GetInt32().Should().Be(422);
        second.GetProperty("kind").GetString().Should().Be("unprocessable-content");
    }

    [Fact]
    public async Task Aggregate_child_status_honors_per_call_typed_error_mapping()
    {
        // Per-call mappings sit at the top of the status precedence chain, so an aggregate's
        // problems[] children must be projected through the same chain as the outer status.
        // Rendering children from ambient options only makes the child status contradict the
        // endpoint's own mapping.
        var ctx = NewContext();
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "42")),
            new Error.Conflict(ResourceRef.For("Item", "42"), "duplicate_key"));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t, o => o.WithErrorMapping<Error.NotFound>(StatusCodes.Status410Gone))
            .ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        var problems = body.RootElement.GetProperty("problems");
        problems[0].GetProperty("status").GetInt32().Should().Be(410);
        problems[1].GetProperty("status").GetInt32().Should().Be(409);
    }

    [Fact]
    public async Task Aggregate_child_status_honors_per_call_error_mapper_delegate()
    {
        var ctx = NewContext();
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "42")),
            new Error.Conflict(ResourceRef.For("Item", "42"), "duplicate_key"));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(
                t => t,
                o => o.WithErrorMapping(e => e is Error.NotFound ? StatusCodes.Status410Gone : default))
            .ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        var problems = body.RootElement.GetProperty("problems");
        problems[0].GetProperty("status").GetInt32().Should().Be(410);

        // The unmatched arm returns `default`; the child must fall through to its ambient status.
        problems[1].GetProperty("status").GetInt32().Should().Be(409);
    }

    [Fact]
    public async Task Aggregate_single_InvalidInput_outer_kind_stays_multi()
    {
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "format", null, "must be email"));
        var agg = new Error.Aggregate(new Error.InvalidInput(fields));
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("kind").GetString().Should().Be("multi");
        ctx.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Aggregate_child_detail_is_scrubbed_for_5xx_children()
    {
        // The outer detail is scrubbed at >= 500, but each child carries its own status. A 5xx
        // child's detail must be scrubbed with the same rule or the problems[] array becomes a
        // disclosure channel for internal context the outer detail deliberately withholds.
        var ctx = NewContext();
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "42")) { Detail = "Item 42 does not exist." },
            new Error.Unexpected("boom") { Detail = "Sensitive internal context that must not leak." });
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
        using var body = await ReadBody(ctx);
        var problems = body.RootElement.GetProperty("problems");

        problems[0].GetProperty("status").GetInt32().Should().Be(404);
        problems[0].GetProperty("detail").GetString().Should().Be("Item 42 does not exist.");

        problems[1].GetProperty("status").GetInt32().Should().Be(500);
        problems[1].GetProperty("detail").GetString().Should().Be("An internal error occurred.");
    }

    [Fact]
    public async Task Aggregate_child_detail_scrubbing_follows_the_mapped_child_status()
    {
        // Scrubbing keys off the resolved child status, so a per-call mapping that promotes a
        // 4xx child into the 5xx range must scrub it, and one that demotes a 5xx child must not.
        var ctx = NewContext();
        var agg = new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "42")) { Detail = "Leaks once promoted to 5xx." },
            new Error.Unexpected("boom") { Detail = "Safe once demoted to 4xx." });
        var r = Result.Fail<T>(agg);

        await r.ToHttpResponse(
                t => t,
                o => o
                    .WithErrorMapping<Error.NotFound>(StatusCodes.Status500InternalServerError)
                    .WithErrorMapping<Error.Unexpected>(StatusCodes.Status400BadRequest))
            .ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        var problems = body.RootElement.GetProperty("problems");

        problems[0].GetProperty("status").GetInt32().Should().Be(500);
        problems[0].GetProperty("detail").GetString().Should().Be("An internal error occurred.");

        problems[1].GetProperty("status").GetInt32().Should().Be(400);
        problems[1].GetProperty("detail").GetString().Should().Be("Safe once demoted to 4xx.");
    }

    // ----------------- Concurrent modification override -----------------

    [Fact]
    public async Task Conflict_concurrent_modification_with_IfMatch_maps_to_412_precondition_failed()
    {
        var ctx = NewContext(ifMatch: "\"etag\"");
        var r = Result.Fail<T>(new Error.Conflict(ResourceRef.For("Item", "1"), FaultCodes.ConcurrentModification));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(412);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("kind").GetString().Should().Be("precondition-failed");
        body.RootElement.GetProperty("code").GetString().Should().Be(FaultCodes.ConcurrentModification);
    }

    [Fact]
    public async Task Conflict_concurrent_modification_without_IfMatch_stays_409_conflict()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.Conflict(ResourceRef.For("Item", "1"), FaultCodes.ConcurrentModification));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(409);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("kind").GetString().Should().Be("conflict");
        body.RootElement.GetProperty("code").GetString().Should().Be(FaultCodes.ConcurrentModification);
    }

    [Fact]
    public async Task Conflict_other_reason_with_IfMatch_stays_409_conflict()
    {
        var ctx = NewContext(ifMatch: "\"etag\"");
        var r = Result.Fail<T>(new Error.Conflict(ResourceRef.For("Item", "1"), "duplicate_key"));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(409);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("kind").GetString().Should().Be("conflict");
        body.RootElement.GetProperty("code").GetString().Should().Be("duplicate_key");
    }

    // ----------------- AuthenticationRequired.Scheme -----------------

    [Fact]
    public async Task AuthenticationRequired_with_explicit_scheme_emits_it_verbatim()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.AuthenticationRequired("Bearer realm=\"api\""));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.Headers["WWW-Authenticate"].ToString().Should().Be("Bearer realm=\"api\"");
    }

    [Fact]
    public async Task AuthenticationRequired_explicit_scheme_takes_precedence_over_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        services.AddSingleton<IAuthenticationSchemeProvider>(new ProviderStub("Negotiate"));
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();

        var r = Result.Fail<T>(new Error.AuthenticationRequired("Bearer realm=\"api\""));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Headers["WWW-Authenticate"].ToString().Should().Be("Bearer realm=\"api\"");
    }

    [Fact]
    public async Task AuthenticationRequired_with_ReasonCode_projects_it_to_wire_code()
    {
        // ReasonCode lets consumers distinguish causes that share the 401 surface
        // (invalid credentials vs missing credentials vs token expired) without
        // parsing Detail. Error.Code overrides to ReasonCode; ResponseFailureWriter
        // projects Error.Code into the ProblemDetails `code` extension verbatim.
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.AuthenticationRequired(Scheme: "Bearer")
        {
            Code = "Authentication.InvalidCredentials",
        });

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("code").GetString().Should().Be("Authentication.InvalidCredentials");
        // Scheme still flows through to WWW-Authenticate independently of ReasonCode.
        ctx.Response.Headers["WWW-Authenticate"].ToString().Should().Be("Bearer");
    }

    [Fact]
    public async Task AuthenticationRequired_without_ReasonCode_emits_the_unspecified_sentinel()
    {
        // A kind is not a reason. `AuthenticationRequired` with no ReasonCode has nothing finer
        // to say than its kind, which `kind` already carries, so `code` degrades to the single
        // sentinel rather than restating the kind and inviting clients to switch on it.
        // (Breaking change: this previously emitted "authentication-required".) Note `kind` is
        // the wire slug for the error, which defaults to the HTTP condition name and is not
        // necessarily Error.Kind -- here `authentication-required` surfaces as `unauthorized`.
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.AuthenticationRequired("Bearer"));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("code").GetString().Should().Be("error.unspecified");
        body.RootElement.GetProperty("kind").GetString().Should().Be("unauthorized");
    }

    private sealed class ProviderStub(string name) : IAuthenticationSchemeProvider
    {
        private readonly AuthenticationScheme _scheme = new(name, name, typeof(IAuthenticationHandler));

        public void AddScheme(AuthenticationScheme scheme) { }
        public Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync() => Task.FromResult<IEnumerable<AuthenticationScheme>>([_scheme]);
        public Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync() => Task.FromResult<AuthenticationScheme?>(_scheme);
        public Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync() => Task.FromResult<AuthenticationScheme?>(_scheme);
        public Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync() => Task.FromResult<AuthenticationScheme?>(_scheme);
        public Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync() => Task.FromResult<AuthenticationScheme?>(_scheme);
        public Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync() => Task.FromResult<AuthenticationScheme?>(_scheme);
        public Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() => Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
        public Task<AuthenticationScheme?> GetSchemeAsync(string n) => Task.FromResult<AuthenticationScheme?>(n == name ? _scheme : null);
        public void RemoveScheme(string n) { }
    }

    // ----------------- Wire-kind round-trip -----------------

    public static TheoryData<Error, string, int> WireKindCases()
    {
        var rr = ResourceRef.For("Item", "1");
        var fields = EquatableArray.Create(new FieldViolation(new InputPointer("/x"), "required", null, "missing"));
        return new()
        {
            { new Error.InvalidInput(fields), "unprocessable-content", 422 },
            { new Error.InvariantViolation("rule_x"), "unprocessable-content", 422 },
            { new Error.NotFound(rr), "not-found", 404 },
            { new Error.Forbidden("policy"), "forbidden", 403 },
            { new Error.Conflict(rr, "duplicate_key"), "conflict", 409 },
            { new Error.Gone(rr), "gone", 410 },
            { new Error.AuthenticationRequired(), "unauthorized", 401 },
            { new Error.RateLimited(), "too-many-requests", 429 },
            { new Error.Unavailable(), "service-unavailable", 503 },
            { new Error.Unexpected("boom"), "internal-server-error", 500 },
            { new Error.Unexpected(FaultCodes.NotImplemented), "not-implemented", 501 },
            { new Error.Aggregate(new Error.NotFound(rr)), "multi", 404 },
        };
    }

    [Theory]
    [MemberData(nameof(WireKindCases))]
    public async Task Outer_wire_kind_and_status_round_trip(Error error, string expectedKind, int expectedStatus)
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(error);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(expectedStatus);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("kind").GetString().Should().Be(expectedKind);
    }

    [Fact]
    public async Task Conflict_concurrent_modification_with_IfMatch_wire_round_trip()
    {
        var ctx = NewContext(ifMatch: "\"etag\"");
        var r = Result.Fail<T>(new Error.Conflict(ResourceRef.For("Item", "1"), FaultCodes.ConcurrentModification));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(412);
        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("kind").GetString().Should().Be("precondition-failed");
    }

    // ----------------- HttpError payload projection to extensions -----------------

    [Fact]
    public async Task MethodNotAllowed_extension_carries_allow_array()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TransportFault(
            new HttpError.MethodNotAllowed(EquatableArray.Create("GET", "POST"))));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        var allow = body.RootElement.GetProperty("allow");
        allow.GetArrayLength().Should().Be(2);
        allow[0].GetString().Should().Be("GET");
    }

    [Fact]
    public async Task NotAcceptable_extension_carries_available_array()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TransportFault(
            new HttpError.NotAcceptable(EquatableArray.Create("application/json"))));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("available")[0].GetString().Should().Be("application/json");
    }

    [Fact]
    public async Task UnsupportedMediaType_extension_carries_supported_array()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TransportFault(
            new HttpError.UnsupportedMediaType(EquatableArray.Create("application/json"))));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("supported")[0].GetString().Should().Be("application/json");
    }

    [Fact]
    public async Task RangeNotSatisfiable_extension_carries_completeLength_and_unit()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TransportFault(new HttpError.RangeNotSatisfiable(1234)));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("completeLength").GetInt64().Should().Be(1234);
        body.RootElement.GetProperty("unit").GetString().Should().Be("bytes");
    }

    [Fact]
    public async Task ContentTooLarge_extension_carries_maxBytes_when_set()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TransportFault(new HttpError.ContentTooLarge(1_000_000)));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("maxBytes").GetInt64().Should().Be(1_000_000);
    }

    [Fact]
    public async Task PreconditionFailed_extension_carries_preconditionKind()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TransportFault(
            new HttpError.PreconditionFailed(ResourceRef.For("Item", "1"), PreconditionKind.IfMatch)));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        using var body = await ReadBody(ctx);
        body.RootElement.GetProperty("preconditionKind").GetString().Should().Be("IfMatch");
    }
}