namespace Trellis.Results.Tests.Maybes;

using Xunit;

/// <summary>
/// Tests for Maybe edge cases including null handling, equality, and boundary conditions.
/// </summary>
public class MaybeEdgeCaseTests
{
    #region TryGetValue Edge Cases

    [Fact]
    public void TryGetValue_OnMaybeWithValue_ShouldReturnTrueAndValue()
    {
        // Arrange
        Maybe<string> maybe = "test";

        // Act
        bool hasValue = maybe.TryGetValue(out var value);

        // Assert
        hasValue.Should().BeTrue();
        value.Should().Be("test");
    }

    [Fact]
    public void TryGetValue_OnMaybeWithoutValue_ShouldReturnFalseAndDefault()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        bool hasValue = maybe.TryGetValue(out var value);

        // Assert
        hasValue.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetValue_OnValueType_WithoutValue_ShouldReturnDefault()
    {
        // Arrange
        Maybe<int> maybe = Maybe<int>.None;

        // Act
        bool hasValue = maybe.TryGetValue(out var value);

        // Assert
        hasValue.Should().BeFalse();
        value.Should().Be(default(int));
    }

    #endregion

    #region GetValueOrThrow Edge Cases

    [Fact]
    public void GetValueOrThrow_OnMaybeWithValue_ShouldReturnValue()
    {
        // Arrange
        Maybe<string> maybe = "test";

        // Act
        var value = maybe.GetValueOrThrow();

        // Assert
        value.Should().Be("test");
    }

