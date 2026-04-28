namespace Trellis.Primitives.Tests;

using System.Text.RegularExpressions;
using Trellis.Testing;

// --- Test value objects with ValidateAdditional ---

/// <summary>
/// RequiredString with regex pattern validation via ValidateAdditional.
/// </summary>
[StringLength(10)]
public partial class Sku : RequiredString<Sku>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!Regex.IsMatch(value, @"^SKU-\d{6}$"))
            errorMessage = "Sku must match pattern SKU-XXXXXX.";
    }
}

/// <summary>
/// RequiredString without ValidateAdditional — ensures the hook is truly optional.
/// </summary>
public partial class PlainName : RequiredString<PlainName> { }

/// <summary>
/// RequiredInt without ValidateAdditional — ensures the hook is truly optional.
/// </summary>
public partial class PlainCount : RequiredInt<PlainCount> { }

/// <summary>
/// RequiredDecimal without ValidateAdditional — ensures the hook is truly optional.
/// </summary>
public partial class PlainRate : RequiredDecimal<PlainRate> { }

/// <summary>
/// RequiredInt with range + custom even-number validation.
/// </summary>
[Range(1, 100)]
public partial class EvenPercentage : RequiredInt<EvenPercentage>
{
    static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)
    {
        if (value % 2 != 0)
            errorMessage = "Even Percentage must be an even number.";
    }
}

/// <summary>
/// RequiredInt without [Range] + custom positive-only validation.
/// </summary>
public partial class PositiveScore : RequiredInt<PositiveScore>
{
    static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)
    {
        if (value < 0)
            errorMessage = "Positive Score must be positive.";
    }
}

/// <summary>
/// RequiredDecimal with custom two-decimal-places validation.
/// </summary>
public partial class PreciseAmount : RequiredDecimal<PreciseAmount>
{
    static partial void ValidateAdditional(decimal value, string fieldName, ref string? errorMessage)
    {
        if (decimal.Round(value, 2) != value)
            errorMessage = "Precise Amount must have at most 2 decimal places.";
    }
}

// --- Tests ---

public class ValidateAdditionalTests
{
    #region RequiredString — Sku with regex pattern

    [Fact]
    public void Sku_ValidPattern_ReturnsSuccess()
    {
        var result = Sku.TryCreate("SKU-123456");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("SKU-123456");
    }

    [Fact]
    public void Sku_InvalidPattern_ReturnsFailure()
    {
        var result = Sku.TryCreate("INVALID");
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Sku must match pattern SKU-XXXXXX.");
    }

    [Fact]
    public void Sku_TooLong_FailsLengthBeforeAdditional()
    {
        // 11 chars — should fail StringLength before reaching ValidateAdditional
        var result = Sku.TryCreate("SKU-1234567");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Sku must be 10 characters or fewer.");
    }

    [Fact]
    public void Sku_NullValue_FailsBuiltInValidation()
    {
        var result = Sku.TryCreate(null);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Sku cannot be empty.");
    }

    [Fact]
    public void Sku_CustomFieldName_PropagatedToAdditionalValidation()
    {
        var result = Sku.TryCreate("BAD", "itemSku");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/itemSku");
    }

    [Fact]
    public void Sku_WithLeadingTrailingWhitespace_ValidatesOnTrimmedValue()
    {
        // "SKU-123456" is 10 chars (at StringLength limit), spaces should be trimmed before validation
        var result = Sku.TryCreate(" SKU-123456 ");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("SKU-123456");
    }

    #endregion

    #region RequiredString — PlainName without ValidateAdditional (optional hook)

    [Fact]
    public void PlainName_WithoutHook_StillWorks()
    {
        var result = PlainName.TryCreate("Hello");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("Hello");
    }

    #endregion

    #region RequiredInt — EvenPercentage (Range + custom)

    [Fact]
    public void EvenPercentage_EvenInRange_ReturnsSuccess()
    {
        var result = EvenPercentage.TryCreate(50);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(50);
    }

    [Fact]
    public void EvenPercentage_OddInRange_ReturnsFailure()
    {
        var result = EvenPercentage.TryCreate(51);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Even Percentage must be an even number.");
    }

    [Fact]
    public void EvenPercentage_OutOfRange_FailsRangeBeforeAdditional()
    {
        var result = EvenPercentage.TryCreate(102);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Even Percentage must be at most 100.");
    }

    [Fact]
    public void EvenPercentage_FromNullableOdd_ReturnsFailure()
    {
        var result = EvenPercentage.TryCreate((int?)51);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Even Percentage must be an even number.");
    }

