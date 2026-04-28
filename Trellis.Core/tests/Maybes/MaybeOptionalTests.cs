namespace Trellis.Core.Tests.Maybes;

using Trellis;
using Trellis.Testing;

/// <summary>
/// Tests for the Maybe.Optional method which transforms optional primitive types 
/// to strongly typed value objects.
/// </summary>
public class MaybeOptionalTests
{
    #region Maybe.Optional with null input

    [Fact]
    public void Optional_WithNullString_ReturnsMaybeNone()
    {
        // Arrange
        string? nullString = null;

        // Act
        var result = Maybe.Optional(nullString, str => Result.Ok(str.ToUpperInvariant()));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeFalse();
    }

    [Fact]
    public void Optional_WithNullNullableInt_ReturnsMaybeNone()
    {
        // Arrange
        int? nullInt = null;

        // Act - Return a non-nullable wrapper type
        var result = Maybe.Optional<int, WrappedInt>(nullInt, num => Result.Ok(new WrappedInt(num * 2)));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeFalse();
    }

    [Fact]
    public void Optional_WithNullNullableGuid_ReturnsMaybeNone()
    {
        // Arrange
        Guid? nullGuid = null;

        // Act
        var result = Maybe.Optional<Guid, string>(nullGuid, g => Result.Ok(g.ToString()));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeFalse();
    }

    #endregion

    #region Maybe.Optional with value and successful function

    [Fact]
    public void Optional_WithValueAndSuccessfulFunction_ReturnsMaybeWithValue()
    {
        // Arrange
        string? value = "hello";

        // Act
        var result = Maybe.Optional(value, str => Result.Ok(str.ToUpperInvariant()));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeTrue();
        result.Unwrap().Unwrap().Should().Be("HELLO");
    }

    [Fact]
    public void Optional_WithNullableIntValueAndSuccessfulFunction_ReturnsMaybeWithValue()
    {
        // Arrange
        int? value = 21;

        // Act - Return a non-nullable wrapper type
        var result = Maybe.Optional<int, WrappedInt>(value, num => Result.Ok(new WrappedInt(num * 2)));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeTrue();
        result.Unwrap().Unwrap().Value.Should().Be(42);
    }

    [Fact]
    public void Optional_WithNullableGuidValueAndSuccessfulFunction_ReturnsMaybeWithValue()
    {
        // Arrange
        var guid = Guid.NewGuid();
        Guid? value = guid;

        // Act
        var result = Maybe.Optional<Guid, string>(value, g => Result.Ok(g.ToString()));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeTrue();
        result.Unwrap().Unwrap().Should().Be(guid.ToString());
    }

    #endregion

    #region Maybe.Optional with value and failing function

    [Fact]
    public void Optional_WithValueAndFailingFunction_ReturnsFailure()
    {
        // Arrange
        string? value = "invalid";
        var expectedError = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Value is invalid" }));

        // Act
        var result = Maybe.Optional(value, str => Result.Fail<string>(expectedError));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(expectedError);
    }

