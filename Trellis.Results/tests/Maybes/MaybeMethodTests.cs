namespace Trellis.Results.Tests.Maybes;

using System.Globalization;
using Trellis;

/// <summary>
/// Tests for Maybe instance methods: Bind, Or, Where, Tap, GetValueOrDefault(Func).
/// </summary>
public class MaybeMethodTests
{
    #region Bind

    [Fact]
    public void Bind_WhenHasValue_ShouldApplySelector()
    {
        var sut = Maybe.From(5);

        var result = sut.Bind(v => Maybe.From(v.ToString(CultureInfo.InvariantCulture)));

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("5");
    }

    [Fact]
    public void Bind_WhenNone_ShouldReturnNone()
    {
        var sut = Maybe<int>.None;

        var result = sut.Bind(v => Maybe.From(v.ToString(CultureInfo.InvariantCulture)));

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Bind_WhenSelectorReturnsNone_ShouldReturnNone()
    {
        var sut = Maybe.From(5);

        var result = sut.Bind(_ => Maybe<string>.None);

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Bind_ShouldComposeMultipleOperations()
    {
        var sut = Maybe.From("42");

        var result = sut
            .Bind(s => int.TryParse(s, out var n) ? Maybe.From(n) : Maybe<int>.None)
            .Bind(n => n > 0 ? Maybe.From(n * 2) : Maybe<int>.None);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(84);
    }

    [Fact]
    public void Bind_WhenSelectorIsNull_ShouldThrowArgumentNullException()
    {
        var sut = Maybe.From(5);

        Action action = () => sut.Bind<string>(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Or

    [Fact]
    public void Or_Value_WhenHasValue_ShouldReturnOriginal()
    {
        var sut = Maybe.From(5);

        var result = sut.Or(10);

        result.Value.Should().Be(5);
    }

    [Fact]
    public void Or_Value_WhenNone_ShouldReturnFallback()
    {
        var sut = Maybe<int>.None;

        var result = sut.Or(10);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(10);
    }

    [Fact]
    public void Or_Func_WhenHasValue_ShouldNotEvaluateFactory()
    {
        var sut = Maybe.From(5);
        var evaluated = false;

        var result = sut.Or(() =>
        {
            evaluated = true;
            return 10;
        });

        result.Value.Should().Be(5);
        evaluated.Should().BeFalse();
    }

    [Fact]
    public void Or_Func_WhenNone_ShouldEvaluateFactory()
    {
        var sut = Maybe<int>.None;

        var result = sut.Or(() => 10);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(10);
    }

    [Fact]
    public void Or_Func_WhenNull_ShouldThrowArgumentNullException()
    {
        var sut = Maybe.From(5);

        Action action = () => sut.Or((Func<int>)null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Or_Maybe_WhenHasValue_ShouldReturnOriginal()
    {
        var sut = Maybe.From(5);
        var fallback = Maybe.From(10);

        var result = sut.Or(fallback);

        result.Value.Should().Be(5);
    }

    [Fact]
    public void Or_Maybe_WhenNone_ShouldReturnFallback()
    {
        var sut = Maybe<int>.None;
        var fallback = Maybe.From(10);

        var result = sut.Or(fallback);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(10);
    }

    [Fact]
    public void Or_MaybeFunc_WhenHasValue_ShouldNotEvaluateFactory()
    {
        var sut = Maybe.From(5);
        var evaluated = false;

        var result = sut.Or(() =>
        {
            evaluated = true;
            return Maybe.From(10);
        });

        result.Value.Should().Be(5);
        evaluated.Should().BeFalse();
    }

    [Fact]
    public void Or_MaybeFunc_WhenNone_ShouldEvaluateFactory()
    {
        var sut = Maybe<int>.None;

        var result = sut.Or(() => Maybe.From(10));

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(10);
    }

    [Fact]
    public void Or_MaybeFunc_WhenNull_ShouldThrowArgumentNullException()
    {
        var sut = Maybe.From(5);

        Action action = () => sut.Or((Func<Maybe<int>>)null!);

        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Where

    [Fact]
    public void Where_WhenHasValueAndPredicatePasses_ShouldReturnSome()
    {
        var sut = Maybe.From(5);

        var result = sut.Where(v => v > 3);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(5);
    }

    [Fact]
    public void Where_WhenHasValueAndPredicateFails_ShouldReturnNone()
    {
        var sut = Maybe.From(5);

        var result = sut.Where(v => v > 10);

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Where_WhenNone_ShouldReturnNone()
    {
        var sut = Maybe<int>.None;

        var result = sut.Where(v => v > 3);

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Where_WhenPredicateIsNull_ShouldThrowArgumentNullException()
    {
        var sut = Maybe.From(5);

        Action action = () => sut.Where(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Tap

    [Fact]
    public void Tap_WhenHasValue_ShouldCallActionWithValue()
    {
        var sut = Maybe.From(5);
        var captured = 0;

        sut.Tap(v => captured = v);

        captured.Should().Be(5);
    }

    [Fact]
    public void Tap_WhenNone_ShouldNotCallAction()
    {
        var sut = Maybe<int>.None;
        var called = false;

        sut.Tap(_ => called = true);

        called.Should().BeFalse();
    }

    [Fact]
    public void Tap_ShouldReturnOriginalMaybe()
    {
        var sut = Maybe.From(5);

        var result = sut.Tap(_ => { });

        result.Should().Be(sut);
    }

    [Fact]
    public void Tap_WhenActionIsNull_ShouldThrowArgumentNullException()
    {
        var sut = Maybe.From(5);

        Action action = () => sut.Tap(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GetValueOrDefault with Func

    [Fact]
    public void GetValueOrDefault_Func_WhenHasValue_ShouldReturnValue()
    {
        var sut = Maybe.From(5);

        var result = sut.GetValueOrDefault(() => 10);

        result.Should().Be(5);
    }

    [Fact]
    public void GetValueOrDefault_Func_WhenNone_ShouldReturnFactoryResult()
    {
        var sut = Maybe<int>.None;

        var result = sut.GetValueOrDefault(() => 10);

        result.Should().Be(10);
    }

    [Fact]
    public void GetValueOrDefault_Func_WhenHasValue_ShouldNotCallFactory()
    {
        var sut = Maybe.From(5);
        var called = false;

        sut.GetValueOrDefault(() =>
        {
            called = true;
            return 10;
        });

        called.Should().BeFalse();
    }

    [Fact]
    public void GetValueOrDefault_Func_WhenNull_ShouldThrowArgumentNullException()
    {
        var sut = Maybe.From(5);

        Action action = () => sut.GetValueOrDefault((Func<int>)null!);

        action.Should().Throw<ArgumentNullException>();
    }

    #endregion
}