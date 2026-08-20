namespace Trellis.FluentValidation.Tests;

using global::FluentValidation;
using Trellis;
using Trellis.FluentValidation;

/// <summary>
/// Pins the projection from FluentValidation's validator names onto the Trellis vocabulary, and in
/// particular the <c>NotEmpty()</c> refinement, which is the one place where a single FluentValidation
/// rule spans three distinct Trellis codes.
/// </summary>
public class ValidationCodeProjectionTests
{
    private sealed record Subject(string Text = "", Guid Id = default, int Count = 0, string[]? Items = null);

    private static string CodeOf(Action<InlineValidator<Subject>> configure, Subject subject)
    {
        var validator = new InlineValidator<Subject>();
        configure(validator);
        var result = validator.Validate(subject).ToResult(subject);
        result.IsFailure.Should().BeTrue();
        return ((Error.InvalidInput)result.Error!).Fields.Items[0].ReasonCode;
    }

    [Fact]
    public void NotEmpty_on_a_blank_string_is_value_not_empty() =>
        CodeOf(v => v.RuleFor(x => x.Text).NotEmpty(), new Subject(Text: "   "))
            .Should().Be(ValidationCodes.ValueNotEmpty);

    [Fact]
    public void NotEmpty_on_a_default_value_type_is_value_not_default_not_value_not_empty()
    {
        // FluentValidation catches Guid.Empty with the same rule it uses for an empty string, but a
        // client cannot act on them the same way, and a Trellis primitive reports value.not-default
        // here. Collapsing both onto value.not-empty would break producer independence for the most
        // commonly written FluentValidation rule there is.
        CodeOf(v => v.RuleFor(x => x.Id).NotEmpty(), new Subject())
            .Should().Be(ValidationCodes.ValueNotDefault);

        CodeOf(v => v.RuleFor(x => x.Count).NotEmpty(), new Subject())
            .Should().Be(ValidationCodes.ValueNotDefault);
    }

    [Fact]
    public void NotEmpty_on_an_absent_value_is_value_not_null() =>
        CodeOf(v => v.RuleFor(x => x.Text).NotEmpty(), new Subject(Text: null!))
            .Should().Be(ValidationCodes.ValueNotNull);

    [Fact]
    public void NotEmpty_on_an_empty_collection_is_value_not_empty() =>
        CodeOf(v => v.RuleFor(x => x.Items).NotEmpty(), new Subject(Items: []))
            .Should().Be(ValidationCodes.ValueNotEmpty);

    [Fact]
    public void A_reserved_validator_name_maps_to_its_trellis_code()
    {
        ValidationCodeProjection.Project("GreaterThanValidator").Should().Be(ValidationCodes.ValueGreaterThan);
        ValidationCodeProjection.Project("AspNetCoreCompatibleEmailValidator").Should().Be(ValidationCodes.StringEmail);
    }

    [Fact]
    public void A_custom_error_code_passes_through_verbatim() =>
        ValidationCodeProjection.Project("order.too-large").Should().Be("order.too-large");

    [Fact]
    public void A_blank_or_legacy_code_becomes_the_neutral_sentinel()
    {
        ValidationCodeProjection.Project(null).Should().Be(ValidationCodes.Unspecified);
        ValidationCodeProjection.Project("").Should().Be(ValidationCodes.Unspecified);
        ValidationCodeProjection.Project(ValidationCodes.LegacyUnspecified).Should().Be(ValidationCodes.Unspecified);
    }

    [Fact]
    public void A_predicate_rule_carries_nothing_a_client_can_branch_on() =>
        CodeOf(v => v.RuleFor(x => x.Text).Must(t => t == "expected"), new Subject(Text: "other"))
            .Should().Be(ValidationCodes.Unspecified);

    [Fact]
    // A non-NotEmpty code must not be rewritten just because an attempted value is present.
    public void The_refinement_applies_only_to_NotEmpty() =>
        ValidationCodeProjection.Project("NotNullValidator", Guid.Empty)
            .Should().Be(ValidationCodes.ValueNotNull);
}
