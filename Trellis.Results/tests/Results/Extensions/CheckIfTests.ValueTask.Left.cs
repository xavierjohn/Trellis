namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for CheckIf async extensions where only the LEFT (input) is async (ValueTask).
/// </summary>
public class CheckIfTests_ValueTask_Left
{
    private static readonly Error TestError = Error.Unexpected("test error");
    private static readonly Error CheckError = Error.Validation("check failed", "field");

    [Fact]
    public async Task CheckIfAsync_ValueTask_Left_Bool_ConditionTrue_CheckPasses()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(true, v => Result.Ok("ok"));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Left_Bool_ConditionTrue_CheckFails()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(true, _ => Result.Fail<string>(CheckError));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Left_Bool_ConditionFalse_SkipsCheck()
    {
        var checkInvoked = false;
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(false, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Left_FailureResult_CheckNotInvoked()
    {
        var checkInvoked = false;
        var resultTask = new ValueTask<Result<int>>(Result.Fail<int>(TestError));

        var sut = await resultTask.CheckIfAsync(true, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeFailure().Which.Should().Be(TestError);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Left_Predicate_True_CheckPasses()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(v => v > 0, v => Result.Ok("ok"));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Left_Predicate_False_SkipsCheck()
    {
        var checkInvoked = false;
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(v => v < 0, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }
}