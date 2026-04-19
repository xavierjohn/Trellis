namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="CheckIfExtensions"/> — sync CheckIf with bool condition and predicate condition.
/// </summary>
public class CheckIfTests
{
    private static readonly Error TestError = Error.Unexpected("test error");
    private static readonly Error CheckError = Error.Validation("check failed", "field");

    #region CheckIf with bool condition — success path

    [Fact]
    public void CheckIf_Bool_SuccessResult_ConditionTrue_CheckPasses_ReturnsOriginal()
    {
        var result = Result.Ok(42);

        var sut = result.CheckIf(true, v => Result.Ok("ok"));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public void CheckIf_Bool_SuccessResult_ConditionTrue_CheckFails_ReturnsCheckFailure()
    {
        var result = Result.Ok(42);

        var sut = result.CheckIf(true, _ => Result.Fail<string>(CheckError));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public void CheckIf_Bool_SuccessResult_ConditionFalse_CheckSkipped_ReturnsOriginal()
    {
        var checkInvoked = false;
        var result = Result.Ok(42);

        var sut = result.CheckIf(false, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse("check function should not be invoked when condition is false");
    }

    #endregion

    #region CheckIf with bool condition — failure path

    [Fact]
    public void CheckIf_Bool_FailureResult_ConditionTrue_CheckNotInvoked_ReturnsOriginalFailure()
    {
        var checkInvoked = false;
        var result = Result.Fail<int>(TestError);

        var sut = result.CheckIf(true, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeFailure().Which.Should().Be(TestError);
        checkInvoked.Should().BeFalse("check function should not be invoked for failed results");
    }

    [Fact]
    public void CheckIf_Bool_FailureResult_ConditionFalse_ReturnsOriginalFailure()
    {
        var result = Result.Fail<int>(TestError);

        var sut = result.CheckIf(false, _ => Result.Ok("ok"));

        sut.Should().BeFailure().Which.Should().Be(TestError);
    }

    #endregion

    #region CheckIf with predicate condition — success path

    [Fact]
    public void CheckIf_Predicate_SuccessResult_PredicateTrue_CheckPasses_ReturnsOriginal()
    {
        var result = Result.Ok(42);

        var sut = result.CheckIf(v => v > 0, v => Result.Ok("ok"));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public void CheckIf_Predicate_SuccessResult_PredicateTrue_CheckFails_ReturnsCheckFailure()
    {
        var result = Result.Ok(42);

        var sut = result.CheckIf(v => v > 0, _ => Result.Fail<string>(CheckError));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public void CheckIf_Predicate_SuccessResult_PredicateFalse_CheckSkipped_ReturnsOriginal()
    {
        var checkInvoked = false;
        var result = Result.Ok(42);

        var sut = result.CheckIf(v => v < 0, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse("check function should not be invoked when predicate is false");
    }

    #endregion

    #region CheckIf with predicate condition — failure path

    [Fact]
    public void CheckIf_Predicate_FailureResult_PredicateNotInvoked()
    {
        var predicateInvoked = false;
        var result = Result.Fail<int>(TestError);

        var sut = result.CheckIf(v =>
        {
            predicateInvoked = true;
            return v > 0;
        }, _ => Result.Ok("ok"));

        sut.Should().BeFailure().Which.Should().Be(TestError);
        predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
    }

    #endregion

    #region Unit overloads

    [Fact]
    public void CheckIf_Bool_Unit_SuccessResult_ConditionTrue_CheckPasses_ReturnsOriginal()
    {
        var result = Result.Ok(42);

        var sut = result.CheckIf(true, _ => Result.Ok());

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public void CheckIf_Predicate_Unit_SuccessResult_PredicateTrue_CheckFails_ReturnsFailure()
    {
        var result = Result.Ok(42);

        var sut = result.CheckIf(v => v > 0, _ => Result.Fail<Unit>(CheckError));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    #endregion

    #region Null argument validation

    [Fact]
    public void CheckIf_Bool_NullFunc_ThrowsArgumentNullException()
    {
        var result = Result.Ok(42);

        var act = () => result.CheckIf(true, (Func<int, Result<string>>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CheckIf_Predicate_NullPredicate_ThrowsArgumentNullException()
    {
        var result = Result.Ok(42);

        var act = () => result.CheckIf(null!, _ => Result.Ok("ok"));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CheckIf_Predicate_NullFunc_ThrowsArgumentNullException()
    {
        var result = Result.Ok(42);

        var act = () => result.CheckIf(v => v > 0, (Func<int, Result<string>>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Chaining integration

    [Fact]
    public void CheckIf_InChain_ConditionFalse_OriginalValuePreserved()
    {
        var result = Result.Ok("hello")
            .Map(s => s.ToUpperInvariant())
            .CheckIf(false, _ => Result.Fail<string>(CheckError))
            .Map(s => s + "!");

        result.Should().BeSuccess().Which.Should().Be("HELLO!");
    }

    [Fact]
    public void CheckIf_InChain_ConditionTrue_CheckFails_ShortCircuits()
    {
        var mapInvoked = false;

        var result = Result.Ok("hello")
            .CheckIf(true, _ => Result.Fail<string>(CheckError))
            .Map(s =>
            {
                mapInvoked = true;
                return s + "!";
            });

        result.Should().BeFailure();
        mapInvoked.Should().BeFalse("downstream operations should not execute after check failure");
    }

    #endregion
}