using Trellis.Testing;
namespace Trellis.Primitives.Tests;

/// <summary>
/// Test value object with range constraint (1–999).
/// </summary>
[Range(1, 999)]
public partial class TestQuantity : RequiredInt<TestQuantity> { }

/// <summary>
/// Test value object with range constraint that allows zero (0–100).
/// </summary>
[Range(0, 100)]
public partial class TestPercentageInt : RequiredInt<TestPercentageInt> { }

/// <summary>
/// Test value object with full int range (int.MinValue–int.MaxValue).
/// </summary>
[Range(int.MinValue, int.MaxValue)]
public partial class FullRangeInt : RequiredInt<FullRangeInt> { }

/// <summary>
/// Tests for RequiredInt [Range] attribute support.
/// Validates that the source generator emits range validation in TryCreate.
/// </summary>
public class RangedIntTests
{
    #region TryCreate(int) — Range validation

    [Fact]
    public void TryCreate_WithinRange_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate(500);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(500);
    }

    [Fact]
    public void TryCreate_BelowMinimum_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate(-10);
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("testQuantity");
        validation.FieldErrors[0].Details[0].Should().Be("Test Quantity must be at least 1.");
    }

    [Fact]
    public void TryCreate_AboveMaximum_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate(5000);
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("testQuantity");
        validation.FieldErrors[0].Details[0].Should().Be("Test Quantity must be at most 999.");
    }

    [Fact]
    public void TryCreate_AtMinBoundary_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate(1);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(1);
    }

    [Fact]
    public void TryCreate_AtMaxBoundary_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate(999);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(999);
    }

    [Fact]
    public void TryCreate_Zero_WithRangeAllowingZero_ReturnsSuccess()
    {
        var result = TestPercentageInt.TryCreate(0);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(0);
    }

    [Fact]
    public void TryCreate_JustBelowMin_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate(0);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_JustAboveMax_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate(1000);
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region TryCreate(string?) — Range validation through string parsing

    [Fact]
    public void TryCreate_FromString_WithinRange_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate("500");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(500);
    }

    [Fact]
    public void TryCreate_FromString_OutOfRange_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate("1000");
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].Details[0].Should().Be("Test Quantity must be at most 999.");
    }

    #endregion

    #region TryCreate(int?) — Nullable range validation

    [Fact]
    public void TryCreate_NullableNull_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate((int?)null);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_NullableWithinRange_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate((int?)500);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(500);
    }

    #endregion

    #region Custom fieldName

    [Fact]
    public void TryCreate_WithCustomFieldName_ReturnsCorrectFieldName()
    {
        var result = TestQuantity.TryCreate(0, "myField");
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("myField");
    }

    #endregion

    #region Create — throws on failure

    [Fact]
    public void Create_OutOfRange_ThrowsInvalidOperationException()
    {
        Action act = () => TestQuantity.Create(0);
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region FullRangeInt — [Range(int.MinValue, int.MaxValue)]

    [Fact]
    public void FullRangeInt_AtIntMinValue_ReturnsSuccess()
    {
        var result = FullRangeInt.TryCreate(int.MinValue);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(int.MinValue);
    }

    [Fact]
    public void FullRangeInt_AtIntMaxValue_ReturnsSuccess()
    {
        var result = FullRangeInt.TryCreate(int.MaxValue);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(int.MaxValue);
    }

    [Fact]
    public void FullRangeInt_Zero_ReturnsSuccess()
    {
        var result = FullRangeInt.TryCreate(0);
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}
