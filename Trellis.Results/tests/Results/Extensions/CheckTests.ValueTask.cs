namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for Check.ValueTask.cs where BOTH input and function are async (ValueTask).
/// Also covers Check.ValueTask.Left.cs (async input, sync function) and
/// Check.ValueTask.Right.cs (sync input, async function).
/// </summary>
public class Check_ValueTask_Tests
{
    #region ValueTask Both — ValueTask<Result<T>> + Func<T, ValueTask<Result<TK>>>

    [Fact]
    public async Task CheckAsync_ValueTaskBoth_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await new ValueTask<Result<string>>(Result.Ok("hello"))
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Ok(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_ValueTaskBoth_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "check failed" };

        var result = await new ValueTask<Result<string>>(Result.Ok("hello"))
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Fail<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_ValueTaskBoth_Failure_CheckNotInvoked()
    {
        var error = new Error.InternalServerError("test") { Detail = "original error" };
        var funcInvoked = false;

        var result = await new ValueTask<Result<string>>(Result.Fail<string>(error))
            .CheckAsync(v => { funcInvoked = true; return new ValueTask<Result<int>>(Result.Ok(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region ValueTask Left — ValueTask<Result<T>> + Func<T, Result<TK>>

    [Fact]
    public async Task CheckAsync_ValueTaskLeft_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await new ValueTask<Result<string>>(Result.Ok("hello"))
            .CheckAsync((string v) => Result.Ok(v.Length));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_ValueTaskLeft_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "check failed" };

        var result = await new ValueTask<Result<string>>(Result.Ok("hello"))
            .CheckAsync((string v) => Result.Fail<int>(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_ValueTaskLeft_Failure_CheckNotInvoked()
    {
        var error = new Error.InternalServerError("test") { Detail = "original error" };
        var funcInvoked = false;

        var result = await new ValueTask<Result<string>>(Result.Fail<string>(error))
            .CheckAsync((string v) => { funcInvoked = true; return Result.Ok(42); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region ValueTask Right — Result<T> + Func<T, ValueTask<Result<TK>>>

    [Fact]
    public async Task CheckAsync_ValueTaskRight_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Result.Ok("hello")
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Ok(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_ValueTaskRight_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "check failed" };

        var result = await Result.Ok("hello")
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Fail<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_ValueTaskRight_Failure_CheckNotInvoked()
    {
        var error = new Error.InternalServerError("test") { Detail = "original error" };
        var funcInvoked = false;

        var result = await Result.Fail<string>(error)
            .CheckAsync(v => { funcInvoked = true; return new ValueTask<Result<int>>(Result.Ok(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion
}