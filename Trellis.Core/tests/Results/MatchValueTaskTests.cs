namespace Trellis.Core.Tests;

using static Trellis.Testing.CallbackRecorderAssertionsExtensions;

/// <summary>
/// Tests for Match/Switch ValueTask extension methods.
/// - ValueTask Left-Async: ValueTask&lt;Result&lt;TIn&gt;&gt; with sync handlers
/// - ValueTask Right-Async: Result&lt;TIn&gt; with ValueTask handlers
/// - ValueTask Both-Async: ValueTask&lt;Result&lt;TIn&gt;&gt; with ValueTask handlers
/// - SwitchAsync with ValueTask
/// </summary>
public class MatchValueTaskTests
{
    #region MatchAsync ValueTask Left-Async (ValueTask<Result<TIn>> + sync handlers)

    [Fact]
    public async Task MatchAsync_ValueTask_Left_Success_CallsOnSuccess()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok(42));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: value => $"Value: {value}",
            onFailure: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Left_Failure_CallsOnFailure()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: value => $"Value: {value}",
            onFailure: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Left_Success_ReturnsTransformed()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok("hello"));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: s => s.Length,
            onFailure: _ => -1
        );

        // Assert
        output.Should().Be(5);
    }

    #endregion

    #region MatchAsync ValueTask Right-Async (Result<TIn> + ValueTask handlers)

    [Fact]
    public async Task MatchAsync_ValueTask_Right_Success_CallsOnSuccess()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var output = await result.MatchAsync(
            onSuccess: value => ValueTask.FromResult($"Value: {value}"),
            onFailure: err => ValueTask.FromResult($"Error: {err.Detail}")
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Right_Failure_CallsOnFailure()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act
        var output = await result.MatchAsync(
            onSuccess: value => ValueTask.FromResult($"Value: {value}"),
            onFailure: err => ValueTask.FromResult($"Error: {err.Detail}")
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Right_Success_CorrectHandlerCalled()
    {
        // Arrange
        var result = Result.Ok("hello");
        var successCalls = new Trellis.Testing.CallbackRecorder<string>();
        var failureCalls = new Trellis.Testing.CallbackRecorder<Error>();

        // Act
        var output = await result.MatchAsync(
            onSuccess: successCalls.Wrap(value => ValueTask.FromResult(value.Length)),
            onFailure: failureCalls.Wrap(err => ValueTask.FromResult(-1))
        );

        // Assert
        output.Should().Be(5);
        successCalls.Should().HaveBeenCalledAtLeastOnce();
        failureCalls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Right_Failure_CorrectHandlerCalled()
    {
        // Arrange
        var result = Result.Fail<string>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid" });
        var successCalls = new Trellis.Testing.CallbackRecorder<string>();
        var failureCalls = new Trellis.Testing.CallbackRecorder<Error>();

        // Act
        var output = await result.MatchAsync(
            onSuccess: successCalls.Wrap(value => ValueTask.FromResult(value.Length)),
            onFailure: failureCalls.Wrap(err => ValueTask.FromResult(-1))
        );

        // Assert
        output.Should().Be(-1);
        successCalls.Should().NotHaveBeenCalled();
        failureCalls.Should().HaveBeenCalledAtLeastOnce();
    }

    #endregion

    #region MatchAsync ValueTask Both-Async (ValueTask<Result<TIn>> + ValueTask handlers)

    [Fact]
    public async Task MatchAsync_ValueTask_Both_Success_CallsOnSuccess()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok(42));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: value => ValueTask.FromResult($"Value: {value}"),
            onFailure: err => ValueTask.FromResult($"Error: {err.Detail}")
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Both_Failure_CallsOnFailure()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: value => ValueTask.FromResult($"Value: {value}"),
            onFailure: err => ValueTask.FromResult($"Error: {err.Detail}")
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Both_DifferentReturnType()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok("hello world"));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: value => ValueTask.FromResult(value.Split(' ').Length),
            onFailure: _ => ValueTask.FromResult(0)
        );

        // Assert
        output.Should().Be(2);
    }

    #endregion

    #region SwitchAsync ValueTask Both-Async

    [Fact]
    public async Task SwitchAsync_ValueTask_Both_Success_CallsOnSuccess()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok(42));
        var output = "";

        // Act
        await resultTask.SwitchAsync(
            onSuccess: value =>
            {
                output = $"Value: {value}";
                return ValueTask.CompletedTask;
            },
            onFailure: err =>
            {
                output = $"Error: {err.Detail}";
                return ValueTask.CompletedTask;
            }
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task SwitchAsync_ValueTask_Both_Failure_CallsOnFailure()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));
        var output = "";

        // Act
        await resultTask.SwitchAsync(
            onSuccess: value =>
            {
                output = $"Value: {value}";
                return ValueTask.CompletedTask;
            },
            onFailure: err =>
            {
                output = $"Error: {err.Detail}";
                return ValueTask.CompletedTask;
            }
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task SwitchAsync_ValueTask_Both_Success_OnlySuccessHandlerCalled()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok("test"));
        var successCalls = new Trellis.Testing.CallbackRecorder<string>();
        var failureCalls = new Trellis.Testing.CallbackRecorder<Error>();

        // Act
        await resultTask.SwitchAsync(
            onSuccess: successCalls.Wrap(_ => ValueTask.CompletedTask),
            onFailure: failureCalls.Wrap(_ => ValueTask.CompletedTask)
        );

        // Assert
        successCalls.Should().HaveBeenCalledAtLeastOnce();
        failureCalls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public async Task SwitchAsync_ValueTask_Both_Failure_OnlyFailureHandlerCalled()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<string>(new Error.Unexpected("test") { Detail = "Boom" }));
        var successCalls = new Trellis.Testing.CallbackRecorder<string>();
        var failureCalls = new Trellis.Testing.CallbackRecorder<Error>();

        // Act
        await resultTask.SwitchAsync(
            onSuccess: successCalls.Wrap(_ => ValueTask.CompletedTask),
            onFailure: failureCalls.Wrap(_ => ValueTask.CompletedTask)
        );

        // Assert
        successCalls.Should().NotHaveBeenCalled();
        failureCalls.Should().HaveBeenCalledAtLeastOnce();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task MatchAsync_ValueTask_Right_WithComplexType()
    {
        // Arrange
        var record = new TestData("Alice", 30);
        var result = Result.Ok(record);

        // Act
        var output = await result.MatchAsync(
            onSuccess: data => ValueTask.FromResult($"{data.Name} is {data.Age}"),
            onFailure: err => ValueTask.FromResult($"Error: {err.Detail}")
        );

        // Assert
        output.Should().Be("Alice is 30");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Both_ChainedAfterPipeline()
    {
        // Arrange & Act — Match at the end of a ValueTask pipeline
        var output = await ValueTask.FromResult(Result.Ok(10))
            .BindAsync(v => Result.Ok(v * 2))
            .MatchAsync(
                onSuccess: v => ValueTask.FromResult($"Result: {v}"),
                onFailure: err => ValueTask.FromResult($"Error: {err.Detail}")
            );

        // Assert
        output.Should().Be("Result: 20");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_Both_ChainedAfterFailedPipeline()
    {
        // Arrange & Act
        var output = await ValueTask.FromResult(Result.Fail<int>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid input" }))
            .BindAsync(v => Result.Ok(v * 2))
            .MatchAsync(
                onSuccess: v => ValueTask.FromResult($"Result: {v}"),
                onFailure: err => ValueTask.FromResult($"Error: {err.Detail}")
            );

        // Assert
        output.Should().Be("Error: Invalid input");
    }

    #endregion

    private record TestData(string Name, int Age);
}