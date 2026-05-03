namespace Trellis.Asp.Tests;

using System.Collections.Immutable;
using System.IO;
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
}