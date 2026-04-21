namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for Check.Task.cs where BOTH input and function are async (Task).
/// Also covers Check.Task.Left.cs (async input, sync function) and
/// Check.Task.Right.cs (sync input, async function).
/// </summary>
public class Check_Task_Tests
{
    #region Task Both — Task<Result<T>> + Func<T, Task<Result<TK>>>

    [Fact]
    public async Task CheckAsync_TaskBoth_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Task.FromResult(Result.Ok("hello"))
            .CheckAsync(v => Task.FromResult(Result.Ok(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_TaskBoth_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "check failed" };

        var result = await Task.FromResult(Result.Ok("hello"))
            .CheckAsync(v => Task.FromResult(Result.Fail<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_TaskBoth_Failure_CheckNotInvoked()
    {
        var error = new Error.InternalServerError("test") { Detail = "original error" };
        var funcInvoked = false;

        var result = await Task.FromResult(Result.Fail<string>(error))
            .CheckAsync(v => { funcInvoked = true; return Task.FromResult(Result.Ok(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Task Left — Task<Result<T>> + Func<T, Result<TK>>

    [Fact]
    public async Task CheckAsync_TaskLeft_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Task.FromResult(Result.Ok("hello"))
            .CheckAsync((string v) => Result.Ok(v.Length));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_TaskLeft_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "check failed" };

        var result = await Task.FromResult(Result.Ok("hello"))
            .CheckAsync((string v) => Result.Fail<int>(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_TaskLeft_Failure_CheckNotInvoked()
    {
        var error = new Error.InternalServerError("test") { Detail = "original error" };
        var funcInvoked = false;

        var result = await Task.FromResult(Result.Fail<string>(error))
            .CheckAsync((string v) => { funcInvoked = true; return Result.Ok(42); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region Task Right — Result<T> + Func<T, Task<Result<TK>>>

    [Fact]
    public async Task CheckAsync_TaskRight_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Result.Ok("hello")
            .CheckAsync(v => Task.FromResult(Result.Ok(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_TaskRight_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "check failed" };

        var result = await Result.Ok("hello")
            .CheckAsync(v => Task.FromResult(Result.Fail<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_TaskRight_Failure_CheckNotInvoked()
    {
        var error = new Error.InternalServerError("test") { Detail = "original error" };
        var funcInvoked = false;

        var result = await Result.Fail<string>(error)
            .CheckAsync(v => { funcInvoked = true; return Task.FromResult(Result.Ok(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion
}