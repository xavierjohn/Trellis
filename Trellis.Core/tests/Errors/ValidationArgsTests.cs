namespace Trellis.Core.Tests.Errors;

using System.Globalization;
using System.Linq;
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
    public void Allowed_names_the_entry_allowed() =>
        ValidationArgs.Allowed(["red", "green"]).Should().ContainKey("allowed");

    /// <remarks>
    /// The whole point of routing every producer through one helper: query binding reads its
    /// members from <c>Enum.GetNames</c> (declaration order), a <c>RequiredEnum</c> reads its own
    /// registry, and nothing would otherwise force the two to agree. A client that diffs the list
    /// across producers must not see a difference that is only ordering.
    /// </remarks>
    [Fact]
    public void Allowed_sorts_ordinally_so_producers_cannot_disagree_on_order() =>
        ValidationArgs.Allowed(["zulu", "alpha", "Mike"])["allowed"]
            .Should().Be(ValidationArgValue.ListOf("Mike", "alpha", "zulu"));

    [Fact]
    public void Allowed_carries_each_name_as_text() =>
        ValidationArgs.Allowed(["red"])["allowed"]
            .Should().Be(new ValidationArgValue.List(EquatableArray.Create<ValidationArgValue>(
                [new ValidationArgValue.Text("red")])));

    /// <remarks>
    /// An enum with no members is degenerate but reachable, and an empty array is the honest
    /// answer — "nothing is permitted" — where omitting the entry would read as "this violation
    /// forgot to say".
    /// </remarks>
    [Fact]
    public void Allowed_with_no_names_is_an_empty_list() =>
        ValidationArgs.Allowed([])["allowed"]
            .Should().Be(ValidationArgValue.ListOf());

    [Fact]
    public void Allowed_reaches_the_wire_as_a_json_array_of_strings()
    {
        var json = JsonSerializer.SerializeToElement(ValidationArgs.Allowed(["green", "red"]));

        json.GetProperty("allowed").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(["green", "red"]);
    }

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

    /// <remarks>
    /// A list a client cannot act on is not worth the bytes it costs. 248 country names serialize
    /// to roughly 3 KB on <em>every</em> rejection, and a request with several invalid enum fields
    /// multiplies that — a small request provoking a large response is an amplification vector, not
    /// merely waste.
    /// </remarks>
    [Fact]
    public void A_member_list_at_the_cap_is_still_published()
    {
        var names = Enumerable.Range(1, ValidationArgs.MaxAllowedMembers)
            .Select(i => i.ToString("D3", CultureInfo.InvariantCulture));

        var args = ValidationArgs.Allowed(names);

        args.Should().ContainKey("allowed");
        args.Should().NotContainKey("allowedCount");
    }

    /// <remarks>
    /// The list is dropped whole rather than truncated. A truncated list is a false statement: it
    /// tells a client that a member it omitted is not permitted, so a client rendering "choose one
    /// of…" shows a wrong list and one validating against it rejects valid input. Absent already
    /// means "not provided" — a blank value and a FluentValidation rule over a RequiredEnum both
    /// omit it — so dropping it costs a client nothing it was not already handling.
    /// </remarks>
    [Fact]
    public void One_member_past_the_cap_drops_the_list_rather_than_truncating_it()
    {
        var names = Enumerable.Range(1, ValidationArgs.MaxAllowedMembers + 1)
            .Select(i => i.ToString("D3", CultureInfo.InvariantCulture));

        var args = ValidationArgs.Allowed(names);

        args.Should().NotContainKey("allowed");
        args["allowedCount"].Should().Be(new ValidationArgValue.Number(ValidationArgs.MaxAllowedMembers + 1));
    }

    [Fact]
    public void The_omitted_count_reaches_the_wire_as_a_json_number()
    {
        var names = Enumerable.Range(1, 248).Select(i => i.ToString("D3", CultureInfo.InvariantCulture));

        var json = JsonSerializer.SerializeToElement(ValidationArgs.Allowed(names));

        json.TryGetProperty("allowed", out _).Should().BeFalse();
        json.GetProperty("allowedCount").ValueKind.Should().Be(JsonValueKind.Number);
        json.GetProperty("allowedCount").GetInt32().Should().Be(248);
    }
}