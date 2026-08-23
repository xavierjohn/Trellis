namespace Trellis.Core.Tests.Errors;

using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Trellis;

public class ValidationArgValueTests
{
    private static string Serialize(ValidationArgValue value) => JsonSerializer.Serialize(value);

    [Fact]
    public void Text_serializes_as_a_json_string() =>
        Serialize(new ValidationArgValue.Text("abc")).Should().Be("\"abc\"");

    [Fact]
    public void Number_serializes_as_a_json_number_not_a_string() =>
        Serialize(new ValidationArgValue.Number(255m)).Should().Be("255");

    [Fact]
    public void Number_preserves_the_scale_it_was_given() =>
        Serialize(new ValidationArgValue.Number(1.50m)).Should().Be("1.50");

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Bool_serializes_as_a_json_boolean_not_a_string(bool value, string expected) =>
        Serialize(new ValidationArgValue.Bool(value)).Should().Be(expected);

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Bool_reads_back_from_a_json_boolean(string json, bool expected) =>
        JsonSerializer.Deserialize<ValidationArgValue>(json)
            .Should().BeOfType<ValidationArgValue.Bool>()
            .Which.Value.Should().Be(expected);

    [Fact]
    public void Bool_converts_implicitly_so_a_flag_need_not_be_quoted()
    {
        ValidationArgValue value = true;
        value.Should().Be(new ValidationArgValue.Bool(true));
    }

    [Fact]
    public void Number_is_written_invariantly_under_a_comma_decimal_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Serialize(new ValidationArgValue.Number(1.5m)).Should().Be("1.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void List_serializes_as_a_json_array()
    {
        var value = ValidationArgValue.ListOf("red", "green");
        Serialize(value).Should().Be("[\"red\",\"green\"]");
    }

    [Fact]
    public void List_may_carry_numbers()
    {
        var value = ValidationArgValue.ListOf(1, 2, 3);
        Serialize(value).Should().Be("[1,2,3]");
    }

    [Fact]
    public void Empty_list_serializes_as_an_empty_array() =>
        Serialize(ValidationArgValue.ListOf()).Should().Be("[]");

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("255")]
    [InlineData("1.50")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("[\"a\",1,true]")]
    [InlineData("[]")]
    public void Round_trips_through_json(string json)
    {
        var value = JsonSerializer.Deserialize<ValidationArgValue>(json);
        value.Should().NotBeNull();
        Serialize(value!).Should().Be(json);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"nested\":1}")]
    public void Reading_an_unsupported_token_fails_rather_than_inventing_a_case(string json)
    {
        // An object would need a schema per reason code to interpret, which is exactly the
        // knowable-from-the-type property the union exists to keep.
        var read = () => JsonSerializer.Deserialize<ValidationArgValue>(json);
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void Reading_null_yields_null_rather_than_a_case() =>
        JsonSerializer.Deserialize<ValidationArgValue>("null").Should().BeNull();

    [Theory]
    [InlineData("1E-100")]
    [InlineData("1E+100")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("0.00000000000000000000000000009")]
    public void Reading_a_number_no_decimal_can_represent_fails_rather_than_corrupting_it(string json)
    {
        // Rounding these publishes an operand the producer never wrote: 1E-100 lands on 0, and
        // 0.00000000000000000000000000009 lands on a different non-zero value entirely.
        var read = () => JsonSerializer.Deserialize<ValidationArgValue>(json);
        read.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("1e2", "100")]
    [InlineData("1E+2", "100")]
    [InlineData("100", "100")]
    [InlineData("1.50", "1.50")]
    [InlineData("-0", "0")]
    [InlineData("0.0", "0.0")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("0", "0")]
    public void Reading_a_number_accepts_every_spelling_a_decimal_can_hold_exactly(string json, string expected)
    {
        // Exactness is decided on significant digits, so a producer that writes 1e2 rather than 100
        // is not punished for the spelling.
        var value = JsonSerializer.Deserialize<ValidationArgValue>(json);

        value.Should().BeOfType<ValidationArgValue.Number>()
            .Which.Value.ToString(CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [Fact]
    public void Lists_with_equal_contents_are_equal()
    {
        ValidationArgValue.ListOf("a", "b").Should().Be(ValidationArgValue.ListOf("a", "b"));
        ValidationArgValue.ListOf("a", "b").GetHashCode()
            .Should().Be(ValidationArgValue.ListOf("a", "b").GetHashCode());
    }

    [Fact]
    public void Lists_with_different_contents_are_not_equal() =>
        ValidationArgValue.ListOf("a", "b").Should().NotBe(ValidationArgValue.ListOf("b", "a"));

    [Fact]
    public void Text_and_number_are_distinct_even_when_they_render_alike() =>
        new ValidationArgValue.Text("255").Should().NotBe(new ValidationArgValue.Number(255m));

    [Theory]
    [InlineData(255)]
    [InlineData(-1)]
    public void Int_converts_implicitly_to_number(int value)
    {
        ValidationArgValue converted = value;
        converted.Should().Be(new ValidationArgValue.Number(value));
    }

    [Fact]
    public void Long_converts_implicitly_to_number()
    {
        ValidationArgValue converted = 9_000_000_000L;
        converted.Should().Be(new ValidationArgValue.Number(9_000_000_000m));
    }

    [Fact]
    public void Decimal_converts_implicitly_to_number()
    {
        ValidationArgValue converted = 1.5m;
        converted.Should().Be(new ValidationArgValue.Number(1.5m));
    }

    [Fact]
    public void String_converts_implicitly_to_text()
    {
        ValidationArgValue converted = "abc";
        converted.Should().Be(new ValidationArgValue.Text("abc"));
    }
}
