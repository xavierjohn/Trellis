namespace Trellis.Core.Tests.Errors;

using System.Reflection;

/// <summary>
/// <see cref="Error.WireCode"/> — the one answer to "what code does a consumer see?".
/// </summary>
/// <remarks>
/// Before this existed, the HTTP writer applied <see cref="Error.HasExplicitCode"/> and the mediator's
/// tracing behavior did not, so an <see cref="Error.NotFound"/> rendered <c>error.unspecified</c> in the
/// response body and <c>not-found</c> on the span. A code is only worth publishing if an operator can
/// carry it from a bug report into a trace query, which requires the two to be the same string.
/// </remarks>
public class ErrorWireCodeTests
{
    [Fact]
    public void WireCode_is_the_sentinel_when_the_case_carries_no_explicit_code() =>
        new Error.NotFound(new ResourceRef("Order", "42")).WireCode
            .Should().Be(ValidationCodes.Unspecified,
                "Code defaults to Kind, and a kind restated as a reason is not a reason");

    [Fact]
    public void WireCode_does_not_leak_the_kind_for_a_case_without_a_code() =>
        new Error.NotFound(new ResourceRef("Order", "42")).WireCode
            .Should().NotBe("not-found");

    [Fact]
    public void WireCode_passes_an_explicit_code_through() =>
        new Error.Conflict(new ResourceRef("Order", "42"), "order.already-shipped").WireCode
            .Should().Be("order.already-shipped");

    [Fact]
    public void WireCode_normalizes_the_legacy_placeholder() =>
        new Error.Conflict(null, ValidationCodes.LegacyUnspecified).WireCode
            .Should().Be(ValidationCodes.Unspecified,
                "a consumer must never see two spellings of 'no reason available'");

    [Fact]
    public void WireCode_reports_a_code_that_happens_to_equal_its_kind() =>
        new Error.Conflict(null, "conflict").WireCode
            .Should().Be("conflict",
                "HasExplicitCode is a presence test, so a producer that deliberately chose its kind keeps it");

    /// <summary>
    /// Every case answers, and none answers with something a consumer cannot switch on. Reflection
    /// rather than a hand-listed set, so a case added later is covered without anyone remembering.
    /// </summary>
    [Fact]
    public void Every_error_case_produces_a_non_blank_wire_code()
    {
        var cases = SampleOfEveryCase();

        cases.Should().NotBeEmpty();
        cases.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.WireCode));
        cases.Where(e => !e.HasExplicitCode)
            .Should().OnlyContain(e => e.WireCode == ValidationCodes.Unspecified);
    }

    [Fact]
    public void TransportFault_carrying_a_bare_fault_stays_opaque()
    {
        // Core cannot read an arbitrary transport payload, so it must not guess. The kind stays
        // available on Code for in-process use, but nothing a consumer sees claims to know a code
        // that was never supplied.
        var error = new Error.TransportFault(new SampleTransportFault("http-timeout"));

        error.HasExplicitCode.Should().BeFalse();
        error.Code.Should().Be("transport-fault", "the in-process value still falls back to the kind");
        error.WireCode.Should().Be(ValidationCodes.Unspecified);
    }

    [Fact]
    public void TransportFault_carrying_a_coded_fault_publishes_the_faults_own_code_unnormalized()
    {
        // A transport's code is the transport's word. ValidationCodes has no jurisdiction over it,
        // so it must reach a consumer exactly as the transport spelled it -- including the legacy
        // placeholder, which means something else entirely in a foreign vocabulary.
        var error = new Error.TransportFault(new SampleCodedFault("precondition-failed", ValidationCodes.LegacyUnspecified));

        error.HasExplicitCode.Should().BeTrue();
        error.Code.Should().Be(ValidationCodes.LegacyUnspecified);
        error.WireCode.Should().Be(ValidationCodes.LegacyUnspecified,
            "normalizing a foreign vocabulary would misreport it as this one");
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
            .Should().BeEmpty("every Error case needs a sample here so its WireCode is asserted");

        return samples;
    }
}
