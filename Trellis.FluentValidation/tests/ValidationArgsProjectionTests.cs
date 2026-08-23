namespace Trellis.FluentValidation.Tests;

using System.Linq;
using global::FluentValidation;
using Trellis;
using Trellis.FluentValidation;

/// <summary>
/// Pins the two independent controls on <c>Args</c>: the per-validator allowlist (correctness)
/// and the containment gate (disclosure).
/// </summary>
public class ValidationArgsProjectionTests
{
    private sealed record Subject(string A = "", string B = "", decimal D = 0m, int N = 0, DateTime When = default, double F = 0d);

    private static FieldViolation Violate(Action<InlineValidator<Subject>> configure, Subject subject)
    {
        var validator = new InlineValidator<Subject>();
        configure(validator);
        var result = validator.Validate(subject).ToResult(subject);
        result.IsFailure.Should().BeTrue();
        var fields = ((Error.InvalidInput)result.Error!).Fields;
        fields.Length.Should().Be(1);
        return fields[0];
    }

    [Fact]
    public void A_double_bound_a_decimal_cannot_represent_stays_text_rather_than_becoming_zero()
    {
        // Lifting this to a decimal underflows it to 0, publishing a bound of zero that the gate
        // never approved — the client was shown 1E-100.
        var violation = Violate(v => v.RuleFor(x => x.F).GreaterThan(1E-100), new Subject(F: 0d));

        violation.Args!["comparisonValue"].Should().Be(new ValidationArgValue.Text("1E-100"));
    }

    [Fact]
    public void A_double_bound_a_decimal_represents_exactly_is_still_lifted_to_a_number()
    {
        var violation = Violate(v => v.RuleFor(x => x.F).GreaterThan(1.5), new Subject(F: 0d));

        violation.Args!["comparisonValue"].Should().Be(new ValidationArgValue.Number(1.5m));
    }

    [Fact]
    public void Length_emits_both_bounds_and_the_submitted_length()
    {
        var violation = Violate(v => v.RuleFor(x => x.A).Length(2, 4), new Subject(A: "abcdefgh"));

        violation.Args.Should().NotBeNull();
        violation.Args!["minLength"].Should().Be(new ValidationArgValue.Number(2));
        violation.Args["maxLength"].Should().Be(new ValidationArgValue.Number(4));
        violation.Args["totalLength"].Should().Be(new ValidationArgValue.Number(8));
    }

    [Fact]
    public void MaximumLength_drops_the_min_length_sentinel()
    {
        var violation = Violate(v => v.RuleFor(x => x.A).MaximumLength(3), new Subject(A: "abcdefgh"));

        violation.Args!.Should().NotContainKey("minLength",
            "FluentValidation populates MinLength = 0 on a MaximumLength failure, and a client rendering 'between 0 and 3' from it would be wrong");
        violation.Args["maxLength"].Should().Be(new ValidationArgValue.Number(3));
    }

    [Fact]
    public void MinimumLength_drops_the_max_length_sentinel()
    {
        var violation = Violate(v => v.RuleFor(x => x.A).MinimumLength(50), new Subject(A: ""));

        violation.Args!.Should().NotContainKey("maxLength",
            "MaxLength is -1 here, which is meaningless rather than merely unhelpful");
        violation.Args["minLength"].Should().Be(new ValidationArgValue.Number(50));
    }

    [Fact]
    public void ExactLength_emits_the_expected_length_under_the_name_its_template_uses()
    {
        var violation = Violate(v => v.RuleFor(x => x.A).Length(4), new Subject(A: "abcdefgh"));

        violation.Args.Should().NotBeNull(
            "ExactLengthValidator derives from LengthValidator with base(n, n) so both bounds carry the right value, but only MaxLength is named by its template - allowlisting MinLength instead would gate every arg out and leave the client a length with no bound to compare it against");
        violation.Args!["maxLength"].Should().Be(new ValidationArgValue.Number(4));
        violation.Args["totalLength"].Should().Be(new ValidationArgValue.Number(8));
    }

