namespace Trellis.Primitives.Tests;

using System.Text.Json;

public partial class GiftWrap : RequiredBool<GiftWrap>
{
}

internal partial class InternalFlag : RequiredBool<InternalFlag>
{
}

public class RequiredBoolTests
{
    [Fact]
    public void TryCreate_True_ReturnsSuccess()
    {
        var result = GiftWrap.TryCreate(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_False_ReturnsSuccess()
    {
        // false is a valid value — NOT rejected!
        var result = GiftWrap.TryCreate(false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().BeFalse();
    }

    [Fact]
    public void TryCreate_Null_ReturnsFailure()
    {
        var result = GiftWrap.TryCreate((bool?)null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("giftWrap");
        validation.FieldErrors[0].Details[0].Should().Be("Gift Wrap cannot be empty.");
    }

    [Fact]
    public void TryCreate_FromString_True_ReturnsSuccess()
    {
        var result = GiftWrap.TryCreate("true");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_FromString_False_ReturnsSuccess()
    {
        var result = GiftWrap.TryCreate("false");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryCreate_FromString_Invalid_ReturnsFailure(string? input)
    {
        var result = GiftWrap.TryCreate(input);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Two_RequiredBool_with_same_value_should_be_equal() =>
        GiftWrap.TryCreate(true)
            .Combine(GiftWrap.TryCreate(true))
            .Tap((b1, b2) =>
            {
                (b1 == b2).Should().BeTrue();
                b1.Equals(b2).Should().BeTrue();
                b1.GetHashCode().Should().Be(b2.GetHashCode());
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Two_RequiredBool_with_different_value_should_be_not_equal() =>
        GiftWrap.TryCreate(true)
            .Combine(GiftWrap.TryCreate(false))
            .Tap((b1, b2) =>
            {
                (b1 != b2).Should().BeTrue();
                b1.Equals(b2).Should().BeFalse();
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Can_use_ToString()
    {
        GiftWrap giftWrap = GiftWrap.TryCreate(true).Value;
        giftWrap.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be("True");
    }

    [Fact]
    public void Can_explicitly_cast_to_RequiredBool()
    {
        GiftWrap giftWrap = (GiftWrap)true;
        giftWrap.Should().Be(GiftWrap.TryCreate(true).Value);
    }

    [Fact]
    public void Can_use_Contains()
    {
        var b1 = GiftWrap.TryCreate(true).Value;
        var b2 = GiftWrap.TryCreate(false).Value;
        IReadOnlyList<GiftWrap> items = new List<GiftWrap> { b1, b2 };

        items.Contains(b1).Should().BeTrue();
    }

    [Fact]
    public void ConvertToJson()
    {
        GiftWrap giftWrap = GiftWrap.TryCreate(true).Value;
        var actual = JsonSerializer.Serialize(giftWrap);
        actual.Should().Be("\"True\"");
    }

    [Fact]
    public void ConvertFromJson()
    {
        var json = "\"true\"";
        GiftWrap actual = JsonSerializer.Deserialize<GiftWrap>(json)!;
        actual.Value.Should().BeTrue();
    }

    [Fact]
    public void ConvertFromJson_FalseValue()
    {
        var json = "\"false\"";
        GiftWrap actual = JsonSerializer.Deserialize<GiftWrap>(json)!;
        actual.Value.Should().BeFalse();
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        var result = GiftWrap.TryCreate((bool?)null, "myField");

        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("myField");
    }

    [Fact]
    public void InternalFlag_TryCreate_ReturnsSuccess()
    {
        var result = InternalFlag.TryCreate(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<InternalFlag>();
    }

    [Fact]
    public void Can_create_from_TryParse()
    {
        GiftWrap.TryParse("true", null, out var giftWrap)
            .Should().BeTrue();

        giftWrap.Should().NotBeNull();
        giftWrap!.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Cannot_create_from_TryParse_invalid_string(string? input)
    {
        GiftWrap.TryParse(input, null, out var giftWrap)
            .Should().BeFalse();

        giftWrap.Should().BeNull();
    }

    [Fact]
    public void Can_create_from_Parse()
    {
        var giftWrap = GiftWrap.Parse("true", null);

        giftWrap.Should().BeOfType<GiftWrap>();
        giftWrap.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Cannot_create_from_Parse_invalid_string(string? input)
    {
        Action act = () => GiftWrap.Parse(input!, null);
        act.Should().Throw<FormatException>();
    }
}