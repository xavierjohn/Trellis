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

    [Fact]
    public void MatchError_Success_InvokesSuccessHandler()
    {
        var result = Result.Success(42);

        var output = result.MatchError(
            onSuccess: value => $"success:{value}",
            onValidation: error => $"validation:{error.Detail}");

        output.Should().Be("success:42");
    }

    [Fact]
    public void MatchError_SpecificHandler_UsesMatchingErrorType()
    {
        var result = Result.Failure<int>(Error.Validation("Invalid input", "value"));

        var output = result.MatchError(
            onSuccess: value => $"success:{value}",
            onValidation: error => $"validation:{error.Detail}",
            onError: error => $"fallback:{error.Code}");

        output.Should().Be("validation:Invalid input");
    }

    [Fact]
    public void MatchError_CatchAll_HandlesUnhandledErrorTypes()
    {
        var result = Result.Failure<int>(Error.NotFound("Missing entity"));

        var output = result.MatchError(
            onSuccess: value => $"success:{value}",
            onValidation: error => $"validation:{error.Detail}",
            onError: error => $"fallback:{error.Code}");

        output.Should().Be($"fallback:{result.Error.Code}");
    }

    [Fact]
    public void MatchError_WithoutMatchingHandlerOrCatchAll_ThrowsInvalidOperationException()
    {
        var result = Result.Failure<int>(Error.NotFound("Missing entity"));

        var act = () => result.MatchError(
            onSuccess: value => $"success:{value}",
            onValidation: error => $"validation:{error.Detail}");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No handler provided for error type NotFoundError*");
    }

    [Fact]
    public void SwitchError_SpecificHandler_InvokesMatchingAction()
    {
        var result = Result.Failure<int>(Error.Conflict("Already exists"));
        var successCalled = false;
        string? outcome = null;

        result.SwitchError(
            onSuccess: _ => successCalled = true,
            onConflict: error => outcome = error.Detail,
            onError: error => outcome = $"fallback:{error.Code}");

        successCalled.Should().BeFalse();
        outcome.Should().Be("Already exists");
    }

    [Fact]
    public async Task MatchErrorAsync_TaskResult_WithCancellationToken_InvokesMatchingHandler()
    {
        var resultTask = Task.FromResult(Result.Failure<int>(Error.ServiceUnavailable("Service down")));
        using var cts = new CancellationTokenSource();

        var output = await resultTask.MatchErrorAsync(
            onSuccess: (value, ct) => Task.FromResult($"success:{value}"),
            onServiceUnavailable: (error, ct) => Task.FromResult(ct == cts.Token ? error.Detail : "wrong-token"),
            onError: (error, ct) => Task.FromResult($"fallback:{error.Code}"),
            cancellationToken: cts.Token);

        output.Should().Be("Service down");
    }

    [Fact]
    public void MatchError_AggregateError_WithOnAggregateHandler_InvokesHandler()
    {
        var errors = new List<Error> { Error.Validation("field error", "field") };
        var result = Result.Failure<int>(new AggregateError(errors));

        var output = result.MatchError(
            onSuccess: value => $"success:{value}",
            onAggregate: ae => $"aggregate:{ae.Errors.Count}",
            onError: error => $"fallback:{error.Code}");

        output.Should().Be("aggregate:1");
    }

    [Fact]
    public void MatchError_AggregateError_WithoutOnAggregateHandler_FallsThroughToOnError()
    {
        var errors = new List<Error> { Error.Validation("field error", "field") };
        var result = Result.Failure<int>(new AggregateError(errors));

        var output = result.MatchError(
            onSuccess: value => $"success:{value}",
            onError: error => $"fallback:{error.Code}");

        output.Should().Be("fallback:aggregate.error");
    }

    [Fact]
    public void MatchError_NonAggregateError_WithOnAggregateProvided_OnAggregateNotCalled()
    {
        var result = Result.Failure<int>(Error.NotFound("Missing entity"));

        var output = result.MatchError(
            onSuccess: value => $"success:{value}",
            onAggregate: ae => $"aggregate:{ae.Errors.Count}",
            onError: error => $"fallback:{error.Code}");

        output.Should().StartWith("fallback:");
    }

    [Fact]
    public void SwitchError_AggregateError_WithOnAggregateHandler_InvokesHandler()
    {
        var errors = new List<Error> { Error.Domain("domain error") };
        var result = Result.Failure<int>(new AggregateError(errors));
        string? outcome = null;

        result.SwitchError(
            onSuccess: _ => { },
            onAggregate: ae => outcome = $"aggregate:{ae.Errors.Count}",
            onError: error => outcome = $"fallback:{error.Code}");

        outcome.Should().Be("aggregate:1");
    }

    [Fact]
    public void SwitchError_AggregateError_WithoutOnAggregateHandler_FallsThroughToOnError()
    {
        var errors = new List<Error> { Error.Domain("domain error") };
        var result = Result.Failure<int>(new AggregateError(errors));
        string? outcome = null;

        result.SwitchError(
            onSuccess: _ => { },
            onError: error => outcome = $"fallback:{error.Code}");

        outcome.Should().Be("fallback:aggregate.error");
    }

    [Fact]
    public void SwitchError_NonAggregateError_WithOnAggregateProvided_OnAggregateNotCalled()
    {
        var result = Result.Failure<int>(Error.Conflict("Already exists"));
        string? outcome = null;

        result.SwitchError(
            onSuccess: _ => { },
            onAggregate: ae => outcome = $"aggregate:{ae.Errors.Count}",
            onError: error => outcome = $"fallback:{error.Code}");

        outcome.Should().StartWith("fallback:");
    }

    [Fact]
    public async Task MatchErrorAsync_TaskResult_AggregateError_WithOnAggregateHandler_InvokesHandler()
    {
        var errors = new List<Error> { Error.Validation("field error", "field") };
        var resultTask = Task.FromResult(Result.Failure<int>(new AggregateError(errors)));

        var output = await resultTask.MatchErrorAsync(
            onSuccess: value => $"success:{value}",
            onAggregate: ae => $"aggregate:{ae.Errors.Count}",
            onError: error => $"fallback:{error.Code}");

        output.Should().Be("aggregate:1");
    }

    [Fact]
    public async Task SwitchErrorAsync_TaskResult_AggregateError_WithOnAggregateHandler_InvokesHandler()
    {
        var errors = new List<Error> { Error.Domain("domain error") };
        var resultTask = Task.FromResult(Result.Failure<int>(new AggregateError(errors)));
        string? outcome = null;

        await resultTask.SwitchErrorAsync(
            onSuccess: (_, ct) => Task.CompletedTask,
            onAggregate: (ae, ct) => { outcome = $"aggregate:{ae.Errors.Count}"; return Task.CompletedTask; },
            onError: (error, ct) => { outcome = $"fallback:{error.Code}"; return Task.CompletedTask; },
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().Be("aggregate:1");
    }
}