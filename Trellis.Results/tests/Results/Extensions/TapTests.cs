namespace Trellis.Results.Tests.Results.Extensions.Tap;

public partial class TapTests : TestBase
{
    protected bool ActionExecuted { get; set; }

    protected void Action() => ActionExecuted = true;

    protected void Action_T(T _) => ActionExecuted = true;

    protected Task Task_Action()
    {
        ActionExecuted = true;
        return Task.CompletedTask;
    }

    protected Task Task_Action_T(T _)
    {
        ActionExecuted = true;
        return Task.CompletedTask;
    }

    protected ValueTask ValueTask_Action()
    {
        ActionExecuted = true;
        return ValueTask.CompletedTask;
    }

    protected ValueTask ValueTask_Action_T(T _)
    {
        ActionExecuted = true;
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void Tap_WithNullAction_ShouldThrowArgumentNullException()
    {
        Result<T> result = Result.Ok(T.Value1);

        var act = () => result.Tap((Action)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "action");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tap_T_executes_action_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.Tap(Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tap_T_executes_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = result.Tap(Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    #region Task
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_Task_T_executes_action_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        Result<T> returned = await result.AsTask().TapAsync(Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_Task_T_executes_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().TapAsync(Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_Task_Left_T_executes_action_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().TapAsync(Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_Task_Left_T_executes_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().TapAsync(Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_Task_Left_T_executes_task_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsTask().TapAsync(Task_Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_Task_Right_T_executes_action_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.TapAsync(Task_Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Fact]
    public async Task TapAsync_Right_Task_WithNullFunc_ShouldThrowArgumentNullException()
    {
        Result<T> result = Result.Ok(T.Value1);

        var act = async () => await result.TapAsync((Func<Task>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_Task_Right_T_executes_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.TapAsync(Task_Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }
    #endregion

    #region ValueTask
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_ValueTask_T_executes_action_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsValueTask().TapAsync(ValueTask_Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_ValueTask_T_executes_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsValueTask().TapAsync(ValueTask_Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_ValueTask_Left_T_executes_action_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsValueTask().TapAsync(Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_ValueTask_Left_T_executes_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.AsValueTask().TapAsync(Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_ValueTask_Right_T_executes_action_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.TapAsync(ValueTask_Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tap_ValueTask_Right_T_executes_action_T_on_result_success_and_returns_self(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.TapAsync(ValueTask_Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }
    #endregion

    #region Async Func<Task> Tests

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TapAsync_Result_FuncTask_ExecutesOnSuccess(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.TapAsync(Task_Action);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TapAsync_Result_FuncTaskT_ExecutesOnSuccess(bool isSuccess)
    {
        Result<T> result = Result.SuccessIf(isSuccess, T.Value1, Error1);

        var returned = await result.TapAsync(Task_Action_T);

        ActionExecuted.Should().Be(isSuccess);
        result.Should().Be(returned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TapAsync_TaskResult_FuncTask_ExecutesOnSuccess(bool isSuccess)
    {
        var resultTask = Result.SuccessIf(isSuccess, T.Value1, Error1).AsTask();

        var returned = await resultTask.TapAsync(Task_Action);

        ActionExecuted.Should().Be(isSuccess);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TapAsync_TaskResult_FuncTaskT_ExecutesOnSuccess(bool isSuccess)
    {
        var resultTask = Result.SuccessIf(isSuccess, T.Value1, Error1).AsTask();

        var returned = await resultTask.TapAsync(Task_Action_T);

        ActionExecuted.Should().Be(isSuccess);
    }

    [Fact]
    public async Task TapAsync_ValueTaskResult_FuncValueTask_ExecutesOnSuccess()
    {
        var resultTask = Result.Ok(T.Value1).AsValueTask();

        var returned = await resultTask.TapAsync(ValueTask_Action);

        ActionExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task TapAsync_ValueTaskResult_FuncValueTaskT_ExecutesOnSuccess()
    {
        var resultTask = Result.Ok(T.Value1).AsValueTask();

        var returned = await resultTask.TapAsync(ValueTask_Action_T);

        ActionExecuted.Should().BeTrue();
    }

    #endregion
}