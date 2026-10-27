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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GetError_DuplicateFailure_PreservesSingleRoot(bool rulesOnly, bool aggregateCause)
    {
        var context = new DefaultHttpContext();
        var original = BoundErrorMetadataTests.MetadataCode.CreateError("code", rulesOnly);
        var duplicate = BoundErrorMetadataTests.MetadataCode.CreateError("code", rulesOnly);
        if (aggregateCause)
        {
            original = original with { Cause = new Error.Aggregate(original.Cause!) };
            duplicate = duplicate with { Cause = new Error.Aggregate(duplicate.Cause!) };
        }

        BoundViolationCollector.AddFrom(context, original, "code", InputLocation.Query);
        BoundViolationCollector.AddFrom(context, original, "code", InputLocation.Query);
        BoundViolationCollector.AddFrom(context, duplicate, "code", InputLocation.Query);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(original.Code);
        result.Detail.Should().Be(original.Detail);
        result.Cause.Should().BeSameAs(original.Cause);
        result.Fields.Items.Should().HaveCount(rulesOnly ? 0 : 1);
        result.Rules.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData(InputLocation.Query, false)]
    [InlineData(InputLocation.Path, false)]
    [InlineData(InputLocation.Header, false)]
    [InlineData(InputLocation.Query, true)]
    public void GetError_CaseOnlyNamedBindings_PreservesSingleRoot(InputLocation location, bool rulesOnly)
    {
        var context = new DefaultHttpContext();
        var original = BoundErrorMetadataTests.MetadataCode.CreateError("code", rulesOnly);
        var duplicate = BoundErrorMetadataTests.MetadataCode.CreateError("CODE", rulesOnly);

        BoundViolationCollector.AddFrom(context, original, "code", location);
        BoundViolationCollector.AddFrom(context, duplicate, "CODE", location);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(original.Code);
        result.Detail.Should().Be(original.Detail);
        result.Cause.Should().BeSameAs(original.Cause);
        result.Fields.Length.Should().Be(rulesOnly ? 0 : 1);
        var pointer = location switch
        {
            InputLocation.Path => InputPointer.ForPath("code"),
            InputLocation.Header => InputPointer.ForHeader("code"),
            _ => InputPointer.ForQuery("code"),
        };
        result.Rules.Items.Should().ContainSingle().Which.Fields.Items.Should().Equal(pointer);
    }

    [Theory]
    [InlineData(InputLocation.Body)]
    [InlineData(InputLocation.Unspecified)]
    public void GetError_CaseOnlyOtherBindings_RemainsIndependent(InputLocation location)
    {
        var context = new DefaultHttpContext();
        var source = Error.InvalidInput.ForRule("order.code-policy") with
        {
            Code = BoundErrorMetadataTests.MetadataCode.RootCode,
        };

        BoundViolationCollector.AddFrom(context, source, "code", location);
        BoundViolationCollector.AddFrom(context, source, "CODE", location);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(ValidationCodes.Unspecified);
        result.Cause.Should().BeOfType<Error.Aggregate>().Which.Errors.Length.Should().Be(2);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("field")]
    [InlineData("cause")]
    public void GetError_CaseOnlyNamedBindings_DifferentMetadata_RemainsIndependent(string difference)
    {
        var context = new DefaultHttpContext();
        var first = BoundErrorMetadataTests.MetadataCode.CreateError("code");
        var second = BoundErrorMetadataTests.MetadataCode.CreateError("CODE");
        if (difference == "root")
            second = second with { Code = "order.other-code" };
        else if (difference == "field")
        {
            first = first with { Fields = EquatableArray.Create(first.Fields[0] with { Field = InputPointer.ForProperty("leaf") }) };
            second = second with { Fields = EquatableArray.Create(second.Fields[0] with { Field = InputPointer.ForProperty("LEAF") }) };
        }
        else
            second = second with { Cause = new Error.Forbidden("orders.restricted") { Detail = "Other policy context." } };

        BoundViolationCollector.AddFrom(context, first, "code", InputLocation.Query);
        BoundViolationCollector.AddFrom(context, second, "CODE", InputLocation.Query);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(ValidationCodes.Unspecified);
        var originals = result.Cause.Should().BeOfType<Error.Aggregate>().Which.Errors.Items;
        originals.Should().HaveCount(2);
        originals[0].Should().BeSameAs(first);
        originals[1].Should().BeSameAs(second);
    }

    [Theory]
    [InlineData(InputLocation.Query)]
    [InlineData(InputLocation.Path)]
    [InlineData(InputLocation.Header)]
    [InlineData(InputLocation.Body)]
    [InlineData(InputLocation.Unspecified)]
    public void GetError_RelocatedRuleFields_ContainsEachLocationOnce(InputLocation location)
    {
        var context = new DefaultHttpContext();
        var first = InputPointer.ForProperty("first");
        var second = InputPointer.ForProperty("second");
        var source = BoundErrorMetadataTests.MetadataCode.CreateError() with
        {
            Rules = EquatableArray.Create(new RuleViolation(
                "order.code-policy", EquatableArray.Create(first, second, first))),
        };

        BoundViolationCollector.AddFrom(context, source, "code", location);

        InputPointer[] expected = location switch
        {
            InputLocation.Query => [InputPointer.ForQuery("code")],
            InputLocation.Path => [InputPointer.ForPath("code")],
            InputLocation.Header => [InputPointer.ForHeader("code")],
            InputLocation.Body => [InputPointer.ForBody("/first"), InputPointer.ForBody("/second")],
            _ => [first, second],
        };
        BoundViolationCollector.GetError(context)!.Rules.Items.Should().ContainSingle()
            .Which.Fields.Items.Should().Equal(expected);
        source.Rules[0].Fields.Items.Should().Equal(first, second, first);
    }

    [Theory]
    [InlineData("other", InputLocation.Query)]
    [InlineData("code", InputLocation.Header)]
    public void GetError_SameGlobalRule_DifferentBindings_RemainsIndependent(string secondName, InputLocation secondLocation)
    {
        var context = new DefaultHttpContext();
        var source = Error.InvalidInput.ForRule("order.code-policy") with
        {
            Code = BoundErrorMetadataTests.MetadataCode.RootCode,
            Detail = BoundErrorMetadataTests.MetadataCode.RootDetail,
        };

        BoundViolationCollector.AddFrom(context, source, "code", InputLocation.Query);
        BoundViolationCollector.AddFrom(context, source, secondName, secondLocation);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(ValidationCodes.Unspecified);
        result.Detail.Should().BeNull();
        result.Fields.Items.Should().BeEmpty();
        result.Rules.Items.Should().ContainSingle().Which.Fields.Items.Should().BeEmpty();
        var originals = result.Cause.Should().BeOfType<Error.Aggregate>().Which.Errors.Items;
        originals.Should().HaveCount(2);
        originals[0].Should().BeSameAs(source);
        originals[1].Should().BeSameAs(source);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("detail")]
    [InlineData("field")]
    [InlineData("rule")]
    public void GetError_SameBinding_DifferentPayloads_RemainsIndependent(string difference)
    {
        var context = new DefaultHttpContext();
        var first = BoundErrorMetadataTests.MetadataCode.CreateError("code");
        var second = difference switch
        {
            "code" => first with { Code = "order.other-code" },
            "detail" => first with { Detail = "Different root detail." },
            "field" => first with
            {
                Fields = EquatableArray.Create(first.Fields[0] with { ReasonCode = "order.other-prefix" }),
            },
            _ => first with
            {
                Rules = EquatableArray.Create(first.Rules[0] with { ReasonCode = "order.other-policy" }),
            },
        };

        BoundViolationCollector.AddFrom(context, first, "code", InputLocation.Query);
        BoundViolationCollector.AddFrom(context, second, "code", InputLocation.Query);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(ValidationCodes.Unspecified);
        result.Detail.Should().BeNull();
        var originals = result.Cause.Should().BeOfType<Error.Aggregate>().Which.Errors.Items;
        originals.Should().HaveCount(2);
        originals[0].Should().BeSameAs(first);
        originals[1].Should().BeSameAs(second);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("nested")]
    [InlineData("aggregate")]
    public void GetError_SameBinding_DifferentCauses_RetainsBothOriginals(string causeShape)
    {
        Error CreateCause(string detail)
        {
            Error cause = new Error.Forbidden("orders.restricted") { Detail = detail };
            if (causeShape != "direct")
                cause = new Error.Forbidden("orders.policy") { Cause = cause };
            return causeShape == "aggregate" ? new Error.Aggregate(cause) : cause;
        }

        var context = new DefaultHttpContext();
        var first = BoundErrorMetadataTests.MetadataCode.CreateError("code") with { Cause = CreateCause("First context.") };
        var second = first with { Cause = CreateCause("Second context.") };
        first.Should().Be(second);

        BoundViolationCollector.AddFrom(context, first, "code", InputLocation.Query);
        BoundViolationCollector.AddFrom(context, second, "code", InputLocation.Query);

        var result = BoundViolationCollector.GetError(context)!;
        result.Code.Should().Be(ValidationCodes.Unspecified);
        result.Detail.Should().BeNull();
        var originals = result.Cause.Should().BeOfType<Error.Aggregate>().Which.Errors.Items;
        originals.Should().HaveCount(2);
        originals[0].Should().BeSameAs(first);
        originals[1].Should().BeSameAs(second);
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