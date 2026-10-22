namespace CookbookSnippets.Tests;

using System.Text.Json;
using CookbookSnippets.Recipe13;
using Trellis.Testing;

public class Recipe13FactoryTests
{
    [Fact]
    public void TryCreate_Primitives_WithWhitespace_PreservesTypedFieldsAndTrimsText()
    {
        var address = ShippingAddress.TryCreate(" 1 Main ", " Redmond ", " WA ", " 98052 ", " US ").Unwrap();

        address.Street.Value.Should().Be("1 Main");
        address.City.Value.Should().Be("Redmond");
        address.State.Value.Should().Be("WA");
        address.PostalCode.Value.Should().Be("98052");
        address.Country.Value.Should().Be("US");
    }

    [Fact]
    public void TryCreate_Typed_WithValidatedFields_PreservesInstances()
    {
        var street = StreetLine.Create("1 Main");
        var city = CityName.Create("Redmond");
        var state = StateCode.Create("WA");
        var postalCode = PostalCodeText.Create("98052");
        var country = CountryCodeText.Create("US");

        var address = ShippingAddress.TryCreate(street, city, state, postalCode, country).Unwrap();

        address.Street.Should().BeSameAs(street);
        address.City.Should().BeSameAs(city);
        address.State.Should().BeSameAs(state);
        address.PostalCode.Should().BeSameAs(postalCode);
        address.Country.Should().BeSameAs(country);
        address.Should().Be(ShippingAddress.TryCreate("1 Main", "Redmond", "WA", "98052", "US").Unwrap());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void TryCreate_Primitives_WithBlankFields_AccumulatesAllViolations(string input)
    {
        var result = ShippingAddress.TryCreate(input, input, input, input, input);

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(
            ["/street", "/city", "/state", "/postalCode", "/country"]);
        error.Fields.Items.Should().OnlyContain(v => v.ReasonCode == ValidationCodes.ValueNotEmpty);
    }

    [Theory]
    [InlineData(false, null, "")]
    [InlineData(true, null, "")]
    [InlineData(false, "", "")]
    [InlineData(true, "", "")]
    [InlineData(false, "ShippingAddress", "/shippingAddress")]
    [InlineData(true, "ShippingAddress", "/shippingAddress")]
    [InlineData(false, "/customers/0/shippingAddress", "/customers/0/shippingAddress")]
    [InlineData(true, "/customers/0/shippingAddress", "/customers/0/shippingAddress")]
    [InlineData(false, "delivery~slot", "/delivery~0slot")]
    [InlineData(true, "delivery~slot", "/delivery~0slot")]
    public void TryCreate_BothOverloads_WithMissingFields_PreservesNestedPointers(
        bool useTypedFactory, string? fieldName, string expectedPrefix)
    {
        var result = useTypedFactory
            ? ShippingAddress.TryCreate(
                (StreetLine?)null, (CityName?)null, (StateCode?)null,
                (PostalCodeText?)null, (CountryCodeText?)null, fieldName)
            : ShippingAddress.TryCreate((string?)null, null, null, null, null, fieldName);

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        error.Fields.Items.Select(v => v.Field.Path).Should().Equal(
            [
                expectedPrefix + "/street",
                expectedPrefix + "/city",
                expectedPrefix + "/state",
                expectedPrefix + "/postalCode",
                expectedPrefix + "/country"
            ]);
        error.Fields.Items.Should().OnlyContain(v => v.ReasonCode == ValidationCodes.ValueNotNull);
    }

    [Fact]
    public void Serialize_TypedComposite_WithPrimitiveFields_PreservesWireShape()
    {
        var address = ShippingAddress.TryCreate("1 Main", "Redmond", "WA", "98052", "US").Unwrap();

        var json = JsonSerializer.Serialize(address);

        json.Should().Be(
            """{"street":"1 Main","city":"Redmond","state":"WA","postalCode":"98052","country":"US"}""");
    }

    [Theory]
    [InlineData("""{"street":"1 Main","city":"Redmond","state":"WA","postalCode":"98052","country":"US"}""")]
    [InlineData("""{"country":"US","postalCode":"98052","state":"WA","city":"Redmond","street":"1 Main"}""")]
    public void Deserialize_TypedComposite_WithAnyJsonMemberOrder_PreservesFieldAssignments(string json)
    {
        var address = JsonSerializer.Deserialize<ShippingAddress>(json);

        Assert.NotNull(address);
        address.Street.Value.Should().Be("1 Main");
        address.City.Value.Should().Be("Redmond");
        address.State.Value.Should().Be("WA");
        address.PostalCode.Value.Should().Be("98052");
        address.Country.Value.Should().Be("US");
    }
}