    [Fact]
    public void Optional_WithNullableIntValueAndFailingFunction_ReturnsFailure()
    {
        // Arrange
        int? value = -5;
        var expectedError = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("number"), "validation.error") { Detail = "Value must be positive" }));

        // Act
        var result = Maybe.Optional<int, WrappedInt>(value, num =>
            num > 0
                ? Result.Ok(new WrappedInt(num))
                : Result.Fail<WrappedInt>(expectedError));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(expectedError);
    }

    #endregion

    #region Maybe.Optional with validation functions

    [Fact]
    public void Optional_WithCustomValidation_ValidValue_ReturnsMaybeWithValue()
    {
        // Arrange
        string? email = "test@example.com";

        // Act - Use custom validation function
        var result = Maybe.Optional(email, str =>
            str.Contains('@')
                ? Result.Ok(str.ToLowerInvariant())
                : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid email" }));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeTrue();
        result.Unwrap().Unwrap().Should().Be("test@example.com");
    }

    [Fact]
    public void Optional_WithCustomValidation_NullValue_ReturnsMaybeNone()
    {
        // Arrange
        string? email = null;

        // Act
        var result = Maybe.Optional(email, str =>
            str.Contains('@')
                ? Result.Ok(str.ToLowerInvariant())
                : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid email" }));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeFalse();
    }

    [Fact]
    public void Optional_WithCustomValidation_InvalidValue_ReturnsFailure()
    {
        // Arrange
        string? email = "not-an-email";

        // Act
        var result = Maybe.Optional(email, str =>
            str.Contains('@')
                ? Result.Ok(str.ToLowerInvariant())
                : Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid email" }));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().BeOfType<Error.UnprocessableContent>();
    }

    #endregion

    #region Maybe.Optional in Combine chains

    [Fact]
    public void Optional_InCombineChain_WithAllValues_ReturnsSuccess()
    {
        // Arrange
        string firstName = "John";
        string? middleName = "Xavier";

        // Act
        var result = Result.Ok(firstName)
            .Combine(Maybe.Optional(middleName, name => Result.Ok(name.ToUpperInvariant())));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var (first, middle) = result.Unwrap();
        first.Should().Be("John");
        middle.HasValue.Should().BeTrue();
        middle.Unwrap().Should().Be("XAVIER");
    }

    [Fact]
    public void Optional_InCombineChain_WithNullOptional_ReturnsSuccessWithNone()
    {
        // Arrange
        string firstName = "John";
        string? middleName = null;

        // Act
        var result = Result.Ok(firstName)
            .Combine(Maybe.Optional(middleName, name => Result.Ok(name.ToUpperInvariant())));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var (first, middle) = result.Unwrap();
        first.Should().Be("John");
        middle.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Optional_InCombineChain_WithFailingOptional_ReturnsFailure()
    {
        // Arrange
        string firstName = "John";
        string? invalidValue = "bad";
        var validationError = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Invalid value" }));

        // Act
        var result = Result.Ok(firstName)
            .Combine(Maybe.Optional(invalidValue, _ => Result.Fail<string>(validationError)));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().BeOfType<Error.UnprocessableContent>();
    }

    #endregion

    #region Edge cases

    [Fact]
    public void Optional_WithEmptyString_FunctionIsInvoked()
    {
        // Arrange
        string? emptyString = "";
        var functionInvoked = false;

        // Act
        var result = Maybe.Optional(emptyString, str =>
        {
            functionInvoked = true;
            return Result.Ok(str);
        });

        // Assert
        functionInvoked.Should().BeTrue("empty string is not null, so function should be invoked");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().HasValue.Should().BeTrue();
        result.Unwrap().Unwrap().Should().Be("");
    }

    [Fact]
    public void Optional_WithDefaultNullableGuid_FunctionIsInvoked()
    {
        // Arrange
        Guid? defaultGuid = Guid.Empty;
        var functionInvoked = false;

        // Act
        var result = Maybe.Optional<Guid, string>(defaultGuid, g =>
        {
            functionInvoked = true;
            return Result.Ok(g.ToString());
        });

        // Assert
        functionInvoked.Should().BeTrue("Guid.Empty is not null, so function should be invoked");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Optional_WithZeroNullableInt_FunctionIsInvoked()
    {
        // Arrange
        int? zero = 0;
        var functionInvoked = false;

        // Act
        var result = Maybe.Optional<int, WrappedInt>(zero, num =>
        {
            functionInvoked = true;
            return Result.Ok(new WrappedInt(num));
        });

        // Assert
        functionInvoked.Should().BeTrue("zero is not null, so function should be invoked");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Unwrap().Value.Should().Be(0);
    }

    #endregion

    #region Helper Types

    private sealed record WrappedInt(int Value);

    #endregion
}