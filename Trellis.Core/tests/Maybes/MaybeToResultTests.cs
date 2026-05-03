namespace Trellis.Core.Tests.Maybes;

public class MaybeToResultTests
{
    [Fact]
    public void ToResult_Func_WhenSomeAndFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybe = Maybe.From(42);

        var act = () => maybe.ToResult((Func<Error>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ferror");
    }

    [Fact]
    public void ToResult_Func_WhenNoneAndFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybe = Maybe<int>.None;

        var act = () => maybe.ToResult((Func<Error>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ferror");
    }

    [Fact]
    public async Task ToResultAsync_Task_WhenFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybeTask = Task.FromResult(Maybe.From(42));

        var act = async () => await maybeTask.ToResultAsync((Func<Error>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WhenFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybeTask = ValueTask.FromResult(Maybe.From(42));

        var act = async () => await maybeTask.ToResultAsync((Func<Error>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
    }
}
