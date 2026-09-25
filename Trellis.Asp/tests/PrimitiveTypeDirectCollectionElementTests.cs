namespace Trellis.Asp.Tests;

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Validation;
using Trellis.Primitives;
using Xunit;

// ---- RequiredXxx<T> source-generated fixtures ----
// [NotDefault] gives each a deterministic non-null invalid value (its type's sentinel) so the
// failure travels through TryCreate -> NormalizeFieldName, rather than through the null-token path
// (ValidatingJsonConverter.OnNullToken), which never calls NormalizeFieldName and so cannot exercise
// this bug. RequiredBool has no [NotDefault] sentinel, so it uses the ValidateAdditional hook instead.
[NotDefault]
public partial class DcString : RequiredString<DcString> { }

[NotDefault]
public partial class DcGuid : RequiredGuid<DcGuid> { }

[NotDefault]
public partial class DcInt : RequiredInt<DcInt> { }

[NotDefault]
public partial class DcLong : RequiredLong<DcLong> { }

[NotDefault]
public partial class DcDecimal : RequiredDecimal<DcDecimal> { }

[NotDefault]
public partial class DcDateTime : RequiredDateTime<DcDateTime> { }

[NotDefault]
public partial class DcDateTimeOffset : RequiredDateTimeOffset<DcDateTimeOffset> { }

public partial class DcBool : RequiredBool<DcBool>
{
    static partial void ValidateAdditional(bool value, string fieldName, ref string? errorMessage)
    {
        if (!value)
            errorMessage = "must be true";
    }
}

public partial class DcOrderState : RequiredEnum<DcOrderState>
{
    public static readonly DcOrderState Member = new();
}

/// <summary>
/// Issue #658 follow-up: every <c>RequiredXxx&lt;T&gt;</c> source-generated primitive and every
/// hand-written <c>Trellis.Primitives</c> scalar resolves its validation field name through the
/// same shared <see cref="StringExtensions.NormalizeFieldName"/> helper, so each one independently
/// exercises the direct-scalar-collection-element sentinel that <see cref="PathTrackingCollectionConverter{TCollection, TElement}"/>
/// relies on. One test per type pins that none of them regress back to appending a synthetic
/// type-name segment (e.g. <c>/items/1/dcString</c> instead of <c>/items/1</c>).
/// </summary>
public sealed class PrimitiveTypeDirectCollectionElementTests
{
    public sealed record StringCmd(IReadOnlyList<DcString> Items);
    public sealed record GuidCmd(IReadOnlyList<DcGuid> Items);
    public sealed record IntCmd(IReadOnlyList<DcInt> Items);
    public sealed record LongCmd(IReadOnlyList<DcLong> Items);
    public sealed record DecimalCmd(IReadOnlyList<DcDecimal> Items);
    public sealed record BoolCmd(IReadOnlyList<DcBool> Items);
    public sealed record DateTimeCmd(IReadOnlyList<DcDateTime> Items);
    public sealed record DateTimeOffsetCmd(IReadOnlyList<DcDateTimeOffset> Items);
    public sealed record EnumCmd(IReadOnlyList<DcOrderState> Items);

    public sealed record AgeCmd(IReadOnlyList<Age> Items);
    public sealed record UrlCmd(IReadOnlyList<Url> Items);
    public sealed record SlugCmd(IReadOnlyList<Slug> Items);
    public sealed record PhoneNumberCmd(IReadOnlyList<PhoneNumber> Items);
    public sealed record PercentageCmd(IReadOnlyList<Percentage> Items);
    public sealed record MonetaryAmountCmd(IReadOnlyList<MonetaryAmount> Items);
    public sealed record LanguageCodeCmd(IReadOnlyList<LanguageCode> Items);
    public sealed record IpAddressCmd(IReadOnlyList<IpAddress> Items);
    public sealed record HostnameCmd(IReadOnlyList<Hostname> Items);
    public sealed record CurrencyCodeCmd(IReadOnlyList<CurrencyCode> Items);
    public sealed record CountryCodeCmd(IReadOnlyList<CountryCode> Items);

