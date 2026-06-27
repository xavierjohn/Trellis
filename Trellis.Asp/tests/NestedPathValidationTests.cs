namespace Trellis.Asp.Tests;

using System.Collections.Generic;
using System.Text.Json;
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
    public void Value_object_in_a_nested_object_reports_the_dotted_path()
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
}
