namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

public class RecoverOnFailureTests
{
    bool _recoveryFunctionCalled;

    [Fact]
    public void RecoverOnFailure_WithNullFunc_ThrowsArgumentNullException()
    {
        var input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });

        var act = () => input.RecoverOnFailure((Func<Result<string>>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public async Task RecoverOnFailureAsync_TaskResult_WithNullResultTask_ThrowsArgumentNullException()
    {
        Task<Result<string>> input = null!;

        Func<Task<Result<string>>> act = () => input.RecoverOnFailureAsync(GetSuccessResult);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    #region RecoverOnFailure_Sync
    /// <summary>
    /// Given a successful result
    /// When RecoverOnFailure is called
    /// Then the recovery function is not called.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_function_does_not_get_executed_for_successful_result()
    {
        Result<string> input = Result.Ok("Success");
        Result<string> output = input.RecoverOnFailure(GetErrorResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    /// <summary>
    /// Given a failed result
    /// When RecoverOnFailure is called with a recovery function that succeeds
    /// Then the recovery function is called and returns success result.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_function_gets_executed_for_failed_result_and_returns_success()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = input.RecoverOnFailure(GetSuccessResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    /// <summary>
    /// Given a failed result
    /// When RecoverOnFailure is called with a recovery function that fails
    /// Then the recovery function is called and returns failed result.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_function_gets_executed_for_failed_result_and_returns_failure()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = input.RecoverOnFailure(GetErrorResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Recovered Error" });
    }

    /// <summary>
    /// Given a failed result
    /// When RecoverOnFailure is called with a recovery function that fails
    /// Then the recovery function with the Error is called and returns success.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_function_gets_executed_with_ErrorParam_for_failed_result_and_returns_success()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = input.RecoverOnFailure(e =>
        {
            e.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
            return GetSuccessResult();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    #endregion

    #region RecoverOnFailure_async_Result_sync_func
    [Fact]
    public async Task Task_Result_success_and_recovery_func_does_not_execute()
    {
        Task<Result<string>> input = Task.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_func_does_execute_and_succeed()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(GetSuccessResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_func_does_execute_and_fail()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Recovered Error" });
    }

    [Fact]
    public async Task Task_RecoverOnFailure_function_gets_executed_with_ErrorParam_for_failed_result_and_returns_success()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e =>
        {
            e.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
            return GetSuccessResult();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }
    #endregion

    #region RecoverOnFailure_sync_Result_async_func
    [Fact]
    public async Task Result_success_and_recovery_async_func_does_not_execute()
    {
        Result<string> input = Result.Ok("Success");
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResultAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task Result_failure_and_recovery_async_func_does_execute_and_succeed()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = await input.RecoverOnFailureAsync(GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Result_failure_and_recovery_async_func_does_execute_and_fail()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResultAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Recovered Error" });
    }

    [Fact]
    public async Task RecoverOnFailure_async_function_gets_executed_with_ErrorParam_for_failed_result_and_returns_success()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = await input.RecoverOnFailureAsync(async e =>
        {
            e.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
            return await GetSuccessResultAsync();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    #endregion

    #region RecoverOnFailure_async_Result_async_func
    [Fact]
    public async Task Task_Result_success_and_recovery_async_func_does_not_execute()
    {
        Task<Result<string>> input = Task.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResultAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_async_func_does_execute_and_succeed()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_async_func_does_execute_and_fail()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResultAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Recovered Error" });
    }

    [Fact]
    public async Task Task_RecoverOnFailure_async_function_gets_executed_with_ErrorParam_for_failed_result_and_returns_success()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(async e =>
        {
            e.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
            return await GetSuccessResultAsync();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }
    #endregion

    #region RecoverOnFailure_with_predicate_sync
    /// <summary>
    /// Given a successful result
    /// When RecoverOnFailure with predicate is called
    /// Then the recovery function is not called.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_with_predicate_does_not_execute_for_successful_result()
    {
        Result<string> input = Result.Ok("Success");
        Result<string> output = input.RecoverOnFailure(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    /// <summary>
    /// Given a failed result with NotFound error
    /// When RecoverOnFailure is called with a predicate that checks for NotFound
    /// Then the recovery function is called and returns success result.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_with_predicate_executes_when_predicate_matches_NotFound()
    {
        Result<string> input = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" });
        Result<string> output = input.RecoverOnFailure(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    /// <summary>
    /// Given a failed result with Unexpected error
    /// When RecoverOnFailure is called with a predicate that checks for NotFound
    /// Then the recovery function is not called and the original error is preserved.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_with_predicate_does_not_execute_when_predicate_does_not_match()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Unexpected error" });
        Result<string> output = input.RecoverOnFailure(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Unexpected error" });
    }

    /// <summary>
    /// Given a failed result with NotFound error
    /// When RecoverOnFailure is called with a predicate that checks for NotFound code
    /// Then the recovery function is called and returns success result.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_with_predicate_executes_when_predicate_matches_error_code()
    {
        Result<string> input = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" });
        Result<string> output = input.RecoverOnFailure(e => e.Code == "not-found", GetSuccessResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    /// <summary>
    /// Given a failed result with NotFound error
    /// When RecoverOnFailure is called with a predicate and function that receives the error
    /// Then the recovery function is called with the error and returns success result.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_with_predicate_and_error_param_executes_when_predicate_matches()
    {
        Result<string> input = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" });
        Result<string> output = input.RecoverOnFailure(e => e is Error.NotFound, e =>
        {
            e.Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" });
            return GetSuccessResult();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    /// <summary>
    /// Given a failed result with Unexpected error
    /// When RecoverOnFailure is called with a predicate and function that receives the error
    /// Then the recovery function is not called and the original error is preserved.
    /// </summary>
    [Fact]
    public void RecoverOnFailure_with_predicate_and_error_param_does_not_execute_when_predicate_does_not_match()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Unexpected error" });
        Result<string> output = input.RecoverOnFailure(e => e is Error.NotFound, e => GetSuccessResult());

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Unexpected error" });
    }

    #endregion

    #region RecoverOnFailure_with_predicate_async_Result_sync_func
    [Fact]
    public async Task Task_Result_success_and_recovery_with_predicate_func_does_not_execute()
    {
        Task<Result<string>> input = Task.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_func_does_execute_when_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_func_does_not_execute_when_not_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_and_error_param_func_does_execute_when_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, e =>
        {
            e.Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });
            return GetSuccessResult();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_and_error_param_func_does_not_execute_when_not_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, e => GetSuccessResult());

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }
    #endregion

    #region RecoverOnFailure_with_predicate_sync_Result_async_func
    [Fact]
    public async Task Result_success_and_recovery_with_predicate_async_func_does_not_execute()
    {
        Result<string> input = Result.Ok("Success");
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task Result_failure_and_recovery_with_predicate_async_func_does_execute_when_matches()
    {
        Result<string> input = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Result_failure_and_recovery_with_predicate_async_func_does_not_execute_when_not_matches()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }

    [Fact]
    public async Task Result_failure_and_recovery_with_predicate_and_error_param_async_func_does_execute_when_matches()
    {
        Result<string> input = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, e =>
        {
            e.Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });
            return GetSuccessResultAsync();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Result_failure_and_recovery_with_predicate_and_error_param_async_func_does_not_execute_when_not_matches()
    {
        Result<string> input = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" });
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, e => GetSuccessResultAsync());

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }

    #endregion

    #region RecoverOnFailure_with_predicate_async_Result_async_func
    [Fact]
    public async Task Task_Result_success_and_recovery_with_predicate_async_func_does_not_execute()
    {
        Task<Result<string>> input = Task.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_async_func_does_execute_when_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_async_func_does_not_execute_when_not_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_and_error_param_async_func_does_execute_when_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, e =>
        {
            e.Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });
            return GetSuccessResultAsync();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task Task_Result_failure_and_recovery_with_predicate_and_error_param_async_func_does_not_execute_when_not_matches()
    {
        Task<Result<string>> input = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, e => GetSuccessResultAsync());

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }
    #endregion

    #region RecoverOnFailure_ValueTask_sync_func
    [Fact]
    public async Task ValueTask_Result_success_and_recovery_func_does_not_execute()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_func_does_execute_and_succeed()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(GetSuccessResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_func_with_error_param()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e =>
        {
            e.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
            return GetSuccessResult();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }
    #endregion

    #region RecoverOnFailure_ValueTask_async_func
    [Fact]
    public async Task ValueTask_Result_success_and_recovery_ValueTask_func_does_not_execute()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(GetErrorResultValueTaskAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_ValueTask_func_does_execute_and_succeed()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(GetSuccessResultValueTaskAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_ValueTask_func_with_error_param()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e =>
        {
            e.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
            return GetSuccessResultValueTaskAsync();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }
    #endregion

    #region RecoverOnFailure_ValueTask_with_predicate_sync_func
    [Fact]
    public async Task ValueTask_Result_success_and_recovery_with_predicate_func_does_not_execute()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_with_predicate_func_does_execute_when_matches()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_with_predicate_func_does_not_execute_when_not_matches()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResult);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_with_predicate_and_error_param_func_does_execute_when_matches()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, e =>
        {
            e.Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });
            return GetSuccessResult();
        });

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }
    #endregion

    #region RecoverOnFailure_ValueTask_with_predicate_async_func
    [Fact]
    public async Task ValueTask_Result_success_and_recovery_with_predicate_ValueTask_func_does_not_execute()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Ok("Success"));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultValueTaskAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_with_predicate_ValueTask_func_does_execute_when_matches()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultValueTaskAsync);

        _recoveryFunctionCalled.Should().BeTrue();
        output.IsSuccess.Should().BeTrue();
        output.Unwrap().Should().Be("Recovered Success");
    }

    [Fact]
    public async Task ValueTask_Result_failure_and_recovery_with_predicate_ValueTask_func_does_not_execute_when_not_matches()
    {
        ValueTask<Result<string>> input = ValueTask.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error" }));
        Result<string> output = await input.RecoverOnFailureAsync(e => e is Error.NotFound, GetSuccessResultValueTaskAsync);

        _recoveryFunctionCalled.Should().BeFalse();
        output.IsFailure.Should().BeTrue();
        output.Error!.Should().Be(new Error.InternalServerError("test") { Detail = "Error" });
    }
    #endregion

    private Result<string> GetSuccessResult()
    {
        _recoveryFunctionCalled = true;
        return Result.Ok("Recovered Success");
    }

    private Result<string> GetErrorResult()
    {
        _recoveryFunctionCalled = true;
        return Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Recovered Error" });
    }

    private Task<Result<string>> GetSuccessResultAsync()
    {
        _recoveryFunctionCalled = true;
        return Task.FromResult(Result.Ok("Recovered Success"));
    }

    private Task<Result<string>> GetErrorResultAsync()
    {
        _recoveryFunctionCalled = true;
        return Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Recovered Error" }));
    }

    private ValueTask<Result<string>> GetSuccessResultValueTaskAsync()
    {
        _recoveryFunctionCalled = true;
        return ValueTask.FromResult(Result.Ok("Recovered Success"));
    }

    private ValueTask<Result<string>> GetErrorResultValueTaskAsync()
    {
        _recoveryFunctionCalled = true;
        return ValueTask.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Recovered Error" }));
    }
}