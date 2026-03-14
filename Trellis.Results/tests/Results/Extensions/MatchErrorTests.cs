namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class MatchErrorTests : TestBase
{
    [Fact]
    public void MatchError_WithNullOnSuccess_ThrowsArgumentNullException()
    {
        var result = Result.Success(42);

        var act = () => result.MatchError<int, string>((Func<int, string>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "onSuccess");
    }

    [Fact]
    public async Task MatchErrorAsync_TaskResult_WithNullResultTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        Func<Task<string>> act = () => resultTask.MatchErrorAsync(
            onSuccess: _ => "success");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }
}