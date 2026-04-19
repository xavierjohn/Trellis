namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for the non-generic Result overloads added in PR5: Combine(Result, Result),
/// TraverseAsync(IEnumerable, Func&lt;TIn, CancellationToken, Task&lt;Result&gt;&gt;), and
/// RecoverOnFailureAsync(Task&lt;Result&gt;, ...). Each overload is exercised across the
/// success path, failure path, and (where applicable) cancellation/short-circuit semantics.
/// </summary>
public class NonGenericResultTests : TestBase
{
    #region Combine(Result, Result)

    [Fact]
    public void Combine_NonGeneric_BothSuccess_ReturnsSuccess()
    {
        var combined = Result.Ok().Combine(Result.Ok());

        combined.Should().BeSuccess();
    }

    [Fact]
    public void Combine_NonGeneric_FirstFailure_ReturnsFirstFailure()
    {
        var combined = Result.Fail(Error1).Combine(Result.Ok());

        combined.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void Combine_NonGeneric_SecondFailure_ReturnsSecondFailure()
    {
        var combined = Result.Ok().Combine(Result.Fail(Error1));

        combined.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void Combine_NonGeneric_BothFailure_AggregatesValidationFieldErrors()
    {
        var first = Result.Fail(Error.Validation("first failed", "f1"));
        var second = Result.Fail(Error.Validation("second failed", "f2"));

        var combined = first.Combine(second);

        var validation = combined.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
        validation.FieldErrors.Select(fe => fe.FieldName).Should().BeEquivalentTo("f1", "f2");
    }

    #endregion

    #region TraverseAsync (non-generic Result selector)

    [Fact]
    public async Task TraverseAsync_NonGeneric_AllSucceed_ReturnsSuccess()
    {
        var items = new[] { 1, 2, 3 };
        var visited = new List<int>();
        var ct = TestContext.Current.CancellationToken;

        var result = await items.TraverseAsync((x, _) =>
        {
            visited.Add(x);
            return Task.FromResult(Result.Ok());
        }, ct);

        result.Should().BeSuccess();
        visited.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task TraverseAsync_NonGeneric_FirstFailure_ShortCircuitsAndReturnsThatError()
    {
        var items = new[] { 1, 2, 3, 4 };
        var visited = new List<int>();
        var ct = TestContext.Current.CancellationToken;

        var result = await items.TraverseAsync((x, _) =>
        {
            visited.Add(x);
            return Task.FromResult(x == 2 ? Result.Fail(Error1) : Result.Ok());
        }, ct);

        result.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
        visited.Should().Equal(1, 2);
    }

    [Fact]
    public async Task TraverseAsync_NonGeneric_Cancellation_StopsIterationAndThrows()
    {
        var items = new[] { 1, 2, 3 };
        var visited = new List<int>();
        using var cts = new CancellationTokenSource();

#pragma warning disable xUnit1051 // Test deliberately uses its own CTS to trigger cancellation mid-iteration.
        Func<Task> act = async () => await items.TraverseAsync((x, ct) =>
        {
            visited.Add(x);
            if (x == 2) cts.Cancel();
            return Task.FromResult(Result.Ok());
        }, cts.Token);
#pragma warning restore xUnit1051

        await act.Should().ThrowAsync<OperationCanceledException>();
        visited.Should().Equal(1, 2);
    }

    [Fact]
    public async Task TraverseAsync_NonGeneric_EmptySource_ReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Array.Empty<int>().TraverseAsync((_, _) => Task.FromResult(Result.Ok()), ct);

        result.Should().BeSuccess();
    }

    #endregion

    #region RecoverOnFailureAsync(Task<Result>, ...)

    [Fact]
    public async Task RecoverOnFailureAsync_NonGeneric_Success_RecoveryNotInvoked()
    {
        var invoked = false;
        Task<Result> input = Task.FromResult(Result.Ok());

        var output = await input.RecoverOnFailureAsync(_ => true, () =>
        {
            invoked = true;
            return Task.FromResult(Result.Ok());
        });

        invoked.Should().BeFalse();
        output.Should().BeSuccess();
    }

    [Fact]
    public async Task RecoverOnFailureAsync_NonGeneric_FailureMatchesPredicate_RecoveryInvoked()
    {
        var invoked = false;
        Task<Result> input = Task.FromResult(Result.Fail(Error1));

        var output = await input.RecoverOnFailureAsync(err => err.Code == Error1.Code, () =>
        {
            invoked = true;
            return Task.FromResult(Result.Ok());
        });

        invoked.Should().BeTrue();
        output.Should().BeSuccess();
    }

    [Fact]
    public async Task RecoverOnFailureAsync_NonGeneric_FailurePredicateNotMatched_OriginalFailureReturned()
    {
        var invoked = false;
        Task<Result> input = Task.FromResult(Result.Fail(Error1));

        var output = await input.RecoverOnFailureAsync(err => err.Code == "Other", () =>
        {
            invoked = true;
            return Task.FromResult(Result.Ok());
        });

        invoked.Should().BeFalse();
        output.Should().BeFailure().Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public async Task RecoverOnFailureAsync_NonGeneric_NullResultTask_ThrowsArgumentNullException()
    {
        Task<Result> input = null!;

        Func<Task<Result>> act = () => input.RecoverOnFailureAsync(_ => true, () => Task.FromResult(Result.Ok()));

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(e => e.ParamName == "resultTask");
    }

    #endregion
}
