namespace Trellis.Core.Tests.Primitives;

public sealed class ComparableString : RequiredString<ComparableString>, IScalarValue<ComparableString, string>
{
    private ComparableString(string value) : base(value)
    {
    }

    public static Result<ComparableString> TryCreate(string? value, string? fieldName = null) =>
        Result.Ok(new ComparableString(value!));
}

public class RequiredStringTests
{
    [Fact]
    public void StartsWith_WithOrdinalIgnoreCase_MatchesCaseInsensitively()
    {
        var value = ComparableString.Create("Hello");

        value.StartsWith("hello", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void Contains_WithOrdinal_CaseSensitive()
    {
        var value = ComparableString.Create("Hello");

        value.Contains("HEL", StringComparison.Ordinal).Should().BeFalse();
    }

    [Fact]
    public void Contains_WithCharOrdinalIgnoreCase_MatchesCaseInsensitively()
    {
        var value = ComparableString.Create("Hello");

        value.Contains('h', StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void EndsWith_WithOrdinalIgnoreCase_Works()
    {
        var value = ComparableString.Create("Hello");

        value.EndsWith("LLO", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void Contains_WithSingleArgument_StillWorks()
    {
        var value = ComparableString.Create("Hello");

        value.Contains("ell").Should().BeTrue();
    }
}
