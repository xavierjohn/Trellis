namespace Trellis.Primitives.Tests;

using System.Text.Json;

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
        result.Value.Value.Should().Be(500);
    }

    [Fact]
    public void TryCreate_BelowMinimum_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate(0);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("testQuantity");
        validation.FieldErrors[0].Details[0].Should().Be("Test Quantity must be at least 1.");
    }

    [Fact]
    public void TryCreate_AboveMaximum_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate(1000);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("testQuantity");
        validation.FieldErrors[0].Details[0].Should().Be("Test Quantity must be at most 999.");
    }

    [Fact]
    public void TryCreate_AtMinBoundary_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate(1);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(1);
    }

    [Fact]
    public void TryCreate_AtMaxBoundary_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate(999);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(999);
    }

    [Fact]
    public void TryCreate_Zero_WithRangeAllowingZero_ReturnsSuccess()
    {
        var result = TestPercentageInt.TryCreate(0);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(0);
    }

    #endregion

    #region TryCreate(string?) — Range validation through string parsing

    [Fact]
    public void TryCreate_FromString_WithinRange_ReturnsSuccess()
    {
        var result = TestQuantity.TryCreate("500");
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(500);
    }

    [Fact]
    public void TryCreate_FromString_OutOfRange_ReturnsFailure()
    {
        var result = TestQuantity.TryCreate("1000");
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Test Quantity must be at most 999.");
    }

    #endregion
}
