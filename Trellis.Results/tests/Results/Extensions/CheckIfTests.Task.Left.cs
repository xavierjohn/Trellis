namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for CheckIf async extensions where only the LEFT (input) is async (Task).
/// Covers both bool condition and predicate condition variants.
/// </summary>
public class CheckIfTests_Task_Left
{
    private static readonly Error TestError = new Error.InternalServerError("test") { Detail = "test error" };
    private static readonly Error CheckError = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "check failed" }));

    #region Bool condition

    [Fact]
    public async Task CheckIfAsync_Task_Left_Bool_ConditionTrue_CheckPasses_ReturnsOriginal()
    {
        var resultTask = Task.FromResult(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(true, v => Result.Ok("ok"));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Left_Bool_ConditionTrue_CheckFails_ReturnsFailure()
    {
        var resultTask = Task.FromResult(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(true, _ => Result.Fail<string>(CheckError));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Left_Bool_ConditionFalse_SkipsCheck()
    {
        var checkInvoked = false;
        var resultTask = Task.FromResult(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(false, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckIfAsync_Task_Left_Bool_FailureResult_CheckNotInvoked()
    {
        var checkInvoked = false;
        var resultTask = Task.FromResult(Result.Fail<int>(TestError));

        var sut = await resultTask.CheckIfAsync(true, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeFailure().Which.Should().Be(TestError);
        checkInvoked.Should().BeFalse();
    }

    #endregion

    #region Predicate condition

    [Fact]
    public async Task CheckIfAsync_Task_Left_Predicate_True_CheckPasses()
    {
        var resultTask = Task.FromResult(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(v => v > 0, v => Result.Ok("ok"));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_Task_Left_Predicate_False_SkipsCheck()
    {
        var checkInvoked = false;
        var resultTask = Task.FromResult(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(v => v < 0, v =>
        {
            checkInvoked = true;
            return Result.Ok("ok");
        });

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkInvoked.Should().BeFalse();
    }

    #endregion
}