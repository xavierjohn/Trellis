namespace Trellis.Core.Tests.Errors;

using System.Reflection;

/// <summary>
/// <see cref="Error.Code"/> — the one answer to "what reason does a consumer see?".
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no second code member. When the HTTP writer applied a sentinel rule and the
/// mediator's tracing behavior did not, an <see cref="Error.NotFound"/> rendered <c>error.unspecified</c>
/// in the response body and <c>not-found</c> on the span. A code is only worth publishing if an operator
/// can carry it from a bug report into a trace query, which requires every surface to read one member.
/// </para>
/// <para>
/// <see cref="Error.Code"/> defaults to <see cref="ValidationCodes.Unspecified"/> rather than to
/// <see cref="Error.Kind"/>, so no boundary can leak a kind as a reason even by accident — the kind is
/// not in the member to leak.
/// </para>
/// </remarks>
public class ErrorCodeTests
{
    [Fact]
    public void Code_is_the_sentinel_when_the_case_carries_no_reason() =>
        new Error.NotFound(new ResourceRef("Order", "42")).Code
            .Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void Code_never_leaks_the_kind_for_a_case_without_a_reason() =>
        new Error.NotFound(new ResourceRef("Order", "42")).Code
            .Should().NotBe("not-found");

    [Fact]
    public void Code_is_the_sentinel_when_reasons_belong_to_the_children() =>
        new Error.Aggregate(new Error.NotFound(new ResourceRef("Order", "42"))).Code
            .Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void Code_is_the_sentinel_when_reasons_belong_to_the_individual_violations() =>
        Error.InvalidInput.ForField("total", "must-be-positive").Code
            .Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void Code_passes_an_explicit_reason_through() =>
        new Error.Conflict(new ResourceRef("Order", "42"), "order.already-shipped").Code
            .Should().Be("order.already-shipped");

    [Fact]
    public void Code_is_the_reason_once_an_optional_case_names_one() =>
        new Error.NotFound(new ResourceRef("Order", "42")) { Code = "order.archived" }.Code
            .Should().Be("order.archived");

    /// <summary>
    /// The vocabulary freeze constrains Trellis, not the application: a code the framework did not
    /// choose reaches a consumer exactly as its producer spelled it, with no rewriting.
    /// </summary>
    [Fact]
    public void Code_does_not_rewrite_an_application_supplied_reason() =>
        new Error.Conflict(null, "validation.error").Code
            .Should().Be("validation.error");

    /// <summary>
    /// A reason that happens to equal the kind is still a reason, and survives verbatim.
    /// </summary>
    [Fact]
    public void Code_keeps_a_reason_that_restates_the_kind() =>
        new Error.Conflict(null, "conflict").Code
            .Should().Be("conflict", "a producer that deliberately chose its kind keeps it");

    /// <summary>
    /// Every case answers, and none answers with something a consumer cannot switch on. Reflection
    /// rather than a hand-listed set, so a case added later is covered without anyone remembering.
    /// </summary>
    [Fact]
    public void Every_error_case_produces_a_non_blank_code()
    {
        var cases = SampleOfEveryCase();

        cases.Should().NotBeEmpty();
        cases.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Code));
    }

    [Fact]
    public void TransportFault_carrying_a_bare_fault_stays_opaque()
    {
        // Core cannot read an arbitrary transport payload, so it must not guess. Nothing a consumer
        // sees claims to know a code that was never supplied.
        var error = new Error.TransportFault(new SampleTransportFault("http-timeout"));

        error.Code.Should().Be(ValidationCodes.Unspecified);
    }

    [Fact]
    public void TransportFault_carrying_a_coded_fault_publishes_the_faults_own_code()
    {
        // A transport's code is the transport's word, and reaches a consumer exactly as the
        // transport spelled it.
        var error = new Error.TransportFault(new SampleCodedFault("precondition-failed", "upstream.precondition"));

        error.Code.Should().Be("upstream.precondition");
    }
    private sealed record SampleTransportFault(string Name) : ITransportFault;

    private sealed record SampleCodedFault(string Kind, string Code) : ICodedTransportFault;

    private static List<Error> SampleOfEveryCase()
    {
        var resource = new ResourceRef("Order", "42");
        var samples = new List<Error>
        {
            new Error.InvalidInput(EquatableArray<FieldViolation>.Empty),
            new Error.InvariantViolation("order.line-limit-exceeded", resource),
            new Error.NotFound(resource),
            new Error.Gone(resource),
            new Error.Conflict(resource, "order.already-shipped"),
            new Error.AuthenticationRequired(),
            new Error.Forbidden("orders.write", resource),
            new Error.RateLimited(),
            new Error.Unavailable(),
            new Error.Unexpected("boom"),
            new Error.TransportFault(new SampleTransportFault("http-timeout")),
            new Error.Aggregate([new Error.NotFound(resource)]),
        };

        var covered = samples.Select(s => s.GetType()).ToHashSet();
        var declared = typeof(Error).GetNestedTypes(BindingFlags.Public)
            .Where(t => t.IsSubclassOf(typeof(Error)) && !t.IsAbstract)
            .ToArray();

        declared.Should().NotBeEmpty();
        declared.Where(t => !covered.Contains(t)).Select(t => t.Name)
            .Should().BeEmpty("every Error case needs a sample here so its Code is asserted");

        return samples;
    }
}
