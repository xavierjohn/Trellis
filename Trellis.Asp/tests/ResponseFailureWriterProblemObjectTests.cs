namespace Trellis.Asp.Tests;

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Problem-object level wire contract: the Aggregate <c>errors</c> array renamed to
/// <c>problems</c>, child extensions projected so a nested <see cref="Error.InvalidInput"/>
/// keeps its own <c>errors</c> map and <c>rules</c>, child <c>type</c> emitted as a URI,
/// and the <c>error.unspecified</c> sentinel applied wherever an error carries no explicit code.
/// </summary>
/// <remarks>
/// Pins invariant 4 (the sentinel is the only string meaning "no finer reason available", at
/// every code-bearing site) and invariant 7 (<c>errors</c> means exactly one thing across the
/// API surface).
/// </remarks>
public sealed class ResponseFailureWriterProblemObjectTests
{
    private const string Sentinel = "error.unspecified";
    private const string LegacyAlias = "validation.error";

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

    private static async Task<JsonDocument> ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> WriteAsync(Error error)
    {
        var ctx = NewContext();
        await Result.Fail<T>(error).ToHttpResponse(t => t).ExecuteAsync(ctx);
        return await ReadBody(ctx);
    }

    // ----------------- errors -> problems (invariant 7) -----------------

    [Fact]
    public async Task Aggregate_children_are_emitted_under_problems_and_never_under_errors()
    {
        // errors must mean exactly one thing across the surface: a flat field -> messages map.
        // The aggregate child array previously collided with it under the same member name.
        using var body = await WriteAsync(new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "42")),
            new Error.Gone(ResourceRef.For("Item", "7"))));

        body.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        var problems = body.RootElement.GetProperty("problems");
        problems.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Aggregate_child_type_is_the_same_uri_the_child_would_carry_at_root()
    {
        // RFC 9457 section 3.1.1 expects type to be a URI. Rather than minting a Trellis-specific
        // namespace, a child carries the framework's default problem type for its own status —
        // the same URI that status yields at the root. This test runs without an
        // IProblemDetailsService customization; CustomizeProblemDetails is deliberately not
        // replayed per child (see DefaultProblemTypeForStatus for why).
        var notFound = new Error.NotFound(ResourceRef.For("Item", "42"));

        using var standalone = await WriteAsync(notFound);
        var expected = standalone.RootElement.GetProperty("type").GetString();

        using var aggregated = await WriteAsync(new Error.Aggregate(notFound));
        var childType = aggregated.RootElement.GetProperty("problems")[0].GetProperty("type").GetString();

        childType.Should().Be(expected);
        childType.Should().NotBeNull();
        Uri.IsWellFormedUriString(childType, UriKind.Absolute).Should().BeTrue(
            "RFC 9457 section 3.1.1 requires type to be a URI, not a bare slug");
    }

    [Fact]
    public async Task Aggregate_child_keeps_its_kind_slug_even_though_type_is_now_a_uri()
    {
        // kind remains the machine-readable slug; only type changes shape.
        using var body = await WriteAsync(new Error.Aggregate(new Error.Gone(ResourceRef.For("Item", "7"))));

        body.RootElement.GetProperty("problems")[0].GetProperty("kind").GetString().Should().Be("gone");
    }

    // ----------------- child extension projection -----------------

    [Fact]
    public async Task Aggregate_child_InvalidInput_retains_its_own_errors_map()
    {
        // Previously the parent called BuildExtensions(parent, default, ...), so a nested
        // InvalidInput lost its field violations entirely.
        var fields = EquatableArray.Create(
            new FieldViolation(new InputPointer("/email"), "string.email", null, "must be email"));

        using var body = await WriteAsync(new Error.Aggregate(
            new Error.NotFound(ResourceRef.For("Item", "42")),
            new Error.InvalidInput(fields)));

        var child = body.RootElement.GetProperty("problems")[1];
        child.GetProperty("errors").GetProperty("email")[0].GetString().Should().Be("must be email");
    }

    [Fact]
    public async Task Aggregate_child_InvalidInput_retains_its_rules()
    {
        var rules = EquatableArray.Create(new RuleViolation("date-range.invalid", Detail: "End must follow start."));

        using var body = await WriteAsync(new Error.Aggregate(
            new Error.InvalidInput(default, rules)));

        var child = body.RootElement.GetProperty("problems")[0];
        var childRules = child.GetProperty("ruleViolations");
        childRules.GetArrayLength().Should().Be(1);
        childRules[0].GetProperty("code").GetString().Should().Be("date-range.invalid");
    }

    [Fact]
    public async Task Aggregate_child_InvalidInput_with_only_rules_still_carries_an_empty_errors_map()
    {
        // Standalone, a rules-only InvalidInput renders via Results.ValidationProblem, and
        // HttpValidationProblemDetails.Errors is a declared property, so `errors: {}` is always
        // present. A child must not be structurally different from the same error at root, or a
        // client that reads `problem.errors` has to special-case nesting.
        var rules = EquatableArray.Create(new RuleViolation("date-range.invalid", Detail: "End must follow start."));
        var error = new Error.InvalidInput(default, rules);

        using var standalone = await WriteAsync(error);
        standalone.RootElement.TryGetProperty("errors", out var rootErrors).Should().BeTrue();
        rootErrors.EnumerateObject().Should().BeEmpty();

        using var body = await WriteAsync(new Error.Aggregate(error));
        var child = body.RootElement.GetProperty("problems")[0];
        child.TryGetProperty("errors", out var childErrors)
            .Should().BeTrue("a validation-shaped child carries `errors` even when empty");
        childErrors.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task Aggregate_child_type_never_degrades_to_a_bare_slug_for_a_status_with_no_default_uri()
    {
        // ASP.NET Core has no default problem type for several statuses Trellis actually emits --
        // notably 429 (RateLimited) and 428 (PreconditionRequired). The child must not silently
        // fall back to the kind slug there, which is the very shape this change removes.
        // RFC 9457 section 3.1.1 makes an absent `type` equivalent to "about:blank", so matching
        // the root's own absence is both correct and keeps child and root consistent.
        var rateLimited = new Error.RateLimited();

        using var standalone = await WriteAsync(rateLimited);
        var rootHasType = standalone.RootElement.TryGetProperty("type", out var rootType);

        using var aggregated = await WriteAsync(new Error.Aggregate(rateLimited));
        var child = aggregated.RootElement.GetProperty("problems")[0];
        child.GetProperty("status").GetInt32().Should().Be(429);

        var childHasType = child.TryGetProperty("type", out var childType);
        childHasType.Should().Be(rootHasType, "a child must carry `type` exactly when the root does");

        if (childHasType)
        {
            childType.GetString().Should().Be(rootType.GetString());
            Uri.IsWellFormedUriString(childType.GetString(), UriKind.Absolute).Should().BeTrue();
        }

        // The regression guard proper: whatever happens, `type` is never the kind slug.
        child.GetProperty("kind").GetString().Should().Be("too-many-requests");
        if (childHasType)
            childType.GetString().Should().NotBe("too-many-requests");
    }

    [Fact]
    public async Task Aggregate_child_Unexpected_retains_its_faultId()
    {
        using var body = await WriteAsync(new Error.Aggregate(
            new Error.Unexpected("unhandled-exception", FaultId: "fault-123")));

        body.RootElement.GetProperty("problems")[0].GetProperty("faultId").GetString().Should().Be("fault-123");
    }

    // ----------------- the sentinel (invariant 4) -----------------

    [Fact]
    public async Task Root_code_degrades_to_the_sentinel_when_the_error_carries_no_explicit_code()
    {
        // NotFound and Gone previously round-tripped their kind slug as a code, which is a
        // kind restated, not a reason.
        using var body = await WriteAsync(new Error.NotFound(ResourceRef.For("Item", "42")));

        body.RootElement.GetProperty("code").GetString().Should().Be(Sentinel);
        body.RootElement.GetProperty("kind").GetString().Should().Be("not-found");
    }

    [Fact]
    public async Task Child_code_degrades_to_the_sentinel_when_the_child_carries_no_explicit_code()
    {
        using var body = await WriteAsync(new Error.Aggregate(new Error.Gone(ResourceRef.For("Item", "7"))));

        body.RootElement.GetProperty("problems")[0].GetProperty("code").GetString().Should().Be(Sentinel);
    }

    [Fact]
    public async Task Explicit_code_equal_to_its_own_kind_is_still_emitted_verbatim()
    {
        // HasExplicitCode is a presence test, never a value comparison. A payload whose code
        // happens to equal its kind is explicit and must survive.
        using var body = await WriteAsync(new Error.Conflict(null, "conflict"));

        body.RootElement.GetProperty("code").GetString().Should().Be("conflict");
    }

    [Fact]
    public async Task Explicit_invariant_violation_code_equal_to_its_own_kind_is_emitted_verbatim()
    {
        using var body = await WriteAsync(new Error.InvariantViolation("invariant-violation"));

        body.RootElement.GetProperty("code").GetString().Should().Be("invariant-violation");
    }

    /// <summary>
    /// The wire and the span have to spell a code identically, or an operator cannot carry one from
    /// a bug report into a trace query. Both now read <see cref="Error.WireCode"/>; this asserts the
    /// HTTP end of that contract, and <c>TracingBehaviorTests</c> asserts the other.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCodeBearingCase))]
    public async Task The_root_code_is_always_Error_WireCode(Error error)
    {
        using var body = await WriteAsync(error);

        body.RootElement.GetProperty("code").GetString().Should().Be(error.WireCode);
    }

    public static TheoryData<Error> EveryCodeBearingCase()
    {
        var resource = new ResourceRef("Order", "42");
        return
        [
            new Error.InvalidInput(EquatableArray<FieldViolation>.Empty),
            new Error.InvariantViolation("order.line-limit-exceeded", resource),
            new Error.NotFound(resource),
            new Error.Gone(resource),
            new Error.Conflict(resource, "order.already-shipped"),
            new Error.Conflict(null, ValidationCodes.LegacyUnspecified),
            new Error.AuthenticationRequired(),
            new Error.Forbidden("orders.write", resource),
            new Error.RateLimited(),
            new Error.Unavailable(),
            new Error.Unavailable("maintenance-window"),
            new Error.Unexpected("boom"),
        ];
    }

    [Fact]
    public async Task Nullable_code_sources_degrade_when_absent_and_survive_when_present()
    {
        using var absent = await WriteAsync(new Error.Unavailable());
        absent.RootElement.GetProperty("code").GetString().Should().Be(Sentinel);

        using var present = await WriteAsync(new Error.Unavailable("maintenance-window"));
        present.RootElement.GetProperty("code").GetString().Should().Be("maintenance-window");
    }

    // ----------------- legacy alias normalization -----------------

    [Fact]
    public async Task Legacy_alias_is_normalized_at_the_root_code()
    {
        using var body = await WriteAsync(new Error.InvariantViolation(LegacyAlias));

        body.RootElement.GetProperty("code").GetString().Should().Be(Sentinel);
    }

    [Fact]
    public async Task Legacy_alias_is_normalized_in_a_problems_child_code()
    {
        using var body = await WriteAsync(new Error.Aggregate(new Error.InvariantViolation(LegacyAlias)));

        body.RootElement.GetProperty("problems")[0].GetProperty("code").GetString().Should().Be(Sentinel);
    }

    [Fact]
    public async Task Legacy_alias_is_normalized_in_a_rule_violation_code()
    {
        // Without this, one payload could carry root error.unspecified beside rules[].code
        // validation.error - two live sentinels, which invariant 4 forbids.
        var rules = EquatableArray.Create(new RuleViolation(LegacyAlias, Detail: "Something is wrong."));

        using var body = await WriteAsync(new Error.InvalidInput(default, rules));

        body.RootElement.GetProperty("ruleViolations")[0].GetProperty("code").GetString().Should().Be(Sentinel);
    }

    [Fact]
    public async Task Non_legacy_rule_violation_codes_are_untouched()
    {
        var rules = EquatableArray.Create(new RuleViolation("date-range.invalid", Detail: "End must follow start."));

        using var body = await WriteAsync(new Error.InvalidInput(default, rules));

        body.RootElement.GetProperty("ruleViolations")[0].GetProperty("code").GetString().Should().Be("date-range.invalid");
    }

    // ----------------- transport faults bypass the rule -----------------

    [Fact]
    public async Task TransportFault_code_bypasses_both_the_sentinel_and_normalization()
    {
        // HttpError.Code is an HTTP precondition name, not a domain code. It is excluded from
        // the vocabulary and must reach the wire unchanged.
        var fault = new Error.TransportFault(
            new HttpError.PreconditionFailed(ResourceRef.For("Item", "42"), PreconditionKind.IfMatch));

        using var body = await WriteAsync(fault);

        body.RootElement.GetProperty("code").GetString().Should().Be("IfMatch");
    }

    [Fact]
    public void TransportFault_WireCode_agrees_with_the_body_for_a_real_HttpError()
    {
        // The span tag reads Error.WireCode while the body goes through the writer. This pins the
        // two together for the one case that bypasses the sentinel, and in doing so asserts that
        // HttpError really does implement ICodedTransportFault — if it regressed to a bare
        // ITransportFault the wire code would silently fall back to the sentinel.
        var fault = new Error.TransportFault(
            new HttpError.PreconditionFailed(ResourceRef.For("Item", "42"), PreconditionKind.IfMatch));

        fault.WireCode.Should().Be("IfMatch");
        fault.HasExplicitCode.Should().BeTrue();
    }
}
