namespace Trellis.Core.Tests.Primitives;

using Trellis;

/// <summary>
/// Every generated <c>RequiredXxx&lt;T&gt;</c> value object and every built-in primitive
/// (<c>EmailAddress</c>, <c>Url</c>, <c>Age</c>, …) resolves its validation field name through
/// this one helper, so its null/empty/named contract is load-bearing for all of them at once —
/// including the direct-scalar-collection-element sentinel (an empty, non-null field name) that
/// <c>PathTrackingCollectionConverter</c> relies on to target the element pointer itself.
/// </summary>
public sealed class StringExtensionsNormalizeFieldNameTests
{
    [Fact]
    public void Null_field_name_falls_back_to_the_default() =>
        ((string?)null).NormalizeFieldName("age").Should().Be("age");

    [Fact]
    public void Empty_field_name_is_preserved_rather_than_replaced_by_the_default() =>
        string.Empty.NormalizeFieldName("age").Should().Be(string.Empty);

    [Fact]
    public void A_supplied_field_name_is_camel_cased_rather_than_replaced_by_the_default() =>
        "UserAge".NormalizeFieldName("age").Should().Be("userAge");
}
