namespace Trellis.Core.Tests.Maybes.Extensions;

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
    public void TryFirst_returns_first_without_reading_tail_and_disposes_enumerator()
    {
        int[] source = [1, 2, 3];
        var result = source.TryFirst();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(1);

        var sourceVisits = 0;
        var disposals = 0;

        IEnumerable<int> CountedSource()
        {
            try
            {
                sourceVisits++;
                yield return 1;
                throw new InvalidOperationException("unvisited-tail");
            }
            finally
            {
                disposals++;
            }
        }

        var counted = CountedSource().TryFirst();

        counted.TryGetValue(out var first).Should().BeTrue();
        first.Should().Be(1);
        sourceVisits.Should().Be(1);
        disposals.Should().Be(1);
    }

    [Fact]
    public void TryFirst_with_predicate_stops_at_first_match_and_disposes_enumerator()
    {
        int[] source = [1, 2, 3, 4];
        var result = source.TryFirst(x => x > 2);
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(3);

        List<int> sourceVisits = [];
        List<int> predicateVisits = [];
        var disposals = 0;

        IEnumerable<int> CountedSource()
        {
            try
            {
                foreach (var value in new[] { 1, 2, 3 })
                {
                    sourceVisits.Add(value);
                    yield return value;
                }

                throw new InvalidOperationException("unvisited-tail");
            }
            finally
            {
                disposals++;
            }
        }

        var counted = CountedSource().TryFirst(value =>
        {
            predicateVisits.Add(value);
            return value > 2;
        });

        counted.TryGetValue(out var first).Should().BeTrue();
        first.Should().Be(3);
        sourceVisits.Should().Equal([1, 2, 3]);
        predicateVisits.Should().Equal([1, 2, 3]);
        disposals.Should().Be(1);
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
    public void Choose_is_deferred_and_disposes_source_after_partial_consumption()
    {
        Maybe<int>[] items = [Maybe.From(1), Maybe<int>.None, Maybe.From(3)];
        items.Choose().Should().Equal(1, 3);

        var sourceVisits = 0;
        var disposals = 0;

        IEnumerable<Maybe<int>> CountedSource()
        {
            try
            {
                sourceVisits++;
                yield return Maybe.From(1);
                sourceVisits++;
                yield return Maybe<int>.None;
                sourceVisits++;
                yield return Maybe.From(3);
            }
            finally
            {
                disposals++;
            }
        }

        var chosen = CountedSource().Choose();

        sourceVisits.Should().Be(0);
        using (var enumerator = chosen.GetEnumerator())
        {
            sourceVisits.Should().Be(0);
            enumerator.MoveNext().Should().BeTrue();
            enumerator.Current.Should().Be(1);
            sourceVisits.Should().Be(1);
        }

        disposals.Should().Be(1);
    }

    [Fact]
    public void Choose_with_selector_is_deferred_and_evaluates_each_consumed_value_once()
    {
        Maybe<int>[] items = [Maybe.From(1), Maybe<int>.None, Maybe.From(3)];
        items.Choose(x => x * 10).Should().Equal(10, 30);

        var sourceVisits = 0;
        var disposals = 0;
        List<int> selectorVisits = [];

        IEnumerable<Maybe<int>> CountedSource()
        {
            try
            {
                sourceVisits++;
                yield return Maybe<int>.None;
                sourceVisits++;
                yield return Maybe.From(1);
                sourceVisits++;
                yield return Maybe.From(3);
            }
            finally
            {
                disposals++;
            }
        }

        var chosen = CountedSource().Choose(value =>
        {
            selectorVisits.Add(value);
            return value * 10;
        });

        sourceVisits.Should().Be(0);
        selectorVisits.Should().BeEmpty();
        using (var enumerator = chosen.GetEnumerator())
        {
            sourceVisits.Should().Be(0);
            selectorVisits.Should().BeEmpty();
            enumerator.MoveNext().Should().BeTrue();
            enumerator.Current.Should().Be(10);
            sourceVisits.Should().Be(2);
            selectorVisits.Should().Equal([1]);
            enumerator.MoveNext().Should().BeTrue();
            enumerator.Current.Should().Be(30);
            sourceVisits.Should().Be(3);
            selectorVisits.Should().Equal([1, 3]);
        }

        disposals.Should().Be(1);
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