namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="DiscardExtensions"/>, <see cref="DiscardTaskExtensions"/>,
/// and <see cref="DiscardValueTaskExtensions"/>.
/// Verifies that Discard/DiscardAsync can be called on success and failure results
/// without throwing and serves as an explicit intent to ignore the outcome.
/// </summary>
public class DiscardTests
{
    private static readonly Error TestError = new Error.InternalServerError("test") { Detail = "test error" };

    #region Discard (sync)

    [Fact]
    public void Discard_SuccessResult_DoesNotThrow()
    {
        var result = Result.Ok(42);

        var act = () => result.Discard();

        act.Should().NotThrow();
    }

    [Fact]
    public void Discard_FailureResult_DoesNotThrow()
    {
        var result = Result.Fail<int>(TestError);

        var act = () => result.Discard();

        act.Should().NotThrow();
    }

    [Fact]
    public void Discard_ReturnsVoid()
    {
        var result = Result.Ok("hello");

        // Discard should be usable as a statement (void return)
        result.Discard();
    }

    #endregion

    #region DiscardAsync (Task)

    [Fact]
    public async Task DiscardAsync_Task_SuccessResult_DoesNotThrow()
    {
        var resultTask = Task.FromResult(Result.Ok(42));

        var act = () => resultTask.DiscardAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DiscardAsync_Task_FailureResult_DoesNotThrow()
    {
        var resultTask = Task.FromResult(Result.Fail<int>(TestError));

        var act = () => resultTask.DiscardAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DiscardAsync_Task_NullTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        var act = () => resultTask.DiscardAsync();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region DiscardAsync (ValueTask)

    [Fact]
    public async Task DiscardAsync_ValueTask_SuccessResult_DoesNotThrow()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var act = () => resultTask.DiscardAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DiscardAsync_ValueTask_FailureResult_DoesNotThrow()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Fail<int>(TestError));

        var act = () => resultTask.DiscardAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Integration — chained pipeline ending with Discard

    [Fact]
    public void Discard_AtEndOfChain_CompilesSilently() =>
        // This pattern should not trigger TRLS001 because Discard returns void
        Result.Ok(42)
            .Map(x => x * 2)
            .Tap(x => _ = x)
            .Discard();

    [Fact]
    public async Task DiscardAsync_AtEndOfAsyncChain_CompilesSilently() =>
        await Task.FromResult(Result.Ok(42))
            .MapAsync(x => x * 2)
            .TapAsync(x => _ = x)
            .DiscardAsync();

    #endregion
}