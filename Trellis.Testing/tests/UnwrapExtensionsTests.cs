namespace Trellis.Testing.Tests;

/// <summary>
/// Tests for <see cref="UnwrapExtensions"/> — Result and Maybe unwrap helpers for test code.
/// </summary>
public class UnwrapExtensionsTests
{
    private static readonly Error TestError = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("TestField"), "validation.error") { Detail = "Test validation error" }));

    #region Result<T>.Unwrap()

    [Fact]
    public void Unwrap_SuccessResult_ReturnsValue()
    {
        var result = Result.Ok(42);

        var value = result.Unwrap();

        value.Should().Be(42);
    }

    [Fact]
    public void Unwrap_SuccessResult_StringValue_ReturnsValue()
    {
        var result = Result.Ok("hello");

        var value = result.Unwrap();

        value.Should().Be("hello");
    }

    [Fact]
    public void Unwrap_FailureResult_ThrowsUnwrapFailedException()
    {
        var result = Result.Fail<int>(TestError);

        var act = () => result.Unwrap();

        act.Should().Throw<UnwrapFailedException>()
            .WithMessage("*Result<Int32>*")
            .WithMessage("*Test validation error*");
    }

    [Fact]
    public void Unwrap_FailureResult_ExceptionContainsErrorCode()
    {
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Item not found." };
        var result = Result.Fail<string>(error);

        var act = () => result.Unwrap();

        act.Should().Throw<UnwrapFailedException>()
            .WithMessage("*not-found*");
    }

    #endregion

    #region Maybe<T>.Unwrap()

    [Fact]
    public void Unwrap_MaybeWithValue_ReturnsValue()
    {
        var maybe = Maybe<int>.From(42);

        var value = maybe.Unwrap();

        value.Should().Be(42);
    }

    [Fact]
    public void Unwrap_MaybeNone_ThrowsUnwrapFailedException()
    {
        var maybe = Maybe<int>.None;

        var act = () => maybe.Unwrap();

        act.Should().Throw<UnwrapFailedException>()
            .WithMessage("*Maybe<Int32>*");
    }

    #endregion

    #region UnwrapAsync (Task)

    [Fact]
    public async Task UnwrapAsync_Task_SuccessResult_ReturnsValue()
    {
        var resultTask = Task.FromResult(Result.Ok(42));

        var value = await resultTask.UnwrapAsync();

        value.Should().Be(42);
    }

    [Fact]
    public async Task UnwrapAsync_Task_FailureResult_ThrowsUnwrapFailedException()
    {
        var resultTask = Task.FromResult(Result.Fail<int>(TestError));

        var act = () => resultTask.UnwrapAsync();

        await act.Should().ThrowAsync<UnwrapFailedException>();
    }

    [Fact]
    public async Task UnwrapAsync_Task_NullTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        var act = () => resultTask.UnwrapAsync();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region UnwrapAsync (ValueTask)

    [Fact]
    public async Task UnwrapAsync_ValueTask_SuccessResult_ReturnsValue()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Ok(42));

        var value = await resultTask.UnwrapAsync();

        value.Should().Be(42);
    }

    [Fact]
    public async Task UnwrapAsync_ValueTask_FailureResult_ThrowsUnwrapFailedException()
    {
        var resultTask = new ValueTask<Result<int>>(Result.Fail<int>(TestError));

        var act = async () => await resultTask.UnwrapAsync();

        await act.Should().ThrowAsync<UnwrapFailedException>();
    }

    #endregion

    #region Integration — typical test pattern

    [Fact]
    public void Unwrap_AfterFluentAssertionGuard_WorksCleanly()
    {
        var result = Result.Ok(42);

        // Typical test pattern: assert success, then extract
        result.Should().BeSuccess();
        var value = result.Unwrap();

        value.Should().Be(42);
    }

    #endregion
}