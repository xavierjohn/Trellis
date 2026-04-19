namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for CheckIf async extensions where only the RIGHT (check function) is async (Task).
/// </summary>
public class CheckIfTests_Task_Right
{
    private static readonly Error TestError = Error.Unexpected("test error");
    private static readonly Error CheckError = Error.Validation("check failed", "field");

    [Fact]
    public async Task CheckIfAsync_Task_Right_Bool_ConditionTrue_CheckPasses()
    {
        var result = Result.Ok(42);

        var sut = await result.CheckIfAsync(true, v => Task.FromResult(Result.Ok("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Right_Bool_ConditionTrue_CheckFails()
    {
        var result = Result.Ok(42);

        var sut = await result.CheckIfAsync(true, _ => Task.FromResult(Result.Fail<string>(CheckError)));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Right_Bool_ConditionFalse_SkipsCheck()
    {
        var checkInvoked = false;
        var result = Result.Ok(42);

        var sut = await result.CheckIfAsync(false, v =>
        {
            checkInvoked = true;
            return Task.FromResult(Result.Ok("ok"));
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_Task_Right_FailureResult_CheckNotInvoked()
    {
        var checkInvoked = false;
        var result = Result.Fail<int>(TestError);

        var sut = await result.CheckIfAsync(true, v =>
        {
            checkInvoked = true;
            return Task.FromResult(Result.Ok("ok"));
        });

        sut.Should().BeFailure().Which.Should().Be(TestError);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_Task_Right_Predicate_True_CheckPasses()
    {
        var result = Result.Ok(42);

        var sut = await result.CheckIfAsync(v => v > 0, v => Task.FromResult(Result.Ok("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Right_Predicate_False_SkipsCheck()
    {
        var checkInvoked = false;
        var result = Result.Ok(42);

        var sut = await result.CheckIfAsync(v => v < 0, v =>
        {
            checkInvoked = true;
            return Task.FromResult(Result.Ok("ok"));
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }
}