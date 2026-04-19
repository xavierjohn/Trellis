namespace Trellis.Results.Tests;

public class MatchTests
{
    #region Match Tests

    [Fact]
    public void Match_WithSuccess_CallsOnSuccess()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var output = result.Match(
            onSuccess: value => $"Value: {value}",
            onFailure: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public void Match_WithFailure_CallsOnFailure()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act
        var output = result.Match(
            onSuccess: value => $"Value: {value}",
            onFailure: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public void Match_WithSuccess_ReturnsTransformedValue()
    {
        // Arrange
        var result = Result.Ok("hello");

        // Act
        var output = result.Match(
            onSuccess: s => s.Length,
            onFailure: _ => -1
        );

        // Assert
        output.Should().Be(5);
    }

    [Fact]
    public void Match_WithFailure_ReturnsDefaultValue()
    {
        // Arrange
        var result = Result.Fail<string>(Error.Validation("Invalid"));

        // Act
        var output = result.Match(
            onSuccess: s => s.Length,
            onFailure: _ => -1
        );

        // Assert
        output.Should().Be(-1);
    }

    #endregion

    #region Switch Tests

    [Fact]
    public void Switch_WithSuccess_CallsOnSuccess()
    {
        // Arrange
        var result = Result.Ok(42);
        var output = "";

        // Act
        result.Switch(
            onSuccess: value => output = $"Value: {value}",
            onFailure: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public void Switch_WithFailure_CallsOnFailure()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));
        var output = "";

        // Act
        result.Switch(
            onSuccess: value => output = $"Value: {value}",
            onFailure: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    #endregion

    #region MatchAsync Tests

    [Fact]
    public async Task MatchAsync_WithTaskResult_Success_CallsOnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: value => $"Value: {value}",
            onFailure: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task MatchAsync_WithTaskResult_Failure_CallsOnFailure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.NotFound("Not found")));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: value => $"Value: {value}",
            onFailure: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task MatchAsync_WithAsyncHandlers_Success_CallsOnSuccess()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        Func<int, Task<string>> onSuccess = async value =>
        {
            await Task.Delay(1);
            return $"Value: {value}";
        };
        Func<Error, Task<string>> onFailure = async err =>
        {
            await Task.Delay(1);
            return $"Error: {err.Detail}";
        };
        var output = await result.MatchAsync(onSuccess, onFailure);

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task MatchAsync_WithAsyncHandlers_Failure_CallsOnFailure()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act
        Func<int, Task<string>> onSuccess = async value =>
        {
            await Task.Delay(1);
            return $"Value: {value}";
        };
        Func<Error, Task<string>> onFailure = async err =>
        {
            await Task.Delay(1);
            return $"Error: {err.Detail}";
        };
        var output = await result.MatchAsync(onSuccess, onFailure);

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task MatchAsync_WithCancellationToken_Success_CallsOnSuccess()
    {
        // Arrange
        var result = Result.Ok(42);
        using var cts = new CancellationTokenSource();

        // Act
        var output = await result.MatchAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Value: {value}";
            },
            onFailure: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: cts.Token
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task MatchAsync_WithCancellationToken_Failure_CallsOnFailure()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));
        using var cts = new CancellationTokenSource();

        // Act
        var output = await result.MatchAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Value: {value}";
            },
            onFailure: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: cts.Token
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task MatchAsync_TaskWithAsyncHandlersAndCancellationToken_Success()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));
        using var cts = new CancellationTokenSource();

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Value: {value}";
            },
            onFailure: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: cts.Token
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task MatchAsync_TaskWithAsyncHandlers_Success()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));

        // Act
        var output = await resultTask.MatchAsync(
            onSuccess: async value =>
            {
                await Task.Delay(1);
                return $"Value: {value}";
            },
            onFailure: async err =>
            {
                await Task.Delay(1);
                return $"Error: {err.Detail}";
            }
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    #endregion

    #region SwitchAsync Tests

    [Fact]
    public async Task SwitchAsync_WithCancellationToken_Success_CallsOnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));
        var output = "";
        using var cts = new CancellationTokenSource();

        // Act
        await resultTask.SwitchAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Value: {value}";
            },
            onFailure: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: cts.Token
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task SwitchAsync_WithCancellationToken_Failure_CallsOnFailure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.NotFound("Not found")));
        var output = "";
        using var cts = new CancellationTokenSource();

        // Act
        await resultTask.SwitchAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Value: {value}";
            },
            onFailure: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: cts.Token
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public async Task SwitchAsync_WithAsyncHandlers_Success_CallsOnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));
        var output = "";

        // Act
        await resultTask.SwitchAsync(
            onSuccess: async value =>
            {
                await Task.Delay(1);
                output = $"Value: {value}";
            },
            onFailure: async err =>
            {
                await Task.Delay(1);
                output = $"Error: {err.Detail}";
            }
        );

        // Assert
        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task SwitchAsync_WithAsyncHandlers_Failure_CallsOnFailure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.NotFound("Not found")));
        var output = "";

        // Act
        await resultTask.SwitchAsync(
            onSuccess: async value =>
            {
                await Task.Delay(1);
                output = $"Value: {value}";
            },
            onFailure: async err =>
            {
                await Task.Delay(1);
                output = $"Error: {err.Detail}";
            }
        );

        // Assert
        output.Should().Be("Error: Not found");
    }

    #endregion
}