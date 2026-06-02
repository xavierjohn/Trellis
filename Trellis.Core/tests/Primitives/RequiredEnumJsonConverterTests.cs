namespace Trellis.Core.Tests.Primitives;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class RequiredEnumJsonConverterTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new RequiredEnumJsonConverter<RequiredEnumJsonConverterTestState>() }
    };

    [Fact]
    public void Read_NullToken_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Deserialize<RequiredEnumJsonConverterTestState>("null", _jsonOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("Cannot deserialize null into RequiredEnum<RequiredEnumJsonConverterTestState>*");
    }

    [Fact]
    public void Read_NullInProperty_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Deserialize<RequiredEnumJsonConverterTestOwner>("{\"value\":null}", _jsonOptions);

        act.Should().Throw<JsonException>()
            .WithMessage("Cannot deserialize null into RequiredEnum<RequiredEnumJsonConverterTestState>*");
    }

    [Fact]
    public void Read_AfterFix_RoundTripStillWorks()
    {
        var json = JsonSerializer.Serialize(RequiredEnumJsonConverterTestState.Active, _jsonOptions);

        var result = JsonSerializer.Deserialize<RequiredEnumJsonConverterTestState>(json, _jsonOptions);

        result.Should().BeSameAs(RequiredEnumJsonConverterTestState.Active);
    }

    [Fact]
    public void Read_InvalidName_ThrowsJsonExceptionWithTruncatedValue()
    {
        var oversized = new string('a', 1000);
        var json = JsonSerializer.Serialize(oversized);

        var act = () => JsonSerializer.Deserialize<RequiredEnumJsonConverterTestState>(json, _jsonOptions);

        act.Should().Throw<JsonException>()
            .Where(ex => ex.Message.Length < 200, "exception message must be bounded")
            .And.Message.Should().Contain($"'{new string('a', 64)}...");
    }

    [Fact]
    public void Read_InvalidNameWithNewlines_SanitizesControlChars()
    {
        var malicious = "foo\nINJECTED LOG LINE\n";
        var json = JsonSerializer.Serialize(malicious);

        var act = () => JsonSerializer.Deserialize<RequiredEnumJsonConverterTestState>(json, _jsonOptions);

        act.Should().Throw<JsonException>()
            .Where(ex => !ex.Message.Contains('\n', StringComparison.Ordinal),
                "newlines must be escaped to prevent log injection")
            .And.Message.Should().Contain("\\u000A");
    }
}

public sealed class RequiredEnumJsonConverterTestOwner
{
    [JsonPropertyName("value")]
    public RequiredEnumJsonConverterTestState Value { get; set; } = null!;
}

public sealed class RequiredEnumJsonConverterTestState :
    RequiredEnum<RequiredEnumJsonConverterTestState>,
    IScalarValue<RequiredEnumJsonConverterTestState, string>
{
    public static readonly RequiredEnumJsonConverterTestState Active = new();
    public static readonly RequiredEnumJsonConverterTestState Archived = new();

    public static Result<RequiredEnumJsonConverterTestState> TryCreate(string? value, string? fieldName = null) =>
        TryFromName(value, fieldName);
}