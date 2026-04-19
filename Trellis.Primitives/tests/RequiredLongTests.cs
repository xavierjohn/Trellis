using Trellis.Testing;
namespace Trellis.Primitives.Tests;

/// <summary>
/// RequiredLong without [Range] — accepts any non-null long.
/// </summary>
public partial class TraceId : RequiredLong<TraceId> { }

/// <summary>
/// RequiredLong with [Range].
/// </summary>
[Range(1, 1000000)]
public partial class SequenceNumber : RequiredLong<SequenceNumber> { }

/// <summary>
/// RequiredLong with [Range] at int.MaxValue boundary — tests int→long cast.
/// </summary>
[Range(0L, 5000000000L)]
public partial class LargeSequence : RequiredLong<LargeSequence> { }

/// <summary>
/// RequiredLong with full long range.
/// </summary>
[Range(long.MinValue, long.MaxValue)]
public partial class FullRangeLong : RequiredLong<FullRangeLong> { }

/// <summary>
/// Tests for RequiredLong value objects.
/// </summary>
public class RequiredLongTests
{
    [Fact]
    public void TryCreate_ValidLong_ReturnsSuccess()
    {
        var result = TraceId.TryCreate(123456789L);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(123456789L);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void TryCreate_ExtremeValues_ReturnsSuccess(long value)
    {
        var result = TraceId.TryCreate(value);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(value);
    }

    [Fact]
    public void TryCreate_Null_ReturnsFailure()
    {
        var result = TraceId.TryCreate((long?)null);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_FromString_ReturnsSuccess()
    {
        var result = TraceId.TryCreate("999999999999");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(999999999999L);
    }

    [Fact]
    public void TryCreate_WithRange_WithinRange_ReturnsSuccess()
    {
        var result = SequenceNumber.TryCreate(500L);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_WithRange_BelowMinimum_ReturnsFailure()
    {
        var result = SequenceNumber.TryCreate(0L);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_WithRange_AboveMaximum_ReturnsFailure()
    {
        var result = SequenceNumber.TryCreate(1000001L);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_WithRange_AtMinBoundary_ReturnsSuccess()
    {
        var result = SequenceNumber.TryCreate(1L);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(1L);
    }

    [Fact]
    public void TryCreate_WithRange_AtMaxBoundary_ReturnsSuccess()
    {
        var result = SequenceNumber.TryCreate(1000000L);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(1000000L);
    }

    [Fact]
    public void TryCreate_WithRange_NullableNull_ReturnsFailure()
    {
        var result = SequenceNumber.TryCreate((long?)null);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_WithRange_FromString_WithinRange_ReturnsSuccess()
    {
        var result = SequenceNumber.TryCreate("500");
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(500L);
    }

    [Fact]
    public void TryCreate_WithRange_FromString_OutOfRange_ReturnsFailure()
    {
        var result = SequenceNumber.TryCreate("1000001");
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_WithRange_WithCustomFieldName_ReturnsCorrectFieldName()
    {
        var result = SequenceNumber.TryCreate(0L, "myField");
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.UnwrapError();
        validation.FieldErrors[0].FieldName.Should().Be("myField");
    }

    [Fact]
    public void Create_WithRange_OutOfRange_ThrowsInvalidOperationException()
    {
        Action act = () => SequenceNumber.Create(0L);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var original = TraceId.TryCreate(42L).Unwrap();
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<TraceId>(json);
        deserialized.Should().Be(original);
    }

    #region LargeSequence — [Range(0L, 5000000000L)] boundary tests

    [Fact]
    public void LargeSequence_AtLongMax_ReturnsSuccess()
    {
        var result = LargeSequence.TryCreate(5000000000L);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(5000000000L);
    }

    [Fact]
    public void LargeSequence_AboveLongMax_ReturnsFailure()
    {
        var result = LargeSequence.TryCreate(5000000001L);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void LargeSequence_Zero_ReturnsSuccess()
    {
        var result = LargeSequence.TryCreate(0L);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void LargeSequence_Negative_ReturnsFailure()
    {
        var result = LargeSequence.TryCreate(-1L);
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region FullRangeLong — [Range(long.MinValue, long.MaxValue)]

    [Fact]
    public void FullRangeLong_AtLongMinValue_ReturnsSuccess()
    {
        var result = FullRangeLong.TryCreate(long.MinValue);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(long.MinValue);
    }

    [Fact]
    public void FullRangeLong_AtLongMaxValue_ReturnsSuccess()
    {
        var result = FullRangeLong.TryCreate(long.MaxValue);
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(long.MaxValue);
    }

    [Fact]
    public void FullRangeLong_Zero_ReturnsSuccess()
    {
        var result = FullRangeLong.TryCreate(0L);
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}
