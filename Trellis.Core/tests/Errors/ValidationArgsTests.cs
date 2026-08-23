namespace Trellis.Core.Tests.Errors;

using System.Text.Json;
using FluentAssertions;
using Trellis;

public class ValidationArgsTests
{
    [Fact]
    public void Of_pairs_a_name_with_a_number_without_quoting_it() =>
        ValidationArgs.Of("maxLength", 50)["maxLength"]
            .Should().Be(new ValidationArgValue.Number(50));

    [Fact]
    public void Of_pairs_a_name_with_text() =>
        ValidationArgs.Of("expected", "USD")["expected"]
            .Should().Be(new ValidationArgValue.Text("USD"));

    [Fact]
    public void Of_builds_two_entries()
    {
        var args = ValidationArgs.Of("min", 0, "max", 255);

        args.Should().HaveCount(2);
        args["min"].Should().Be(new ValidationArgValue.Number(0));
        args["max"].Should().Be(new ValidationArgValue.Number(255));
    }

    [Fact]
    public void Of_builds_more_than_two_entries()
    {
        var args = ValidationArgs.Of(
            ("expectedPrecision", 3),
            ("expectedScale", 1),
            ("actualScale", 4),
            ("digits", 5));

        args.Should().HaveCount(4);
        args["actualScale"].Should().Be(new ValidationArgValue.Number(4));
    }

    [Fact]
    public void Of_mixes_text_numbers_and_lists_in_one_call()
    {
        var args = ValidationArgs.Of(
            ("expected", "red"),
            ("allowed", ValidationArgValue.ListOf("red", "green")),
            ("maxChoices", 2));

        args["expected"].Should().Be(new ValidationArgValue.Text("red"));
        args["allowed"].Should().Be(ValidationArgValue.ListOf("red", "green"));
        args["maxChoices"].Should().Be(new ValidationArgValue.Number(2));
    }

    [Fact]
    public void Of_with_no_pairs_is_empty() =>
        ValidationArgs.Of().Should().BeEmpty();

    [Fact]
    public void Of_lets_a_later_pair_win_over_an_earlier_one_of_the_same_name() =>
        ValidationArgs.Of(("max", 1), ("max", 2))["max"]
            .Should().Be(new ValidationArgValue.Number(2));

    [Fact]
    public void A_violations_args_reach_the_wire_as_json_numbers_and_arrays()
    {
        var violation = new FieldViolation(
            InputPointer.ForProperty("colour"),
            "colour.not-allowed",
            ValidationArgs.Of(("maxChoices", 2), ("allowed", ValidationArgValue.ListOf("red", "green"))));

        var json = JsonSerializer.SerializeToElement(violation.Args);

        json.GetProperty("maxChoices").ValueKind.Should().Be(JsonValueKind.Number);
        json.GetProperty("maxChoices").GetInt32().Should().Be(2);
        json.GetProperty("allowed").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("allowed").EnumerateArray().Select(e => e.GetString()).Should().Equal(["red", "green"]);
    }

    [Fact]
    public void Violations_with_equal_args_are_equal()
    {
        var left = new FieldViolation(InputPointer.ForProperty("a"), "code", ValidationArgs.Of("max", 1));
        var right = new FieldViolation(InputPointer.ForProperty("a"), "code", ValidationArgs.Of("max", 1));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void A_numeric_arg_and_its_textual_twin_do_not_make_violations_equal()
    {
        var number = new FieldViolation(InputPointer.ForProperty("a"), "code", ValidationArgs.Of("max", 1));
        var text = new FieldViolation(InputPointer.ForProperty("a"), "code", ValidationArgs.Of("max", "1"));

        number.Should().NotBe(text);
    }
}
