namespace Trellis.Asp.Tests;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="IfNoneMatchExtensions"/> — create-if-absent pattern helpers.
/// </summary>
public class IfNoneMatchExtensionsTests
{
    [Fact]
    public void EnforceIfNoneMatchPrecondition_NullETags_ReturnsOriginalResult()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition(null);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_WildcardOnFailure_PreservesOriginalError()
    {
        var result = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" });

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.NotFound>();
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_NonWildcardTags_ReturnsOriginalResult()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Strong("abc123"), EntityTagValue.Strong("def456")]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_EmptyArray_ReturnsOriginalResult()
    {
        var result = Result.Ok("value");

        var actual = result.EnforceIfNoneMatchPrecondition([]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public async Task EnforceIfNoneMatchPreconditionAsync_Task_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = Task.FromResult(Result.Ok("value"));

        var actual = await resultTask.EnforceIfNoneMatchPreconditionAsync([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
    }

    [Fact]
    public async Task EnforceIfNoneMatchPreconditionAsync_ValueTask_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = new ValueTask<Result<string>>(Result.Ok("value"));

        var actual = await resultTask.EnforceIfNoneMatchPreconditionAsync([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<Error.PreconditionFailed>();
    }
}