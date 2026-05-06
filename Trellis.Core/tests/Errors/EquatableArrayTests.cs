namespace Trellis.Core.Tests.Errors;

using System.Collections.Immutable;

/// <summary>
/// Focused tests for <see cref="EquatableArray{T}"/>. End-to-end use is covered indirectly by
/// FieldViolation/RuleViolation/Page tests, but the equality-and-empty-singleton semantics
/// deserve dedicated coverage since this is the wrapper that restores structural equality
/// to <see cref="ImmutableArray{T}"/>.
/// </summary>
public class EquatableArrayTests
{
    [Fact]
    public void Default_and_Empty_compare_equal()
    {
        EquatableArray<int> def = default;

        def.Equals(EquatableArray<int>.Empty).Should().BeTrue();
        (def == EquatableArray<int>.Empty).Should().BeTrue();
    }

    [Fact]
    public void Default_and_Empty_have_same_hash()
    {
        EquatableArray<int> def = default;

        def.GetHashCode().Should().Be(EquatableArray<int>.Empty.GetHashCode());
    }

    [Fact]
    public void Empty_is_a_cached_singleton()
    {
        var first = EquatableArray<string>.Empty;
        var second = EquatableArray<string>.Empty;

        // Field-singleton semantics: the underlying ImmutableArray reference is identical.
        first.Items.Equals(second.Items).Should().BeTrue();
        first.Length.Should().Be(0);
    }

    [Fact]
    public void Items_on_default_returns_empty_immutable_array()
    {
        EquatableArray<int> def = default;

        def.Items.IsDefault.Should().BeFalse();
        def.Items.Length.Should().Be(0);
    }

    [Fact]
    public void Two_arrays_with_identical_contents_are_equal()
    {
        var left = EquatableArray.Create(1, 2, 3);
        var right = EquatableArray.Create(1, 2, 3);

        left.Equals(right).Should().BeTrue();
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Different_contents_compare_unequal()
    {
        var left = EquatableArray.Create(1, 2, 3);
        var right = EquatableArray.Create(1, 2, 4);

        left.Equals(right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void Order_is_significant()
    {
        var left = EquatableArray.Create(1, 2, 3);
        var right = EquatableArray.Create(3, 2, 1);

        left.Equals(right).Should().BeFalse();
    }

    [Fact]
    public void From_enumerable_round_trips_contents()
    {
        IEnumerable<string> source = new[] { "a", "b", "c" };

        var array = EquatableArray.From(source);

        array.Length.Should().Be(3);
        array.Items.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Equality_handles_null_elements()
    {
        var left = EquatableArray.Create<string?>("a", null, "c");
        var right = EquatableArray.Create<string?>("a", null, "c");

        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    // ---------- N-C-5 entry-point null-guards ----------

    [Fact]
    public void GenericCreate_NullItems_ThrowsArgumentNullException_WithItemsParamName()
    {
        // N-C-5 (GPT-5.5 meta-review): EquatableArray<T>.Create(params T[] items) is foundational
        // for immutable error collections; passing a null params array must throw
        // ArgumentNullException with paramName "items" rather than a framework-level null failure
        // from inside ToImmutableArray().
        string[] items = null!;

        var act = () => EquatableArray<string>.Create(items);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("items");
    }

    [Fact]
    public void GenericFrom_NullItems_ThrowsArgumentNullException_WithItemsParamName()
    {
        // N-C-5 follow-up: EquatableArray<T>.From(IEnumerable<T> items) — same shape.
        IEnumerable<string> items = null!;

        var act = () => EquatableArray<string>.From(items);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("items");
    }

    [Fact]
    public void NonGenericCreate_NullItems_ThrowsArgumentNullException_WithItemsParamName()
    {
        // N-C-5 follow-up: non-generic companion EquatableArray.Create<T>(params T[] items).
        string[] items = null!;

        var act = () => EquatableArray.Create<string>(items);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("items");
    }

    [Fact]
    public void NonGenericFrom_NullItems_ThrowsArgumentNullException_WithItemsParamName()
    {
        // N-C-5 follow-up: non-generic companion EquatableArray.From<T>(IEnumerable<T> items).
        IEnumerable<string> items = null!;

        var act = () => EquatableArray.From<string>(items);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("items");
    }
}
