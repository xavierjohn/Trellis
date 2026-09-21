namespace Trellis.Core.Tests.Results;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

public class ResultEnsureFactoryTests
{
    public enum GuardForm { Boolean, Predicate, AsyncPredicate }

    [Theory]
    [InlineData(GuardForm.Boolean, true)]
    [InlineData(GuardForm.Boolean, false)]
    [InlineData(GuardForm.Predicate, true)]
    [InlineData(GuardForm.Predicate, false)]
    [InlineData(GuardForm.AsyncPredicate, true)]
    [InlineData(GuardForm.AsyncPredicate, false)]
    public async Task Ensure_Factory_Condition_InvokesFactoryOnlyOnFailure(GuardForm form, bool condition)
    {
        using var tracing = new ActivityTestHelper();
        var calls = 0;
        var error = new Error.Forbidden("guard.denied");

        var result = await Evaluate(form, condition, () => { calls++; return error; });

        if (condition)
            result.Should().BeSuccess().Which.Should().Be(Unit.Default);
        else
            result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().Be(condition ? 0 : 1);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("Ensure",
            condition ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    [Theory]
    [InlineData(GuardForm.Boolean)]
    [InlineData(GuardForm.Predicate)]
    [InlineData(GuardForm.AsyncPredicate)]
    public async Task Ensure_Factory_PassingGuard_DoesNotInvokeThrowingFactory(GuardForm form)
    {
        var result = await Evaluate(form, true, () => throw new InvalidOperationException("must not run"));

        result.Should().BeSuccess();
    }

    [Theory]
    [InlineData(GuardForm.Boolean)]
    [InlineData(GuardForm.Predicate)]
    [InlineData(GuardForm.AsyncPredicate)]
    public async Task Ensure_Factory_NullFactory_ThrowsEvenOnSuccess(GuardForm form)
    {
        var act = () => Evaluate(form, true, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("errorFactory");
    }

    [Theory]
    [InlineData(GuardForm.Boolean)]
    [InlineData(GuardForm.Predicate)]
    [InlineData(GuardForm.AsyncPredicate)]
    public async Task Ensure_Factory_NullProducedError_ThrowsInsteadOfReturningFailureWithoutError(GuardForm form)
    {
        var act = () => Evaluate(form, false, () => null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
    }

    [Theory]
    [InlineData(GuardForm.Boolean)]
    [InlineData(GuardForm.Predicate)]
    [InlineData(GuardForm.AsyncPredicate)]
    public async Task Ensure_Factory_Exception_PropagatesUnchanged(GuardForm form)
    {
        var exception = new InvalidOperationException("factory failed");
        var act = () => Evaluate(form, false, () => throw exception);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ensure_PredicateFactory_PredicateRunsOnceBeforeFactory(bool condition)
    {
        var order = new List<string>();

        var result = Result.Ensure(
            () => { order.Add("predicate"); return condition; },
            () => { order.Add("factory"); return new Error.Forbidden("guard.denied"); });

        result.IsSuccess.Should().Be(condition);
        order.Should().Equal(condition ? ["predicate"] : ["predicate", "factory"]);
    }

    [Fact]
    public void Ensure_PredicateFactory_NullFactory_DoesNotInvokePredicate()
    {
        var calls = 0;
        var act = () => Result.Ensure(() => { calls++; return true; }, errorFactory: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("errorFactory");
        calls.Should().Be(0);
    }

    [Fact]
    public void Ensure_PredicateFactory_NullPredicate_DoesNotInvokeFactory()
    {
        var calls = 0;
        var act = () => Result.Ensure((Func<bool>)null!,
            () => { calls++; return new Error.Forbidden("guard.denied"); });

        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
        calls.Should().Be(0);
    }

    [Fact]
    public void Ensure_PredicateFactory_ThrownPredicate_DoesNotInvokeFactory()
    {
        var exception = new InvalidOperationException("predicate failed");
        var calls = 0;
        var act = () => Result.Ensure((Func<bool>)(() => throw exception),
            () => { calls++; return new Error.Forbidden("guard.denied"); });

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureAsync_Factory_PendingPredicate_WaitsBeforeCreatingError(bool condition)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var predicateCalls = 0;
        var factoryCalls = 0;
        var pending = Result.EnsureAsync(
            () => { predicateCalls++; return completion.Task; },
            () => { factoryCalls++; return new Error.Forbidden("guard.denied"); });

        predicateCalls.Should().Be(1);
        factoryCalls.Should().Be(0);
        pending.IsCompleted.Should().BeFalse();
        completion.SetResult(condition);

        var result = await pending.WaitAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().Be(condition);
        predicateCalls.Should().Be(1);
        factoryCalls.Should().Be(condition ? 0 : 1);
    }

    [Fact]
    public async Task EnsureAsync_Factory_NullFactory_DoesNotInvokePredicate()
    {
        var calls = 0;
        var act = () => Result.EnsureAsync(
            () => { calls++; return Task.FromResult(true); }, errorFactory: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("errorFactory");
        calls.Should().Be(0);
    }

    [Fact]
    public async Task EnsureAsync_Factory_NullPredicate_DoesNotInvokeFactory()
    {
        var calls = 0;
        var act = () => Result.EnsureAsync((Func<Task<bool>>)null!,
            () => { calls++; return new Error.Forbidden("guard.denied"); });

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("predicate");
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureAsync_Factory_PredicateException_DoesNotInvokeFactory(bool throwSynchronously)
    {
        var exception = new InvalidOperationException("predicate failed");
        var calls = 0;
        Func<Task<bool>> predicate = throwSynchronously
            ? () => throw exception
            : () => Task.FromException<bool>(exception);
        var act = () => Result.EnsureAsync(predicate,
            () => { calls++; return new Error.Forbidden("guard.denied"); });

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task EnsureAsync_Factory_CancelledPredicate_PropagatesCancellationWithoutFactory()
    {
        var token = new CancellationToken(canceled: true);
        var calls = 0;
        var act = () => Result.EnsureAsync(() => Task.FromCanceled<bool>(token),
            () => { calls++; return new Error.Forbidden("guard.denied"); });

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(token);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Ensure_EagerOverloads_NullLiterals_RemainUnambiguousAndPreserveBehavior()
    {
        Result.Ensure(true, null!).Should().BeSuccess();
        Result.Ensure(() => true, null!).Should().BeSuccess();
        (await Result.EnsureAsync(() => Task.FromResult(true), null!)).Should().BeSuccess();

        var boolean = () => Result.Ensure(false, null!);
        var predicate = () => Result.Ensure(() => false, null!);
        var asyncPredicate = () => Result.EnsureAsync(() => Task.FromResult(false), null!);

        boolean.Should().Throw<ArgumentNullException>().WithParameterName("error");
        predicate.Should().Throw<ArgumentNullException>().WithParameterName("error");
        await asyncPredicate.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
    }

    private static Task<Result<Unit>> Evaluate(GuardForm form, bool condition, Func<Error> errorFactory) =>
        form switch
        {
            GuardForm.Boolean => Task.FromResult(Result.Ensure(condition, errorFactory)),
            GuardForm.Predicate => Task.FromResult(Result.Ensure(() => condition, errorFactory)),
            GuardForm.AsyncPredicate => Result.EnsureAsync(() => Task.FromResult(condition), errorFactory),
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
}
