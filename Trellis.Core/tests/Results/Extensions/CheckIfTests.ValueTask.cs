namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for CheckIf async extensions where BOTH input and check function are async (ValueTask).
/// Covers both bool condition and predicate condition variants.
/// </summary>
public class CheckIfTests_ValueTask
{
    private static readonly Error TestError = new Error.Unexpected("test") { Detail = "test error" };
    private static readonly Error CheckError = new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), ValidationCodes.Unspecified) { Detail = "check failed" }));

    #region Bool condition

    [Fact]
    public async Task CheckIfAsync_ValueTask_Bool_ConditionTrue_CheckPasses_ReturnsOriginal()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(true, v => new ValueTask<Result<string>>(Result.Ok("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Bool_ConditionTrue_CheckFails_ReturnsFailure()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(true, _ => new ValueTask<Result<string>>(Result.Fail<string>(CheckError)));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Bool_ConditionFalse_SkipsCheck()
    {
        var checkCalls = new CallbackRecorder<int>();
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(false, checkCalls.Wrap(v => new ValueTask<Result<string>>(Result.Ok("ok"))));

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkCalls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Bool_FailureResult_CheckNotInvoked()
    {
        var checkCalls = new CallbackRecorder<int>();
        var resultTask = new ValueTask<Result<int>>(Result.Fail<int>(TestError));

        var sut = await resultTask.CheckIfAsync(true, checkCalls.Wrap(v => new ValueTask<Result<string>>(Result.Ok("ok"))));

        sut.Should().BeFailure().Which.Should().Be(TestError);
        checkCalls.Should().NotHaveBeenCalled();
    }

    #endregion

    #region Predicate condition

    [Fact]
    public async Task CheckIfAsync_ValueTask_Predicate_True_CheckPasses()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(v => v > 0, v => new ValueTask<Result<string>>(Result.Ok("ok")));

        sut.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Predicate_True_CheckFails()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(v => v > 0, _ => new ValueTask<Result<string>>(Result.Fail<string>(CheckError)));

        sut.Should().BeFailure().Which.Should().Be(CheckError);
    }

    [Fact]
    public async Task CheckIfAsync_ValueTask_Predicate_False_SkipsCheck()
    {
        var checkCalls = new CallbackRecorder<int>();
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var sut = await resultTask.CheckIfAsync(v => v < 0, checkCalls.Wrap(v => new ValueTask<Result<string>>(Result.Ok("ok"))));

        sut.Should().BeSuccess().Which.Should().Be(42);
        checkCalls.Should().NotHaveBeenCalled();
    }

    #endregion
}