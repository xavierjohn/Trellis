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

    /// <summary>
    /// Regression for inspection finding 2.4-4: <c>typeof(T).Name</c> for closed generics
    /// produces the CLR mangled form <c>"Name`N"</c>. <see cref="ResourceRef.For{TResource}"/>
    /// must strip the backtick mangling so a closed generic resource type is emitted as
    /// <c>"List"</c> on the wire instead of <c>"List`1"</c>.
    /// </summary>
    [Fact]
    public void For_generic_strips_backtick_mangling_from_closed_generic_simple_name()
    {
        var resource = ResourceRef.For<List<string>>("42");

        resource.Type.Should().Be("List",
            "the CLR-mangled simple name 'List`1' is not a useful resource identifier and must not leak through to the wire");
        resource.Id.Should().Be("42");
    }

    /// <summary>
    /// Regression for inspection finding 2.4-4 (multi-arg): same mangling-strip for
    /// closed generics with multiple type arguments.
    /// </summary>
    [Fact]
    public void For_generic_strips_backtick_mangling_from_multi_arg_closed_generic()
    {
        var resource = ResourceRef.For<Dictionary<string, int>>();

        resource.Type.Should().Be("Dictionary");
    }

    /// <summary>
    /// Regression for inspection finding ASP m-4: the <c>PreconditionFailed</c> resource ref
    /// in <c>TrellisHttpResult&lt;TDomain, TBody&gt;</c> uses <c>typeof(TDomain).Name</c> for the
    /// resource type. When TDomain is <c>Maybe&lt;Order&gt;</c> the wire detail becomes
    /// <c>"Maybe`1"</c>. <see cref="ResourceRef.For{TResource}"/> must peel the
    /// <see cref="Maybe{T}"/> wrapper so the meaningful inner domain name is exposed.
    /// </summary>
    [Fact]
    public void For_generic_peels_Maybe_wrapper_to_expose_inner_domain_name()
    {
        var resource = ResourceRef.For<Maybe<CustomerAccount>>("abc");

        resource.Type.Should().Be(nameof(CustomerAccount),
            "Maybe<T> is a documented wrapping case for resource references; the inner domain " +
            "name (CustomerAccount) is the meaningful resource identifier");
        resource.Id.Should().Be("abc");
    }

    /// <summary>
    /// Maybe-peeling is recursive so even pathological double-wrappings collapse to the
    /// underlying domain type name.
    /// </summary>
    [Fact]
    public void For_generic_peels_nested_Maybe_wrappers_recursively()
    {
        var resource = ResourceRef.For<Maybe<Maybe<CustomerAccount>>>();

        resource.Type.Should().Be(nameof(CustomerAccount));
    }

    /// <summary>
    /// The public <see cref="ResourceRef.FormatTypeName"/> helper exposes the friendly-name
    /// formatting so other Trellis components (e.g. AOT-generated JSON converter fallback
    /// messages) can sanitize CLR mangling without duplicating the algorithm. It strips
    /// backtick mangling but does NOT peel <see cref="Maybe{T}"/> (that is intentionally
    /// scoped to <see cref="ResourceRef.For{TResource}"/>, which owns the resource-naming
    /// contract).
    /// </summary>
    [Fact]
    public void FormatTypeName_strips_backtick_mangling_for_closed_generics()
    {
        ResourceRef.FormatTypeName(typeof(List<string>)).Should().Be("List");
        ResourceRef.FormatTypeName(typeof(Dictionary<string, int>)).Should().Be("Dictionary");
    }

    [Fact]
    public void FormatTypeName_returns_simple_name_unchanged_for_non_generic_types()
    {
        ResourceRef.FormatTypeName(typeof(CustomerAccount)).Should().Be(nameof(CustomerAccount));
        ResourceRef.FormatTypeName(typeof(string)).Should().Be("String");
        ResourceRef.FormatTypeName(typeof(int)).Should().Be("Int32");
    }

    [Fact]
    public void FormatTypeName_does_not_peel_Maybe_wrapper() =>
        ResourceRef.FormatTypeName(typeof(Maybe<CustomerAccount>)).Should().Be("Maybe",
            "Maybe-peeling is part of the For<T>() resource-naming contract, not a property " +
            "of the general-purpose type-name formatter; callers needing peeling should use For<T>");

    [Fact]
    public void FormatTypeName_throws_for_null_type()
    {
        var act = () => ResourceRef.FormatTypeName(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "type");
    }

    private sealed record CustomerAccount;

    private sealed class CustomId(string value)
    {
        public override string ToString() => $"custom:{value}";
    }
}