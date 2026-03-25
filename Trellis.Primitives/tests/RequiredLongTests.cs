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
/// Tests for RequiredLong value objects.
/// </summary>
public class RequiredLongTests
{
    [Fact]
    public void TryCreate_ValidLong_ReturnsSuccess()
    {
        var result = TraceId.TryCreate(123456789L);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(123456789L);
    }

    [Fact]
    public void TryCreate_Zero_ReturnsSuccess()
    {
        var result = TraceId.TryCreate(0L);
        result.IsSuccess.Should().BeTrue();
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
        result.Value.Value.Should().Be(999999999999L);
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
    public void JsonRoundTrip()
    {
        var original = TraceId.TryCreate(42L).Value;
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<TraceId>(json);
        deserialized.Should().Be(original);
    }
}