    [Fact]
    public void EvenPercentage_FromStringEven_ReturnsSuccess()
    {
        var result = EvenPercentage.TryCreate("50");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(50);
    }

    [Fact]
    public void EvenPercentage_FromStringOdd_ReturnsFailure()
    {
        var result = EvenPercentage.TryCreate("51");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Even Percentage must be an even number.");
    }

    #endregion

    #region RequiredInt — PositiveScore (no Range + custom)

    [Fact]
    public void PositiveScore_Positive_ReturnsSuccess()
    {
        var result = PositiveScore.TryCreate(5);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PositiveScore_Negative_ReturnsFailure()
    {
        var result = PositiveScore.TryCreate(-1);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Positive Score must be positive.");
    }

    [Fact]
    public void PositiveScore_Zero_AcceptedByAdditionalValidation()
    {
        // Zero passes built-in (no zero-check), but ValidateAdditional allows it (< 0 check)
        var result = PositiveScore.TryCreate(0);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(0);
    }

    [Fact]
    public void PositiveScore_FromNullableNegative_ReturnsFailure()
    {
        var result = PositiveScore.TryCreate((int?)-3);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Positive Score must be positive.");
    }

    [Fact]
    public void PositiveScore_FromStringNegative_ReturnsFailure()
    {
        var result = PositiveScore.TryCreate("-5");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Positive Score must be positive.");
    }

    #endregion

    #region RequiredDecimal — PreciseAmount (custom decimal places)

    [Fact]
    public void PreciseAmount_TwoDecimalPlaces_ReturnsSuccess()
    {
        var result = PreciseAmount.TryCreate(10.99m);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(10.99m);
    }

    [Fact]
    public void PreciseAmount_ThreeDecimalPlaces_ReturnsFailure()
    {
        var result = PreciseAmount.TryCreate(10.999m);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Precise Amount must have at most 2 decimal places.");
    }

    [Fact]
    public void PreciseAmount_Zero_PassesAllValidation()
    {
        // Zero passes built-in (no zero-check) and ValidateAdditional (0.00 has 2 decimal places)
        var result = PreciseAmount.TryCreate(0m);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(0m);
    }

    [Fact]
    public void PreciseAmount_FromNullableValid_ReturnsSuccess()
    {
        var result = PreciseAmount.TryCreate((decimal?)5.50m);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PreciseAmount_FromNullableInvalid_ReturnsFailure()
    {
        var result = PreciseAmount.TryCreate((decimal?)5.555m);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Precise Amount must have at most 2 decimal places.");
    }

    [Fact]
    public void PreciseAmount_FromStringValid_ReturnsSuccess()
    {
        var result = PreciseAmount.TryCreate("25.00");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PreciseAmount_FromStringInvalid_ReturnsFailure()
    {
        var result = PreciseAmount.TryCreate("25.123");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Precise Amount must have at most 2 decimal places.");
    }

    #endregion

    #region Optionality — RequiredInt without ValidateAdditional

    [Fact]
    public void PlainCount_WithoutHook_StillWorks()
    {
        var result = PlainCount.TryCreate(42);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(42);
    }

    #endregion

    #region Optionality — RequiredDecimal without ValidateAdditional

    [Fact]
    public void PlainRate_WithoutHook_StillWorks()
    {
        var result = PlainRate.TryCreate(3.14m);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(3.14m);
    }

    #endregion

    #region Create throws when ValidateAdditional rejects

    [Fact]
    public void Sku_Create_ValidPattern_ReturnsInstance()
    {
        var sku = Sku.Create("SKU-123456");
        sku.Value.Should().Be("SKU-123456");
    }

    [Fact]
    public void Sku_Create_InvalidPattern_ThrowsInvalidOperationException()
    {
        Action act = () => Sku.Create("INVALID");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create Sku:*");
    }

    [Fact]
    public void EvenPercentage_Create_Odd_ThrowsInvalidOperationException()
    {
        Action act = () => EvenPercentage.Create(51);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create EvenPercentage:*");
    }

    [Fact]
    public void PreciseAmount_Create_TooManyDecimals_ThrowsInvalidOperationException()
    {
        Action act = () => PreciseAmount.Create(1.999m);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create PreciseAmount:*");
    }

    #endregion

    #region fieldName propagation for int and decimal

    [Fact]
    public void EvenPercentage_CustomFieldName_PropagatedToAdditionalValidation()
    {
        var result = EvenPercentage.TryCreate(51, "discount");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/discount");
    }

    [Fact]
    public void PreciseAmount_CustomFieldName_PropagatedToAdditionalValidation()
    {
        var result = PreciseAmount.TryCreate(1.999m, "price");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/price");
    }

    #endregion
}