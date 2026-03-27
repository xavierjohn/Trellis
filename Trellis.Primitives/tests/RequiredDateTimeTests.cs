namespace Trellis.Primitives.Tests;

using System.Text.Json;

public partial class OrderDate : RequiredDateTime<OrderDate>
{
}

internal partial class InternalDate : RequiredDateTime<InternalDate>
{
}

public class RequiredDateTimeTests
{
    [Fact]
    public void TryCreate_ValidDate_ReturnsSuccess()
    {
        var date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var result = OrderDate.TryCreate(date);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(date);
    }

    [Fact]
    public void TryCreate_UtcNow_ReturnsSuccess()
    {
        var now = DateTime.UtcNow;
        var result = OrderDate.TryCreate(now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(now);
    }

    [Fact]
    public void TryCreate_MinValue_ReturnsFailure()
    {
        var result = OrderDate.TryCreate(DateTime.MinValue);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("orderDate");
        validation.FieldErrors[0].Details[0].Should().Be("Order Date cannot be empty.");
    }

    [Fact]
    public void TryCreate_MaxValue_ReturnsSuccess()
    {
        var result = OrderDate.TryCreate(DateTime.MaxValue);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(DateTime.MaxValue);
    }

    [Fact]
    public void TryCreate_Null_ReturnsFailure()
    {
        var result = OrderDate.TryCreate((DateTime?)null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("orderDate");
        validation.FieldErrors[0].Details[0].Should().Be("Order Date cannot be empty.");
    }

    [Fact]
    public void TryCreate_FromString_ReturnsSuccess()
    {
        var result = OrderDate.TryCreate("2026-01-15T12:00:00Z");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Year.Should().Be(2026);
        result.Value.Value.Month.Should().Be(1);
        result.Value.Value.Day.Should().Be(15);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryCreate_FromString_Invalid_ReturnsFailure(string? input)
    {
        var result = OrderDate.TryCreate(input);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Two_RequiredDateTime_with_same_value_should_be_equal()
    {
        var date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        OrderDate.TryCreate(date)
            .Combine(OrderDate.TryCreate(date))
            .Tap((d1, d2) =>
            {
                (d1 == d2).Should().BeTrue();
                d1.Equals(d2).Should().BeTrue();
                d1.GetHashCode().Should().Be(d2.GetHashCode());
            })
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Two_RequiredDateTime_with_different_value_should_be_not_equal()
    {
        var date1 = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2026, 6, 20, 14, 30, 0, DateTimeKind.Utc);
        OrderDate.TryCreate(date1)
            .Combine(OrderDate.TryCreate(date2))
            .Tap((d1, d2) =>
            {
                (d1 != d2).Should().BeTrue();
                d1.Equals(d2).Should().BeFalse();
            })
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Can_use_ToString()
    {
        var date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        OrderDate orderDate = OrderDate.TryCreate(date).Value;
        orderDate.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Can_explicitly_cast_to_RequiredDateTime()
    {
        var date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        OrderDate orderDate = (OrderDate)date;
        orderDate.Should().Be(OrderDate.TryCreate(date).Value);
    }

    [Fact]
    public void ConvertToJson()
    {
        var date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        OrderDate orderDate = OrderDate.TryCreate(date).Value;

        var actual = JsonSerializer.Serialize(orderDate);
        actual.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        OrderDate original = OrderDate.TryCreate(date).Value;

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OrderDate>(json);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void JsonRoundTrip_PreservesUtcKind()
    {
        var utcDate = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);
        OrderDate original = OrderDate.TryCreate(utcDate).Value;

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OrderDate>(json)!;

        deserialized.Value.Kind.Should().Be(DateTimeKind.Utc,
            "UTC DateTimeKind must survive JSON round-trip");
    }

    [Fact]
    public void ToString_ProducesIso8601RoundTrippableFormat()
    {
        var date = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        OrderDate orderDate = OrderDate.TryCreate(date).Value;

        // ParsableJsonConverter uses ToString() — must produce round-trippable output
#pragma warning disable CA1305 // Testing the parameterless ToString used by JSON serialization
        var str = orderDate.ToString();
#pragma warning restore CA1305

        // Must be parseable back to the same value with Kind preserved
        DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed).Should().BeTrue();
        parsed.Kind.Should().Be(DateTimeKind.Utc);
        parsed.Should().Be(date);
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        var result = OrderDate.TryCreate((DateTime?)null, "myField");

        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("myField");
    }

    [Fact]
    public void InternalDate_TryCreate_ReturnsSuccess()
    {
        var result = InternalDate.TryCreate(DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<InternalDate>();
    }

    [Fact]
    public void Can_create_from_TryParse()
    {
        OrderDate.TryParse("2026-01-15T12:00:00Z", null, out var orderDate)
            .Should().BeTrue();

        orderDate.Should().NotBeNull();
        orderDate!.Value.Year.Should().Be(2026);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Cannot_create_from_TryParse_invalid_string(string? input)
    {
        OrderDate.TryParse(input, null, out var orderDate)
            .Should().BeFalse();

        orderDate.Should().BeNull();
    }

    [Fact]
    public void Can_create_from_Parse()
    {
        var orderDate = OrderDate.Parse("2026-01-15T12:00:00Z", null);

        orderDate.Should().BeOfType<OrderDate>();
        orderDate.Value.Year.Should().Be(2026);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Cannot_create_from_Parse_invalid_string(string? input)
    {
        Action act = () => OrderDate.Parse(input!, null);
        act.Should().Throw<FormatException>();
    }
}