namespace Trellis.Results.Tests.Maybes.Extensions;

using Trellis;

public class CollectionExtensionTests
{
    #region TryFirst

    [Fact]
    public void TryFirst_empty_collection_returns_none()
    {
        var result = Array.Empty<int>().TryFirst();
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public void TryFirst_single_item_returns_some()
    {
        int[] source = [42];
        var result = source.TryFirst();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void TryFirst_multiple_items_returns_first()
    {
        int[] source = [1, 2, 3];
        var result = source.TryFirst();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public void TryFirst_with_predicate_match_found_returns_some()
    {
        int[] source = [1, 2, 3, 4];
        var result = source.TryFirst(x => x > 2);
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public void TryFirst_with_predicate_no_match_returns_none()
    {
        int[] source = [1, 2, 3];
        var result = source.TryFirst(x => x > 10);
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public void TryFirst_null_source_throws()
    {
        IEnumerable<int> source = null!;
        var act = () => source.TryFirst();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryFirst_null_predicate_throws()
    {
        int[] source = [1];
        var act = () => source.TryFirst(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region TryLast

    [Fact]
    public void TryLast_empty_collection_returns_none()
    {
        var result = Array.Empty<int>().TryLast();
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public void TryLast_single_item_returns_some()
    {
        int[] source = [42];
        var result = source.TryLast();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void TryLast_multiple_items_returns_last()
    {
        int[] source = [1, 2, 3];
        var result = source.TryLast();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public void TryLast_with_predicate_match_found_returns_last_matching()
    {
        int[] source = [1, 2, 3, 4];
        var result = source.TryLast(x => x > 2);
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(4);
    }

    [Fact]
    public void TryLast_with_predicate_no_match_returns_none()
    {
        int[] source = [1, 2, 3];
        var result = source.TryLast(x => x > 10);
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public void TryLast_null_source_throws()
    {
        IEnumerable<int> source = null!;
        var act = () => source.TryLast();
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Choose

    [Fact]
    public void Choose_all_some_returns_all_values()
    {
        Maybe<int>[] items = [Maybe.From(1), Maybe.From(2), Maybe.From(3)];
        items.Choose().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Choose_all_none_returns_empty()
    {
        Maybe<int>[] items = [Maybe<int>.None, Maybe<int>.None];
        items.Choose().Should().BeEmpty();
    }

    [Fact]
    public void Choose_mixed_returns_only_some_values()
    {
        Maybe<int>[] items = [Maybe.From(1), Maybe<int>.None, Maybe.From(3)];
        items.Choose().Should().Equal(1, 3);
    }

    [Fact]
    public void Choose_with_selector_transforms_values()
    {
        Maybe<int>[] items = [Maybe.From(1), Maybe<int>.None, Maybe.From(3)];
        items.Choose(x => x * 10).Should().Equal(10, 30);
    }

    [Fact]
    public void Choose_empty_source_returns_empty()
    {
        var items = Array.Empty<Maybe<int>>();
        items.Choose().Should().BeEmpty();
    }

    [Fact]
    public void Choose_null_source_throws()
    {
        IEnumerable<Maybe<int>> source = null!;
        var act = () => source.Choose();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Choose_with_selector_null_source_throws()
    {
        IEnumerable<Maybe<int>> source = null!;
        var act = () => source.Choose(x => x);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Choose_null_selector_throws()
    {
        Maybe<int>[] items = [Maybe.From(1)];
        var act = () => items.Choose<int, int>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}