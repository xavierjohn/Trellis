namespace Trellis.Results.Tests.Results.Extensions;

public class FlattenValidationErrorsTests
{
    [Fact]
    public void FlattenValidationErrors_WithValidationError_ReturnsSameError()
    {
        var validationError = ValidationError.For("email", "Email is required");
        var result = Result.Failure<string>(validationError);

        var flattened = result.FlattenValidationErrors();

        flattened.Should().BeSameAs(validationError);
    }

    [Fact]
    public void FlattenValidationErrors_AggregateWithMultipleValidationErrors_MergesFieldErrors()
    {
        var ve1 = ValidationError.For("email", "Email is required");
        var ve2 = ValidationError.For("name", "Name is required");
        var aggregate = new AggregateError([ve1, ve2]);
        var result = Result.Failure<string>(aggregate);

        var flattened = result.FlattenValidationErrors();

        flattened.Should().NotBeNull();
        flattened!.FieldErrors.Should().HaveCount(2);
        flattened.FieldErrors.Select(f => f.FieldName).Should().Contain("email");
        flattened.FieldErrors.Select(f => f.FieldName).Should().Contain("name");
    }

    [Fact]
    public void FlattenValidationErrors_NestedAggregateErrors_FlattensRecursively()
    {
        var ve1 = ValidationError.For("email", "Email is required");
        var ve2 = ValidationError.For("name", "Name is required");
        var innerAggregate = new AggregateError([ve2]);
        var outerAggregate = new AggregateError([ve1, innerAggregate]);
        var result = Result.Failure<string>(outerAggregate);

        var flattened = result.FlattenValidationErrors();

        flattened.Should().NotBeNull();
        flattened!.FieldErrors.Should().HaveCount(2);
        flattened.FieldErrors.Select(f => f.FieldName).Should().Contain("email");
        flattened.FieldErrors.Select(f => f.FieldName).Should().Contain("name");
    }

    [Fact]
    public void FlattenValidationErrors_AggregateWithNoValidationErrors_ReturnsNull()
    {
        var aggregate = new AggregateError([Error.Unexpected("something broke")]);
        var result = Result.Failure<string>(aggregate);

        var flattened = result.FlattenValidationErrors();

        flattened.Should().BeNull();
    }

    [Fact]
    public void FlattenValidationErrors_NonAggregateNonValidationError_ReturnsNull()
    {
        var result = Result.Failure<string>(Error.Unexpected("something broke"));

        var flattened = result.FlattenValidationErrors();

        flattened.Should().BeNull();
    }

    [Fact]
    public void FlattenValidationErrors_AggregateWithMixedErrors_ExtractsOnlyValidationErrors()
    {
        var ve = ValidationError.For("email", "Email is required");
        var unexpected = Error.Unexpected("something broke");
        var aggregate = new AggregateError([ve, unexpected]);
        var result = Result.Failure<string>(aggregate);

        var flattened = result.FlattenValidationErrors();

        flattened.Should().NotBeNull();
        flattened!.FieldErrors.Should().ContainSingle();
        flattened.FieldErrors[0].FieldName.Should().Be("email");
    }
}
