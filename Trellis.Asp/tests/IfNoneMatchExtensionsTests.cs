namespace Trellis.Asp.Tests;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="IfNoneMatchExtensions"/> — create-if-absent pattern helpers.
/// </summary>
public class IfNoneMatchExtensionsTests
{
    [Fact]
    public void CheckIfNoneMatchForCreate_NullETags_ReturnsOriginalResult()
    {
        var result = Result.Success("value");

        var actual = result.CheckIfNoneMatchForCreate(null);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void CheckIfNoneMatchForCreate_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var result = Result.Success("value");

        var actual = result.CheckIfNoneMatchForCreate(["*"]);

        actual.Should().BeFailureOfType<PreconditionFailedError>();
    }

    [Fact]
    public void CheckIfNoneMatchForCreate_WildcardOnFailure_PreservesOriginalError()
    {
        var result = Result.Failure<string>(Error.NotFound("not found"));

        var actual = result.CheckIfNoneMatchForCreate(["*"]);

        actual.Should().BeFailureOfType<NotFoundError>();
    }

    [Fact]
    public void CheckIfNoneMatchForCreate_NonWildcardTags_ReturnsOriginalResult()
    {
        var result = Result.Success("value");

        var actual = result.CheckIfNoneMatchForCreate(["abc123", "def456"]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void CheckIfNoneMatchForCreate_EmptyArray_ReturnsOriginalResult()
    {
        var result = Result.Success("value");

        var actual = result.CheckIfNoneMatchForCreate([]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public async Task CheckIfNoneMatchForCreateAsync_Task_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = Task.FromResult(Result.Success("value"));

        var actual = await resultTask.CheckIfNoneMatchForCreateAsync(["*"]);

        actual.Should().BeFailureOfType<PreconditionFailedError>();
    }

    [Fact]
    public async Task CheckIfNoneMatchForCreateAsync_ValueTask_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = new ValueTask<Result<string>>(Result.Success("value"));

        var actual = await resultTask.CheckIfNoneMatchForCreateAsync(["*"]);

        actual.Should().BeFailureOfType<PreconditionFailedError>();
    }
}
