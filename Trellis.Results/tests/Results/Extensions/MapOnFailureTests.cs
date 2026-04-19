using System.Diagnostics;
using Trellis.Results.Tests.Helpers;
using Trellis.Testing;

namespace Trellis.Results.Tests.Results.Extensions;

public class MapOnFailureTests : TestBase
{
    [Fact]
    public void MapOnFailure_WithNullMap_ThrowsArgumentNullException()
    {
        var original = Result.Fail<int>(Error1);

        var act = () => original.MapOnFailure((Func<Error, Error>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "map");
    }

    [Fact]
    public async Task MapOnFailureAsync_TaskResult_WithNullResultTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> original = null!;

        Func<Task<Result<int>>> act = () => original.MapOnFailureAsync(e => e);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    [Fact]
    public void MapOnFailure_transforms_failure_error()
    {
        var original = Result.Fail<int>(Error1);

        var mapped = original.MapOnFailure(e => Error.Conflict($"Wrapped: {e.Detail}"));

        mapped.Should().BeFailure();
        mapped.Error.Should().Be(Error.Conflict($"Wrapped: {Error1.Detail}"));
    }

    [Fact]
    public void MapOnFailure_does_not_touch_success()
    {
        var success = Result.Ok(42);

        var mapped = success.MapOnFailure(e => Error.Unexpected("ShouldNotHappen"));

        mapped.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    #region MapErrorAsync - Task<Result<T>> with Func<Error, Error>

    [Fact]
    public async Task MapOnFailureAsync_TaskResult_WithFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1).AsTask();

        var mapped = await original.MapOnFailureAsync(e => Error.Conflict($"Wrapped: {e.Detail}"));

        mapped.Should().BeFailure();
        mapped.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task MapOnFailureAsync_TaskResult_WithFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42).AsTask();

        var mapped = await original.MapOnFailureAsync(e => Error.Unexpected("ShouldNotHappen"));

        mapped.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    #endregion

    #region MapErrorAsync - Result<T> with Func<Error, Task<Error>>

    [Fact]
    public async Task MapOnFailureAsync_Result_WithAsyncFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1);

        Func<Error, Task<Error>> mapper = async e =>
        {
            await Task.Delay(1);
            return Error.Conflict($"Wrapped: {e.Detail}");
        };

        var mapped = await original.MapOnFailureAsync(mapper);

        mapped.Should().BeFailure();
        mapped.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task MapOnFailureAsync_Result_WithAsyncFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42);

        Func<Error, Task<Error>> mapper = async e =>
        {
            await Task.Delay(1);
            return Error.Unexpected("ShouldNotHappen");
        };

        var mapped = await original.MapOnFailureAsync(mapper);

        mapped.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    #endregion

    #region MapErrorAsync - Task<Result<T>> with Func<Error, Task<Error>>

    [Fact]
    public async Task MapOnFailureAsync_TaskResult_WithAsyncFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1).AsTask();

        var mapped = await original.MapOnFailureAsync(async e =>
        {
            await Task.Delay(1);
            return Error.Conflict($"Wrapped: {e.Detail}");
        });

        mapped.Should().BeFailure();
        mapped.Error.Should().BeOfType<ConflictError>();
    }

    #endregion

    #region MapErrorAsync - ValueTask<Result<T>> with Func<Error, Error>

    [Fact]
    public async Task MapOnFailureAsync_ValueTaskResult_WithFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1).AsValueTask();

        var mapped = await original.MapOnFailureAsync(e => Error.Conflict($"Wrapped: {e.Detail}"));

        mapped.Should().BeFailure();
        mapped.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task MapOnFailureAsync_ValueTaskResult_WithFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42).AsValueTask();

        var mapped = await original.MapOnFailureAsync(e => Error.Unexpected("ShouldNotHappen"));

        mapped.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    #endregion

    #region MapErrorAsync - Result<T> with Func<Error, ValueTask<Error>>

    [Fact]
    public async Task MapOnFailureAsync_Result_WithValueTaskFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1);

        var mapped = await original.MapOnFailureAsync(e =>
            ValueTask.FromResult<Error>(Error.Conflict($"Wrapped: {e.Detail}")));

        mapped.Should().BeFailure();
        mapped.Error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public async Task MapOnFailureAsync_Result_WithValueTaskFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42);

        var mapped = await original.MapOnFailureAsync(e =>
            ValueTask.FromResult<Error>(Error.Unexpected("ShouldNotHappen")));

        mapped.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    #endregion

    #region MapErrorAsync - ValueTask<Result<T>> with Func<Error, ValueTask<Error>>

    [Fact]
    public async Task MapOnFailureAsync_ValueTaskResult_WithValueTaskFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1).AsValueTask();

        var mapped = await original.MapOnFailureAsync(e =>
            ValueTask.FromResult<Error>(Error.Conflict($"Wrapped: {e.Detail}")));

        mapped.Should().BeFailure();
        mapped.Error.Should().BeOfType<ConflictError>();
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void MapOnFailure_ConvertsDomainErrorToApiError()
    {
        // Arrange
        var domainResult = Result.Fail<string>(Error.Domain("Insufficient balance"));

        // Act
        var apiResult = domainResult.MapOnFailure(e => Error.BadRequest($"API Error: {e.Detail}"));

        // Assert
        apiResult.Should().BeFailure();
        apiResult.Error.Should().BeOfType<BadRequestError>();
        apiResult.Error.Detail.Should().Contain("Insufficient balance");
    }

    [Fact]
    public void MapOnFailure_AddsContextToError()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Entity not found"));

        // Act
        var enriched = result.MapOnFailure(e =>
            Error.NotFound($"User lookup failed: {e.Detail}", "user-123"));

        // Assert
        enriched.Should().BeFailure();
        enriched.Error.Instance.Should().Be("user-123");
    }

    [Fact]
    public async Task MapOnFailureAsync_ChainedErrorTransformations()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Unexpected("Internal error")).AsTask();

        // Act
        var transformed = await result
            .MapOnFailureAsync(e => Error.ServiceUnavailable($"Service error: {e.Detail}"));

        // Assert
        transformed.Should().BeFailure();
        transformed.Error.Should().BeOfType<ServiceUnavailableError>();
    }

    [Fact]
    public void MapOnFailure_Success_LogsOkStatus()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok(42)
            .MapOnFailure(error => Error.Unexpected($"Wrapped: {error.Detail}"));

        result.Should().BeSuccess()
            .Which.Should().Be(42);
        activityTest.AssertActivityCapturedWithStatus("MapOnFailure", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task MapOnFailureAsync_Result_WithAsyncFunc_Failure_LogsErrorStatus()
    {
        using var activityTest = new ActivityTestHelper();

        var result = await Result.Fail<int>(Error.Validation("Bad input"))
            .MapOnFailureAsync(error => Task.FromResult<Error>(Error.Conflict($"Wrapped: {error.Detail}")));

        result.Should().BeFailureOfType<ConflictError>();
        activityTest.AssertActivityCapturedWithStatus("MapOnFailure", ActivityStatusCode.Error);
    }

    #endregion
}