    [Fact]
    public void An_overridden_message_suppresses_every_arg()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.A).MaximumLength(3).WithMessage("bad"),
            new Subject(A: "abcdefgh"));

        violation.Args.Should().BeNull(
            "the application deliberately took those values out of its prose; Trellis must not put them back in a sibling member");
    }

    [Fact]
    public void A_regular_expression_is_never_emitted()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.A).Matches("^[A-Z]{2}SECRET[0-9]+$"),
            new Subject(A: "nope"));

        violation.Args.Should().BeNull(
            "the default format message never renders the pattern, so emitting it would be a new disclosure of an internal format an attacker would otherwise have to guess");
    }

    [Fact]
    public void A_short_pattern_colliding_with_the_property_name_is_not_emitted()
    {
        var violation = Violate(v => v.RuleFor(x => x.A).Matches("A"), new Subject(A: "b"));

        violation.Args.Should().BeNull(
            "the pattern 'A' is a substring of \"'A' is not in the correct format\" only because the property is also named A; plain containment alone would disclose it");
    }

    [Fact]
    public void A_comparison_against_a_literal_emits_the_value_the_default_message_already_shows()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.A).Equal("expected-literal"),
            new Subject(A: "other"));

        violation.Args!["comparisonValue"].Should().Be(new ValidationArgValue.Text("expected-literal"));
    }

    [Fact]
    public void A_long_comparison_value_is_bounded()
    {
        var long_ = new string('X', 500);
        var violation = Violate(
            v => v.RuleFor(x => x.A).Equal(x => x.B),
            new Subject(A: "other", B: long_));

        var comparison = violation.Args!["comparisonValue"]
            .Should().BeOfType<ValidationArgValue.Text>().Subject.Value;

        comparison.Length.Should().BeLessThan(80,
            "no structural rule identifies which string args carry submitted input, so the bound is universal");
        comparison.Should().EndWith("...");
    }

    [Fact]
    public void A_control_character_is_escaped_rather_than_suppressed()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.A).Equal(x => x.B),
            new Subject(A: "other", B: "foo\0bar"));

        violation.Args!["comparisonValue"].Should().Be(new ValidationArgValue.Text("foo\\u0000bar"),
            "escaping re-encodes a character the message already carried rather than revealing a new one, "
            + "so reconciliation must not demand that the escaped form appear in the message verbatim");
    }

    [Fact]
    public void A_custom_error_code_suppresses_every_arg()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.A).MaximumLength(3).WithErrorCode("customer.inactive"),
            new Subject(A: "abcdefgh"));

        violation.ReasonCode.Should().Be("customer.inactive");
        violation.Args.Should().BeNull(
            "an unrecognised code resolves to no template at all, which fails the gate for every placeholder - the code is preserved, the args are not guessed at");
    }

    [Fact]
    public void An_app_authored_placeholder_is_not_emitted()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.A)
                  .Must((_, _, ctx) =>
                  {
                      ctx.MessageFormatter.AppendArgument("Secret", "supersecret");
                      return false;
                  })
                  .WithMessage("bad {Secret}"),
            new Subject());

        violation.Args.Should().BeNull(
            "Trellis cannot classify an app-authored placeholder as client-facing, diagnostic or secret");
    }

    [Fact]
    public void The_submitted_value_is_never_emitted()
    {
        var violation = Violate(v => v.RuleFor(x => x.A).NotEmpty(), new Subject(A: ""));

        violation.Args.Should().BeNull();
    }

    [Fact]
    public void InclusiveBetween_emits_both_ends()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.N).InclusiveBetween(1, 10),
            new Subject(N: 42));

        violation.Args!["from"].Should().Be(new ValidationArgValue.Number(1));
        violation.Args["to"].Should().Be(new ValidationArgValue.Number(10));
    }

    [Fact]
    public void ScalePrecision_emits_the_expected_and_observed_shape()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.D).PrecisionScale(3, 1, ignoreTrailingZeros: true),
            new Subject(D: 123.45m));

        violation.Args!["expectedPrecision"].Should().Be(new ValidationArgValue.Number(3));
        violation.Args["expectedScale"].Should().Be(new ValidationArgValue.Number(1));
    }

    [Fact]
    public void A_date_whose_encoding_adds_precision_the_message_never_showed_is_suppressed()
    {
        var precise = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(1234567);
        var violation = Violate(
            v => v.RuleFor(x => x.When).GreaterThan(precise),
            new Subject(When: new DateTime(2020, 1, 1)));

        violation.Args.Should().BeNull(
            "FluentValidation rendered the bound culture-sensitively and dropped the fractional ticks, so emitting the round-trip form would put precision on the wire that the message the client receives never contained");
    }

    [Fact]
    public void A_date_is_suppressed_even_under_the_default_message()
    {
        var whole = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var violation = Violate(
            v => v.RuleFor(x => x.When).GreaterThan(whole),
            new Subject(When: new DateTime(2020, 1, 1)));

        violation.Args.Should().BeNull(
            "FluentValidation renders a date culture-sensitively and Trellis encodes it round-trippable, so the two never reconcile - dates are a standing false negative, which is the direction chosen deliberately because the alternative direction discloses");
    }

    [Fact]
    public void A_numeric_arg_is_unaffected_by_the_reconciliation_check()
    {
        var violation = Violate(
            v => v.RuleFor(x => x.N).GreaterThan(10),
            new Subject(N: 3));

        violation.Args!["comparisonValue"].Should().Be(new ValidationArgValue.Number(10),
            "a numeric value encodes to exactly what FluentValidation rendered, so nothing is suppressed");
    }
}