    [Fact]
    public void GetValueOrThrow_OnMaybeWithoutValue_ShouldThrowWithDefaultMessage()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        Action act = () => maybe.GetValueOrThrow();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Maybe has no value*");
    }

    [Fact]
    public void GetValueOrThrow_OnMaybeWithoutValue_WithCustomMessage_ShouldThrowWithCustomMessage()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;
        string customMessage = "Custom error message";

        // Act
        Action act = () => maybe.GetValueOrThrow(customMessage);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage(customMessage);
    }

    [Fact]
    public void Value_OnMaybeWithoutValue_ShouldThrow()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        Action act = () => { var _ = maybe.Value; };

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Maybe has no value*");
    }

    #endregion

    #region GetValueOrDefault Edge Cases

    [Fact]
    public void GetValueOrDefault_OnMaybeWithValue_ShouldReturnValue()
    {
        // Arrange
        Maybe<string> maybe = "test";

        // Act
        var value = maybe.GetValueOrDefault("default");

        // Assert
        value.Should().Be("test");
    }

    [Fact]
    public void GetValueOrDefault_OnMaybeWithoutValue_ShouldReturnDefault()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        var value = maybe.GetValueOrDefault("default");

        // Assert
        value.Should().Be("default");
    }

    [Fact]
    public void GetValueOrDefault_WithNullDefault_ShouldWorkCorrectly()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        var value = maybe.GetValueOrDefault(null!);

        // Assert
        value.Should().BeNull();
    }

    [Fact]
    public void GetValueOrDefault_OnValueType_ShouldWorkCorrectly()
    {
        // Arrange
        Maybe<int> maybe = Maybe<int>.None;

        // Act
        var value = maybe.GetValueOrDefault(42);

        // Assert
        value.Should().Be(42);
    }

    #endregion

    #region Implicit Conversion Edge Cases

    [Fact]
    public void ImplicitConversion_FromNull_ShouldCreateMaybeWithoutValue()
    {
        // Act
        Maybe<string> maybe = (string)null!;

        // Assert
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldCreateMaybeWithValue()
    {
        // Act
        Maybe<string> maybe = "test";

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be("test");
    }

    [Fact]
    public void ImplicitConversion_FromMaybe_ShouldReturnSameMaybe()
    {
        // Arrange
        Maybe<string> original = "test";

        // Act
        Maybe<string> converted = original;

        // Assert
        converted.Should().Be(original);
        converted.Value.Should().Be("test");
    }

    #endregion

    #region Equality Edge Cases

    [Fact]
    public void Equals_TwoMaybesWithoutValue_ShouldBeEqual()
    {
        // Arrange
        Maybe<string> maybe1 = Maybe<string>.None;
        Maybe<string> maybe2 = Maybe<string>.None;

        // Act & Assert
        maybe1.Should().Be(maybe2);
        (maybe1 == maybe2).Should().BeTrue();
        (maybe1 != maybe2).Should().BeFalse();
        maybe1.Equals(maybe2).Should().BeTrue();
    }

    [Fact]
    public void Equals_MaybeWithValueAndMaybeWithoutValue_ShouldNotBeEqual()
    {
        // Arrange
        Maybe<string> maybe1 = "test";
        Maybe<string> maybe2 = Maybe<string>.None;

        // Act & Assert
        maybe1.Should().NotBe(maybe2);
        (maybe1 == maybe2).Should().BeFalse();
        (maybe1 != maybe2).Should().BeTrue();
    }

    [Fact]
    public void Equals_MaybeWithValue_ComparedToValue_ShouldBeEqual()
    {
        // Arrange
        Maybe<string> maybe = "test";
        string value = "test";

        // Act & Assert
        maybe.Equals(value).Should().BeTrue();
        (maybe == value).Should().BeTrue();
        (maybe != value).Should().BeFalse();
    }

    [Fact]
    public void Equals_MaybeWithoutValue_ComparedToNull_ShouldBeEqual()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act & Assert
        maybe.Equals(null).Should().BeTrue();
    }

    [Fact]
    public void Equals_MaybeWithValue_ComparedToNull_ShouldNotBeEqual()
    {
        // Arrange
        Maybe<string> maybe = "test";

        // Act & Assert
        maybe.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_ComparedToObject_ShouldWorkCorrectly()
    {
        // Arrange
        Maybe<string> maybe = "test";
        object obj = "test";

        // Act & Assert
        maybe.Equals(obj).Should().BeTrue();
    }

    [Fact]
    public void Equals_ComparedToDifferentType_ShouldReturnFalse()
    {
        // Arrange
        Maybe<string> maybe = "test";
        object obj = 42;

        // Act & Assert
        maybe.Equals(obj).Should().BeFalse();
    }

    [Fact]
    public void Equals_ComparedToMaybeOfDifferentType_ShouldReturnFalse()
    {
        // Arrange
        Maybe<string> maybe1 = "test";
        object maybe2 = Maybe.From(42);

        // Act & Assert
        maybe1.Equals(maybe2).Should().BeFalse();
    }

    #endregion

    #region Operator Equality Edge Cases

    [Fact]
    public void OperatorEquality_MaybeWithValueAndValue_ShouldWork()
    {
        // Arrange
        Maybe<string> maybe = "test";
        string value = "test";

        // Act & Assert
        (maybe == value).Should().BeTrue();
        (value == maybe).Should().BeTrue(); // Reversed order
    }

    [Fact]
    public void OperatorInequality_MaybeWithValueAndDifferentValue_ShouldWork()
    {
        // Arrange
        Maybe<string> maybe = "test";
        string value = "different";

        // Act & Assert
        (maybe != value).Should().BeTrue();
        (value != maybe).Should().BeTrue(); // Reversed order
    }

    [Fact]
    public void OperatorEquality_WithObject_ShouldWork()
    {
        // Arrange
        Maybe<string> maybe = "test";
        object? obj = "test";

        // Act & Assert
        (maybe == obj).Should().BeTrue();
    }

    [Fact]
    public void OperatorInequality_WithObject_ShouldWork()
    {
        // Arrange
        Maybe<string> maybe = "test";
        object? obj = "different";

        // Act & Assert
        (maybe != obj).Should().BeTrue();
    }

    #endregion

    #region GetHashCode Edge Cases

    [Fact]
    public void GetHashCode_TwoMaybesWithSameValue_ShouldHaveSameHashCode()
    {
        // Arrange
        Maybe<string> maybe1 = "test";
        Maybe<string> maybe2 = "test";

        // Act
        int hash1 = maybe1.GetHashCode();
        int hash2 = maybe2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void GetHashCode_TwoMaybesWithoutValue_ShouldHaveSameHashCode()
    {
        // Arrange
        Maybe<string> maybe1 = Maybe<string>.None;
        Maybe<string> maybe2 = Maybe<string>.None;

        // Act
        int hash1 = maybe1.GetHashCode();
        int hash2 = maybe2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
        hash1.Should().Be(0); // Hash for empty Maybe is 0
    }

    [Fact]
    public void GetHashCode_MaybeWithValueAndMaybeWithoutValue_ShouldHaveDifferentHashCodes()
    {
        // Arrange
        Maybe<string> maybe1 = "test";
        Maybe<string> maybe2 = Maybe<string>.None;

        // Act
        int hash1 = maybe1.GetHashCode();
        int hash2 = maybe2.GetHashCode();

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void GetHashCode_IsConsistent()
    {
        // Arrange
        Maybe<string> maybe = "test";

        // Act
        int hash1 = maybe.GetHashCode();
        int hash2 = maybe.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    #endregion

    #region ToString Edge Cases

    [Fact]
    public void ToString_OnMaybeWithValue_ShouldReturnValueString()
    {
        // Arrange
        Maybe<string> maybe = "test";

        // Act
        string str = maybe.ToString();

        // Assert
        str.Should().Be("test");
    }

    [Fact]
    public void ToString_OnMaybeWithoutValue_ShouldReturnEmptyString()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        string str = maybe.ToString();

        // Assert
        str.Should().BeEmpty();
    }

    [Fact]
    public void ToString_OnMaybeWithValueType_ShouldReturnValueString()
    {
        // Arrange
        Maybe<int> maybe = 42;

        // Act
        string str = maybe.ToString();

        // Assert
        str.Should().Be("42");
    }

    [Fact]
    public void ToString_OnMaybeWithComplexType_ShouldReturnToString()
    {
        // Arrange
        var date = new DateTime(2024, 1, 1);
        Maybe<DateTime> maybe = date;

        // Act
        string str = maybe.ToString();

        // Assert
        str.Should().Be(date.ToString(System.Globalization.CultureInfo.CurrentCulture)); // Uses underlying object's ToString()
    }

    #endregion

    #region HasValue and HasNoValue Edge Cases

    [Fact]
    public void HasValue_AndHasNoValue_ShouldBeOpposites()
    {
        // Arrange
        Maybe<string> withValue = "test";
        Maybe<string> withoutValue = Maybe<string>.None;

        // Assert
        withValue.HasValue.Should().BeTrue();
        withValue.HasNoValue.Should().BeFalse();

        withoutValue.HasValue.Should().BeFalse();
        withoutValue.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Value Type Edge Cases

    [Fact]
    public void Maybe_WithDefaultValueType_ShouldNotHaveValue()
    {
        // Act
        Maybe<int> maybe = default;

        // Assert
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Maybe_WithNonDefaultValueType_ShouldHaveValue()
    {
        // Act
        Maybe<int> maybe = 42;

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be(42);
    }

    [Fact]
    public void Maybe_WithZeroValueType_ShouldHaveValue()
    {
        // Act
        Maybe<int> maybe = 0;

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be(0);
    }

    [Fact]
    public void Maybe_WithStruct_ShouldWorkCorrectly()
    {
        // Arrange
        var date = DateTime.Now;

        // Act
        Maybe<DateTime> maybe = date;

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be(date);
    }

    #endregion

    #region Maybe.From Edge Cases

    [Fact]
    public void MaybeFrom_WithNull_ShouldCreateMaybeWithoutValue()
    {
        // Act
        var maybe = Maybe.From<string>(null!);

        // Assert
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void MaybeFrom_WithValue_ShouldCreateMaybeWithValue()
    {
        // Act
        var maybe = Maybe.From("test");

        // Assert
        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be("test");
    }

    #endregion

    #region Map

    [Fact]
    public void Map_OnMaybeWithValue_ShouldReturnTransformedValue()
    {
        // Arrange
        Maybe<string> maybe = "hello";

        // Act
        var result = maybe.Map(v => v.ToUpperInvariant());

        // Assert
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("HELLO");
    }

    [Fact]
    public void Map_OnMaybeWithoutValue_ShouldReturnNone()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        var result = maybe.Map(v => v.ToUpperInvariant());

        // Assert
        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Map_OnMaybeWithValue_ShouldTransformType()
    {
        // Arrange
        Maybe<string> maybe = "42";

        // Act
        var result = maybe.Map(int.Parse);

        // Assert
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    #endregion

    #region Match

    [Fact]
    public void Match_OnMaybeWithValue_ShouldCallSome()
    {
        // Arrange
        Maybe<string> maybe = "hello";

        // Act
        var result = maybe.Match(v => $"Got: {v}", () => "Nothing");

        // Assert
        result.Should().Be("Got: hello");
    }

    [Fact]
    public void Match_OnMaybeWithoutValue_ShouldCallNone()
    {
        // Arrange
        Maybe<string> maybe = Maybe<string>.None;

        // Act
        var result = maybe.Match(v => $"Got: {v}", () => "Nothing");

        // Assert
        result.Should().Be("Nothing");
    }

    [Fact]
    public void Match_OnMaybeWithValue_ShouldNotCallNone()
    {
        // Arrange
        Maybe<int> maybe = 42;
        var noneCalled = false;

        // Act
        var result = maybe.Match(v => v * 2, () => { noneCalled = true; return -1; });

        // Assert
        result.Should().Be(84);
        noneCalled.Should().BeFalse();
    }

    [Fact]
    public void Match_OnMaybeWithoutValue_ShouldNotCallSome()
    {
        // Arrange
        Maybe<int> maybe = Maybe<int>.None;
        var someCalled = false;

        // Act
        var result = maybe.Match(v => { someCalled = true; return v; }, () => -1);

        // Assert
        result.Should().Be(-1);
        someCalled.Should().BeFalse();
    }

    #endregion

    #region Maybe.None Edge Cases

    [Fact]
    public void MaybeNone_ShouldCreateMaybeWithoutValue()
    {
        // Act
        var maybe = Maybe<string>.None;

        // Assert
        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void MaybeNone_MultipleCallsShouldBehaveSame()
    {
        // Act
        var maybe1 = Maybe<string>.None;
        var maybe2 = Maybe<string>.None;

        // Assert
        maybe1.Should().Be(maybe2);
    }

    #endregion
}