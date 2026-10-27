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

    [Theory]
    [InlineData(InputLocation.Query)]
    [InlineData(InputLocation.Body)]
    public void GetError_SingleFailure_PreservesRootMetadataAndRelocatesRules(InputLocation location)
    {
        var context = new DefaultHttpContext();
        var source = BoundErrorMetadataTests.MetadataCode.CreateError();

        BoundViolationCollector.AddFrom(context, source, "code", location);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(source.Code);
        result.Detail.Should().Be(source.Detail);
        result.Cause.Should().BeSameAs(source.Cause);
        var pointer = location == InputLocation.Query
            ? InputPointer.ForQuery("code")
            : InputPointer.ForBody("/value");
        result.Fields.Items.Should().ContainSingle().Which.Field.Should().Be(pointer);
        var rule = result.Rules.Items.Should().ContainSingle().Which;
        rule.Fields.Items.Should().Equal(pointer);
        rule.Args.Should().BeSameAs(source.Rules.Items[0].Args);
        source.Fields.Items[0].Field.In.Should().Be(InputLocation.Unspecified);
        source.Rules.Items[0].Fields.Items[0].In.Should().Be(InputLocation.Unspecified);
        BoundViolationCollector.HasViolationFor(context, "code").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetError_MultipleFailures_UsesNeutralRootAndRetainsEachOriginal(bool reverse)
    {
        var context = new DefaultHttpContext();
        (Error.InvalidInput Error, string Name, InputLocation Location)[] bindings =
        [
            (BoundErrorMetadataTests.MetadataCode.CreateError("first") with
                { Code = "order.first", Detail = "First failure." }, "first", InputLocation.Path),
            (BoundErrorMetadataTests.MetadataCode.CreateError("second") with
                { Code = "order.second", Detail = "Second failure." }, "second", InputLocation.Query),
        ];
        if (reverse)
            Array.Reverse(bindings);

        foreach (var binding in bindings)
            BoundViolationCollector.AddFrom(context, binding.Error, binding.Name, binding.Location);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(ValidationCodes.Unspecified);
        result.Detail.Should().BeNull();
        result.Fields.Items.Select(field => field.Field.In).Should().Equal(bindings.Select(binding => binding.Location));
        result.Rules.Items.Select(rule => rule.Fields.Items[0].In).Should().Equal(bindings.Select(binding => binding.Location));
        var originals = result.Cause.Should().BeOfType<Error.Aggregate>().Which.Errors.Items;
        originals.Select(error => error.Code).Should().Equal(bindings.Select(binding => binding.Error.Code));
        for (var index = 0; index < originals.Length; index++)
            originals[index].Should().BeSameAs(bindings[index].Error);
    }

    [Fact]
    public void GetError_NonValidationFailure_RetainsTheOriginalAsCause()
    {
        var context = new DefaultHttpContext();
        var source = new Error.InvariantViolation("order.invalid-scalar")
        {
            Detail = "Invalid scalar.",
            Cause = new Error.Forbidden("orders.restricted"),
        };

        BoundViolationCollector.AddFrom(context, source, "code", InputLocation.Query);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(source.Code);
        result.Detail.Should().Be(source.Detail);
        result.Cause.Should().BeSameAs(source);
        result.Fields.Items.Should().ContainSingle().Which.ReasonCode.Should().Be(source.Code);
    }
}