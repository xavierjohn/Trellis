namespace Trellis.Testing.Tests.Assertions;

public class ResultAssertionsAsyncTests
{
    [Fact]
    public async Task BeSuccessAsync_Should_Pass_When_Success()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));

        // Act & Assert
        var constraint = await resultTask.BeSuccessAsync();
        constraint.Which.Should().Be(42);
    }

    [Fact]
    public async Task BeSuccessAsync_Should_Fail_When_Failure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var act = async () => await resultTask.BeSuccessAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BeFailureAsync_Should_Pass_When_Failure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureAsync();
        constraint.Which.Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public async Task BeFailureAsync_Should_Fail_When_Success()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));

        // Act
        var act = async () => await resultTask.BeFailureAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BeFailureOfTypeAsync_Should_Pass_When_Type_Matches()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureOfTypeAsync<int, Error.NotFound>();
        constraint.Which.Detail.Should().Be("Not found");
    }

    [Fact]
    public async Task BeFailureOfTypeAsync_Should_Fail_When_Type_Different()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var act = async () => await resultTask.BeFailureOfTypeAsync<int, Error.UnprocessableContent>();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    #region ValueTask Tests

    [Fact]
    public async Task BeSuccessAsync_ValueTask_Should_Pass_When_Success()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok(42));

        // Act & Assert
        var constraint = await resultTask.BeSuccessAsync();
        constraint.Which.Should().Be(42);
    }

    [Fact]
    public async Task BeSuccessAsync_ValueTask_Should_Fail_When_Failure()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var act = async () => await resultTask.BeSuccessAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BeFailureAsync_ValueTask_Should_Pass_When_Failure()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureAsync();
        constraint.Which.Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public async Task BeFailureOfTypeAsync_ValueTask_Should_Pass_When_Type_Matches()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureOfTypeAsync<int, Error.NotFound>();
        constraint.Which.Detail.Should().Be("Not found");
    }

    #endregion
}