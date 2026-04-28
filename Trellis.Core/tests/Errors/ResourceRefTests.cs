namespace Trellis.Core.Tests.Errors;

using System.Globalization;

/// <summary>
/// Tests for <see cref="ResourceRef"/> helper factories.
/// </summary>
public class ResourceRefTests
{
    [Fact]
    public void For_with_explicit_type_and_id_creates_resource_ref()
    {
        var resource = ResourceRef.For("Order", 42);

        resource.Type.Should().Be("Order");
        resource.Id.Should().Be("42");
    }

    [Fact]
    public void For_with_explicit_type_and_null_id_creates_collection_ref()
    {
        var resource = ResourceRef.For("Order");

        resource.Type.Should().Be("Order");
        resource.Id.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_with_blank_explicit_type_throws(string? type)
    {
        var act = () => ResourceRef.For(type!);

        act.Should().Throw<ArgumentException>()
            .Where(exception => exception.ParamName == "type");
    }

    [Fact]
    public void For_generic_uses_exact_type_name()
    {
        var resource = ResourceRef.For<CustomerAccount>("abc");

        resource.Type.Should().Be(nameof(CustomerAccount));
        resource.Id.Should().Be("abc");
    }

    [Fact]
    public void For_generic_with_null_id_creates_collection_ref()
    {
        var resource = ResourceRef.For<CustomerAccount>();

        resource.Type.Should().Be(nameof(CustomerAccount));
        resource.Id.Should().BeNull();
    }

    [Fact]
    public void For_formats_formattable_ids_with_invariant_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var resource = ResourceRef.For("Invoice", 1234.56m);

            resource.Id.Should().Be("1234.56");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void For_uses_ToString_for_non_formattable_ids()
    {
        var id = new CustomId("abc");

        var resource = ResourceRef.For("Widget", id);

        resource.Id.Should().Be("custom:abc");
    }

    private sealed record CustomerAccount;

    private sealed class CustomId(string value)
    {
        public override string ToString() => $"custom:{value}";
    }
}