namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class CheckTests
{
    #region Check with Result<TK>

    [Fact]
    public void Check_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = Result.Success("hello")
            .Check(v => Result.Success(v.Length));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public void Check_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = Result.Success("hello")
            .Check(v => Result.Failure<int>(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public void Check_Failure_CheckNotInvoked_ReturnsOriginalFailure()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = Result.Failure<string>(error)
            .Check(v => { funcInvoked = true; return Result.Success(42); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Check with Result<Unit>

    [Fact]
    public void Check_Unit_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = Result.Success("hello")
            .Check(v => Result.Success());

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public void Check_Unit_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("unit check failed");

        var result = Result.Success("hello")
            .Check(v => Result.Failure(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public void Check_Unit_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = Result.Failure<string>(error)
            .Check(v => { funcInvoked = true; return Result.Success(); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Null argument

    [Fact]
    public void Check_NullFunc_ThrowsArgumentNullException()
    {
        var result = Result.Success("hello");

        var act = () => result.Check((Func<string, Result<int>>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    #endregion
}