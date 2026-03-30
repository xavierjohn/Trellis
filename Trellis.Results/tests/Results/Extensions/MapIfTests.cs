namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class MapIfTests
{
    #region MapIf with bool condition

    [Fact]
    public void MapIf_Success_ConditionTrue_TransformsValue()
    {
        var sut = Result.Success(10);

        var result = sut.MapIf(true, x => x * 2);

        result.Should().BeSuccess().Which.Should().Be(20);
    }

    [Fact]
    public void MapIf_Success_ConditionFalse_ReturnsOriginal()
    {
        var sut = Result.Success(10);

        var result = sut.MapIf(false, x => x * 2);

        result.Should().BeSuccess().Which.Should().Be(10);
    }

    [Fact]
    public void MapIf_Failure_ConditionTrue_ReturnsOriginalFailure()
    {
        var sut = Result.Failure<int>(Error.Unexpected("some error"));

        var result = sut.MapIf(true, x => x * 2);

        result.Should().BeFailure().Which.Should().Be(Error.Unexpected("some error"));
    }

    [Fact]
    public void MapIf_Failure_ConditionFalse_ReturnsOriginalFailure()
    {
        var sut = Result.Failure<int>(Error.Unexpected("some error"));

        var result = sut.MapIf(false, x => x * 2);

        result.Should().BeFailure().Which.Should().Be(Error.Unexpected("some error"));
    }

    #endregion

    #region MapIf with predicate

    [Fact]
    public void MapIf_Success_PredicateTrue_TransformsValue()
    {
        var sut = Result.Success(10);

        var result = sut.MapIf(x => x > 5, x => x * 2);

        result.Should().BeSuccess().Which.Should().Be(20);
    }

    [Fact]
    public void MapIf_Success_PredicateFalse_ReturnsOriginal()
    {
        var sut = Result.Success(3);

        var result = sut.MapIf(x => x > 5, x => x * 2);

        result.Should().BeSuccess().Which.Should().Be(3);
    }

    [Fact]
    public void MapIf_Failure_PredicateNotInvoked()
    {
        var predicateInvoked = false;
        var sut = Result.Failure<int>(Error.Unexpected("some error"));

        var result = sut.MapIf(x => { predicateInvoked = true; return true; }, x => x * 2);

        predicateInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(Error.Unexpected("some error"));
    }

    #endregion

    #region Null arguments

    [Fact]
    public void MapIf_NullFunc_ThrowsArgumentNullException()
    {
        var sut = Result.Success(10);

        var act = () => sut.MapIf(true, (Func<int, int>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public void MapIf_NullPredicate_ThrowsArgumentNullException()
    {
        var sut = Result.Success(10);

        var act = () => sut.MapIf((Func<int, bool>)null!, x => x * 2);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "predicate");
    }

    #endregion
}