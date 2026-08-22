namespace Trellis.Asp.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Pins the reason code that <see cref="BoundViolationCollector"/> synthesizes when a bound value
/// fails with something other than <see cref="Error.InvalidInput"/>.
/// </summary>
public sealed class BoundViolationCollectorCodeTests
{
    [Fact]
    public void AddFrom_WholeError_RecordsTheCode_NotTheKind()
    {
        // This violation is published as fieldViolations[].code in problem+json, so it is an
        // operator- and client-facing code and must obey the same rule as the root code: it spells
        // the reason the producer named, never the case the error fell into. A NotFound that named
        // no reason therefore records the sentinel — not "not-found", which is not a member of the
        // reason-code vocabulary at all, so a client matching on it would be matching a value the
        // vocabulary never promised.
        var context = new DefaultHttpContext();

        BoundViolationCollector.AddFrom(
            context,
            new Error.NotFound(ResourceRef.For("Order", "42")),
            "orderId",
            InputLocation.Path);

        var violation = BoundViolationCollector.Get(context).Should().ContainSingle().Subject;
        violation.ReasonCode.Should().Be(ValidationCodes.Unspecified);
    }

    [Fact]
    public void AddFrom_WholeError_KeepsAnExplicitCode()
    {
        var context = new DefaultHttpContext();

        BoundViolationCollector.AddFrom(
            context,
            new Error.InvariantViolation(ValidationCodes.FormatInteger),
            "quantity",
            InputLocation.Query);

        var violation = BoundViolationCollector.Get(context).Should().ContainSingle().Subject;
        violation.ReasonCode.Should().Be(ValidationCodes.FormatInteger);
    }
}
