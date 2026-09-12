namespace Trellis.Core.Tests.Pagination;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class PageBuilderTests
{
    private sealed record Row(Guid Id, DateTimeOffset CreatedAt, string Name);

    // ───── Single-key over-fetch ───────────────────────────────────────────────

    [Fact]
    public void Single_key_under_fill_returns_no_next_cursor()
    {
        var pageSize = PageSize.FromRequested(10);
        var fetched = new List<Row>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "a"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "b"),
        };

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.Id));

        page.Items.Count.Should().Be(2);
        page.Next.Should().BeNull();
        page.Previous.Should().BeNull();
        page.RequestedLimit.Should().Be(10);
        page.AppliedLimit.Should().Be(10);
    }

    [Fact]
    public void Single_key_exact_fill_returns_no_next_cursor()
    {
        var pageSize = PageSize.FromRequested(3);
        var fetched = new List<Row>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "a"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "b"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "c"),
        };

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.Id));

        page.Items.Count.Should().Be(3);
        page.Next.Should().BeNull();
        page.Previous.Should().BeNull();
    }

    [Fact]
    public void Single_key_over_fill_returns_next_cursor_from_last_kept_item()
    {
        var pageSize = PageSize.FromRequested(3);
        var lastKeptId = Guid.NewGuid();
        var sentinelId = Guid.NewGuid();
        var fetched = new List<Row>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "a"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "b"),
            new(lastKeptId, DateTimeOffset.UtcNow, "c"),
            new(sentinelId, DateTimeOffset.UtcNow, "d"),
        };

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.Id));

        page.Items.Count.Should().Be(3);
        page.Next.Should().NotBeNull();
        page.Previous.Should().BeNull();

        var decoded = CursorCodec.TryDecode<Guid>(page.Next!);
        decoded.IsSuccess.Should().BeTrue();
        decoded.TryGetValue(out var decodedId).Should().BeTrue();
        decodedId.Should().Be(lastKeptId);
    }

    [Fact]
    public void Single_key_empty_list_returns_empty_page()
    {
        var pageSize = PageSize.FromRequested(50);
        var fetched = new List<Row>();

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.Id));

        page.Items.Count.Should().Be(0);
        page.Next.Should().BeNull();
        page.Previous.Should().BeNull();
    }

    [Fact]
    public void Single_key_preserves_was_capped_in_page()
    {
        var pageSize = PageSize.FromRequested(1000); // requested=1000, applied=100 (Max)
        var fetched = Enumerable.Range(0, 101) // applied + 1
            .Select(_ => new Row(Guid.NewGuid(), DateTimeOffset.UtcNow, "x"))
            .ToList();

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.Id));

        page.Items.Count.Should().Be(100);
        page.RequestedLimit.Should().Be(1000);
        page.AppliedLimit.Should().Be(100);
        page.WasCapped.Should().BeTrue();
        page.Next.Should().NotBeNull();
    }

    // ───── Composite over-fetch ────────────────────────────────────────────────

    [Fact]
    public void Composite_under_fill_returns_no_next_cursor()
    {
        var pageSize = PageSize.FromRequested(10);
        var fetched = new List<Row>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-2), "a"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), "b"),
        };

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.CreatedAt, r.Id));

        page.Items.Count.Should().Be(2);
        page.Next.Should().BeNull();
        page.Previous.Should().BeNull();
    }

    [Fact]
    public void Composite_over_fill_returns_next_cursor_from_last_kept_item()
    {
        var pageSize = PageSize.FromRequested(2);
        var lastKeptCreated = new DateTimeOffset(2026, 5, 22, 14, 30, 12, TimeSpan.Zero);
        var lastKeptId = Guid.NewGuid();
        var sentinelCreated = lastKeptCreated.AddSeconds(1);
        var sentinelId = Guid.NewGuid();

        var fetched = new List<Row>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-10), "a"),
            new(lastKeptId, lastKeptCreated, "b"),
            new(sentinelId, sentinelCreated, "c"),
        };

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.CreatedAt, r.Id));

        page.Items.Count.Should().Be(2);
        page.Next.Should().NotBeNull();
        page.Previous.Should().BeNull();

        var decoded = CursorCodec.TryDecodeComposite<Guid>(page.Next!);
        decoded.IsSuccess.Should().BeTrue();
        decoded.TryGetValue(out var pair).Should().BeTrue();
        pair.CreatedAt.Should().Be(lastKeptCreated);
        pair.Id.Should().Be(lastKeptId);
    }

    [Fact]
    public void Composite_empty_list_returns_empty_page()
    {
        var pageSize = PageSize.FromRequested(25);
        var fetched = new List<Row>();

        var page = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.CreatedAt, r.Id));

        page.Items.Count.Should().Be(0);
        page.Next.Should().BeNull();
        page.Previous.Should().BeNull();
    }

    [Fact]
    public void Previous_is_always_null_forward_only()
    {
        var pageSize = PageSize.FromRequested(3);
        var fetched = new List<Row>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "a"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "b"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "c"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d"),
        };

        var single = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.Id));
        var composite = PageBuilder.FromOverFetch(fetched, pageSize, r => CursorCodec.Encode(r.CreatedAt, r.Id));

        single.Previous.Should().BeNull();
        composite.Previous.Should().BeNull();
    }

    // ───── Defensive validation ────────────────────────────────────────────────

    [Fact]
    public void Single_key_rejects_default_pageSize()
    {
        var fetched = new List<Row> { new(Guid.NewGuid(), DateTimeOffset.UtcNow, "a") };

        var act = () => PageBuilder.FromOverFetch(fetched, null!, r => CursorCodec.Encode(r.Id));

        act.Should().Throw<ArgumentNullException>().WithParameterName("pageSize");
    }

    [Fact]
    public void Composite_rejects_default_pageSize()
    {
        var fetched = new List<Row> { new(Guid.NewGuid(), DateTimeOffset.UtcNow, "a") };

        var act = () => PageBuilder.FromOverFetch(fetched, null!, r => CursorCodec.Encode(r.CreatedAt, r.Id));

        act.Should().Throw<ArgumentNullException>().WithParameterName("pageSize");
    }
}