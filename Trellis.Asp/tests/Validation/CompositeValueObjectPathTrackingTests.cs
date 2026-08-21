namespace Trellis.Asp.Tests.Validation;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Validation;
using Trellis.Primitives;
using Xunit;

/// <summary>
/// §8.2(a) — the wrapper installation gate must cover <em>primitive-only composite value objects</em>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ContainsScalarValueTransitively</c> decides whether a container property gets a path-tracking
/// wrapper. Its original question was "does this subtree contain a scalar value object?", which misses
/// a composite value object built entirely from primitives: it contains no <c>IScalarValue</c>, so the
/// walk returns <see langword="false"/> and no wrapper is installed — yet its converter still throws
/// composite-relative pointers that need re-rooting.
/// </para>
/// <para>
/// Without a wrapper the only remaining tier is arm 2's best-effort rebase from
/// <c>JsonException.Path</c>. That tier is lossy by construction: it round-trips through JSONPath, so a
/// property name containing <c>/</c> or <c>~</c> — both legal in JSON and both requiring RFC 6901
/// escaping — cannot be recovered unambiguously. The ancestor stack is lossless because it never
/// serialises the path to a parseable string in the first place.
/// </para>
/// </remarks>
public sealed class CompositeValueObjectPathTrackingTests
{
    private static JsonSerializerOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddScalarValueValidationForMinimalApi();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
    }

    [Fact]
    public void Primitive_only_composite_value_object_reports_an_absolute_pointer()
    {
        var options = BuildOptions();
        const string json = """{ "shipTo": { "street": "", "city": "Seattle" } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            var ex = Assert.Throws<TrellisJsonValidationException>(
                () => JsonSerializer.Deserialize<CompositeOrderCommand>(json, options));

            ex.InvalidInput.Should().NotBeNull();
            ex.InvalidInput!.Fields[0].Field.Path.Should().Be(
                "/shipTo/street",
                "the composite converter emits the composite-relative '/street' and the ASP layer must re-root it");
            JsonValidationPathRebase.IsMarked(ex).Should().BeTrue(
                "a re-rooted exception must be marked so no outer wrapper prefixes it a second time");
        }
    }

    [Fact]
    public void Composite_value_object_inside_a_collection_reports_an_index_precise_pointer()
    {
        var options = BuildOptions();
        const string json = """{ "stops": [ { "street": "1 Main", "city": "Seattle" }, { "street": "", "city": "Boston" } ] }""";

        using (ValidationErrorsContext.BeginScope())
        {
            var ex = Assert.Throws<TrellisJsonValidationException>(
                () => JsonSerializer.Deserialize<CompositeRouteCommand>(json, options));

            ex.InvalidInput.Should().NotBeNull();
            ex.InvalidInput!.Fields[0].Field.Path.Should().Be(
                "/stops/1/street",
                "the element index is only knowable from the collection wrapper, never from the composite converter");
            JsonValidationPathRebase.IsMarked(ex).Should().BeTrue(
                "a re-rooted exception must be marked so no outer wrapper prefixes it a second time");
        }
    }

    /// <summary>
    /// A dictionary key is arbitrary user input, so it is the one path segment that can legally contain
    /// the RFC 6901 escape characters. This is the concrete reason the lossless ancestor stack — and not
    /// <c>JsonException.Path</c>, which round-trips through a parseable JSONPath string — is the
    /// authoritative base path.
    /// </summary>
    [Fact]
    public void Dictionary_key_containing_rfc6901_escapes_is_escaped_not_split()
    {
        var options = BuildOptions();
        const string json = """{ "prices": { "a/b~c": { "street": "", "city": "Seattle" } } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            var ex = Assert.Throws<TrellisJsonValidationException>(
                () => JsonSerializer.Deserialize<CompositePriceCommand>(json, options));

            ex.InvalidInput!.Fields[0].Field.Path.Should().Be(
                "/prices/a~1b~0c/street",
                "'/' escapes to '~1' and '~' to '~0', so the key stays one segment instead of splitting the pointer");
        }
    }

    [Fact]
    public void Composite_value_object_inside_a_string_keyed_dictionary_reports_a_key_precise_pointer()
    {
        var options = BuildOptions();
        const string json = """{ "prices": { "USD": { "street": "1 Main", "city": "Seattle" }, "EUR": { "street": "", "city": "Paris" } } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            var ex = Assert.Throws<TrellisJsonValidationException>(
                () => JsonSerializer.Deserialize<CompositePriceCommand>(json, options));

            ex.InvalidInput!.Fields[0].Field.Path.Should().Be("/prices/EUR/street");
        }
    }

    /// <summary>
    /// §5.8 — a wrong token where an array was expected. Arrays are in the guaranteed
    /// body-pointer tier, so this failure must produce a field violation; as a plain
    /// <c>JsonException</c> it could not produce one at all and was invisible to clients.
    /// </summary>
    [Fact]
    public void A_non_array_token_for_a_tracked_collection_reports_the_property_pointer()
    {
        var options = BuildOptions();
        const string json = """{ "stops": { "not": "an array" } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            var ex = Assert.Throws<TrellisJsonValidationException>(
                () => JsonSerializer.Deserialize<CompositeRouteCommand>(json, options));

            ex.InvalidInput!.Fields[0].Field.Path.Should().Be(
                "/stops",
                "the property segment is pushed before throwing, so the pointer comes from the live ancestor stack rather than a lossy rebase");
        }
    }

    public sealed record CompositePriceCommand(Dictionary<string, CompositeAddress> Prices);

    public sealed record CompositeOrderCommand(CompositeAddress ShipTo);

    public sealed record CompositeRouteCommand(List<CompositeAddress> Stops);

    [JsonConverter(typeof(CompositeValueObjectJsonConverter<CompositeAddress>))]
    public sealed class CompositeAddress : ValueObject
    {
        private CompositeAddress(string street, string city)
        {
            Street = street;
            City = city;
        }

        public string Street { get; private set; } = string.Empty;

        public string City { get; private set; } = string.Empty;

        protected override void GetEqualityComponents(ref EqualityComponents components)
        {
            components.Add(Street);
            components.Add(City);
        }

        public static Result<CompositeAddress> TryCreate(string street, string city, string? fieldName = null)
        {
            var violations = new List<FieldViolation>();
            if (string.IsNullOrWhiteSpace(street))
                violations.Add(new FieldViolation(InputPointer.ForProperty("street"), ValidationCodes.Unspecified) { Detail = "Street is required." });
            if (string.IsNullOrWhiteSpace(city))
                violations.Add(new FieldViolation(InputPointer.ForProperty("city"), ValidationCodes.Unspecified) { Detail = "City is required." });

            return violations.Count > 0
                ? Result.Fail<CompositeAddress>(new Error.InvalidInput(EquatableArray.Create(violations.ToArray())))
                : Result.Ok(new CompositeAddress(street, city));
        }
    }
}
