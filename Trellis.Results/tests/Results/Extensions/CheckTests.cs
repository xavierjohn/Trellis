namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for Check.cs — synchronous Check extensions for Result.
/// </summary>
public class Check_Tests
{
    #region Check with Result<TK>

    [Fact]
    public void Check_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = Result.Ok("hello")
            .Check(v => Result.Ok(v.Length));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public void Check_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = Result.Ok("hello")
            .Check(v => Result.Fail<int>(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public void Check_Failure_CheckNotInvoked_ReturnsOriginalFailure()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = Result.Fail<string>(error)
            .Check(v => { funcInvoked = true; return Result.Ok(42); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Check with Result<Unit>

    [Fact]
    public void Check_Unit_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = Result.Ok("hello")
            .Check(v => Result.Ok());

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public void Check_Unit_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("unit check failed");

        var result = Result.Ok("hello")
            .Check(v => Result.Fail(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public void Check_Unit_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = Result.Fail<string>(error)
            .Check(v => { funcInvoked = true; return Result.Ok(); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Null argument

    [Fact]
    public void Check_NullFunc_ThrowsArgumentNullException()
    {
        var result = Result.Ok("hello");

        var act = () => result.Check((Func<string, Result<int>>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    #endregion
}