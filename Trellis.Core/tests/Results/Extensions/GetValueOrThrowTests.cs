namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="GetValueOrThrowExtensions"/> — the production-safe throwing extractor for
/// trust-boundary crossings where a failed result represents a broken invariant the caller
/// cannot act on (typically persistence DTO → entity rehydration). Mirrors
/// <see cref="Maybe{T}.GetValueOrThrow(string?)"/> for ergonomic symmetry between the two ROP types.
/// </summary>
public class GetValueOrThrowTests
{
    #region Sync — Result<T>.GetValueOrThrow

    [Fact]
    public void GetValueOrThrow_Success_returns_value()
    {
        var sut = Result.Ok(42);

        var value = sut.GetValueOrThrow();

        value.Should().Be(42);
    }

    [Fact]
    public void GetValueOrThrow_Success_with_custom_message_returns_value_and_does_not_throw()
    {
        var sut = Result.Ok("hello");

        var value = sut.GetValueOrThrow("custom message that should not appear");

        value.Should().Be("hello");
    }

    [Fact]
    public void GetValueOrThrow_Success_with_null_reference_value_returns_null()
    {
        // Result.Ok<string?>(null!) is unusual but legal: the contract is "return what's there".
        var sut = Result.Ok<string?>(null);

        var value = sut.GetValueOrThrow();

        value.Should().BeNull();
    }

    [Fact]
    public void GetValueOrThrow_Failure_throws_InvalidOperationException()
    {
        var sut = Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42")));

        var act = () => sut.GetValueOrThrow();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetValueOrThrow_Failure_default_message_includes_TValue_name_and_Error_code()
    {
        var sut = Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42")));

        var act = () => sut.GetValueOrThrow();

        // Default message format: "Result<{TValue}> was a failure. Error: [{Code}] {DisplayMessage}"
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Int32*not-found*");
    }

    [Fact]
    public void GetValueOrThrow_Failure_with_custom_errorMessage_throws_with_that_exact_message()
    {
        var sut = Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42")));

        var act = () => sut.GetValueOrThrow("Reconstructing User 42 from DB row");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Reconstructing User 42 from DB row");
    }

    [Fact]
    public void GetValueOrThrow_default_Result_throws()
    {
        // default(Result<T>) is a typed failure carrying the shared "default_initialized" sentinel
        // (TRLS019). Verify GetValueOrThrow surfaces it rather than returning default(TValue).
        Result<string> sut = default;

        var act = () => sut.GetValueOrThrow();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default_initialized*");
    }

    [Fact]
    public void GetValueOrThrow_Failure_with_Detail_renders_Detail_in_default_message()
    {
        var sut = Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42"))
            { Detail = "User 42 has been purged." });

        var act = () => sut.GetValueOrThrow();

        // GetDisplayMessage() prefers Detail over Code template when Detail is set.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*User 42 has been purged.*");
    }

    #endregion

    #region Async — Task<Result<T>>.GetValueOrThrowAsync

    [Fact]
    public async Task GetValueOrThrowAsync_Task_Success_returns_value()
    {
        var sut = Task.FromResult(Result.Ok(42));

        var value = await sut.GetValueOrThrowAsync();

        value.Should().Be(42);
    }

    [Fact]
    public async Task GetValueOrThrowAsync_Task_Failure_throws_InvalidOperationException()
    {
        var sut = Task.FromResult(Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42"))));

        var act = () => sut.GetValueOrThrowAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetValueOrThrowAsync_Task_Failure_with_custom_message_uses_it_verbatim()
    {
        var sut = Task.FromResult(Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42"))));

        var act = () => sut.GetValueOrThrowAsync("Reconstructing User 42 from DB row");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("Reconstructing User 42 from DB row");
    }

    [Fact]
    public async Task GetValueOrThrowAsync_Task_null_resultTask_throws_ArgumentNullException()
    {
        Task<Result<int>>? sut = null;

        var act = () => sut!.GetValueOrThrowAsync();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region Async — ValueTask<Result<T>>.GetValueOrThrowAsync

    [Fact]
    public async Task GetValueOrThrowAsync_ValueTask_Success_returns_value()
    {
        var sut = ValueTask.FromResult(Result.Ok(42));

        var value = await sut.GetValueOrThrowAsync();

        value.Should().Be(42);
    }

    [Fact]
    public async Task GetValueOrThrowAsync_ValueTask_Failure_throws_InvalidOperationException()
    {
        var sut = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42"))));

        var act = async () => await sut.GetValueOrThrowAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetValueOrThrowAsync_ValueTask_Failure_with_custom_message_uses_it_verbatim()
    {
        var sut = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(ResourceRef.For("User", "42"))));

        var act = async () => await sut.GetValueOrThrowAsync("Reconstructing User 42");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("Reconstructing User 42");
    }

    #endregion
}
