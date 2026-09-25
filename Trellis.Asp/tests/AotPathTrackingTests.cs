namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Issue #664: index-precise validation field paths must work under Native AOT, at parity with the
/// reflection pipeline fixed in #658.
/// </summary>
/// <remarks>
/// <para>
/// Under Native AOT the type-info modifier cannot call <c>Type.MakeGenericType</c>, so the closed
/// path-tracking converters are created at compile time by the source generator and handed to
/// <see cref="ScalarValuePathTracking"/>. These tests exercise exactly that resolution path by
/// suppressing dynamic path-converter construction, which is what makes them meaningful on a JIT
/// test host: without the suppression the reflection pipeline would produce the right answer for the
/// wrong reason.
/// </para>
/// <para>
/// The DTO types here are unique to this class so the process-wide registry cannot be perturbed by,
/// or perturb, tests running in parallel.
/// </para>
/// </remarks>
[Collection(nameof(AotPathTrackingTests))]
public sealed class AotPathTrackingTests : IDisposable
{
    public sealed class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
    {
        private Email(string value) : base(value) { }

        public static Email CreateForTest(string value) => new(value);

        public static Result<Email> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            return string.IsNullOrWhiteSpace(value) || !value.Contains('@')
                ? Result.Fail<Email>(new Error.InvalidInput(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Email address is not valid." })))
                : Result.Ok(new Email(value));
        }
    }

    public sealed record AotMemberDto(Email Email);

    public sealed record AotTeamCommand(List<AotMemberDto> Members);

    public sealed record AotEmailListCommand(IReadOnlyList<Email> Emails);

    public sealed record AotEmailDictionaryCommand(Dictionary<string, Email> Prices);

    public sealed record AotCustomConvertedEmailCommand(
        [property: JsonConverter(typeof(AotEmailAliasConverter))] Email Contact);

    public sealed record AotCustomConvertedMaybeEmailCommand(
        [property: JsonConverter(typeof(AotMaybeEmailAliasConverter))] Maybe<Email> Contact);

    public sealed class AotEmailAliasConverter : JsonConverter<Email>
    {
        public override Email? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() == "primary" ? Email.CreateForTest("ada@x.com") : throw new JsonException();

        public override void Write(Utf8JsonWriter writer, Email value, JsonSerializerOptions options) =>
            writer.WriteStringValue("primary");
    }

    public sealed class AotMaybeEmailAliasConverter : JsonConverter<Maybe<Email>>
    {
        public override Maybe<Email> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() == "primary"
                ? Maybe.From(Email.CreateForTest("ada@x.com"))
                : throw new JsonException();

        public override void Write(Utf8JsonWriter writer, Maybe<Email> value, JsonSerializerOptions options) =>
            writer.WriteStringValue("primary");
    }

    public sealed record AotAliasedMemberDto([property: JsonPropertyName("primary_email")] Email Contact);

    public sealed record AotAliasedTeamCommand(List<AotAliasedMemberDto> Members);

    public sealed record AotAddressDto(Email Email);

    public sealed record AotPersonCommand(AotAddressDto Contact);

    public void Dispose() => ScalarValuePathTracking.ClearForTests();

    private static JsonSerializerOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddScalarValueValidationForMinimalApi();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
    }

    [Fact]
    public void Without_generated_registrations_the_aot_path_reports_only_the_leaf_name()
    {
        ScalarValuePathTracking.ClearForTests();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{ "members": [ { "email": "not-an-email" } ] }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotTeamCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields[0].Field.Path.Should().Be(
                "/email",
                "this is the pre-#664 Native AOT behaviour the generated registrations exist to fix");
        }
    }

    [Fact]
    public void Registered_collection_reports_the_index_precise_path_without_runtime_generic_construction()
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterProperty<Email>();
        ScalarValuePathTracking.RegisterCollection<List<AotMemberDto>, AotMemberDto>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{ "members": [ { "email": "ada@x.com" }, { "email": "not-an-email" } ] }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotTeamCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/members/1/email");
        }
    }

    [Theory]
    [InlineData("""{ "members": [ { "primary_email": "not-an-email" } ] }""")]
    [InlineData("""{ "members": [ { "primary_email": null } ] }""")]
    public void Registered_collection_uses_the_effective_json_property_name_without_runtime_generic_construction(
        string json)
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterProperty<Email>();
        ScalarValuePathTracking.RegisterCollection<List<AotAliasedMemberDto>, AotAliasedMemberDto>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotAliasedTeamCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/members/0/primary_email");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Property_level_custom_converter_is_preserved(bool simulateNativeAot)
    {
        ScalarValuePathTracking.ClearForTests();
        if (simulateNativeAot)
            ScalarValuePathTracking.RegisterProperty<Email>();

        using var aot = simulateNativeAot
            ? ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests()
            : null;
        var options = BuildOptions();
        const string json = """{"contact":"primary"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var command = JsonSerializer.Deserialize<AotCustomConvertedEmailCommand>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeFalse();
            command!.Contact.Value.Should().Be("ada@x.com");
            JsonSerializer.Serialize(command, options).Should().Be(json);
        }
    }

    [Fact]
    public void Registered_optional_property_preserves_property_level_custom_converter_without_runtime_generic_construction()
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterProperty<Maybe<Email>>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{"contact":"primary"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var command = JsonSerializer.Deserialize<AotCustomConvertedMaybeEmailCommand>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeFalse();
            JsonSerializer.Serialize(command, options).Should().Be(json);
        }
    }

    [Theory]
    [InlineData("""{ "emails": [ "ada@x.com", "not-an-email" ] }""", "/emails/1")]
    [InlineData("""{ "emails": [ null ] }""", "/emails/0")]
    public void Registered_direct_scalar_collection_reports_the_element_path_without_runtime_generic_construction(
        string json,
        string expectedPath)
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterCollection<IReadOnlyList<Email>, Email>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotEmailListCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be(expectedPath);
        }
    }

    [Theory]
    [InlineData("""{ "prices": { "USD": "ada@x.com", "EUR": "not-an-email" } }""")]
    [InlineData("""{ "prices": { "EUR": null } }""")]
    public void Registered_direct_scalar_dictionary_reports_the_key_path_without_runtime_generic_construction(
        string json)
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterDictionary<Dictionary<string, Email>, Email>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotEmailDictionaryCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/prices/EUR");
        }
    }

    [Fact]
    public void Registered_nested_object_reports_the_full_path_without_runtime_generic_construction()
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterProperty<Email>();
        ScalarValuePathTracking.RegisterObject<AotAddressDto>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{ "contact": { "email": "not-an-email" } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<AotPersonCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/contact/email");
        }
    }

    [Fact]
    public void Registrations_leave_round_trip_serialization_unchanged()
    {
        ScalarValuePathTracking.ClearForTests();
        ScalarValuePathTracking.RegisterProperty<Email>();
        ScalarValuePathTracking.RegisterCollection<List<AotMemberDto>, AotMemberDto>();

        using var aot = ServiceCollectionExtensions.SuppressDynamicPathConverterConstructionForTests();
        var options = BuildOptions();
        const string json = """{"members":[{"email":"ada@x.com"}]}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var command = JsonSerializer.Deserialize<AotTeamCommand>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeFalse();
            JsonSerializer.Serialize(command, options).Should().Be(json);
        }
    }
}