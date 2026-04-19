namespace Trellis.Primitives.Tests;

using System.Text.Json;
using Trellis.Testing;

/// <summary>
/// Test value object with max length only.
/// </summary>
[StringLength(10)]
public partial class ShortCode : RequiredString<ShortCode>
{
}

/// <summary>
/// Test value object with both min and max length.
/// </summary>
[StringLength(50, MinimumLength = 3)]
public partial class UserName : RequiredString<UserName>
{
}

/// <summary>
/// Tests for RequiredString [StringLength] attribute support.
/// Validates that the source generator emits length validation in TryCreate.
/// </summary>
public class RequiredStringLengthTests
{
    #region MaxLength only

    [Fact]
    public void TryCreate_MaxLengthOnly_ValidLength_ReturnsSuccess()
    {
        var result = ShortCode.TryCreate("ABC");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("ABC");
    }

    [Fact]
    public void TryCreate_MaxLengthOnly_ExactMaxLength_ReturnsSuccess()
    {
        var result = ShortCode.TryCreate("1234567890"); // exactly 10
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("1234567890");
    }

    [Fact]
    public void TryCreate_MaxLengthOnly_ValidAfterTrimming_ReturnsTrimmedSuccess()
    {
        var result = ShortCode.TryCreate("  ABC123  "); // trims to 6 characters

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("ABC123");
    }

    [Fact]
    public void TryCreate_MaxLengthOnly_ExceedsMaxLength_ReturnsFailure()
    {
        var result = ShortCode.TryCreate("12345678901"); // 11 characters
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/shortCode");
        validation.Fields[0].Detail.Should().Be("Short Code must be 10 characters or fewer.");
    }

    [Fact]
    public void TryCreate_MaxLengthOnly_NullValue_ReturnsEmptyError()
    {
        var result = ShortCode.TryCreate(null);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Short Code cannot be empty.");
    }

    [Fact]
    public void TryCreate_MaxLengthOnly_EmptyString_ReturnsEmptyError()
    {
        var result = ShortCode.TryCreate("");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Short Code cannot be empty.");
    }

    [Fact]
    public void TryCreate_MaxLengthOnly_CustomFieldName_UsesInErrorMessage()
    {
        var result = ShortCode.TryCreate("12345678901", "code");
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/code");
    }

    #endregion

    #region MinLength and MaxLength

    [Fact]
    public void TryCreate_MinAndMaxLength_ValidLength_ReturnsSuccess()
    {
        var result = UserName.TryCreate("John");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("John");
    }

    [Fact]
    public void TryCreate_MinAndMaxLength_ExactMinLength_ReturnsSuccess()
    {
        var result = UserName.TryCreate("abc"); // exactly 3
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("abc");
    }

    [Fact]
    public void TryCreate_MinAndMaxLength_ExactMaxLength_ReturnsSuccess()
    {
        var result = UserName.TryCreate(new string('x', 50)); // exactly 50
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_MinAndMaxLength_BelowMinLength_ReturnsFailure()
    {
        var result = UserName.TryCreate("ab"); // 2 characters, min is 3
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/userName");
        validation.Fields[0].Detail.Should().Be("User Name must be at least 3 characters.");
    }

    [Fact]
    public void TryCreate_MinAndMaxLength_ExceedsMaxLength_ReturnsFailure()
    {
        var result = UserName.TryCreate(new string('x', 51)); // 51 characters, max is 50
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("User Name must be 50 characters or fewer.");
    }

    [Fact]
    public void TryCreate_MinAndMaxLength_NullValue_ReturnsEmptyError()
    {
        var result = UserName.TryCreate(null);
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("User Name cannot be empty.");
    }

    #endregion

    #region Create (throwing variant)

    [Fact]
    public void Create_ValidLength_ReturnsInstance()
    {
        var shortCode = ShortCode.Create("ABC");
        shortCode.Value.Should().Be("ABC");
    }

    [Fact]
    public void Create_ExceedsMaxLength_ThrowsInvalidOperationException()
    {
        Action act = () => ShortCode.Create("12345678901");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create ShortCode*");
    }

    [Fact]
    public void Create_BelowMinLength_ThrowsInvalidOperationException()
    {
        Action act = () => UserName.Create("ab");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create UserName*");
    }

    #endregion

    #region Parse / TryParse

    [Fact]
    public void Parse_ValidLength_ReturnsInstance()
    {
        var shortCode = ShortCode.Parse("ABC", null);
        shortCode.Value.Should().Be("ABC");
    }

    [Fact]
    public void Parse_ExceedsMaxLength_ThrowsFormatException()
    {
        Action act = () => ShortCode.Parse("12345678901", null);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_ValidLength_ReturnsTrue()
    {
        ShortCode.TryParse("ABC", null, out var result).Should().BeTrue();
        result!.Value.Should().Be("ABC");
    }

    [Fact]
    public void TryParse_ExceedsMaxLength_ReturnsFalse()
    {
        ShortCode.TryParse("12345678901", null, out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    #endregion

    #region JSON serialization

    [Fact]
    public void JsonSerialize_ValidLength_RoundTrips()
    {
        var shortCode = ShortCode.Create("ABC123");
        var json = JsonSerializer.Serialize(shortCode);
        var deserialized = JsonSerializer.Deserialize<ShortCode>(json);
        deserialized!.Value.Should().Be("ABC123");
    }

    [Fact]
    public void JsonDeserialize_ExceedsMaxLength_ThrowsFormatException()
    {
        var json = JsonSerializer.Serialize("12345678901");
        Action act = () => JsonSerializer.Deserialize<ShortCode>(json);
        act.Should().Throw<FormatException>();
    }

    #endregion

    #region Explicit cast

    [Fact]
    public void ExplicitCast_ValidLength_ReturnsInstance()
    {
        ShortCode sc = (ShortCode)"ABC";
        sc.Value.Should().Be("ABC");
    }

    [Fact]
    public void ExplicitCast_ExceedsMaxLength_ThrowsInvalidOperationException()
    {
        Action act = () => { ShortCode sc = (ShortCode)"12345678901"; };
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create ShortCode:*Short Code must be 10 characters or fewer*");
    }

    #endregion

    #region No StringLength attribute — existing behavior preserved

    [Fact]
    public void TryCreate_NoStringLength_AnyLength_ReturnsSuccess()
    {
        // TrackingId has no [StringLength] — should accept any length
        var result = TrackingId.TryCreate(new string('x', 10000));
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}