    private static JsonSerializerOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddScalarValueValidationForMinimalApi();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
    }

    /// <summary>
    /// Deserializes <paramref name="json"/> as <typeparamref name="TCommand"/> and asserts the
    /// resulting body validation reports exactly one violation at <paramref name="expectedPath"/> —
    /// the index-precise element pointer, with no appended type-name leaf segment.
    /// </summary>
    private static void AssertSingleElementPath<TCommand>(string json, string expectedPath)
    {
        var options = BuildOptions();
        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<TCommand>(json, options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be(expectedPath);
        }
    }

    [Fact]
    public void RequiredString_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<StringCmd>("""{ "items": [ "ok", "" ] }""", "/items/1");

    [Fact]
    public void RequiredGuid_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<GuidCmd>(
            """{ "items": [ "11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000" ] }""",
            "/items/1");

    [Fact]
    public void RequiredInt_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<IntCmd>("""{ "items": [ 5, 0 ] }""", "/items/1");

    [Fact]
    public void RequiredLong_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<LongCmd>("""{ "items": [ 5, 0 ] }""", "/items/1");

    [Fact]
    public void RequiredDecimal_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<DecimalCmd>("""{ "items": [ 5.5, 0 ] }""", "/items/1");

    [Fact]
    public void RequiredBool_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<BoolCmd>("""{ "items": [ true, false ] }""", "/items/1");

    [Fact]
    public void RequiredDateTime_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<DateTimeCmd>(
            """{ "items": [ "2024-01-01T00:00:00", "0001-01-01T00:00:00" ] }""",
            "/items/1");

    [Fact]
    public void RequiredDateTimeOffset_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<DateTimeOffsetCmd>(
            """{ "items": [ "2024-01-01T00:00:00+00:00", "0001-01-01T00:00:00+00:00" ] }""",
            "/items/1");

    [Fact]
    public void RequiredEnum_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<EnumCmd>("""{ "items": [ "Member", "Bogus" ] }""", "/items/1");

    [Fact]
    public void Age_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<AgeCmd>("""{ "items": [ 30, -1 ] }""", "/items/1");

    [Fact]
    public void Url_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<UrlCmd>(
            """{ "items": [ "https://example.com", "not a url" ] }""",
            "/items/1");

    [Fact]
    public void Slug_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<SlugCmd>("""{ "items": [ "hello-world", "Not Valid!" ] }""", "/items/1");

    [Fact]
    public void PhoneNumber_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<PhoneNumberCmd>(
            """{ "items": [ "+14155551234", "555-1234" ] }""",
            "/items/1");

    [Fact]
    public void Percentage_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<PercentageCmd>("""{ "items": [ 50, 150 ] }""", "/items/1");

    [Fact]
    public void MonetaryAmount_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<MonetaryAmountCmd>("""{ "items": [ 10.5, -1 ] }""", "/items/1");

    [Fact]
    public void LanguageCode_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<LanguageCodeCmd>("""{ "items": [ "en", "xyz" ] }""", "/items/1");

    [Fact]
    public void IpAddress_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<IpAddressCmd>("""{ "items": [ "127.0.0.1", "not-an-ip" ] }""", "/items/1");

    [Fact]
    public void Hostname_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<HostnameCmd>(
            """{ "items": [ "example.com", "-bad-host-" ] }""",
            "/items/1");

    [Fact]
    public void CurrencyCode_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<CurrencyCodeCmd>("""{ "items": [ "USD", "US" ] }""", "/items/1");

    [Fact]
    public void CountryCode_direct_collection_element_reports_the_element_path() =>
        AssertSingleElementPath<CountryCodeCmd>("""{ "items": [ "US", "USA" ] }""", "/items/1");
}
