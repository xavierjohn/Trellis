namespace Trellis.Core.Tests.Maybes.Extensions;

using Trellis;
using Trellis.Testing;

public class MaybeInvariantTests
{
    #region AllOrNone — 2 values

    [Fact]
    public void AllOrNone2_both_present_returns_success()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.AllOrNone(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AllOrNone2_both_none_returns_success()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.AllOrNone(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AllOrNone2_first_present_second_none_returns_validation_error()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.AllOrNone(first, second, "first", "second");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/second");
    }

    [Fact]
    public void AllOrNone2_first_none_second_present_returns_validation_error()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.AllOrNone(first, second, "first", "second");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/first");
    }

    #endregion

    #region AllOrNone — 3 values

    [Fact]
    public void AllOrNone3_all_present_returns_success()
    {
        // Arrange
        var a = Maybe.From("a");
        var b = Maybe.From(1);
        var c = Maybe.From(true);

        // Act
        var result = MaybeInvariant.AllOrNone(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AllOrNone3_all_none_returns_success()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe<int>.None;
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.AllOrNone(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AllOrNone3_mixed_returns_validation_error_for_missing_fields()
    {
        // Arrange
        var a = Maybe.From("a");
        var b = Maybe<int>.None;
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.AllOrNone(a, b, c, "a", "b", "c");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/b");
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/c");
        validation.Fields.Items.Should().NotContain(fe => fe.Field.Path == "/a");
    }

    #endregion

    #region AllOrNone — 4 values

    [Fact]
    public void AllOrNone4_all_present_returns_success()
    {
        // Arrange
        var a = Maybe.From("a");
        var b = Maybe.From(1);
        var c = Maybe.From(true);
        var d = Maybe.From(3.14);

        // Act
        var result = MaybeInvariant.AllOrNone(a, b, c, d, "a", "b", "c", "d");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AllOrNone4_all_none_returns_success()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe<int>.None;
        var c = Maybe<bool>.None;
        var d = Maybe<double>.None;

        // Act
        var result = MaybeInvariant.AllOrNone(a, b, c, d, "a", "b", "c", "d");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AllOrNone4_one_missing_returns_validation_error()
    {
        // Arrange
        var a = Maybe.From("a");
        var b = Maybe.From(1);
        var c = Maybe<bool>.None;
        var d = Maybe.From(3.14);

        // Act
        var result = MaybeInvariant.AllOrNone(a, b, c, d, "a", "b", "c", "d");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().ContainSingle(fe => fe.Field.Path == "/c");
    }

    #endregion

    #region Requires

    [Fact]
    public void Requires_source_none_returns_success()
    {
        // Arrange
        var source = Maybe<string>.None;
        var required = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.Requires(source, required, "source", "required");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void Requires_both_present_returns_success()
    {
        // Arrange
        var source = Maybe.From("hello");
        var required = Maybe.From(42);

        // Act
        var result = MaybeInvariant.Requires(source, required, "source", "required");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void Requires_source_present_required_none_returns_validation_error()
    {
        // Arrange
        var source = Maybe.From("hello");
        var required = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.Requires(source, required, "source", "required");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().ContainSingle(fe => fe.Field.Path == "/required");
    }

    [Fact]
    public void Requires_source_none_required_present_returns_success()
    {
        // Arrange — required being present when source is absent is fine
        var source = Maybe<string>.None;
        var required = Maybe.From(42);

        // Act
        var result = MaybeInvariant.Requires(source, required, "source", "required");

        // Assert
        result.Should().BeSuccess();
    }

    #endregion

    #region MutuallyExclusive — 2 values

    [Fact]
    public void MutuallyExclusive2_both_none_returns_success()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.MutuallyExclusive(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void MutuallyExclusive2_first_present_returns_success()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.MutuallyExclusive(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void MutuallyExclusive2_second_present_returns_success()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.MutuallyExclusive(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void MutuallyExclusive2_both_present_returns_validation_error()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.MutuallyExclusive(first, second, "first", "second");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/first");
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/second");
    }

    #endregion

    #region MutuallyExclusive — 3 values

    [Fact]
    public void MutuallyExclusive3_none_present_returns_success()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe<int>.None;
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.MutuallyExclusive(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void MutuallyExclusive3_one_present_returns_success()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe.From(42);
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.MutuallyExclusive(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void MutuallyExclusive3_two_present_returns_validation_error()
    {
        // Arrange
        var a = Maybe.From("hello");
        var b = Maybe<int>.None;
        var c = Maybe.From(true);

        // Act
        var result = MaybeInvariant.MutuallyExclusive(a, b, c, "a", "b", "c");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/a");
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/c");
        validation.Fields.Items.Should().NotContain(fe => fe.Field.Path == "/b");
    }

    #endregion

    #region ExactlyOne — 2 values

    [Fact]
    public void ExactlyOne2_first_present_returns_success()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.ExactlyOne(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void ExactlyOne2_second_present_returns_success()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.ExactlyOne(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void ExactlyOne2_both_none_returns_validation_error()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.ExactlyOne(first, second, "first", "second");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/first");
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/second");
    }

    [Fact]
    public void ExactlyOne2_both_present_returns_validation_error()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.ExactlyOne(first, second, "first", "second");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/first");
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/second");
    }

    #endregion

    #region ExactlyOne — 3 values

    [Fact]
    public void ExactlyOne3_one_present_returns_success()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe.From(42);
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.ExactlyOne(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void ExactlyOne3_none_present_returns_validation_error()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe<int>.None;
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.ExactlyOne(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public void ExactlyOne3_two_present_returns_validation_error_only_for_present_fields()
    {
        // Arrange
        var a = Maybe.From("hello");
        var b = Maybe.From(42);
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.ExactlyOne(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
        var error = (Error.UnprocessableContent)result.Error!;
        // Only a and b should be listed, not c (which is absent)
        error.Fields.Items.Should().HaveCount(2);
        error.Fields.Items.Select(e => e.Field.Path).Should().BeEquivalentTo(["/a", "/b"]);
    }

    #endregion

    #region AtLeastOne — 2 values

    [Fact]
    public void AtLeastOne2_first_present_returns_success()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.AtLeastOne(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AtLeastOne2_both_present_returns_success()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.AtLeastOne(first, second, "first", "second");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AtLeastOne2_both_none_returns_validation_error()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.AtLeastOne(first, second, "first", "second");

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/first");
        validation.Fields.Items.Should().Contain(fe => fe.Field.Path == "/second");
    }

    #endregion

    #region AtLeastOne — 3 values

    [Fact]
    public void AtLeastOne3_one_present_returns_success()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe.From(42);
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.AtLeastOne(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void AtLeastOne3_none_present_returns_validation_error()
    {
        // Arrange
        var a = Maybe<string>.None;
        var b = Maybe<int>.None;
        var c = Maybe<bool>.None;

        // Act
        var result = MaybeInvariant.AtLeastOne(a, b, c, "a", "b", "c");

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    #endregion

    #region Parameter Validation

    [Fact]
    public void AllOrNone2_null_first_field_name_throws()
    {
        // Act
        var act = () => MaybeInvariant.AllOrNone(
            Maybe.From("a"), Maybe.From(1), null!, "second");

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("firstFieldName");
    }

    [Fact]
    public void AllOrNone2_null_second_field_name_throws()
    {
        // Act
        var act = () => MaybeInvariant.AllOrNone(
            Maybe.From("a"), Maybe.From(1), "first", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("secondFieldName");
    }

    [Fact]
    public void Requires_null_source_field_name_throws()
    {
        // Act
        var act = () => MaybeInvariant.Requires(
            Maybe.From("a"), Maybe.From(1), null!, "required");

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("sourceFieldName");
    }

    [Fact]
    public void Requires_null_required_field_name_throws()
    {
        // Act
        var act = () => MaybeInvariant.Requires(
            Maybe.From("a"), Maybe.From(1), "source", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("requiredFieldName");
    }

    [Fact]
    public void MutuallyExclusive2_null_field_name_throws()
    {
        // Act
        var act = () => MaybeInvariant.MutuallyExclusive(
            Maybe.From("a"), Maybe.From(1), null!, "second");

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("firstFieldName");
    }

    [Fact]
    public void ExactlyOne2_null_field_name_throws()
    {
        // Act
        var act = () => MaybeInvariant.ExactlyOne(
            Maybe.From("a"), Maybe.From(1), null!, "second");

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("firstFieldName");
    }

    [Fact]
    public void AtLeastOne2_null_field_name_throws()
    {
        // Act
        var act = () => MaybeInvariant.AtLeastOne(
            Maybe.From("a"), Maybe.From(1), null!, "second");

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("firstFieldName");
    }

    #endregion

    #region Error Code Validation

    [Fact]
    public void AllOrNone_failure_uses_standard_validation_error_code()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.AllOrNone(first, second, "first", "second");

        // Assert
        result.Should().BeFailure().Which.Code.Should().Be("unprocessable-content");
    }

    [Fact]
    public void Requires_failure_uses_standard_validation_error_code()
    {
        // Arrange
        var source = Maybe.From("hello");
        var required = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.Requires(source, required, "source", "required");

        // Assert
        result.Should().BeFailure().Which.Code.Should().Be("unprocessable-content");
    }

    [Fact]
    public void MutuallyExclusive_failure_uses_standard_validation_error_code()
    {
        // Arrange
        var first = Maybe.From("hello");
        var second = Maybe.From(42);

        // Act
        var result = MaybeInvariant.MutuallyExclusive(first, second, "first", "second");

        // Assert
        result.Should().BeFailure().Which.Code.Should().Be("unprocessable-content");
    }

    [Fact]
    public void ExactlyOne_failure_uses_standard_validation_error_code()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.ExactlyOne(first, second, "first", "second");

        // Assert
        result.Should().BeFailure().Which.Code.Should().Be("unprocessable-content");
    }

    [Fact]
    public void AtLeastOne_failure_uses_standard_validation_error_code()
    {
        // Arrange
        var first = Maybe<string>.None;
        var second = Maybe<int>.None;

        // Act
        var result = MaybeInvariant.AtLeastOne(first, second, "first", "second");

        // Assert
        result.Should().BeFailure().Which.Code.Should().Be("unprocessable-content");
    }

    #endregion
}
