namespace Trellis.Results.Tests.Maybes;

using Trellis;

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
        result.Value.HasValue.Should().BeFalse();
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
        result.Value.HasValue.Should().BeFalse();
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
        result.Value.HasValue.Should().BeFalse();
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
        result.Value.HasValue.Should().BeTrue();
        result.Value.Value.Should().Be("HELLO");
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
        result.Value.HasValue.Should().BeTrue();
        result.Value.Value.Value.Should().Be(42);
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
        result.Value.HasValue.Should().BeTrue();
        result.Value.Value.Should().Be(guid.ToString());
    }

    #endregion

    #region Maybe.Optional with value and failing function

    [Fact]
    public void Optional_WithValueAndFailingFunction_ReturnsFailure()
    {
        // Arrange
        string? value = "invalid";
        var expectedError = Error.Validation("Value is invalid", "field");

        // Act
        var result = Maybe.Optional(value, str => Result.Fail<string>(expectedError));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(expectedError);
    }

    [Fact]
    public void Optional_WithNullableIntValueAndFailingFunction_ReturnsFailure()
    {
        // Arrange
        int? value = -5;
        var expectedError = Error.Validation("Value must be positive", "number");

        // Act
        var result = Maybe.Optional<int, WrappedInt>(value, num =>
            num > 0
                ? Result.Ok(new WrappedInt(num))
                : Result.Fail<WrappedInt>(expectedError));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(expectedError);
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
                : Result.Fail<string>(Error.Validation("Invalid email")));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HasValue.Should().BeTrue();
        result.Value.Value.Should().Be("test@example.com");
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
                : Result.Fail<string>(Error.Validation("Invalid email")));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HasValue.Should().BeFalse();
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
                : Result.Fail<string>(Error.Validation("Invalid email")));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
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
        var (first, middle) = result.Value;
        first.Should().Be("John");
        middle.HasValue.Should().BeTrue();
        middle.Value.Should().Be("XAVIER");
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
        var (first, middle) = result.Value;
        first.Should().Be("John");
        middle.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Optional_InCombineChain_WithFailingOptional_ReturnsFailure()
    {
        // Arrange
        string firstName = "John";
        string? invalidValue = "bad";
        var validationError = Error.Validation("Invalid value", "field");

        // Act
        var result = Result.Ok(firstName)
            .Combine(Maybe.Optional(invalidValue, _ => Result.Fail<string>(validationError)));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
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
        result.Value.HasValue.Should().BeTrue();
        result.Value.Value.Should().Be("");
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
        result.Value.Value.Value.Should().Be(0);
    }

    #endregion

    #region Helper Types

    private sealed record WrappedInt(int Value);

    #endregion
}