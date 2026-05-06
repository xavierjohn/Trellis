namespace Trellis.Core.Tests.Maybes.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for Maybe LINQ extension methods (Select, SelectMany) enabling query syntax.
/// </summary>
public class MaybeLinqTests
{
    #region Select

    [Fact]
    public void Select_WhenMaybeHasValue_ShouldProjectValue()
    {
        Maybe<string> sut = Maybe.From("hello");

        var result = sut.Select(v => v.ToUpperInvariant());

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("HELLO");
    }

    [Fact]
    public void Select_WhenMaybeHasNoValue_ShouldReturnNone()
    {
        Maybe<string> sut = Maybe<string>.None;

        var result = sut.Select(v => v.ToUpperInvariant());

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Select_WithLinqSyntax_ShouldWork()
    {
        Maybe<string> sut = Maybe.From("hello");

        var result = from v in sut
                     select v.ToUpperInvariant();

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("HELLO");
    }

    #endregion

    #region SelectMany

    [Fact]
    public void SelectMany_WhenBothHaveValues_ShouldComposeValues()
    {
        Maybe<string> first = Maybe.From("Hello");
        Maybe<string> second = Maybe.From("World");

        var result = first.SelectMany(_ => second, (a, b) => $"{a} {b}");

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("Hello World");
    }

    [Fact]
    public void SelectMany_WhenSourceIsNone_ShouldReturnNone()
    {
        Maybe<string> first = Maybe<string>.None;
        Maybe<string> second = Maybe.From("World");

        var result = first.SelectMany(_ => second, (a, b) => $"{a} {b}");

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void SelectMany_WhenCollectionIsNone_ShouldReturnNone()
    {
        Maybe<string> first = Maybe.From("Hello");
        Maybe<string> second = Maybe<string>.None;

        var result = first.SelectMany(_ => second, (a, b) => $"{a} {b}");

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void SelectMany_WithLinqSyntax_TwoFromClauses_ShouldWork()
    {
        Maybe<string> firstName = Maybe.From("John");
        Maybe<string> lastName = Maybe.From("Doe");

        var result = from f in firstName
                     from l in lastName
                     select $"{f} {l}";

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("John Doe");
    }

    [Fact]
    public void SelectMany_WithLinqSyntax_ThreeFromClauses_ShouldWork()
    {
        Maybe<string> a = Maybe.From("A");
        Maybe<string> b = Maybe.From("B");
        Maybe<string> c = Maybe.From("C");

        var result = from x in a
                     from y in b
                     from z in c
                     select $"{x}{y}{z}";

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("ABC");
    }

    [Fact]
    public void SelectMany_WithLinqSyntax_WhenAnyIsNone_ShouldReturnNone()
    {
        Maybe<string> a = Maybe.From("A");
        Maybe<string> b = Maybe<string>.None;
        Maybe<string> c = Maybe.From("C");

        var result = from x in a
                     from y in b
                     from z in c
                     select $"{x}{y}{z}";

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void SelectMany_WithDifferentTypes_ShouldWork()
    {
        Maybe<string> name = Maybe.From("Alice");
        Maybe<int> age = Maybe.From(30);

        var result = from n in name
                     from a in age
                     select new PersonRecord(n, a);

        result.HasValue.Should().BeTrue();
        result.Value.Name.Should().Be("Alice");
        result.Value.Age.Should().Be(30);
    }

    #endregion

    #region m-C-2 entry-point null-guards

    [Fact]
    public void Select_NullSelector_ThrowsArgumentNullException_WithSelectorParamName()
    {
        var maybe = Maybe.From("hello");
        Func<string, int> selector = null!;

        var act = () => maybe.Select(selector);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("selector");
    }

    [Fact]
    public void SelectMany_NullCollectionSelector_ThrowsArgumentNullException_WithCollectionSelectorParamName()
    {
        var maybe = Maybe.From("hello");
        Func<string, Maybe<int>> collectionSelector = null!;

        var act = () => maybe.SelectMany(collectionSelector, (a, b) => $"{a}{b}");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("collectionSelector");
    }

    [Fact]
    public void SelectMany_NullResultSelector_ThrowsArgumentNullException_WithResultSelectorParamName()
    {
        var maybe = Maybe.From("hello");
        Func<string, int, string> resultSelector = null!;

        var act = () => maybe.SelectMany(_ => Maybe.From(7), resultSelector);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resultSelector");
    }

    #endregion

    private sealed record PersonRecord(string Name, int Age);
}