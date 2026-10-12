namespace Trellis.Core.Tests.Primitives;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class RequiredEnumJsonConverterTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new RequiredEnumJsonConverter<RequiredEnumJsonConverterTestState>() }
    };

    [Theory]
    [InlineData("\"unknown\"", ValidationCodes.EnumNameUndefined)]
    [InlineData("null", ValidationCodes.ValueNotNull)]
    [InlineData("42", ValidationCodes.FormatConversion)]
    [InlineData("{\"nested\":[1,2]}", ValidationCodes.FormatConversion)]
    public void TryRead_InvalidValue_ReturnsStructuredFailureAfterConsumingTheValue(string json, string code)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json + " "), isFinalBlock: false, state: default);
        reader.Read();

        var result = new RequiredEnumJsonConverter<RequiredEnumJsonConverterTestState>()
            .TryRead(ref reader, typeof(RequiredEnumJsonConverterTestState), _jsonOptions);

        result.IsFailure.Should().BeTrue();
        var error = result.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        var violation = error.Fields.Items.Should().ContainSingle().Subject;
        violation.ReasonCode.Should().Be(code);
        violation.Field.Should().Be(InputPointer.Root);
        violation.Detail.Should().NotBeNullOrWhiteSpace();
        reader.BytesConsumed.Should().Be(System.Text.Encoding.UTF8.GetByteCount(json));
    }

    [Fact]
    public void TryRead_ValidName_ReturnsTheDeclaredMember()
    {
        var reader = new Utf8JsonReader("\"Active\""u8);
        reader.Read();

        var result = new RequiredEnumJsonConverter<RequiredEnumJsonConverterTestState>()
            .TryRead(ref reader, typeof(RequiredEnumJsonConverterTestState), _jsonOptions);

        result.TryGetValue(out var value).Should().BeTrue();
        value.Should().BeSameAs(RequiredEnumJsonConverterTestState.Active);
    }

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
    public void Read_InvalidName_CarriesAStructuredViolationWithTheSanitizedMessage()
    {
        var json = JsonSerializer.Serialize("not-a-member");

        var act = () => JsonSerializer.Deserialize<RequiredEnumJsonConverterTestState>(json, _jsonOptions);

        var ex = act.Should().Throw<TrellisJsonValidationException>(
            "the boundary recognizes a structured validation failure only through this type")
            .Subject.Single();

        var violation = ex.InvalidInput!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().BeEmpty("a scalar converter does not know where in the document it sits");
        violation.Detail.Should().Be(ex.Message,
            "the curated, sanitized message is the detail — propagating the producer's raw detail instead would silently drop the sanitization");
    }

    [Fact]
    public void Read_NullToken_CarriesAStructuredViolation()
    {
        var act = () => JsonSerializer.Deserialize<RequiredEnumJsonConverterTestState>("null", _jsonOptions);

        var ex = act.Should().Throw<TrellisJsonValidationException>().Subject.Single();
        ex.InvalidInput!.Fields.Items.Should().ContainSingle();
    }

    [Fact]
    public void Read_WrongTokenType_CarriesAStructuredViolation()
    {
        var act = () => JsonSerializer.Deserialize<RequiredEnumJsonConverterTestState>("42", _jsonOptions);

        var ex = act.Should().Throw<TrellisJsonValidationException>().Subject.Single();
        ex.InvalidInput!.Fields.Items.Should().ContainSingle();
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
}