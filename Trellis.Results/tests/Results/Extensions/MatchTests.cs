namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class MatchTests : TestBase
{
    [Fact]
    public void Match_WithNullOnSuccess_ThrowsArgumentNullException()
    {
        var result = Result.Success(42);

        var act = () => result.Match<int, string>((Func<int, string>)null!, _ => "error");

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "onSuccess");
    }

    [Fact]
    public async Task MatchAsync_TaskResult_WithNullResultTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        Func<Task<string>> act = () => resultTask.MatchAsync(
            onSuccess: _ => "success",
            onFailure: _ => "error");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }
}