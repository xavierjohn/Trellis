namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for CheckIf async extensions where only the RIGHT (check function) is async (ValueTask).
/// </summary>
public class CheckIfTests_ValueTask_Right
{
    private static readonly Error TestError = Error.Unexpected("test error");
    private static readonly Error CheckError = Error.Validation("check failed", "field");

    [Fact]
    public async Task CheckIfAsync_ValueTask_Right_Bool_ConditionTrue_CheckPasses()
    {
        var result = Result.Success(42);

        var sut = await result.CheckIfAsync(true, v => new ValueTask<Result<string>>(Result.Success("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Right_Bool_ConditionTrue_CheckFails()
    {
        var result = Result.Success(42);

        var sut = await result.CheckIfAsync(true, _ => new ValueTask<Result<string>>(Result.Failure<string>(CheckError)));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Right_Bool_ConditionFalse_SkipsCheck()
    {
        var checkInvoked = false;
        var result = Result.Success(42);

        var sut = await result.CheckIfAsync(false, v =>
        {
            checkInvoked = true;
            return new ValueTask<Result<string>>(Result.Success("ok"));
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Right_FailureResult_CheckNotInvoked()
    {
        var checkInvoked = false;
        var result = Result.Failure<int>(TestError);

        var sut = await result.CheckIfAsync(true, v =>
        {
            checkInvoked = true;
            return new ValueTask<Result<string>>(Result.Success("ok"));
        });

        sut.Should().BeFailure().Which.Should().Be(TestError);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Right_Predicate_True_CheckPasses()
    {
        var result = Result.Success(42);

        var sut = await result.CheckIfAsync(v => v > 0, v => new ValueTask<Result<string>>(Result.Success("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Right_Predicate_False_SkipsCheck()
    {
        var checkInvoked = false;
        var result = Result.Success(42);

        var sut = await result.CheckIfAsync(v => v < 0, v =>
        {
            checkInvoked = true;
            return new ValueTask<Result<string>>(Result.Success("ok"));
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }
}