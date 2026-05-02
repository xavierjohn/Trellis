using Trellis.Testing;

namespace Trellis.Core.Tests.Results.Extensions.Linq;

// CA2012: query-expression desugaring synthesizes a temporary holding the source ValueTask<Result<T>>
// before invoking Select/SelectMany/Where. The desugared call is exactly one consumption (semantically
// safe), but the analyzer cannot prove that across the synthesized lambdas. Suppressed locally.
#pragma warning disable CA2012
public class AsyncLinqTests : TestBase
{
    private static Task<Result<int>> OkTaskAsync(int value) => Task.FromResult(Result.Ok(value));
    private Task<Result<int>> FailTaskAsync() => Task.FromResult(Result.Fail<int>(Error1));
    private static Result<int> OkSync(int value) => Result.Ok(value);
    private Result<int> FailSync() => Result.Fail<int>(Error1);

    [Fact]
    public async Task SelectMany_TaskTask_AllSuccess_ReturnsCombinedValue()
    {
        var combined = await (
            from a in OkTaskAsync(2)
            from b in OkTaskAsync(3)
            select a + b);

        combined.Should().BeSuccess().Which.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_TaskTask_FirstFailure_ShortCircuitsCollectionSelector()
    {
        var collectionSelectorInvoked = false;

        var combined = await (
            from a in FailTaskAsync()
            from b in InvocationCounting()
            select a + b);

        combined.Should().BeFailure().Which.Should().Be(Error1);
        collectionSelectorInvoked.Should().BeFalse();

        Task<Result<int>> InvocationCounting()
        {
            collectionSelectorInvoked = true;
            return OkTaskAsync(3);
        }
    }

    [Fact]
    public async Task SelectMany_TaskTask_SecondFailure_DoesNotInvokeResultSelector()
    {
        var resultSelectorInvoked = false;

        var combined = await (
            from a in OkTaskAsync(2)
            from b in FailTaskAsync()
            select Combine(a, b));

        combined.Should().BeFailure().Which.Should().Be(Error1);
        resultSelectorInvoked.Should().BeFalse();

        int Combine(int a, int b) { resultSelectorInvoked = true; return a + b; }
    }

    [Fact]
    public async Task SelectMany_TaskSync_AllSuccess_ReturnsCombinedValue()
    {
        var combined = await (
            from a in OkTaskAsync(2)
            from b in OkSync(3)
            select a + b);

        combined.Should().BeSuccess().Which.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_SyncTask_AllSuccess_ReturnsCombinedValue()
    {
        var combined = await (
            from a in OkSync(2)
            from b in OkTaskAsync(3)
            select a + b);

        combined.Should().BeSuccess().Which.Should().Be(5);
    }

    [Fact]
    public async Task Select_Task_ProjectsSuccessValue()
    {
        var projected = await (from a in OkTaskAsync(5) select a * 2);

        projected.Should().BeSuccess().Which.Should().Be(10);
    }

    [Fact]
    public async Task Select_Task_PropagatesFailure()
    {
        var projected = await (from a in FailTaskAsync() select a * 2);

        projected.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public async Task Where_Task_PredicateTrue_KeepsSuccess()
    {
        var filtered = await (from a in OkTaskAsync(15) where a > 10 select a);

        filtered.Should().BeSuccess().Which.Should().Be(15);
    }

    [Fact]
    public async Task Where_Task_PredicateFalse_ConvertsToFailure()
    {
        var filtered = await (from a in OkTaskAsync(5) where a > 10 select a);

        filtered.Should().HaveErrorDetail("Result filtered out by predicate.");
    }

    private static ValueTask<Result<int>> OkValueTaskAsync(int value) => new(Result.Ok(value));
    private ValueTask<Result<int>> FailValueTaskAsync() => new(Result.Fail<int>(Error1));

    [Fact]
    public async Task SelectMany_ValueTaskValueTask_AllSuccess_ReturnsCombinedValue()
    {
        var combined = await (
            from a in OkValueTaskAsync(2)
            from b in OkValueTaskAsync(3)
            select a + b);

        combined.Should().BeSuccess().Which.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_ValueTaskSync_AllSuccess_ReturnsCombinedValue()
    {
        var combined = await (
            from a in OkValueTaskAsync(2)
            from b in OkSync(3)
            select a + b);

        combined.Should().BeSuccess().Which.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_SyncValueTask_AllSuccess_ReturnsCombinedValue()
    {
        var combined = await (
            from a in OkSync(2)
            from b in OkValueTaskAsync(3)
            select a + b);

        combined.Should().BeSuccess().Which.Should().Be(5);
    }

    [Fact]
    public async Task Select_ValueTask_ProjectsSuccessValue()
    {
        var projected = await (from a in OkValueTaskAsync(5) select a * 2);

        projected.Should().BeSuccess().Which.Should().Be(10);
    }

    [Fact]
    public async Task Where_ValueTask_PredicateFalse_ConvertsToFailure()
    {
        var filtered = await (from a in OkValueTaskAsync(5) where a > 10 select a);

        filtered.Should().HaveErrorDetail("Result filtered out by predicate.");
    }

    [Fact]
    public async Task SelectMany_TaskTask_ExceptionInCollectionSelector_Propagates()
    {
        Func<Task> act = async () => _ = await (
            from a in OkTaskAsync(2)
            from b in ThrowingTaskAsync()
            select a + b);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        static Task<Result<int>> ThrowingTaskAsync() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task SelectMany_TaskTask_CancellationTokenInClosure_ShortCircuitsViaThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var resultSelectorInvoked = false;

        Func<Task> act = async () => _ = await (
            from a in OkTaskAsync(2)
            from b in CancelAware(cts.Token)
            select Combine(a, b));

        await act.Should().ThrowAsync<OperationCanceledException>();
        resultSelectorInvoked.Should().BeFalse();

        static Task<Result<int>> CancelAware(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return OkTaskAsync(3);
        }

        int Combine(int a, int b) { resultSelectorInvoked = true; return a + b; }
    }
}
#pragma warning restore CA2012
