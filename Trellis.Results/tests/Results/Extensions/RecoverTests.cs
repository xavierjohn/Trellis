namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for Recover extension methods that convert failures to successes with a fallback value.
/// </summary>
public class RecoverTests
{
    #region Recover_Value

    [Fact]
    public void Recover_WhenResultIsSuccess_ShouldReturnOriginalSuccess()
    {
        var sut = Result.Ok("Hello");

        var result = sut.Recover("Fallback");

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public void Recover_WhenResultIsFailure_ShouldReturnSuccessWithFallback()
    {
        var sut = Result.Fail<string>(Error.Unexpected("error"));

        var result = sut.Recover("Fallback");

        result.Should().BeSuccess().Which.Should().Be("Fallback");
    }

    #endregion

    #region Recover_Func

    [Fact]
    public void Recover_Func_WhenResultIsSuccess_ShouldNotCallFunc()
    {
        var sut = Result.Ok("Hello");
        var funcCalled = false;

        var result = sut.Recover(() => { funcCalled = true; return "Fallback"; });

        result.Should().BeSuccess().Which.Should().Be("Hello");
        funcCalled.Should().BeFalse();
    }

    [Fact]
    public void Recover_Func_WhenResultIsFailure_ShouldCallFuncAndReturnSuccess()
    {
        var sut = Result.Fail<string>(Error.Unexpected("error"));

        var result = sut.Recover(() => "Fallback");

        result.Should().BeSuccess().Which.Should().Be("Fallback");
    }

    [Fact]
    public void Recover_Func_WithNullFunc_ShouldThrowArgumentNullException()
    {
        var sut = Result.Fail<string>(Error.Unexpected("error"));

        var act = () => sut.Recover((Func<string>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "fallbackFunc");
    }

    #endregion

    #region Recover_ErrorFunc

    [Fact]
    public void Recover_ErrorFunc_WhenResultIsSuccess_ShouldNotCallFunc()
    {
        var sut = Result.Ok("Hello");
        var funcCalled = false;

        var result = sut.Recover((Error _) => { funcCalled = true; return "Fallback"; });

        result.Should().BeSuccess().Which.Should().Be("Hello");
        funcCalled.Should().BeFalse();
    }

    [Fact]
    public void Recover_ErrorFunc_WhenResultIsFailure_ShouldPassErrorToFuncAndReturnSuccess()
    {
        var error = Error.NotFound("resource not found");
        var sut = Result.Fail<string>(error);
        Error receivedError = null!;

        var result = sut.Recover(e => { receivedError = e; return "Fallback"; });

        result.Should().BeSuccess().Which.Should().Be("Fallback");
        receivedError.Should().Be(error);
    }

    [Fact]
    public void Recover_ErrorFunc_WithNullFunc_ShouldThrowArgumentNullException()
    {
        var sut = Result.Fail<string>(Error.Unexpected("error"));

        var act = () => sut.Recover((Func<Error, string>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "fallbackFunc");
    }

    #endregion

    #region Task

    [Fact]
    public async Task RecoverAsync_Task_WhenSuccess_ShouldReturnOriginal()
    {
        var sut = Task.FromResult(Result.Ok("Hello"));

        var result = await sut.RecoverAsync("Fallback");

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task RecoverAsync_Task_WhenFailure_ShouldReturnFallback()
    {
        var sut = Task.FromResult(Result.Fail<string>(Error.Unexpected("error")));

        var result = await sut.RecoverAsync("Fallback");

        result.Should().BeSuccess().Which.Should().Be("Fallback");
    }

    [Fact]
    public async Task RecoverAsync_Task_WithNullTask_ShouldThrowArgumentNullException()
    {
        Task<Result<string>> sut = null!;

        var act = async () => await sut.RecoverAsync("Fallback");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RecoverAsync_Task_Func_WhenFailure_ShouldCallFunc()
    {
        var sut = Task.FromResult(Result.Fail<string>(Error.Unexpected("error")));

        var result = await sut.RecoverAsync(() => "Fallback");

        result.Should().BeSuccess().Which.Should().Be("Fallback");
    }

    [Fact]
    public async Task RecoverAsync_Task_ErrorFunc_WhenFailure_ShouldPassError()
    {
        var error = Error.NotFound("not found");
        var sut = Task.FromResult(Result.Fail<string>(error));
        Error receivedError = null!;

        var result = await sut.RecoverAsync(e => { receivedError = e; return "Fallback"; });

        result.Should().BeSuccess().Which.Should().Be("Fallback");
        receivedError.Should().Be(error);
    }

    #endregion

    #region ValueTask

    [Fact]
    public async Task RecoverAsync_ValueTask_WhenSuccess_ShouldReturnOriginal()
    {
        var sut = new ValueTask<Result<string>>(Result.Ok("Hello"));

        var result = await sut.RecoverAsync("Fallback");

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task RecoverAsync_ValueTask_WhenFailure_ShouldReturnFallback()
    {
        var sut = new ValueTask<Result<string>>(Result.Fail<string>(Error.Unexpected("error")));

        var result = await sut.RecoverAsync("Fallback");

        result.Should().BeSuccess().Which.Should().Be("Fallback");
    }

    [Fact]
    public async Task RecoverAsync_ValueTask_Func_WhenFailure_ShouldCallFunc()
    {
        var sut = new ValueTask<Result<string>>(Result.Fail<string>(Error.Unexpected("error")));

        var result = await sut.RecoverAsync(() => "Fallback");

        result.Should().BeSuccess().Which.Should().Be("Fallback");
    }

    [Fact]
    public async Task RecoverAsync_ValueTask_ErrorFunc_WhenFailure_ShouldPassError()
    {
        var error = Error.NotFound("not found");
        var sut = new ValueTask<Result<string>>(Result.Fail<string>(error));

        var result = await sut.RecoverAsync(e => "Recovered: " + e.Detail);

        result.Should().BeSuccess().Which.Should().Be("Recovered: not found");
    }

    #endregion
}