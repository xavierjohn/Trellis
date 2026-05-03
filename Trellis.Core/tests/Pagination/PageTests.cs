namespace Trellis.Core.Tests.Pagination;

using System;

/// <summary>
/// Unit tests for the <see cref="Page{T}"/> pagination envelope.
/// </summary>
public class PageTests
{
    [Fact]
    public void DeliveredCount_equals_items_count()
    {
        var items = new[] { 1, 2, 3 };
        var page = new Page<int>(items, Next: null, Previous: null, RequestedLimit: 10, AppliedLimit: 5);

        page.DeliveredCount.Should().Be(3);
    }

    [Fact]
    public void WasCapped_true_when_applied_less_than_requested()
    {
        var page = new Page<int>(Array.Empty<int>(), null, null, RequestedLimit: 10, AppliedLimit: 5);

        page.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void WasCapped_false_when_applied_equals_requested()
    {
        var page = new Page<int>(Array.Empty<int>(), null, null, RequestedLimit: 10, AppliedLimit: 10);

        page.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void Empty_factory_returns_zero_items_and_no_cursors()
    {
        var page = Page.Empty<int>(requestedLimit: 25, appliedLimit: 10);

        page.Items.Should().BeEmpty();
        page.Next.Should().BeNull();
        page.Previous.Should().BeNull();
        page.RequestedLimit.Should().Be(25);
        page.AppliedLimit.Should().Be(10);
        page.DeliveredCount.Should().Be(0);
        page.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void Equality_uses_item_sequence_not_list_reference()
    {
        var first = new Page<int>([1, 2, 3], null, null, RequestedLimit: 10, AppliedLimit: 10);
        var second = new Page<int>([1, 2, 3], null, null, RequestedLimit: 10, AppliedLimit: 10);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Constructor_defensively_copies_items()
    {
        var items = new List<int> { 1, 2 };
        var page = new Page<int>(items, null, null, RequestedLimit: 10, AppliedLimit: 10);

        items.Add(3);

        page.Items.Should().Equal([1, 2]);
        page.DeliveredCount.Should().Be(2);
    }

    [Fact]
    public void Default_struct_returns_zero_for_derived_properties()
    {
        Page<int> def = default;
        def.DeliveredCount.Should().Be(0);
        def.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void Constructor_rejects_null_items()
    {
        var act = () => new Page<int>(null!, null, null, 10, 10);
        act.Should().Throw<ArgumentNullException>().WithParameterName("Items");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_requested_limit(int requested)
    {
        var act = () => new Page<int>(Array.Empty<int>(), null, null, requested, 1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("RequestedLimit");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_applied_limit(int applied)
    {
        var act = () => new Page<int>(Array.Empty<int>(), null, null, 10, applied);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("AppliedLimit");
    }

    [Fact]
    public void Constructor_rejects_applied_greater_than_requested()
    {
        var act = () => new Page<int>(Array.Empty<int>(), null, null, 5, 10);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("AppliedLimit");
    }

    [Fact]
    public void Default_struct_returns_empty_items_and_zero_delivered_count()
    {
        // default(Page<T>) bypasses the public ctor; the defensive Items getter returns
        // an empty list rather than null so consumers iterating directly don't NRE.
        var defaulted = default(Page<int>);

        defaulted.Items.Should().NotBeNull();
        defaulted.Items.Should().BeEmpty();
        defaulted.DeliveredCount.Should().Be(0);
        defaulted.RequestedLimit.Should().Be(0);
        defaulted.AppliedLimit.Should().Be(0);
        defaulted.WasCapped.Should().BeFalse();
    }
}
