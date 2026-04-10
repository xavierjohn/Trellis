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
        var result = Result.Success("value");

        var actual = result.EnforceIfNoneMatchPrecondition(null);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var result = Result.Success("value");

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<PreconditionFailedError>();
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_WildcardOnFailure_PreservesOriginalError()
    {
        var result = Result.Failure<string>(Error.NotFound("not found"));

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<NotFoundError>();
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_NonWildcardTags_ReturnsOriginalResult()
    {
        var result = Result.Success("value");

        var actual = result.EnforceIfNoneMatchPrecondition([EntityTagValue.Strong("abc123"), EntityTagValue.Strong("def456")]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public void EnforceIfNoneMatchPrecondition_EmptyArray_ReturnsOriginalResult()
    {
        var result = Result.Success("value");

        var actual = result.EnforceIfNoneMatchPrecondition([]);

        actual.Should().BeSuccess();
        actual.Should().HaveValue("value");
    }

    [Fact]
    public async Task EnforceIfNoneMatchPreconditionAsync_Task_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = Task.FromResult(Result.Success("value"));

        var actual = await resultTask.EnforceIfNoneMatchPreconditionAsync([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<PreconditionFailedError>();
    }

    [Fact]
    public async Task EnforceIfNoneMatchPreconditionAsync_ValueTask_WildcardOnSuccess_ReturnsPreconditionFailed()
    {
        var resultTask = new ValueTask<Result<string>>(Result.Success("value"));

        var actual = await resultTask.EnforceIfNoneMatchPreconditionAsync([EntityTagValue.Wildcard()]);

        actual.Should().BeFailureOfType<PreconditionFailedError>();
    }
}