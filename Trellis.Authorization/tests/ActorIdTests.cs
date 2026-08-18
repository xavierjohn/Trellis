namespace Trellis.Authorization.Tests;

using Trellis.Testing;

/// <summary>
/// Behavioural tests for the strongly-typed <see cref="ActorId"/> value object that
/// wraps the principal id behind <see cref="Actor.Id"/>.
/// </summary>
public class ActorIdTests
{
    [Fact]
    public void TryCreate_NullValue_ReturnsFailure()
    {
        var result = ActorId.TryCreate(null);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_EmptyValue_ReturnsFailure()
    {
        var result = ActorId.TryCreate(string.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_WhitespaceOnly_ReturnsFailure_AfterTrim()
    {
        var result = ActorId.TryCreate("   ");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_ValidValue_ReturnsSuccess()
    {
        var result = ActorId.TryCreate("user-1");

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("user-1");
    }

    [Fact]
    public void TryCreate_TrimsLeadingAndTrailingWhitespace()
    {
        var result = ActorId.TryCreate("  user-1  ");

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("user-1");
    }

    [Fact]
    public void Equality_TwoIdsWithSameValue_AreEqual()
    {
        var a = ActorId.TryCreate("user-1").Unwrap();
        var b = ActorId.TryCreate("user-1").Unwrap();

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_OrdinalCaseSensitive()
    {
        var lower = ActorId.TryCreate("user-1").Unwrap();
        var upper = ActorId.TryCreate("USER-1").Unwrap();

        lower.Equals(upper).Should().BeFalse();
    }

    [Fact]
    public void ImplicitConversion_ToString_ReturnsUnderlyingValue()
    {
        var id = ActorId.TryCreate("user-1").Unwrap();
        string raw = id;

        raw.Should().Be("user-1");
    }
}