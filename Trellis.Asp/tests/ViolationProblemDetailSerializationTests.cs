namespace Trellis.Asp.Tests;

using System.Text.Json;

/// <summary>
/// Pins the wire shape of the violation records.
///
/// Two rules pull in opposite directions and both matter:
/// <c>detail</c> and <c>args</c> are optional enrichment, so their absence means "nothing to
/// add" and they must be <em>omitted</em> rather than serialized as <c>null</c>. But an empty
/// <c>locations</c> is a <em>positive statement</em> that a rule is form-level rather than bound
/// to any field, so it must serialize as <c>[]</c> — a client reading <c>locations?.length</c>
/// cannot tell an omitted member from an empty one.
/// </summary>
public class ViolationProblemDetailSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static JsonElement Serialize<T>(T value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, Options)).RootElement;

    [Fact]
    public void Field_violation_omits_detail_and_args_when_absent()
    {
        var json = Serialize(new FieldViolationProblemDetail(
            "validation.error",
            Detail: null,
            new ViolationLocation("body", "/email", null),
            Args: null));

        json.TryGetProperty("detail", out _).Should().BeFalse("an absent detail is omitted, not null");
        json.TryGetProperty("args", out _).Should().BeFalse("absent args are omitted, not null");
        json.GetProperty("code").GetString().Should().Be("validation.error");
    }

    [Fact]
    public void Field_violation_emits_detail_and_args_when_present()
    {
        var json = Serialize(new FieldViolationProblemDetail(
            "validation.error",
            "Email address is not valid.",
            new ViolationLocation("body", "/email", null),
            new Dictionary<string, string> { ["min"] = "3" }));

        json.GetProperty("detail").GetString().Should().Be("Email address is not valid.");
        json.GetProperty("args").GetProperty("min").GetString().Should().Be("3");
    }

    [Fact]
    public void Rule_violation_emits_an_empty_locations_array_rather_than_omitting_it()
    {
        var json = Serialize(new RuleViolationProblemDetail(
            "validation.error",
            "Something is wrong with the form.",
            Locations: [],
            Args: null));

        json.TryGetProperty("locations", out var locations).Should().BeTrue(
            "an empty locations array states that the rule is form-level, which an omitted member cannot express");
        locations.ValueKind.Should().Be(JsonValueKind.Array);
        locations.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Rule_violation_omits_detail_and_args_when_absent()
    {
        var json = Serialize(new RuleViolationProblemDetail(
            "validation.error",
            Detail: null,
            Locations: [],
            Args: null));

        json.TryGetProperty("detail", out _).Should().BeFalse();
        json.TryGetProperty("args", out _).Should().BeFalse();
    }

    // --- the location object is a discriminated shape, never both members ---

    [Fact]
    public void A_body_location_carries_a_pointer_and_no_name()
    {
        var json = Serialize(new ViolationLocation("body", "/customer/email", null));

        json.GetProperty("in").GetString().Should().Be("body");
        json.GetProperty("pointer").GetString().Should().Be("/customer/email");
        json.TryGetProperty("name", out _).Should().BeFalse();
    }

    [Fact]
    public void A_query_location_carries_a_name_and_no_pointer()
    {
        var json = Serialize(new ViolationLocation("query", null, "page"));

        json.GetProperty("in").GetString().Should().Be("query");
        json.GetProperty("name").GetString().Should().Be("page");
        json.TryGetProperty("pointer", out _).Should().BeFalse(
            "a JSON Pointer addresses a location in a document, and a query parameter is not in one");
    }

    [Fact]
    public void The_in_discriminator_is_always_present() =>
        Serialize(new ViolationLocation("unknown", "", null))
            .GetProperty("in").GetString().Should().Be("unknown");
}
