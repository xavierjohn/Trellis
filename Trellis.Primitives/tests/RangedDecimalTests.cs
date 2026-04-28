using Trellis.Testing;
namespace Trellis.Primitives.Tests;

/// <summary>
/// Test value object with range constraint (1–999).
/// </summary>
[Range(1, 999)]
public partial class TestPrice : RequiredDecimal<TestPrice> { }

/// <summary>
/// Test value object with fractional range constraint (0.01–99.99).
/// </summary>
[Range(0.01, 99.99)]
public partial class FractionalPrice : RequiredDecimal<FractionalPrice> { }

/// <summary>
/// Test value object with large range using double min/max values.
/// </summary>
[Range(-1e15, 1e15)]
public partial class LargeRangeDecimal : RequiredDecimal<LargeRangeDecimal> { }

/// <summary>
/// Test value object with scientific-notation range values.
/// 1e20 produces "1E+20" from double.ToString() which is not a valid decimal literal.
/// </summary>
[Range(0, 1e20)]
public partial class ScientificNotationDecimal : RequiredDecimal<ScientificNotationDecimal> { }

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
        result.Unwrap().Value.Should().Be(99.99m);
    }

    [Fact]
    public void TryCreate_BelowMinimum_ReturnsFailure()
    {
        var result = TestPrice.TryCreate(0m);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Test Price must be at least 1.");
    }

    [Fact]
    public void TryCreate_AboveMaximum_ReturnsFailure()
    {
        var result = TestPrice.TryCreate(1000m);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Test Price must be at most 999.");
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

    [Fact]
    public void TryCreate_AtMinBoundary_ReturnsSuccess()
    {
        var result = TestPrice.TryCreate(1m);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(1m);
    }

    [Fact]
    public void TryCreate_AtMaxBoundary_ReturnsSuccess()
    {
        var result = TestPrice.TryCreate(999m);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(999m);
    }

    [Fact]
    public void TryCreate_JustBelowMin_ReturnsFailure()
    {
        var result = TestPrice.TryCreate(0.99m);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_JustAboveMax_ReturnsFailure()
    {
        var result = TestPrice.TryCreate(999.01m);
        result.IsFailure.Should().BeTrue();
    }

    #region FractionalPrice — [Range(0.01, 99.99)]

    [Fact]
    public void FractionalPrice_WithinRange_ReturnsSuccess()
    {
        var result = FractionalPrice.TryCreate(50.00m);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void FractionalPrice_AtMinBoundary_ReturnsSuccess()
    {
        var result = FractionalPrice.TryCreate(0.01m);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void FractionalPrice_AtMaxBoundary_ReturnsSuccess()
    {
        var result = FractionalPrice.TryCreate(99.99m);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void FractionalPrice_BelowMin_ReturnsFailure()
    {
        var result = FractionalPrice.TryCreate(0.009m);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void FractionalPrice_AboveMax_ReturnsFailure()
    {
        var result = FractionalPrice.TryCreate(100.00m);
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region TryCreate(decimal?) — Nullable range validation

    [Fact]
    public void TryCreate_NullableNull_ReturnsFailure()
    {
        var result = TestPrice.TryCreate((decimal?)null);
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Custom fieldName

    [Fact]
    public void TryCreate_WithCustomFieldName_ReturnsCorrectFieldName()
    {
        var result = TestPrice.TryCreate(0m, "myField");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/myField");
    }

    #endregion

    #region Create — throws on failure

    [Fact]
    public void Create_OutOfRange_ThrowsInvalidOperationException()
    {
        Action act = () => TestPrice.Create(0m);
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region LargeRangeDecimal — [Range(-1e15, 1e15)]

    [Fact]
    public void LargeRangeDecimal_AtMinBoundary_ReturnsSuccess()
    {
        var result = LargeRangeDecimal.TryCreate(-1_000_000_000_000_000m);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(-1_000_000_000_000_000m);
    }

    [Fact]
    public void LargeRangeDecimal_AtMaxBoundary_ReturnsSuccess()
    {
        var result = LargeRangeDecimal.TryCreate(1_000_000_000_000_000m);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(1_000_000_000_000_000m);
    }

    [Fact]
    public void LargeRangeDecimal_BelowMin_ReturnsFailure()
    {
        var result = LargeRangeDecimal.TryCreate(-1_000_000_000_000_001m);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void LargeRangeDecimal_AboveMax_ReturnsFailure()
    {
        var result = LargeRangeDecimal.TryCreate(1_000_000_000_000_001m);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void LargeRangeDecimal_Zero_ReturnsSuccess()
    {
        var result = LargeRangeDecimal.TryCreate(0m);
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region ScientificNotationDecimal — [Range(0, 1e20)] — validates no scientific notation in generated code

    [Fact]
    public void ScientificNotationDecimal_WithinRange_ReturnsSuccess()
    {
        // 1e20 would produce "1E+20" from double.ToString() which is not a valid decimal literal.
        // The generator must format it as "100000000000000000000m" instead.
        var result = ScientificNotationDecimal.TryCreate(50_000_000_000_000_000_000m);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ScientificNotationDecimal_AboveMax_ReturnsFailure()
    {
        var result = ScientificNotationDecimal.TryCreate(100_000_000_000_000_000_001m);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ScientificNotationDecimal_Negative_ReturnsFailure()
    {
        var result = ScientificNotationDecimal.TryCreate(-1m);
        result.IsFailure.Should().BeTrue();
    }

    #endregion
}