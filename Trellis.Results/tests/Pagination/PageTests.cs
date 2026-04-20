namespace Trellis.Results.Tests.Pagination;

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
}
