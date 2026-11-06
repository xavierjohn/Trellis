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
        var calls = new CallbackRecorder();
        var error = new Error.Forbidden("guard.denied");

        var result = await Evaluate(form, condition, calls.Wrap<Error>(() => error));

        if (condition)
            result.Should().BeSuccess().Which.Should().Be(Unit.Default);
        else
            result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().HaveBeenCalledTimes(condition ? 0 : 1);
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

        await act.ShouldRejectNullAsync("errorFactory");
    }

    [Theory]
    [InlineData(GuardForm.Boolean)]
    [InlineData(GuardForm.Predicate)]
    [InlineData(GuardForm.AsyncPredicate)]
    public async Task Ensure_Factory_NullProducedError_ThrowsInsteadOfReturningFailureWithoutError(GuardForm form)
    {
        var act = () => Evaluate(form, false, () => null!);

        await act.ShouldRejectNullAsync("error");
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
        var calls = new CallbackRecorder();
        Action act = () => Result.Ensure(calls.Wrap(() => true), errorFactory: null!);

        act.ShouldRejectNull("errorFactory");
        calls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public void Ensure_PredicateFactory_NullPredicate_DoesNotInvokeFactory()
    {
        var calls = new CallbackRecorder();
        Action act = () => Result.Ensure((Func<bool>)null!,
            calls.Wrap<Error>(() => new Error.Forbidden("guard.denied")));

        act.ShouldRejectNull("predicate");
        calls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public void Ensure_PredicateFactory_ThrownPredicate_DoesNotInvokeFactory()
    {
        var exception = new InvalidOperationException("predicate failed");
        var calls = new CallbackRecorder();
        var act = () => Result.Ensure((Func<bool>)(() => throw exception),
            calls.Wrap<Error>(() => new Error.Forbidden("guard.denied")));

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
        calls.Should().NotHaveBeenCalled();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureAsync_Factory_PendingPredicate_WaitsBeforeCreatingError(bool condition)
    {
        var completion = new ControlledAsync<bool>();
        var predicateCalls = new CallbackRecorder();
        var factoryCalls = new CallbackRecorder();
        var pending = Result.EnsureAsync(
            predicateCalls.Wrap(() => completion.Task),
            factoryCalls.Wrap<Error>(() => new Error.Forbidden("guard.denied")));

        var result = await pending.ShouldBePendingThenReleaseAsync(
            () => completion.TryComplete(condition),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken,
            whilePending: () =>
            {
                predicateCalls.Should().HaveBeenCalledOnce();
                factoryCalls.Should().NotHaveBeenCalled();
            });

        result.IsSuccess.Should().Be(condition);
        predicateCalls.Should().HaveBeenCalledOnce();
        factoryCalls.Should().HaveBeenCalledTimes(condition ? 0 : 1);
    }

    [Fact]
    public async Task EnsureAsync_Factory_NullFactory_DoesNotInvokePredicate()
    {
        var calls = new CallbackRecorder();
        var act = () => Result.EnsureAsync(
            calls.Wrap(() => Task.FromResult(true)), errorFactory: null!);

        await act.ShouldRejectNullAsync("errorFactory");
        calls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public async Task EnsureAsync_Factory_NullPredicate_DoesNotInvokeFactory()
    {
        var calls = new CallbackRecorder();
        var act = () => Result.EnsureAsync((Func<Task<bool>>)null!,
            calls.Wrap<Error>(() => new Error.Forbidden("guard.denied")));

        await act.ShouldRejectNullAsync("predicate");
        calls.Should().NotHaveBeenCalled();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureAsync_Factory_PredicateException_DoesNotInvokeFactory(bool throwSynchronously)
    {
        var exception = new InvalidOperationException("predicate failed");
        var calls = new CallbackRecorder();
        Func<Task<bool>> predicate = throwSynchronously
            ? () => throw exception
            : () => Task.FromException<bool>(exception);
        var act = () => Result.EnsureAsync(predicate,
            calls.Wrap<Error>(() => new Error.Forbidden("guard.denied")));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        calls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public async Task EnsureAsync_Factory_CancelledPredicate_PropagatesCancellationWithoutFactory()
    {
        using var canceled = CanceledToken.Create();
        var calls = new CallbackRecorder();

        await Result.EnsureAsync(canceled.ToTask<bool>,
            calls.Wrap<Error>(() => new Error.Forbidden("guard.denied")))
            .ShouldPropagateCancellationAsync(canceled.Token, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        calls.Should().NotHaveBeenCalled();
    }

    [Fact]
    public async Task Ensure_EagerOverloads_NullLiterals_RemainUnambiguousAndPreserveBehavior()
    {
        Result.Ensure(true, null!).Should().BeSuccess();
        Result.Ensure(() => true, null!).Should().BeSuccess();
        (await Result.EnsureAsync(() => Task.FromResult(true), null!)).Should().BeSuccess();

        Action boolean = () => Result.Ensure(false, null!);
        Action predicate = () => Result.Ensure(() => false, null!);
        var asyncPredicate = () => Result.EnsureAsync(() => Task.FromResult(false), null!);

        boolean.ShouldRejectNull("error");
        predicate.ShouldRejectNull("error");
        await asyncPredicate.ShouldRejectNullAsync("error");
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