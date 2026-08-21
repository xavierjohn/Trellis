namespace Trellis.FluentValidation.Tests;

using global::FluentValidation;
using Trellis;
using Trellis.FluentValidation;

/// <summary>
/// Pins the opt-in that widens the validation-arg allowlist: it must reach the wire, it must not
/// bypass the disclosure gates, and it must refuse the placeholders that carry submitted input.
/// </summary>
public class ValidationArgsOptionsTests
{
    private sealed record Subject(string A = "", int N = 0);

    private static FieldViolation Violate(
        Action<InlineValidator<Subject>> configure,
        Subject subject,
        ValidationArgsOptions? options)
    {
        var validator = new InlineValidator<Subject>();
        configure(validator);
        var result = validator.Validate(subject).ToResult(subject, argsOptions: options);
        result.IsFailure.Should().BeTrue();
        return ((Error.InvalidInput)result.Error!).Fields[0];
    }

    [Fact]
    public void An_unallowlisted_validator_emits_no_args_by_default()
    {
        var violation = Violate(MinimumAgeRule, new Subject(N: 12), options: null);

        violation.Args.Should().BeNull();
    }

    [Fact]
    public void AllowArgs_admits_a_placeholder_the_framework_withholds()
    {
        var options = new ValidationArgsOptions().AllowArgs("MinimumAge", "MinAge");

        var violation = Violate(MinimumAgeRule, new Subject(N: 12), options);

        violation.Args!["minAge"].Should().Be("18");
    }

    [Fact]
    public void An_opted_in_arg_still_cannot_carry_what_the_message_does_not()
    {
        var options = new ValidationArgsOptions().AllowArgs("MinimumAge", "MinAge");

        var violation = Violate(
            v => MinimumAgeRule(v, "too young"),
            new Subject(N: 12),
            options);

        violation.Args.Should().BeNull(
            "the message no longer contains the bound, so publishing it would be a new disclosure");
    }

    private static void MinimumAgeRule(InlineValidator<Subject> validator) =>
        MinimumAgeRule(validator, "Must be at least {MinAge}.");

    private static void MinimumAgeRule(InlineValidator<Subject> validator, string message) =>
        validator.RuleFor(x => x.N)
            .Must((_, value, context) =>
            {
                context.MessageFormatter.AppendArgument("MinAge", 18);
                return value >= 18;
            })
            .WithErrorCode("MinimumAge")
            .WithMessage(message);

    [Fact]
    public void Widening_cannot_reintroduce_a_denied_placeholder()
    {
        var options = new ValidationArgsOptions().AllowArgs("GreaterThanValidator", "PropertyName");

        var violation = Violate(
            v => v.RuleFor(x => x.N).GreaterThan(5),
            new Subject(N: 1),
            options);

        violation.Args!["comparisonValue"].Should().Be("5");
        violation.Args.Should().NotContainKey("propertyName",
            "PropertyName is on the universal denylist, so widening cannot reintroduce it");
    }

    [Fact]
    public void Widening_does_not_bypass_the_containment_gate()
    {
        var options = new ValidationArgsOptions().AllowArgs("GreaterThanValidator", "ComparisonValue");

        var violation = Violate(
            v => v.RuleFor(x => x.N).GreaterThan(5).WithMessage("out of range"),
            new Subject(N: 1),
            options);

        violation.Args.Should().BeNull(
            "an application that took the bound out of its message must not have it put back by an opt-in");
    }

    [Theory]
    [InlineData("PropertyValue")]
    [InlineData("PropertyPath")]
    [InlineData("propertyvalue")]
    [InlineData("propertypath")]
    public void AllowArgs_refuses_the_permanently_denied_placeholders(string name)
    {
        var act = () => new ValidationArgsOptions().AllowArgs("GreaterThanValidator", name);

        act.Should().Throw<ArgumentException>().WithMessage($"*'{name}'*");
    }

    [Fact]
    public void AllowArgs_says_PropertyValue_is_denied_for_disclosure() =>
        new Func<object>(() => new ValidationArgsOptions().AllowArgs("GreaterThanValidator", "PropertyValue"))
            .Should().Throw<ArgumentException>().WithMessage("*submitted input*");

    [Fact]
    public void AllowArgs_says_PropertyPath_is_denied_for_redundancy() =>
        new Func<object>(() => new ValidationArgsOptions().AllowArgs("GreaterThanValidator", "PropertyPath"))
            .Should().Throw<ArgumentException>().WithMessage("*already reports*",
                "PropertyPath is withheld because the violation's location carries it, not because it discloses input");

    [Fact]
    public void AllowArgs_refuses_to_widen_the_shared_default() =>
        new Func<object>(() => ValidationArgsOptions.Default.AllowArgs("GreaterThanValidator", "ComparisonValue"))
            .Should().Throw<InvalidOperationException>().WithMessage("*shared*",
                "widening the process-wide instance would be a global effect from what reads like a local one");

    [Fact]
    public void The_shared_default_widens_nothing() =>
        ValidationArgsOptions.Default.IsEmpty.Should().BeTrue();

    [Fact]
    public void AllowArgs_rejects_a_blank_error_code() =>
        new Func<object>(() => new ValidationArgsOptions().AllowArgs("  ", "MaxLength"))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void AllowArgs_is_chainable_and_accumulates()
    {
        var options = new ValidationArgsOptions()
            .AllowArgs("GreaterThanValidator", "ComparisonValue")
            .AllowArgs("GreaterThanValidator", "ComparisonProperty");

        var violation = Violate(v => v.RuleFor(x => x.N).GreaterThan(5), new Subject(N: 1), options);

        violation.Args!["comparisonValue"].Should().Be("5");
    }

    [Fact]
    public void The_default_instance_widens_nothing()
    {
        var violation = Violate(MinimumAgeRule, new Subject(N: 12), ValidationArgsOptions.Default);

        violation.Args.Should().BeNull();
    }
}
