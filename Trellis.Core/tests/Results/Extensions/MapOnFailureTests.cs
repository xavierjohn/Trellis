using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

namespace Trellis.Core.Tests.Results.Extensions;

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

        var mapped = original.MapOnFailure(e => new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {e.Detail}" });

        mapped.Should().BeFailure();
        mapped.Error!.Should().Be(new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {Error1.Detail}" });
    }

    [Fact]
    public void MapOnFailure_does_not_touch_success()
    {
        var success = Result.Ok(42);

        var mapped = success.MapOnFailure(e => new Error.InternalServerError("test") { Detail = "ShouldNotHappen" });

        mapped.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    #region MapErrorAsync - Task<Result<T>> with Func<Error, Error>

    [Fact]
    public async Task MapOnFailureAsync_TaskResult_WithFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1).AsTask();

        var mapped = await original.MapOnFailureAsync(e => new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {e.Detail}" });

        mapped.Should().BeFailure();
        mapped.Error!.Should().BeOfType<Error.Conflict>();
    }

    [Fact]
    public async Task MapOnFailureAsync_TaskResult_WithFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42).AsTask();

        var mapped = await original.MapOnFailureAsync(e => new Error.InternalServerError("test") { Detail = "ShouldNotHappen" });

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
            return new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {e.Detail}" };
        };

        var mapped = await original.MapOnFailureAsync(mapper);

        mapped.Should().BeFailure();
        mapped.Error!.Should().BeOfType<Error.Conflict>();
    }

    [Fact]
    public async Task MapOnFailureAsync_Result_WithAsyncFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42);

        Func<Error, Task<Error>> mapper = async e =>
        {
            await Task.Delay(1);
            return new Error.InternalServerError("test") { Detail = "ShouldNotHappen" };
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
            return new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {e.Detail}" };
        });

        mapped.Should().BeFailure();
        mapped.Error!.Should().BeOfType<Error.Conflict>();
    }

    #endregion

    #region MapErrorAsync - ValueTask<Result<T>> with Func<Error, Error>

    [Fact]
    public async Task MapOnFailureAsync_ValueTaskResult_WithFunc_TransformsError()
    {
        var original = Result.Fail<int>(Error1).AsValueTask();

        var mapped = await original.MapOnFailureAsync(e => new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {e.Detail}" });

        mapped.Should().BeFailure();
        mapped.Error!.Should().BeOfType<Error.Conflict>();
    }

    [Fact]
    public async Task MapOnFailureAsync_ValueTaskResult_WithFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42).AsValueTask();

        var mapped = await original.MapOnFailureAsync(e => new Error.InternalServerError("test") { Detail = "ShouldNotHappen" });

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
            ValueTask.FromResult<Error>(new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {e.Detail}" }));

        mapped.Should().BeFailure();
        mapped.Error!.Should().BeOfType<Error.Conflict>();
    }

    [Fact]
    public async Task MapOnFailureAsync_Result_WithValueTaskFunc_DoesNotTouchSuccess()
    {
        var original = Result.Ok(42);

        var mapped = await original.MapOnFailureAsync(e =>
            ValueTask.FromResult<Error>(new Error.InternalServerError("test") { Detail = "ShouldNotHappen" }));

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
            ValueTask.FromResult<Error>(new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {e.Detail}" }));

        mapped.Should().BeFailure();
        mapped.Error!.Should().BeOfType<Error.Conflict>();
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void MapOnFailure_ConvertsDomainErrorToApiError()
    {
        // Arrange
        var domainResult = Result.Fail<string>(new Error.Conflict(null, "domain.violation") { Detail = "Insufficient balance" });

        // Act
        var apiResult = domainResult.MapOnFailure(e => new Error.BadRequest("bad.request") { Detail = $"API Error: {e.Detail}" });

        // Assert
        apiResult.Should().BeFailure();
        apiResult.Error!.Should().BeOfType<Error.BadRequest>();
        apiResult.Error!.Detail.Should().Contain("Insufficient balance");
    }

    [Fact]
    public void MapOnFailure_AddsContextToError()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Entity not found" });

        // Act
        var enriched = result.MapOnFailure(e =>
            new Error.NotFound(new ResourceRef("Resource", "user-123"?.ToString())) { Detail = $"User lookup failed: {e.Detail}" });

        // Assert
        enriched.Should().BeFailure();
        enriched.Error!.Should().BeOfType<Error.NotFound>().Which.Resource.Id.Should().Be("user-123");
    }

    [Fact]
    public async Task MapOnFailureAsync_ChainedErrorTransformations()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.InternalServerError("test") { Detail = "Internal error" }).AsTask();

        // Act
        var transformed = await result
            .MapOnFailureAsync(e => new Error.ServiceUnavailable() { Detail = $"Service error: {e.Detail}" });

        // Assert
        transformed.Should().BeFailure();
        transformed.Error!.Should().BeOfType<Error.ServiceUnavailable>();
    }

    [Fact]
    public void MapOnFailure_Success_LogsOkStatus()
    {
        using var activityTest = new ActivityTestHelper();

        var result = Result.Ok(42)
            .MapOnFailure(error => new Error.InternalServerError("test") { Detail = $"Wrapped: {error.Detail}" });

        result.Should().BeSuccess()
            .Which.Should().Be(42);
        activityTest.AssertActivityCapturedWithStatus("MapOnFailure", ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task MapOnFailureAsync_Result_WithAsyncFunc_Failure_LogsErrorStatus()
    {
        using var activityTest = new ActivityTestHelper();

        var result = await Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad input" })
            .MapOnFailureAsync(error => Task.FromResult<Error>(new Error.Conflict(null, "conflict") { Detail = $"Wrapped: {error.Detail}" }));

        result.Should().BeFailureOfType<Error.Conflict>();
        activityTest.AssertActivityCapturedWithStatus("MapOnFailure", ActivityStatusCode.Error);
    }

    #endregion
}