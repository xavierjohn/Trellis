namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for CheckIf async extensions where BOTH input and check function are async (Task).
/// </summary>
public class CheckIfTests_Task
{
    private static readonly Error TestError = Error.Unexpected("test error");
    private static readonly Error CheckError = Error.Validation("check failed", "field");

    #region Bool condition

    [Fact]
    public async Task CheckIfAsync_Task_Both_Bool_ConditionTrue_CheckPasses_ReturnsOriginal()
    {
        var resultTask = Task.FromResult(Result.Success(42));

        var sut = await resultTask.CheckIfAsync(true, v => Task.FromResult(Result.Success("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Both_Bool_ConditionTrue_CheckFails_ReturnsFailure()
    {
        var resultTask = Task.FromResult(Result.Success(42));

        var sut = await resultTask.CheckIfAsync(true, _ => Task.FromResult(Result.Failure<string>(CheckError)));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Both_Bool_ConditionFalse_SkipsCheck()
    {
        var checkInvoked = false;
        var resultTask = Task.FromResult(Result.Success(42));

        var sut = await resultTask.CheckIfAsync(false, v =>
        {
            checkInvoked = true;
            return Task.FromResult(Result.Success("ok"));
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_Task_Both_Bool_FailureResult_CheckNotInvoked()
    {
        var checkInvoked = false;
        var resultTask = Task.FromResult(Result.Failure<int>(TestError));

        var sut = await resultTask.CheckIfAsync(true, v =>
        {
            checkInvoked = true;
            return Task.FromResult(Result.Success("ok"));
        });

        sut.Should().BeFailure().Which.Should().Be(TestError);
        checkInvoked.Should().BeFalse();
    }

    #endregion

    #region Predicate condition

    [Fact]
    public async Task CheckIfAsync_Task_Both_Predicate_True_CheckPasses()
    {
        var resultTask = Task.FromResult(Result.Success(42));

        var sut = await resultTask.CheckIfAsync(v => v > 0, v => Task.FromResult(Result.Success("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Both_Predicate_False_SkipsCheck()
    {
        var checkInvoked = false;
        var resultTask = Task.FromResult(Result.Success(42));

        var sut = await resultTask.CheckIfAsync(v => v < 0, v =>
        {
            checkInvoked = true;
            return Task.FromResult(Result.Success("ok"));
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }

    #endregion
}
