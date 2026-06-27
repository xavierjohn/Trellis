namespace Trellis.Asp.Tests;

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
/// Issue #658: the scalar value-object auto-validation path must report the index-precise JSON
/// path (e.g. <c>/members/0/email</c>) for a value object nested inside a collection or another
/// object, at parity with the FluentValidation integration — not just the leaf property name.
/// </summary>
public sealed class NestedPathValidationTests
{
    // Hand-written scalar value object (this test project has no source generator).
    public sealed class TestEmail : ScalarValueObject<TestEmail, string>, IScalarValue<TestEmail, string>
    {
        private TestEmail(string value) : base(value) { }

        public static Result<TestEmail> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            return string.IsNullOrWhiteSpace(value) || !value.Contains('@')
                ? Result.Fail<TestEmail>(new Error.InvalidInput(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email address is not valid." })))
                : Result.Ok(new TestEmail(value));
        }
    }

    public sealed record MemberDto(TestEmail Email);

    public sealed record CreateMembersCommand(List<MemberDto> Members);

    public sealed record AddressDto(TestEmail Email);

    public sealed record CreatePersonCommand(AddressDto Contact);

    // A node whose property graph references its own type, exercising the cyclic-graph path: the
    // wrapper must not capture a converter at modifier time (which would re-enter metadata resolution
    // for a self-referential DTO and recurse forever).
    public sealed record NodeDto(TestEmail Email, NodeDto? Child = null);

    // A value object that reports its failure as a multi-field RuleViolation carrying a field pointer,
    // exercising the rule-pointer prefixing path rather than only the FieldViolation path.
    public sealed class TestRuleEmail : ScalarValueObject<TestRuleEmail, string>, IScalarValue<TestRuleEmail, string>
    {
        private TestRuleEmail(string value) : base(value) { }

        public static Result<TestRuleEmail> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            return string.IsNullOrWhiteSpace(value) || !value.Contains('@')
                ? Result.Fail<TestRuleEmail>(new Error.InvalidInput(
                    EquatableArray<FieldViolation>.Empty,
                    EquatableArray.Create(new RuleViolation(
                        "email.rule",
                        EquatableArray.Create(InputPointer.ForProperty(field)),
                        Detail: "Email address is not valid."))))
                : Result.Ok(new TestRuleEmail(value));
        }
    }

    public sealed record RuleMemberDto(TestRuleEmail Email);

    public sealed record CreateRuleMembersCommand(List<RuleMemberDto> Members);

    // A consumer-supplied converter on a container property: it consumes the value itself, so Trellis
    // must not overwrite it with a path-tracking wrapper (which would bypass the consumer's converter).
    private sealed class SkippingAddressConverter : JsonConverter<AddressDto?>
    {
        public override AddressDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, AddressDto? value, JsonSerializerOptions options) =>
            writer.WriteNullValue();
    }

    public sealed record PersonWithCustomContact(
        [property: JsonConverter(typeof(SkippingAddressConverter))] AddressDto? Contact);

    private static JsonSerializerOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddScalarValueValidationForMinimalApi();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
    }

    [Fact]
    public void Value_object_in_a_collection_reports_the_index_precise_path()
    {
        var options = BuildOptions();
        const string json = """{ "members": [ { "email": "not-an-email" }, { "email": "ada@x.com" } ] }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<CreateMembersCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/members/0/email");
        }
    }

    [Fact]
    public void Value_object_in_a_nested_object_reports_the_json_pointer_path()
    {
        var options = BuildOptions();
        const string json = """{ "contact": { "email": "not-an-email" } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<CreatePersonCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/contact/email");
        }
    }

    [Fact]
    public void Top_level_value_object_path_is_unchanged()
    {
        var options = BuildOptions();
        const string json = """{ "email": "not-an-email" }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<MemberDto>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields[0].Field.Path.Should().Be("/email");
        }
    }

    [Fact]
    public void Value_object_in_a_self_referential_object_graph_reports_the_nested_path()
    {
        var options = BuildOptions();
        const string json = """{ "email": "ada@x.com", "child": { "email": "not-an-email" } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<NodeDto>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/child/email");
        }
    }

    [Fact]
    public void Rule_violation_field_pointers_are_prefixed_with_the_ancestor_path()
    {
        var options = BuildOptions();
        const string json = """{ "members": [ { "email": "not-an-email" } ] }""";

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<CreateRuleMembersCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Rules.Items.Should().ContainSingle();
            error.Rules.Items[0].Fields.Items.Should().ContainSingle();
            error.Rules.Items[0].Fields.Items[0].Path.Should().Be("/members/0/email");
        }
    }

    [Fact]
    public void Explicit_property_level_converter_on_a_container_is_not_overwritten()
    {
        var options = BuildOptions();
        const string json = """{ "contact": { "email": "not-an-email" } }""";

        using (ValidationErrorsContext.BeginScope())
        {
            var result = JsonSerializer.Deserialize<PersonWithCustomContact>(json, options);

            // The consumer's [JsonConverter] owns the property, so the path-tracking wrapper is not
            // installed over it: it consumed the (invalid) nested email without our pipeline validating it.
            ValidationErrorsContext.GetUnprocessableContent().Should().BeNull();
            result!.Contact.Should().BeNull();
        }
    }

    [Fact]
    public void BeginScope_clears_a_stale_current_property_name()
    {
        var options = BuildOptions();
        ValidationErrorsContext.CurrentPropertyName = "stale";
        try
        {
            using (ValidationErrorsContext.BeginScope())
            {
                JsonSerializer.Deserialize<TestEmail>("\"not-an-email\"", options);

                var error = ValidationErrorsContext.GetUnprocessableContent();
                error.Should().NotBeNull();
                // A new scope starts at the document root: the stale leaf name must not leak into the path.
                error!.Fields[0].Field.Path.Should().NotContain("stale");
            }
        }
        finally
        {
            ValidationErrorsContext.CurrentPropertyName = null;
        }
    }

    [Fact]
    public void AddError_with_an_already_formed_pointer_is_not_re_escaped()
    {
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("/members/0/email", "bad");

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            // A value already starting with '/' is a fully-formed pointer; its slashes must not be escaped.
            error!.Fields[0].Field.Path.Should().Be("/members/0/email");
        }
    }

    [Fact]
    public void AddError_with_an_empty_field_name_targets_the_root()
    {
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError(string.Empty, "bad");

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields[0].Field.Path.Should().Be(string.Empty);
        }
    }
}
