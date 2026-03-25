namespace Trellis.Primitives.Tests;

/// <summary>
/// Test value object with range constraint (1–999).
/// </summary>
[Range(1, 999)]
public partial class TestPrice : RequiredDecimal<TestPrice> { }

/// <summary>
/// Tests for RequiredDecimal [Range] attribute support.
/// Validates that the source generator emits range validation in TryCreate.
/// </summary>
public class RangedDecimalTests
{
    [Fact]
    public void TryCreate_WithinRange_ReturnsSuccess()
    {
        var result = TestPrice.TryCreate(99.99m);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(99.99m);
    }

    [Fact]
    public void TryCreate_BelowMinimum_ReturnsFailure()
    {
        var result = TestPrice.TryCreate(0m);
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Test Price must be at least 1.");
    }

    [Fact]
    public void TryCreate_AboveMaximum_ReturnsFailure()
    {
        var result = TestPrice.TryCreate(1000m);
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Test Price must be at most 999.");
    }

    [Fact]
    public void TryCreate_FromString_WithinRange_ReturnsSuccess()
    {
        var result = TestPrice.TryCreate("50.00");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_FromString_OutOfRange_ReturnsFailure()
    {
        var result = TestPrice.TryCreate("0");
        result.IsFailure.Should().BeTrue();
    }
}
