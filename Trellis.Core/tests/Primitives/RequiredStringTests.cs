namespace Trellis.Core.Tests.Primitives;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;

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
    public void RequiredString_Type_HasDebuggerDisplayAttribute()
    {
        var attribute = typeof(RequiredString<>).GetCustomAttribute<DebuggerDisplayAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Value.Should().Be("{Value}");
    }

    [Fact]
    public void RequiredGuid_Type_HasDebuggerDisplayAttribute()
    {
        var attribute = typeof(RequiredGuid<>).GetCustomAttribute<DebuggerDisplayAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Value.Should().Be("{Value}");
    }

    [Fact]
    public void RequiredEnum_Type_HasDebuggerDisplayAttribute()
    {
        var attribute = typeof(RequiredEnum<>).GetCustomAttribute<DebuggerDisplayAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Value.Should().Be("{Value}");
    }

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

    // The three single-argument query helpers deliberately keep the BCL's own comparison
    // semantics so EF Core can translate them, and the BCL is not self-consistent:
    // StartsWith/EndsWith are culture-sensitive while Contains is ordinal. These pin that
    // divergence so it cannot change unnoticed, and so the documented example stays true.
    //
    // A globalization-invariant host has no culture data, so `new CultureInfo("en-US")`
    // throws there and culture-sensitive comparison collapses to ordinal-like behavior.
    // The divergence these tests describe genuinely does not exist in that configuration,
    // so they skip rather than fail.
    //
    // Invariant mode can be enabled through two channels that must both be checked: the
    // MSBuild property, which surfaces as the AppContext switch, and the environment
    // variable, which does NOT set that switch.
    private static bool IsGlobalizationInvariant
    {
        get
        {
            if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var enabled) && enabled)
                return true;

            var value = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT");
            return value == "1" || (bool.TryParse(value, out var parsed) && parsed);
        }
    }

    [Fact]
    public void EndsWith_WithSingleArgument_IsCultureSensitive_AndIgnoresSoftHyphen()
    {
        Assert.SkipWhen(IsGlobalizationInvariant, "Requires culture data; the host is globalization-invariant.");

        using var _ = new CultureScope("en-US");
        var value = ComparableString.Create("cooper\u00ADative");

        value.EndsWith("rative").Should().BeTrue();
    }

    [Fact]
    public void StartsWith_WithSingleArgument_IsCultureSensitive_AndIgnoresSoftHyphen()
    {
        Assert.SkipWhen(IsGlobalizationInvariant, "Requires culture data; the host is globalization-invariant.");

        using var _ = new CultureScope("en-US");
        var value = ComparableString.Create("cooper\u00ADative");

        value.StartsWith("coopera").Should().BeTrue();
    }

    [Fact]
    public void Contains_WithSingleArgument_IsOrdinal_AndDoesNotIgnoreSoftHyphen()
    {
        Assert.SkipWhen(IsGlobalizationInvariant, "Requires culture data; the host is globalization-invariant.");

        using var _ = new CultureScope("en-US");
        var value = ComparableString.Create("cooper\u00ADative");

        // Same receiver and same argument as EndsWith above, opposite answer.
        value.Contains("rative").Should().BeFalse();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo original = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = this.original;
    }
}