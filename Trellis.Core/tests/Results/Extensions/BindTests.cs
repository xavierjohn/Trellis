namespace Trellis.Core.Tests.Results.Extensions.Bind;

using Trellis.Testing;

public partial class BindTests : TestBase
{

    [Fact]
    public void Bind_WithNullFunc_ShouldThrowArgumentNullException()
    {
        var result = Result.Ok(42);

        Action act = () => result.Bind<int, string>(null!);

        act.ShouldRejectNull("func");
    }

    [Fact]
    public void Bind_WithSuccessResult_ShouldReturnNewResult()
    {
        // Arrange
        var result = Result.Ok<int>(42);
        var functionCalls = new CallbackRecorder<int>();

        // Act
        var newResult = result.Bind(functionCalls.Wrap(value => Result.Ok<string>($"Value: {value}")));

        // Assert
        functionCalls.Should().HaveBeenCalledAtLeastOnce();
        newResult.Should().BeSuccess()
            .Which.Should().Be("Value: 42");
    }

    [Fact]
    public void Bind_WithFailureResult_ShouldReturnFailedResult()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);
        var functionCalls = new CallbackRecorder<int>();

        // Act
        var newResult = result.Bind(functionCalls.Wrap(value => Result.Ok<string>($"Value: {value}")));

        // Assert
        functionCalls.Should().NotHaveBeenCalled();
        newResult.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public async Task BindAsync_Left_Task_ShouldReturnResult()
    {
        // Arrange
        var result = Result.Ok("Hello").AsTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}")));

        // Assert
        functionCalls.Should().HaveBeenCalledAtLeastOnce();
        actual.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public async Task BindAsync_Left_Task_ShouldReturnFailedResult()
    {
        // Arrange
        var result = Result.Fail<string>(Error1).AsTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}")));

        // Assert
        functionCalls.Should().NotHaveBeenCalled();
        actual.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public async Task BindAsync_Right_Task_ShouldReturnResult()
    {
        // Arrange
        var result = Result.Ok("Hello");
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsTask()));

        // Assert
        functionCalls.Should().HaveBeenCalledAtLeastOnce();
        actual.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public async Task BindAsync_Right_Task_ShouldReturnFailedResult()
    {
        // Arrange
        var result = Result.Fail<string>(Error1);
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsTask()));

        // Assert
        functionCalls.Should().NotHaveBeenCalled();
        actual.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public async Task BindAsync_Right_Task_WithNullFunc_ShouldThrowArgumentNullException()
    {
        var result = Result.Ok("Hello");

        var act = async () => await result.BindAsync((Func<string, Task<Result<string>>>)null!);

        await act.ShouldRejectNullAsync("func");
    }

    [Fact]
    public async Task BindAsync_Both_Task_ShouldReturnResult()
    {
        // Arrange
        var result = Result.Ok("Hello").AsTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsTask()));

        // Assert
        functionCalls.Should().HaveBeenCalledAtLeastOnce();
        actual.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public async Task BindAsync_Both_Task_ShouldReturnFailedResult()
    {
        // Arrange
        var result = Result.Fail<string>(Error1).AsTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsTask()));

        // Assert
        functionCalls.Should().NotHaveBeenCalled();
        actual.Should().BeFailure().Which.Should().Be(Error1);
    }

    // Bind ValueTask
    [Fact]
    public async Task BindAsync_Left_ValueTask_ShouldReturnResult()
    {
        // Arrange
        var result = Result.Ok("Hello").AsValueTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}")));

        // Assert
        functionCalls.Should().HaveBeenCalledAtLeastOnce();
        actual.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public async Task BindAsync_Left_ValueTask_ShouldReturnFailedResult()
    {
        // Arrange
        var result = Result.Fail<string>(Error1).AsValueTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}")));

        // Assert
        functionCalls.Should().NotHaveBeenCalled();
        actual.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public async Task BindAsync_Right_ValueTask_ShouldReturnResult()
    {
        // Arrange
        var result = Result.Ok("Hello");
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsValueTask()));

        // Assert
        functionCalls.Should().HaveBeenCalledAtLeastOnce();
        actual.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public async Task BindAsync_Right_ValueTask_ShouldReturnFailedResult()
    {
        // Arrange
        var result = Result.Fail<string>(Error1);
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsValueTask()));

        // Assert
        functionCalls.Should().NotHaveBeenCalled();
        actual.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public async Task BindAsync_Both_ValueTask_ShouldReturnResult()
    {
        // Arrange
        var result = Result.Ok("Hello").AsValueTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsValueTask()));

        // Assert
        functionCalls.Should().HaveBeenCalledAtLeastOnce();
        actual.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public async Task BindAsync_Both_ValueTask_ShouldReturnFailedResult()
    {
        // Arrange
        var result = Result.Fail<string>(Error1).AsValueTask();
        var functionCalls = new CallbackRecorder<string>();

        // Act
        var actual = await result.BindAsync(functionCalls.Wrap(str => Result.Ok($"{str}").AsValueTask()));

        // Assert
        functionCalls.Should().NotHaveBeenCalled();
        actual.Should().BeFailure().Which.Should().Be(Error1);
    }

}