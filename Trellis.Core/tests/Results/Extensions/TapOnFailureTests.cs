namespace Trellis.Core.Tests.Results.Extensions.TapOnFailure;

using Trellis.Testing;

public class TapOnFailureTests : TestBase
{
    private bool _actionExecuted;
    private Error? _capturedError;

    public TapOnFailureTests()
    {
        _actionExecuted = false;
        _capturedError = null;
    }

    #region TapOnFailure with Action

    [Fact]
    public void TapOnFailure_WithNullAction_ThrowsArgumentNullException()
    {
        var result = Result.Fail<int>(Error1);

        var act = () => result.TapOnFailure((Action)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "action");
    }

    [Fact]
    public void TapOnFailure_WithAction_FailureResult_ExecutesAction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);

        // Act
        var actual = result.TapOnFailure(() => _actionExecuted = true);

        // Assert
        _actionExecuted.Should().BeTrue();
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void TapOnFailure_WithAction_SuccessResult_DoesNotExecuteAction()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = result.TapOnFailure(() => _actionExecuted = true);

        // Assert
        _actionExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    #endregion

    #region TapOnFailure with Action<Error>

    [Fact]
    public void TapOnFailure_WithActionError_FailureResult_ExecutesActionWithError()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);

        // Act
        var actual = result.TapOnFailure(error =>
        {
            _actionExecuted = true;
            _capturedError = error;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        Assert.NotNull(_capturedError);
        _capturedError.Code.Should().Be(Error1.Code);
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void TapOnFailure_WithActionError_SuccessResult_DoesNotExecuteAction()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = result.TapOnFailure(error =>
        {
            _actionExecuted = true;
            _capturedError = error;
        });

        // Assert
        _actionExecuted.Should().BeFalse();
        Assert.Null(_capturedError);
        actual.Should().BeSuccess();
    }

    #endregion

    #region TapOnFailureAsync - Task<Result<T>> with Action

    [Fact]
    public async Task TapOnFailureAsync_TaskResult_WithNullResultTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> result = null!;

        Func<Task<Result<int>>> act = () => result.TapOnFailureAsync(() => { });

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    [Fact]
    public async Task TapOnFailureAsync_TaskResult_WithAction_FailureResult_ExecutesAction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1).AsTask();

        // Act
        var actual = await result.TapOnFailureAsync(() => _actionExecuted = true);

        // Assert
        _actionExecuted.Should().BeTrue();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_TaskResult_WithAction_SuccessResult_DoesNotExecuteAction()
    {
        // Arrange
        var result = Result.Ok(42).AsTask();

        // Act
        var actual = await result.TapOnFailureAsync(() => _actionExecuted = true);

        // Assert
        _actionExecuted.Should().BeFalse();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task TapOnFailureAsync_TaskResult_WithActionError_FailureResult_ExecutesAction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1).AsTask();

        // Act
        var actual = await result.TapOnFailureAsync(error =>
        {
            _actionExecuted = true;
            _capturedError = error;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        Assert.NotNull(_capturedError);
        actual.Should().BeFailure();
    }

    #endregion

    #region TapOnFailureAsync - Result<T> with Func<Task>

    [Fact]
    public async Task TapOnFailureAsync_Result_WithNullFuncTask_ThrowsArgumentNullException()
    {
        var result = Result.Fail<int>(Error1);

        Func<Task<Result<int>>> act = () => result.TapOnFailureAsync((Func<Task>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public async Task TapOnFailureAsync_Result_WithFuncTask_FailureResult_ExecutesFunction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);

        // Act
        var actual = await result.TapOnFailureAsync(() =>
        {
            _actionExecuted = true;
            return Task.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_Result_WithFuncTask_SuccessResult_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = await result.TapOnFailureAsync(() =>
        {
            _actionExecuted = true;
            return Task.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeFalse();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task TapOnFailureAsync_Result_WithFuncErrorTask_FailureResult_ExecutesFunction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);

        // Act
        var actual = await result.TapOnFailureAsync(error =>
        {
            _actionExecuted = true;
            _capturedError = error;
            return Task.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        Assert.NotNull(_capturedError);
        actual.Should().BeFailure();
    }

    #endregion

    #region TapOnFailureAsync - Task<Result<T>> with Func<Task>

    [Fact]
    public async Task TapOnFailureAsync_TaskResult_WithFuncTask_FailureResult_ExecutesFunction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1).AsTask();

        // Act
        var actual = await result.TapOnFailureAsync(() =>
        {
            _actionExecuted = true;
            return Task.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_TaskResult_WithFuncErrorTask_FailureResult_ExecutesFunction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1).AsTask();

        // Act
        var actual = await result.TapOnFailureAsync(error =>
        {
            _actionExecuted = true;
            _capturedError = error;
            return Task.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        Assert.NotNull(_capturedError);
        actual.Should().BeFailure();
    }

    #endregion

    #region TapOnFailureAsync - ValueTask<Result<T>> with Action

    [Fact]
    public async Task TapOnFailureAsync_ValueTaskResult_WithAction_FailureResult_ExecutesAction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1).AsValueTask();

        // Act
        var actual = await result.TapOnFailureAsync(() => _actionExecuted = true);

        // Assert
        _actionExecuted.Should().BeTrue();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_ValueTaskResult_WithAction_SuccessResult_DoesNotExecuteAction()
    {
        // Arrange
        var result = Result.Ok(42).AsValueTask();

        // Act
        var actual = await result.TapOnFailureAsync(() => _actionExecuted = true);

        // Assert
        _actionExecuted.Should().BeFalse();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task TapOnFailureAsync_ValueTaskResult_WithActionError_FailureResult_ExecutesAction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1).AsValueTask();

        // Act
        var actual = await result.TapOnFailureAsync(error =>
        {
            _actionExecuted = true;
            _capturedError = error;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        Assert.NotNull(_capturedError);
        actual.Should().BeFailure();
    }

    #endregion

    #region TapOnFailureAsync - Result<T> with Func<ValueTask>

    [Fact]
    public async Task TapOnFailureAsync_Result_WithFuncValueTask_FailureResult_ExecutesFunction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);

        // Act
        var actual = await result.TapOnFailureAsync(() =>
        {
            _actionExecuted = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_Result_WithFuncValueTask_SuccessResult_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = await result.TapOnFailureAsync(() =>
        {
            _actionExecuted = true;
            return ValueTask.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeFalse();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task TapOnFailureAsync_Result_WithFuncErrorValueTask_FailureResult_ExecutesFunction()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);

        // Act
        var actual = await result.TapOnFailureAsync(error =>
        {
            _actionExecuted = true;
            _capturedError = error;
            return ValueTask.CompletedTask;
        });

        // Assert
        _actionExecuted.Should().BeTrue();
        Assert.NotNull(_capturedError);
        actual.Should().BeFailure();
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void TapOnFailure_LoggingErrorDetails()
    {
        // Arrange
        var result = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", "user-123"?.ToString())) { Detail = "User not found" });
        var loggedMessage = string.Empty;

        // Act
#pragma warning disable IDE0053 // Multi-statement lambda cannot use expression body
        var actual = result.TapOnFailure(error =>
        {
            loggedMessage = $"Error: {error.Code} - {error.Detail}";
        });
#pragma warning restore IDE0053

        // Assert
        loggedMessage.Should().Contain("not-found");
        loggedMessage.Should().Contain("User not found");
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task TapOnFailureAsync_IncrementErrorMetrics()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.ServiceUnavailable() { Detail = "External service down" });
        var errorCount = 0;

        // Act
        var actual = await result.TapOnFailureAsync(error =>
        {
            if (error is Error.ServiceUnavailable)
                errorCount++;
            return Task.CompletedTask;
        });

        // Assert
        errorCount.Should().Be(1);
        actual.Should().BeFailure();
    }

    [Fact]
    public void TapOnFailure_ChainedWithOtherOperations()
    {
        // Arrange
        var errorMessages = new List<string>();

        // Act
        var result = Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid input" })
            .TapOnFailure(error => errorMessages.Add("First handler"))
            .TapOnFailure(error => errorMessages.Add("Second handler"))
            .TapOnFailure(error => errorMessages.Add("Third handler"));

        // Assert
        errorMessages.Should().HaveCount(3);
        errorMessages.Should().Equal("First handler", "Second handler", "Third handler");
        result.Should().BeFailure();
    }

    #endregion
}