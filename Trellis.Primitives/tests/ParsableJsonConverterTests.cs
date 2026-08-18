namespace Trellis.Primitives.Tests;

using System.Text.Json;
using Trellis.Testing;

/// <summary>
/// Cross-cutting contract tests for <see cref="ParsableJsonConverter{T}"/>, the converter every
/// generated <c>Required*</c> primitive uses. Per-primitive round-trip coverage lives with each
/// primitive's own tests; these pin the behaviour that must hold across all of them.
/// </summary>
public class ParsableJsonConverterTests
{
    // The converter throws JsonException for every other failure (bad token type, null for a
    // non-nullable target). A parse failure must not leak IParsable's FormatException, which
    // callers catching JsonException around JsonSerializer would miss.
    [Fact]
    public void Deserializing_a_malformed_value_throws_JsonException() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<TicketNumber>("\"not-a-number\""))
            .Should().Throw<JsonException>();

    [Fact]
    public void Deserializing_a_value_that_fails_validation_throws_JsonException() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<Percentage>("\"150\""))
            .Should().Throw<JsonException>();

    [Fact]
    public void JsonException_from_a_parse_failure_preserves_the_underlying_cause() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<TicketNumber>("\"not-a-number\""))
            .Should().Throw<JsonException>()
            .WithInnerException<FormatException>();

    [Fact]
    public void Deserializing_an_unexpected_token_still_throws_JsonException() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<TicketNumber>("[]"))
            .Should().Throw<JsonException>();

    // Null is rejected by the converter itself rather than being handed to Parse, so there is
    // no inner exception to unwrap.
    [Fact]
    public void Deserializing_null_throws_JsonException_without_attempting_a_parse() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<TicketNumber>("null"))
            .Should().Throw<JsonException>()
            .Which.InnerException.Should().BeNull();

    // Regression: the converter must opt into HandleNull, otherwise System.Text.Json bypasses
    // Read for null tokens on reference-type targets and a non-nullable primitive silently
    // deserializes to null.
    [Fact]
    public void Deserializing_a_null_property_throws_rather_than_yielding_a_null_primitive() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<TicketHolder>("""{"Number":null}"""))
            .Should().Throw<JsonException>();

    [Fact]
    public void Serializing_a_null_property_writes_a_JSON_null() =>
        JsonSerializer.Serialize(new TicketHolder(null!)).Should().Be("""{"Number":null}""");

    private sealed record TicketHolder(TicketNumber Number);

    [Fact]
    public void Numeric_backed_primitives_serialize_as_JSON_numbers() =>
        JsonSerializer.Serialize(TicketNumber.TryCreate(42).Unwrap()).Should().Be("42");

    [Fact]
    public void Boolean_backed_primitives_serialize_as_JSON_booleans() =>
        JsonSerializer.Serialize(GiftWrap.TryCreate(true).Unwrap()).Should().Be("true");

    [Fact]
    public void String_backed_primitives_serialize_as_JSON_strings() =>
        JsonSerializer.Serialize(Slug.TryCreate("my-slug").Unwrap()).Should().Be("\"my-slug\"");
}