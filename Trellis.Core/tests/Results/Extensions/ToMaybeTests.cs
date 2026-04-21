namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for ToMaybe extension methods that convert Result{T} to Maybe{T}.
/// </summary>
public class ToMaybeTests
{
    #region Sync

    [Fact]
    public void ToMaybe_WhenResultIsSuccess_ShouldReturnSomeWithValue()
    {
        var sut = Result.Ok("Hello");

        var maybe = sut.ToMaybe();

        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be("Hello");
    }

    [Fact]
    public void ToMaybe_WhenResultIsFailure_ShouldReturnNone()
    {
        var sut = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "some error" });

        var maybe = sut.ToMaybe();

        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void ToMaybe_WhenResultIsSuccessWithComplexType_ShouldReturnSomeWithValue()
    {
        var value = new TestRecord("test", 42);
        var sut = Result.Ok(value);

        var maybe = sut.ToMaybe();

        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be(value);
    }

    #endregion

    #region Task

    [Fact]
    public async Task ToMaybeAsync_Task_WhenResultIsSuccess_ShouldReturnSomeWithValue()
    {
        var sut = Task.FromResult(Result.Ok("Hello"));

        var maybe = await sut.ToMaybeAsync();

        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be("Hello");
    }

    [Fact]
    public async Task ToMaybeAsync_Task_WhenResultIsFailure_ShouldReturnNone()
    {
        var sut = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "error" }));

        var maybe = await sut.ToMaybeAsync();

        maybe.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task ToMaybeAsync_Task_WithNullTask_ShouldThrowArgumentNullException()
    {
        Task<Result<string>> sut = null!;

        var act = async () => await sut.ToMaybeAsync();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region ValueTask

    [Fact]
    public async Task ToMaybeAsync_ValueTask_WhenResultIsSuccess_ShouldReturnSomeWithValue()
    {
        var sut = new ValueTask<Result<string>>(Result.Ok("Hello"));

        var maybe = await sut.ToMaybeAsync();

        maybe.HasValue.Should().BeTrue();
        maybe.Value.Should().Be("Hello");
    }

    [Fact]
    public async Task ToMaybeAsync_ValueTask_WhenResultIsFailure_ShouldReturnNone()
    {
        var sut = new ValueTask<Result<string>>(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "error" }));

        var maybe = await sut.ToMaybeAsync();

        maybe.HasNoValue.Should().BeTrue();
    }

    #endregion

    private sealed record TestRecord(string Name, int Value);
}