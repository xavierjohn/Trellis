namespace Trellis.Asp.Tests;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Every failure response carries top-level <c>code</c> and <c>kind</c>, whichever layer wrote it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ResponseFailureWriter</c> is not the only emitter of a Problem Details failure. The binder
/// seams write one before a handler is ever reached, and the idempotency middleware writes one
/// before routing. A client cannot tell which layer answered it, so an envelope member that
/// appears only on some of them is a member no client can rely on — and the documented contract
/// says it can.
/// </para>
/// <para>
/// These tests assert on serialized wire bytes rather than on the in-memory
/// <c>ProblemDetails</c>, because the members live in <c>Extensions</c> and only the writer
/// decides whether they reach the root of the document.
/// </para>
/// </remarks>
public sealed class FailureEnvelopeParityTests
{
    private static DefaultHttpContext NewContext(System.Action<TrellisAspOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (configure is not null)
            services.AddTrellisAsp(configure);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return doc.RootElement.Clone();
    }

    private static void ShouldCarryEnvelope(JsonElement problem, string code, string kind)
    {
        problem.TryGetProperty("code", out var actualCode).Should().BeTrue("every failure response carries a top-level 'code'");
        actualCode.GetString().Should().Be(code);

        problem.TryGetProperty("kind", out var actualKind).Should().BeTrue("every failure response carries a top-level 'kind'");
        actualKind.GetString().Should().Be(kind);
    }

    // ----------------- Minimal API endpoint filter -----------------

    [Fact]
    public async Task EndpointFilter_scalar_rejection_carries_envelope()
    {
        var ctx = NewContext();
        var filter = new ScalarValueValidationEndpointFilter();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok());

