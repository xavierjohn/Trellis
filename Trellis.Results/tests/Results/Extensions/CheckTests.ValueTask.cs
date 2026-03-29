namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class CheckValueTaskTests
{
    #region ValueTask Both — ValueTask<Result<T>> + Func<T, ValueTask<Result<TK>>>

    [Fact]
    public async Task CheckAsync_ValueTaskBoth_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await new ValueTask<Result<string>>(Result.Success("hello"))
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Success(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_ValueTaskBoth_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = await new ValueTask<Result<string>>(Result.Success("hello"))
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Failure<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_ValueTaskBoth_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = await new ValueTask<Result<string>>(Result.Failure<string>(error))
            .CheckAsync(v => { funcInvoked = true; return new ValueTask<Result<int>>(Result.Success(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region ValueTask Left — ValueTask<Result<T>> + Func<T, Result<TK>>

    [Fact]
    public async Task CheckAsync_ValueTaskLeft_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await new ValueTask<Result<string>>(Result.Success("hello"))
            .CheckAsync((string v) => Result.Success(v.Length));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_ValueTaskLeft_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = await new ValueTask<Result<string>>(Result.Success("hello"))
            .CheckAsync((string v) => Result.Failure<int>(error));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_ValueTaskLeft_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = await new ValueTask<Result<string>>(Result.Failure<string>(error))
            .CheckAsync((string v) => { funcInvoked = true; return Result.Success(42); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion

    #region ValueTask Right — Result<T> + Func<T, ValueTask<Result<TK>>>

    [Fact]
    public async Task CheckAsync_ValueTaskRight_Success_CheckPasses_ReturnsOriginalValue()
    {
        var result = await Result.Success("hello")
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Success(v.Length)));

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public async Task CheckAsync_ValueTaskRight_Success_CheckFails_ReturnsCheckFailure()
    {
        var error = Error.Validation("check failed");

        var result = await Result.Success("hello")
            .CheckAsync(v => new ValueTask<Result<int>>(Result.Failure<int>(error)));

        result.Should().BeFailure().Which.Should().Be(error);
    }

    [Fact]
    public async Task CheckAsync_ValueTaskRight_Failure_CheckNotInvoked()
    {
        var error = Error.Unexpected("original error");
        var funcInvoked = false;

        var result = await Result.Failure<string>(error)
            .CheckAsync(v => { funcInvoked = true; return new ValueTask<Result<int>>(Result.Success(42)); });

        funcInvoked.Should().BeFalse();
        result.Should().BeFailure().Which.Should().Be(error);
    }

    #endregion
}