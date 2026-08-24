namespace Trellis.Asp.Tests;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Branch coverage for <see cref="ResponseFailureWriter"/>: companion headers (Allow,
/// Content-Range), validation problem vs problem path, status redaction for 5xx, and the
/// extensions builder (faultId, rules).
/// </summary>
public sealed class ResponseFailureWriterTests
{
    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    /// <summary>
    /// Builds an HttpContext whose RequestServices include a stub <see cref="IAuthenticationSchemeProvider"/>
    /// reporting <paramref name="defaultChallengeScheme"/> as the default challenge scheme. Used to exercise
    /// the synthesis path in <see cref="ResponseFailureWriter"/> without spinning up the real
    /// ASP.NET Core authentication subsystem.
    /// </summary>
    private static DefaultHttpContext NewAuthContext(string defaultChallengeScheme)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        services.AddSingleton<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(
            new StubAuthSchemeProvider(defaultChallengeScheme));
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class StubAuthSchemeProvider(string defaultChallengeScheme)
        : Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider
    {
        private readonly Microsoft.AspNetCore.Authentication.AuthenticationScheme _scheme =
            new(defaultChallengeScheme, defaultChallengeScheme,
                typeof(Microsoft.AspNetCore.Authentication.IAuthenticationHandler));

        public void AddScheme(Microsoft.AspNetCore.Authentication.AuthenticationScheme scheme) { }
        public Task<IEnumerable<Microsoft.AspNetCore.Authentication.AuthenticationScheme>> GetAllSchemesAsync()
            => Task.FromResult<IEnumerable<Microsoft.AspNetCore.Authentication.AuthenticationScheme>>([_scheme]);
        public Task<Microsoft.AspNetCore.Authentication.AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
            => Task.FromResult<Microsoft.AspNetCore.Authentication.AuthenticationScheme?>(_scheme);
        public Task<Microsoft.AspNetCore.Authentication.AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
            => Task.FromResult<Microsoft.AspNetCore.Authentication.AuthenticationScheme?>(_scheme);
        public Task<Microsoft.AspNetCore.Authentication.AuthenticationScheme?> GetDefaultForbidSchemeAsync()
            => Task.FromResult<Microsoft.AspNetCore.Authentication.AuthenticationScheme?>(_scheme);
        public Task<Microsoft.AspNetCore.Authentication.AuthenticationScheme?> GetDefaultSignInSchemeAsync()
            => Task.FromResult<Microsoft.AspNetCore.Authentication.AuthenticationScheme?>(_scheme);
        public Task<Microsoft.AspNetCore.Authentication.AuthenticationScheme?> GetDefaultSignOutSchemeAsync()
            => Task.FromResult<Microsoft.AspNetCore.Authentication.AuthenticationScheme?>(_scheme);
        public Task<IEnumerable<Microsoft.AspNetCore.Authentication.AuthenticationScheme>> GetRequestHandlerSchemesAsync()
            => Task.FromResult<IEnumerable<Microsoft.AspNetCore.Authentication.AuthenticationScheme>>([]);
        public Task<Microsoft.AspNetCore.Authentication.AuthenticationScheme?> GetSchemeAsync(string name)
            => Task.FromResult<Microsoft.AspNetCore.Authentication.AuthenticationScheme?>(
                name == defaultChallengeScheme ? _scheme : null);
        public void RemoveScheme(string name) { }
    }

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    private sealed record T(int Id);

    [Fact]
    public async Task Unauthorized_without_auth_configured_emits_no_WwwAuthenticate_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.AuthenticationRequired());

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.Headers.ContainsKey("WWW-Authenticate")
            .Should().BeFalse("no auth configuration means there is no scheme to synthesize");
    }

    [Fact]
    public async Task Unauthorized_synthesizes_default_challenge_scheme_from_AuthenticationSchemeProvider()
    {
        var ctx = NewAuthContext(defaultChallengeScheme: "Bearer");
        var r = Result.Fail<T>(new Error.AuthenticationRequired());

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        var values = ctx.Response.Headers["WWW-Authenticate"];
        values.Count.Should().Be(1);
        values.ToString().Should().Be("Bearer");
    }

    [Fact]
    public async Task Unauthorized_synthesizes_using_configured_challenge_scheme_name()
    {
        var ctx = NewAuthContext(defaultChallengeScheme: "ApiJwt");
        var r = Result.Fail<T>(new Error.AuthenticationRequired());

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Headers["WWW-Authenticate"].ToString().Should().Be("ApiJwt");
    }

    [Fact]
    public async Task Unauthorized_synthesis_skipped_when_response_already_carries_WwwAuthenticate()
    {
        var ctx = NewAuthContext(defaultChallengeScheme: "Bearer");
        ctx.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"upstream\"";
        var r = Result.Fail<T>(new Error.AuthenticationRequired());

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        var values = ctx.Response.Headers["WWW-Authenticate"];
        values.Count.Should().Be(1);
        values.ToString().Should().Be("Bearer realm=\"upstream\"");
    }

    [Fact]
    public async Task Unauthorized_with_non_401_mapped_status_does_not_synthesize()
    {
        var ctx = NewAuthContext(defaultChallengeScheme: "Bearer");
        var r = Result.Fail<T>(new Error.AuthenticationRequired());

        await r.ToHttpResponse(t => t, o => o.WithErrorMapping<Error.AuthenticationRequired>(500))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
        ctx.Response.Headers.ContainsKey("WWW-Authenticate")
            .Should().BeFalse("WWW-Authenticate is bound to 401 responses");
    }

    [Fact]
    public async Task RangeNotSatisfiable_emits_ContentRange_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TransportFault(new HttpError.RangeNotSatisfiable(1234)));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(416);
        ctx.Response.Headers["Content-Range"].ToString().Should().Be("bytes */1234");
    }

    [Fact]
    public async Task UnprocessableContent_with_field_violations_writes_validation_problem()
    {
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "format", null, "must be email"),
            new FieldViolation(new InputPointer("/email"), "required", null, "required"));
        var r = Result.Fail<T>(new Error.InvalidInput(fields));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task UnprocessableContent_with_only_rules_writes_validation_problem()
    {
        var ctx = NewContext();
        var rules = EquatableArray.Create(
            new RuleViolation("must_have_items",
                EquatableArray.Create(new InputPointer("/items")),
                null, "Order must have items."));
        var r = Result.Fail<T>(new Error.InvalidInput(default, rules));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task UnprocessableContent_empty_falls_back_to_plain_problem()
    {
        var ctx = NewContext();
        // No fields, no rules: skips ValidationProblem and writes plain Problem.
        var r = Result.Fail<T>(new Error.InvalidInput(default));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task InternalServerError_writes_500_problem_response()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.Unexpected("FAULT-7") { Detail = "stack trace leak" });

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task TooManyRequests_without_RetryAfter_does_not_emit_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.RateLimited());

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(429);
        ctx.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task ValidationProblem_with_5xx_status_scrubs_detail()
    {
        // Regression for m-13: when a custom WithErrorMapping promotes UnprocessableContent
        // to a 5xx status, the validation-branch detail must be scrubbed identically to
        // the plain Problem branch. Previously the validation branch leaked unprocessable.Detail.
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "format", null, "must be email"));
        var error = new Error.InvalidInput(fields)
        {
            Detail = "Sensitive internal context that must not leak.",
        };
        var r = Result.Fail<T>(error);

        await r.ToHttpResponse(t => t, o => o.WithErrorMapping<Error.InvalidInput>(500))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("An internal error occurred.");
        body.Should().NotContain("Sensitive internal context");
    }

    [Fact]
    public async Task ValidationProblem_with_4xx_status_keeps_detail()
    {
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "format", null, "must be email"));
        var error = new Error.InvalidInput(fields)
        {
            Detail = "One or more validation errors occurred.",
        };
        var r = Result.Fail<T>(error);

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("One or more validation errors occurred.");
    }

    // ---------------------------------------------------------------------
    // Bundle C / m-9 (#33): JSON Pointer field paths translated to MVC
    // dot+bracket convention on the wire `errors` keys, matching ASP.NET
    // Core's default ValidationProblemDetails shape (so OpenAPI codegen and
    // React form libraries like react-hook-form / Formik can lookup
    // setError(key, ...) directly without a slash→dot translation shim).
    // RFC 6901 escapes (~1, ~0) are decoded so segments containing literal
    // '/' or '~' appear correctly in the wire key.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("/email", "email")]                       // single segment: bare (regression guard)
    [InlineData("/customer/email", "customer.email")]     // nested object: dot
    [InlineData("/items/0", "items[0]")]                  // object → array index: brackets
    [InlineData("/items/0/name", "items[0].name")]        // object → array → object
    [InlineData("/items/0/tags/3", "items[0].tags[3]")]   // mixed nesting
    [InlineData("/0/name", "[0].name")]                   // root array index
    [InlineData("/foo~1bar", "foo/bar")]                  // RFC 6901 ~1 unescape
    [InlineData("/foo~0bar", "foo~bar")]                  // RFC 6901 ~0 unescape
    public async Task UnprocessableContent_translates_pointer_to_MVC_dot_bracket(string pointerPath, string expectedKey)
    {
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer(pointerPath), "format", null, "must be valid"));
        var r = Result.Fail<T>(new Error.InvalidInput(fields));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty(expectedKey, out _)
            .Should().BeTrue($"expected MVC convention key '{expectedKey}' for pointer '{pointerPath}'");
    }

    [Fact]
    public async Task UnprocessableContent_does_not_emit_JSON_Pointer_slash_form_on_the_wire()
    {
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/items/0/name"), "format", null, "must be valid"));
        var r = Result.Fail<T>(new Error.InvalidInput(fields));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        var errorKeys = body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToArray();
        errorKeys.Should().Contain("items[0].name");
        errorKeys.Should().NotContain(
            key => key.Contains("items/0/name", StringComparison.Ordinal),
            "JSON Pointer slash form must not appear in the wire `errors` keys");

        // The pointer form is correct — and required — inside the structured location object,
        // which addresses the document rather than an MVC model-binding key.
        body.RootElement
            .GetProperty("fieldViolations")[0]
            .GetProperty("location")
            .GetProperty("pointer")
            .GetString()
            .Should().Be("/items/0/name");
    }

    [Fact]
    public async Task UnprocessableContent_aggregates_multiple_violations_for_same_pointer_under_one_MVC_key()
    {
        // Regression guard: two FieldViolations with the same pointer must aggregate into ONE
        // wire `errors` key with an array of two messages — not two separate keys.
        var ctx = NewContext();
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/items/0/name"), "required", null, "is required"),
            new FieldViolation(new InputPointer("/items/0/name"), "format", null, "must be valid"));
        var r = Result.Fail<T>(new Error.InvalidInput(fields));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var errors = body.RootElement.GetProperty("errors");
        var messages = errors.GetProperty("items[0].name").EnumerateArray();
        messages.Should().HaveCount(2);
    }

    // ----------------- The sentinel is not a discriminator -----------------

    /// <remarks>
    /// <para>
    /// <see cref="Error.Code"/> defaults to the <c>error.unspecified</c> sentinel, so an error
    /// that named no reason puts the sentinel on the wire — the same value seeded onto a problem
    /// that had no <c>Error</c> behind it at all. For 404 the collision reaches both envelope
    /// members: <c>KindForStatus(404)</c> returns the same <c>not-found</c> slug that
    /// <c>Error.NotFound.Kind</c> reports, so a handler's "no such row" and the framework's
    /// "no such route" agree on <c>code</c> and <c>kind</c> alike.
    /// </para>
    /// <para>
    /// The two documents are not byte-identical — <c>instance</c> is synthesized from the
    /// <c>ResourceRef</c> by default, and is asserted here so that the one member which does
    /// differ is pinned rather than merely implied. It is a URI reference identifying the
    /// occurrence, though, not a member a client can dispatch on.
    /// </para>
    /// <para>
    /// Pinned because <c>trellis-api-asp.md</c> now states this outright. A reader who instead
    /// takes the sentinel as evidence that no <c>Error</c> existed will build a client that
    /// branches on a distinction the envelope does not carry.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NotFound_naming_no_reason_reports_the_same_code_and_kind_as_a_route_miss()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For<T>(42)));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        body.RootElement.GetProperty("code").GetString().Should().Be(ValidationCodes.Unspecified);
        body.RootElement.GetProperty("kind").GetString().Should().Be("not-found");

        ProblemEnvelope.KindForStatus(404).Should().Be(
            body.RootElement.GetProperty("kind").GetString(),
            "a route miss seeds its kind from the status alone, and it must land on the same slug "
            + "for the two responses to agree on the envelope");

        body.RootElement.GetProperty("instance").GetString().Should().Be(
            "/ts/42",
            "instance is synthesized from the ResourceRef, so it is the member that does NOT "
            + "collide with a route miss — the envelope claim is about code and kind only");
    }

    /// <remarks>
    /// The other half of the same contract: the sentinel is a default, not a fixed value for the
    /// case. Naming a reason is what makes the two responses tell apart, and it is available on
    /// every error case rather than only the validation ones.
    /// </remarks>
    [Fact]
    public async Task NotFound_carries_the_reason_code_the_handler_named()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.NotFound(ResourceRef.For<T>(42)) { Code = "account.not-found" });

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        body.RootElement.GetProperty("code").GetString().Should().Be("account.not-found");
        body.RootElement.GetProperty("kind").GetString().Should().Be(
            "not-found",
            "the reason names why within the case; it does not change which case answered");
    }
}