using Trellis.Testing;

namespace Trellis.Core.Tests.Maybes.Extensions.Linq;

// CA2012: query-expression desugaring synthesizes a temporary holding the source ValueTask<Maybe<T>>
// before invoking Select/SelectMany/Where. The desugared call is exactly one consumption (semantically
// safe), but the analyzer cannot prove that across the synthesized lambdas. Suppressed locally.
#pragma warning disable CA2012
public class AsyncLinqTests : TestBase
{
    private static Task<Maybe<int>> SomeTaskAsync(int value) => Task.FromResult(Maybe.From(value));
    private static Task<Maybe<int>> NoneTaskAsync() => Task.FromResult(Maybe<int>.None);
    private static Maybe<int> SomeSync(int value) => Maybe.From(value);
    private static Maybe<int> NoneSync() => Maybe<int>.None;

    private static ValueTask<Maybe<int>> SomeValueTaskAsync(int value) => new(Maybe.From(value));
    private static ValueTask<Maybe<int>> NoneValueTaskAsync() => new(Maybe<int>.None);

    #region SelectMany — Task / Task (all async)

    [Fact]
    public async Task SelectMany_TaskTask_AllSome_ReturnsCombinedValue()
    {
        var combined = await (
            from a in SomeTaskAsync(2)
            from b in SomeTaskAsync(3)
            select a + b);

        combined.HasValue.Should().BeTrue();
        combined.Value.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_TaskTask_FirstNone_ShortCircuitsCollectionSelector()
    {
        var collectionSelectorInvoked = false;

        var combined = await (
            from a in NoneTaskAsync()
            from b in InvocationCounting()
            select a + b);

        combined.HasNoValue.Should().BeTrue();
        collectionSelectorInvoked.Should().BeFalse();

        Task<Maybe<int>> InvocationCounting()
        {
            collectionSelectorInvoked = true;
            return SomeTaskAsync(3);
        }
    }

    [Fact]
    public async Task SelectMany_TaskTask_SecondNone_DoesNotInvokeResultSelector()
    {
        var resultSelectorInvoked = false;

        var combined = await (
            from a in SomeTaskAsync(2)
            from b in NoneTaskAsync()
            select Combine(a, b));

        combined.HasNoValue.Should().BeTrue();
        resultSelectorInvoked.Should().BeFalse();

        int Combine(int a, int b) { resultSelectorInvoked = true; return a + b; }
    }

    #endregion

    #region SelectMany — Task / Sync (Left mixed)

    [Fact]
    public async Task SelectMany_TaskSync_AllSome_ReturnsCombinedValue()
    {
        var combined = await (
            from a in SomeTaskAsync(2)
            from b in SomeSync(3)
            select a + b);

        combined.HasValue.Should().BeTrue();
        combined.Value.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_TaskSync_FirstNone_ShortCircuitsCollectionSelector()
    {
        var collectionSelectorInvoked = false;

        var combined = await (
            from a in NoneTaskAsync()
            from b in InvocationCounting()
            select a + b);

        combined.HasNoValue.Should().BeTrue();
        collectionSelectorInvoked.Should().BeFalse();

        Maybe<int> InvocationCounting()
        {
            collectionSelectorInvoked = true;
            return SomeSync(3);
        }
    }

    [Fact]
    public async Task SelectMany_TaskSync_SecondNone_DoesNotInvokeResultSelector()
    {
        var resultSelectorInvoked = false;

        var combined = await (
            from a in SomeTaskAsync(2)
            from b in NoneSync()
            select Combine(a, b));

        combined.HasNoValue.Should().BeTrue();
        resultSelectorInvoked.Should().BeFalse();

        int Combine(int a, int b) { resultSelectorInvoked = true; return a + b; }
    }

    #endregion

    #region SelectMany — Sync / Task (Right mixed)

    [Fact]
    public async Task SelectMany_SyncTask_AllSome_ReturnsCombinedValue()
    {
        var combined = await (
            from a in SomeSync(2)
            from b in SomeTaskAsync(3)
            select a + b);

        combined.HasValue.Should().BeTrue();
        combined.Value.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_SyncTask_FirstNone_ShortCircuitsCollectionSelector()
    {
        var collectionSelectorInvoked = false;

        var combined = await (
            from a in NoneSync()
            from b in InvocationCounting()
            select a + b);

        combined.HasNoValue.Should().BeTrue();
        collectionSelectorInvoked.Should().BeFalse();

        Task<Maybe<int>> InvocationCounting()
        {
            collectionSelectorInvoked = true;
            return SomeTaskAsync(3);
        }
    }

    [Fact]
    public async Task SelectMany_SyncTask_SecondNone_DoesNotInvokeResultSelector()
    {
        var resultSelectorInvoked = false;

        var combined = await (
            from a in SomeSync(2)
            from b in NoneTaskAsync()
            select Combine(a, b));

        combined.HasNoValue.Should().BeTrue();
        resultSelectorInvoked.Should().BeFalse();

        int Combine(int a, int b) { resultSelectorInvoked = true; return a + b; }
    }

    #endregion

    #region Select / Where — Task

    [Fact]
    public async Task Select_Task_ProjectsSomeValue()
    {
        var projected = await (from a in SomeTaskAsync(5) select a * 2);

        projected.HasValue.Should().BeTrue();
        projected.Value.Should().Be(10);
    }

    [Fact]
    public async Task Select_Task_PropagatesNone()
    {
        var projected = await (from a in NoneTaskAsync() select a * 2);

        projected.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task Where_Task_PredicateTrue_KeepsSome()
    {
        var filtered = await (from a in SomeTaskAsync(15) where a > 10 select a);

        filtered.HasValue.Should().BeTrue();
        filtered.Value.Should().Be(15);
    }

    [Fact]
    public async Task Where_Task_PredicateFalse_ConvertsToNone()
    {
        var filtered = await (from a in SomeTaskAsync(5) where a > 10 select a);

        filtered.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task Where_Task_OnNoneInput_StaysNone()
    {
        var filtered = await (from a in NoneTaskAsync() where a > 10 select a);

        filtered.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region ValueTask parity

    [Fact]
    public async Task SelectMany_ValueTaskValueTask_AllSome_ReturnsCombinedValue()
    {
        var combined = await (
            from a in SomeValueTaskAsync(2)
            from b in SomeValueTaskAsync(3)
            select a + b);

        combined.HasValue.Should().BeTrue();
        combined.Value.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_ValueTaskValueTask_FirstNone_ShortCircuits()
    {
        var combined = await (
            from a in NoneValueTaskAsync()
            from b in SomeValueTaskAsync(3)
            select a + b);

        combined.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task SelectMany_ValueTaskSync_AllSome_ReturnsCombinedValue()
    {
        var combined = await (
            from a in SomeValueTaskAsync(2)
            from b in SomeSync(3)
            select a + b);

        combined.HasValue.Should().BeTrue();
        combined.Value.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_SyncValueTask_AllSome_ReturnsCombinedValue()
    {
        var combined = await (
            from a in SomeSync(2)
            from b in SomeValueTaskAsync(3)
            select a + b);

        combined.HasValue.Should().BeTrue();
        combined.Value.Should().Be(5);
    }

    [Fact]
    public async Task SelectMany_SyncValueTask_FirstNone_ShortCircuitsCollectionSelector()
    {
        var collectionSelectorInvoked = false;

        var combined = await (
            from a in NoneSync()
            from b in InvocationCounting()
            select a + b);

        combined.HasNoValue.Should().BeTrue();
        collectionSelectorInvoked.Should().BeFalse();

        ValueTask<Maybe<int>> InvocationCounting()
        {
            collectionSelectorInvoked = true;
            return SomeValueTaskAsync(3);
        }
    }

    [Fact]
    public async Task Select_ValueTask_ProjectsSomeValue()
    {
        var projected = await (from a in SomeValueTaskAsync(5) select a * 2);

        projected.HasValue.Should().BeTrue();
        projected.Value.Should().Be(10);
    }

    [Fact]
    public async Task Where_ValueTask_PredicateFalse_ConvertsToNone()
    {
        var filtered = await (from a in SomeValueTaskAsync(5) where a > 10 select a);

        filtered.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Edge cases

    [Fact]
    public async Task SelectMany_TaskTask_ExceptionInCollectionSelector_Propagates()
    {
        Func<Task> act = async () => _ = await (
            from a in SomeTaskAsync(2)
            from b in ThrowingTaskAsync()
            select a + b);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        static Task<Maybe<int>> ThrowingTaskAsync() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task AsTask_RoundTrips()
    {
        var maybe = Maybe.From(42);
        var task = maybe.AsTask();

        var result = await task;

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task AsValueTask_RoundTrips()
    {
        var maybe = Maybe.From(42);
        var valueTask = maybe.AsValueTask();

        var result = await valueTask;

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task AsTask_None_RoundTrips()
    {
        var none = Maybe<int>.None;
        var task = none.AsTask();

        var result = await task;

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task AsValueTask_None_RoundTrips()
    {
        var none = Maybe<int>.None;
        var valueTask = none.AsValueTask();

        var result = await valueTask;

        result.HasNoValue.Should().BeTrue();
    }

    #endregion
}
#pragma warning restore CA2012
