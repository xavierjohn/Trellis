namespace Trellis.Asp.Tests;

using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Branch coverage for <see cref="ResponseFailureWriter"/>: companion headers (Allow, Retry-After,
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

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    private sealed record T(int Id);

    [Fact]
    public async Task ServiceUnavailable_with_RetryAfter_emits_RetryAfter_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.ServiceUnavailable(RetryAfterValue.FromSeconds(60)));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(503);
        ctx.Response.Headers["Retry-After"].ToString().Should().Be("60");
    }

    [Fact]
    public async Task RangeNotSatisfiable_emits_ContentRange_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.RangeNotSatisfiable(1234));

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
        var r = Result.Fail<T>(new Error.UnprocessableContent(fields));

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
        var r = Result.Fail<T>(new Error.UnprocessableContent(default, rules));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task UnprocessableContent_empty_falls_back_to_plain_problem()
    {
        var ctx = NewContext();
        // No fields, no rules: skips ValidationProblem and writes plain Problem.
        var r = Result.Fail<T>(new Error.UnprocessableContent(default));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task InternalServerError_writes_500_problem_response()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.InternalServerError("FAULT-7") { Detail = "stack trace leak" });

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task TooManyRequests_without_RetryAfter_does_not_emit_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<T>(new Error.TooManyRequests());

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
        var error = new Error.UnprocessableContent(fields)
        {
            Detail = "Sensitive internal context that must not leak.",
        };
        var r = Result.Fail<T>(error);

        await r.ToHttpResponse(t => t, o => o.WithErrorMapping<Error.UnprocessableContent>(500))
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
        var error = new Error.UnprocessableContent(fields)
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
        var r = Result.Fail<T>(new Error.UnprocessableContent(fields));

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
        var r = Result.Fail<T>(new Error.UnprocessableContent(fields));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        var raw = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        raw.Should().Contain("items[0].name");
        raw.Should().NotContain("items/0/name", "JSON Pointer slash form must not appear in the wire `errors` keys");
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
        var r = Result.Fail<T>(new Error.UnprocessableContent(fields));

        await r.ToHttpResponse(t => t).ExecuteAsync(ctx);

        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var errors = body.RootElement.GetProperty("errors");
        var messages = errors.GetProperty("items[0].name").EnumerateArray();
        messages.Should().HaveCount(2);
    }
}