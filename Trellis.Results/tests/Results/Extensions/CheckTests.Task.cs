namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class CheckTaskTests
{
    #region Task Both — Task<Result<T>> + Func<T, Task<Result<TK>>>

    [Fact]
    public async Task CheckAsync_TaskBoth_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Task.FromResult(Result.Success("hello"))
            .CheckAsync(v => Task.FromResult(Result.Success(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_TaskBoth_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = await Task.FromResult(Result.Success("hello"))
            .CheckAsync(v => Task.FromResult(Result.Failure<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_TaskBoth_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = await Task.FromResult(Result.Failure<string>(error))
            .CheckAsync(v => { funcInvoked = true; return Task.FromResult(Result.Success(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Task Left — Task<Result<T>> + Func<T, Result<TK>>

    [Fact]
    public async Task CheckAsync_TaskLeft_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Task.FromResult(Result.Success("hello"))
            .CheckAsync((string v) => Result.Success(v.Length));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_TaskLeft_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = await Task.FromResult(Result.Success("hello"))
            .CheckAsync((string v) => Result.Failure<int>(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_TaskLeft_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = await Task.FromResult(Result.Failure<string>(error))
            .CheckAsync((string v) => { funcInvoked = true; return Result.Success(42); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Task Right — Result<T> + Func<T, Task<Result<TK>>>

    [Fact]
    public async Task CheckAsync_TaskRight_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Result.Success("hello")
            .CheckAsync(v => Task.FromResult(Result.Success(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_TaskRight_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = await Result.Success("hello")
            .CheckAsync(v => Task.FromResult(Result.Failure<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_TaskRight_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = await Result.Failure<string>(error)
            .CheckAsync(v => { funcInvoked = true; return Task.FromResult(Result.Success(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion
}