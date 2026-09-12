namespace Trellis.Core.Tests.Pagination;

using System;
using System.Linq;

public sealed class PageMapTests
{
    private sealed record Source(int N);
    private sealed record Dest(string Label);

    [Fact]
    public void Constructor_accepts_absent_next()
    {
        var page = new Page<int>(
            Items: Array.Empty<int>(),
            Next: default(Cursor),
            Previous: null,
            RequestedLimit: 10,
            AppliedLimit: 10);

        page.Next.Should().BeNull();
    }

    [Fact]
    public void Constructor_accepts_absent_previous()
    {
        var page = new Page<int>(
            Items: Array.Empty<int>(),
            Next: null,
            Previous: default(Cursor),
            RequestedLimit: 10,
            AppliedLimit: 10);

        page.Previous.Should().BeNull();
    }

    [Fact]
    public void Map_projects_items_and_preserves_cursors_and_limits()
    {
        var next = new Cursor("next-token");
        var prev = new Cursor("prev-token");
        var source = new Page<Source>(
            new Source[] { new(1), new(2), new(3) },
            next, prev, RequestedLimit: 50, AppliedLimit: 10);

        var mapped = source.Map(s => new Dest($"v{s.N}"));

        mapped.Items.Should().HaveCount(3);
        mapped.Items.Select(d => d.Label).Should().Equal("v1", "v2", "v3");
        mapped.Next.Should().Be(next);
        mapped.Previous.Should().Be(prev);
        mapped.RequestedLimit.Should().Be(50);
        mapped.AppliedLimit.Should().Be(10);
        mapped.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void Map_on_empty_page_returns_empty_page_with_same_limits()
    {
        var source = Page.Empty<Source>(requestedLimit: 25, appliedLimit: 25);

        var mapped = source.Map(s => new Dest($"v{s.N}"));

        mapped.Items.Should().BeEmpty();
        mapped.Next.Should().BeNull();
        mapped.Previous.Should().BeNull();
        mapped.RequestedLimit.Should().Be(25);
        mapped.AppliedLimit.Should().Be(25);
        mapped.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void Map_preserves_null_next_and_previous()
    {
        var source = new Page<Source>(
            new Source[] { new(7) },
            Next: null, Previous: null, RequestedLimit: 10, AppliedLimit: 10);

        var mapped = source.Map(s => new Dest($"v{s.N}"));

        mapped.Next.Should().BeNull();
        mapped.Previous.Should().BeNull();
        mapped.Items.Single().Label.Should().Be("v7");
    }

    [Fact]
    public void Map_throws_when_selector_is_null()
    {
        var source = new Page<Source>(
            new Source[] { new(1) },
            Next: null, Previous: null, RequestedLimit: 10, AppliedLimit: 10);

        var act = () => source.Map<Dest>(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}