        object? result;
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddBodyError("amount", "value.greater-than-or-equal", "Amount cannot be negative.");
            result = await filter.InvokeAsync(new TestFilterContext(ctx), next);
        }

        await ((Microsoft.AspNetCore.Http.IResult)result!).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
        ShouldCarryEnvelope(await ReadProblemAsync(ctx), "error.unspecified", "unprocessable-content");
    }

    [Fact]
    public async Task EndpointFilter_kind_follows_the_error_not_the_mapped_status()
    {
        // MapError moves the status; it does not turn a semantic failure into a different kind.
        // ResponseFailureWriter derives kind from the error for exactly this reason, so a binder
        // seam that derived it from the status would disagree with the handler seam.
        var ctx = NewContext(o => o.MapError<Error.InvalidInput>(400));
        var filter = new ScalarValueValidationEndpointFilter();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok());

        object? result;
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("email", "Email is required.");
            result = await filter.InvokeAsync(new TestFilterContext(ctx), next);
        }

        await ((Microsoft.AspNetCore.Http.IResult)result!).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        ShouldCarryEnvelope(await ReadProblemAsync(ctx), "error.unspecified", "unprocessable-content");
    }

    // ----------------- Binding middleware -----------------

    [Fact]
    public async Task Middleware_semantic_json_failure_carries_envelope()
    {
        var ctx = NewContext();
        ctx.Request.Path = "/accounts";
        var inner = new TrellisJsonValidationException("Amount cannot be negative.");
        typeof(JsonException).GetProperty("Path")!.SetValue(inner, "$.initialDeposit.amount");
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest, inner);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
        ShouldCarryEnvelope(await ReadProblemAsync(ctx), "error.unspecified", "unprocessable-content");
    }

    [Fact]
    public async Task Middleware_malformed_json_carries_bad_request_kind()
    {
        // Not an Error.InvalidInput: the bytes never parsed, so no value was rejected. There is
        // no error object to derive a kind from, and the HTTP condition is the honest answer.
        var ctx = NewContext();
        ctx.Request.Path = "/accounts";
        var bre = new BadHttpRequestException(
            "Failed to read body",
            StatusCodes.Status400BadRequest,
            new JsonException("Unexpected token."));

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        ShouldCarryEnvelope(await ReadProblemAsync(ctx), "error.unspecified", "bad-request");
    }

    [Fact]
    public async Task Middleware_unrecognized_bad_request_carries_envelope()
    {
        var ctx = NewContext();
        ctx.Request.Path = "/accounts";
        var bre = new BadHttpRequestException("Failed to read body", StatusCodes.Status400BadRequest);

        var middleware = new ScalarValueValidationMiddleware(_ => throw bre);
        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        ShouldCarryEnvelope(await ReadProblemAsync(ctx), "error.unspecified", "bad-request");
    }

    // ----------------- MVC action filter -----------------

    [Fact]
    public void MvcFilter_scalar_rejection_carries_envelope()
    {
        var filter = new ScalarValueValidationFilter();
        var context = NewActionExecutingContext();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddBodyError("amount", "value.greater-than-or-equal", "Amount cannot be negative.");
            filter.OnActionExecuting(context);
        }

        var problem = ShouldBeProblem(context, expectedStatus: 422);
        problem.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("error.unspecified");
        problem.Extensions.Should().ContainKey("kind").WhoseValue.Should().Be("unprocessable-content");
    }

    [Fact]
    public void MvcFilter_model_binding_failure_carries_bad_request_kind()
    {
        var filter = new ScalarValueValidationFilter();
        var context = NewActionExecutingContext();
        context.ModelState.AddModelError("id", "The value 'abc' is not valid.");

        using (ValidationErrorsContext.BeginScope())
        {
            filter.OnActionExecuting(context);
        }

        var problem = ShouldBeProblem(context, expectedStatus: 400);
        problem.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("error.unspecified");
        problem.Extensions.Should().ContainKey("kind").WhoseValue.Should().Be("bad-request");
    }

    /// <remarks>
    /// The body-deserialization path, which is how a real MVC request reaches this filter: the
    /// converter throws, MVC records the exception in ModelState, and the filter rebuilds the
    /// problem from it. This is a distinct emitter from the ambient-scope path above, and it is
    /// the one the Showcase replay caught still answering with no envelope.
    /// </remarks>
    [Fact]
    public void MvcFilter_json_body_rejection_carries_envelope()
    {
        var filter = new ScalarValueValidationFilter();
        var context = NewActionExecutingContext();
        var rejected = Error.InvalidInput.ForField("/initialDeposit/amount", "value.greater-than-or-equal", "Amount cannot be negative.");
        context.ModelState.AddModelError(
            "request",
            new TrellisJsonValidationException("Amount cannot be negative.") { InvalidInput = rejected },
            metadata: new EmptyModelMetadataProvider().GetMetadataForType(typeof(object)));

        using (ValidationErrorsContext.BeginScope())
        {
            filter.OnActionExecuting(context);
        }

        var problem = ShouldBeProblem(context, expectedStatus: 422);
        problem.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("error.unspecified");
        problem.Extensions.Should().ContainKey("kind").WhoseValue.Should().Be("unprocessable-content");
    }

    // ----------------- Idempotency middleware -----------------    //
    // That seam hand-serializes its Problem Details before routing, so it never reaches
    // ResponseFailureWriter. Its envelope is asserted in IdempotencyMiddlewareTests, next to the
    // existing assertions on the codes it emits.

    // ----------------- AddTrellisProblemDetails customization -----------------
    //
    // The exception handler and status-code pages write Problem Details that no Trellis writer
    // ever touched. Those failures have no Error behind them, so the status is all there is.

    [Fact]
    public void ProblemDetailsCustomization_seeds_the_envelope_when_no_writer_supplied_one()
    {
        var problem = CustomizeProblem(new ProblemDetails { Status = 500 });

        problem.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("error.unspecified");
        problem.Extensions.Should().ContainKey("kind").WhoseValue.Should().Be("internal-server-error");
    }

    /// <remarks>
    /// The hook also runs over problems a Trellis writer produced. Their <c>kind</c> came from
    /// the error, which is the better answer, so seeding must not overwrite it — otherwise a
    /// remapped `Error.InvalidInput` would be relabelled by the status it happens to land on.
    /// </remarks>
    [Fact]
    public void ProblemDetailsCustomization_leaves_an_existing_envelope_alone()
    {
        var written = new ProblemDetails { Status = 400 };
        ProblemEnvelope.Apply(written.Extensions, "account.frozen", "unprocessable-content");

        var problem = CustomizeProblem(written);

        problem.Extensions["code"].Should().Be("account.frozen");
        problem.Extensions["kind"].Should().Be("unprocessable-content");
    }

    /// <remarks>
    /// A document that arrives with only one of the two members is the case a paired
    /// <c>ContainsKey</c> guard misses: it reads as "someone already supplied an envelope"
    /// and leaves the response one member short of the invariant. Each member is therefore
    /// seeded independently.
    /// </remarks>
    [Theory]
    [InlineData("code", "kind", "unprocessable-content")]
    [InlineData("kind", "code", "error.unspecified")]
    public void ProblemDetailsCustomization_seeds_the_member_a_partial_envelope_is_missing(
        string presentMember,
        string missingMember,
        string expectedSeededValue)
    {
        var written = new ProblemDetails { Status = 422 };
        written.Extensions[presentMember] = presentMember == "code" ? "account.frozen" : "unprocessable-content";

        var problem = CustomizeProblem(written);

        problem.Extensions.Should().ContainKey(missingMember).WhoseValue.Should().Be(expectedSeededValue);
        problem.Extensions[presentMember].Should().Be(presentMember == "code" ? "account.frozen" : "unprocessable-content");
    }

    // ----------------- `type` parity -----------------
    //
    // `type` sits directly above the envelope members and drifts the same way. A seam that
    // hand-writes `about:blank` answers a 422 differently from the writer that resolves the
    // status URI, and a client cannot tell which layer replied.

    [Theory]
    [InlineData(400, "https://tools.ietf.org/html/rfc9110#section-15.5.1")]
    [InlineData(409, "https://tools.ietf.org/html/rfc9110#section-15.5.10")]
    [InlineData(422, "https://tools.ietf.org/html/rfc4918#section-11.2")]
    public void ProblemTypeForStatus_resolves_the_uri_the_response_writer_uses(int status, string expected) =>
        ProblemEnvelope.ProblemTypeForStatus(status).Should().Be(expected);

    /// <remarks>
    /// Several statuses Trellis emits have no framework default. RFC 9457 §3.1.1 makes an absent
    /// <c>type</c> equivalent to <c>about:blank</c>, so omitting it is correct; writing a bare
    /// kind slug into a member declared to be a URI reference is not.
    /// </remarks>
    [Theory]
    [InlineData(429)]
    [InlineData(428)]
    [InlineData(451)]
    public void ProblemTypeForStatus_is_absent_where_the_framework_has_no_default(int status) =>
        ProblemEnvelope.ProblemTypeForStatus(status).Should().BeNull();

    [Fact]
    public void ProblemDetailsCustomization_does_not_label_a_success_as_a_failure()
    {
        var problem = CustomizeProblem(new ProblemDetails { Status = 200 });

        problem.Extensions.Should().NotContainKey("code");
        problem.Extensions.Should().NotContainKey("kind");
    }

    private static ProblemDetails CustomizeProblem(ProblemDetails problemDetails)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrellisProblemDetails();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Response.StatusCode = problemDetails.Status ?? 200;

        options.CustomizeProblemDetails!(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });

        return problemDetails;
    }

    private static ActionExecutingContext NewActionExecutingContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);
    }

    private static ProblemDetails ShouldBeProblem(ActionExecutingContext context, int expectedStatus)
    {
        context.Result.Should().BeOfType<ProblemDetailsActionResult>();
        var objectResult = (ProblemDetailsActionResult)context.Result!;
        objectResult.StatusCode.Should().Be(expectedStatus);
        objectResult.Value.Should().BeAssignableTo<ProblemDetails>();
        return (ProblemDetails)objectResult.Value!;
    }

    private sealed class TestFilterContext(HttpContext httpContext) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = httpContext;

        public override IList<object?> Arguments => [];

        public override T GetArgument<T>(int index) => default!;
    }
}
