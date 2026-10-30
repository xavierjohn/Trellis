using Trellis.Mediator;
using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

public class TransactionalCommandBehaviorTests
{
    [Fact]
    public async Task Handle_failed_handler_does_not_commit()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var failure = Result.Fail<string>(new Error.Conflict(Resource: null, Code: "domain.violation") { Detail = "bad" });

        // Act
        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(failure),
            ct);

        // Assert
        result.Should().BeFailure();
        uow.CommitAttemptCount.Should().Be(0);
        using var nextScope = uow.BeginScope();
        nextScope.IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_commit_failure_returns_commit_error()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork { CommitResult = Result.Fail(new Error.Conflict(Resource: null, Code: "conflict") { Detail = "concurrency" }) };
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var handlerResult = Result.Ok("staged");

        // Act
        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(handlerResult),
            ct);

        // Assert
        result.Should().BeFailure();
        result.UnwrapError().Should().BeOfType<Error.Conflict>();
        uow.CommitAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_unit_result_successful_handler_commits()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeUnitCommand, Result<Unit>>(uow);
        var expected = Result.Ok();

        // Act
        var result = await behavior.Handle(
            new FakeUnitCommand(),
            (_, _) => new ValueTask<Result<Unit>>(expected),
            ct);

        // Assert
        result.Should().BeSuccess();
        uow.CommitAttemptCount.Should().Be(1);
    }

    /// <summary>
    /// Regression for the GPT-5.5 review finding (Major #1): "Nested commands can commit before
    /// the outer command outcome is known". When an outer command's handler dispatches a nested
    /// command via the same scoped <see cref="IUnitOfWork"/>, the inner
    /// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> previously called
    /// <see cref="IUnitOfWork.CommitAsync"/> immediately on inner success — committing both the
    /// outer's staged work and the inner's. The fix wraps each command in a scope and defers
    /// commit until the outermost scope unwinds.
    /// </summary>
    [Fact]
    public async Task Handle_nested_inner_success_does_not_commit_until_outermost_scope_exits()
    {
        // Arrange — simulate the inner command's behavior running inside the outer's scope.
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var outerBehavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var innerBehavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);

        // Act — outer's "next" simulates the handler dispatching a nested command synchronously
        // (the inner runs to completion inside the outer's scope, mirroring an in-process
        // mediator dispatch through the shared scoped DbContext / IUnitOfWork).
        var result = await outerBehavior.Handle(
            new FakeCommand(),
            async (_, innerCt) =>
            {
                var innerResult = await innerBehavior.Handle(
                    new FakeCommand(),
                    (_, _) => new ValueTask<Result<string>>(Result.Ok("inner-done")),
                    innerCt);
                innerResult.Should().BeSuccess();
                return Result.Ok("outer-done");
            },
            ct);

        // Assert — exactly one commit (the outer's), at the outermost scope unwind.
        result.Should().BeSuccess();
        uow.CommitCallCount.Should().Be(2,
            "both outer and inner behaviors call CommitAsync; the inner call is deferred internally");
        uow.CommitAttemptCount.Should().Be(1,
            "only the outermost scope attempts a commit; nested CommitAsync calls return deferred success");
    }

    /// <summary>
    /// Regression: when the outer command fails, neither the inner's deferred commit nor the
    /// outer's commit fire — <c>CommitAsync</c> is never called on the success path, so no
    /// <c>SaveChanges</c> runs and nothing is persisted. (Per-scope rollback of staged changes
    /// is not supported: any entities the inner handler tracked remain in the change tracker
    /// until the <c>DbContext</c> itself is disposed.)
    /// </summary>
    [Fact]
    public async Task Handle_nested_outer_failure_after_inner_success_does_not_commit_anything()
    {
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var outerBehavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var innerBehavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);

        var result = await outerBehavior.Handle(
            new FakeCommand(),
            async (_, innerCt) =>
            {
                var innerResult = await innerBehavior.Handle(
                    new FakeCommand(),
                    (_, _) => new ValueTask<Result<string>>(Result.Ok("inner-done")),
                    innerCt);
                innerResult.Should().BeSuccess();
                return Result.Fail<string>(new Error.Conflict(Resource: null, Code: "outer.failed") { Detail = "outer rejected" });
            },
            ct);

        result.Should().BeFailure();
        uow.CommitCallCount.Should().Be(1,
            "only the inner's deferred CommitAsync was called; the outer's failure short-circuits before its commit");
        uow.CommitAttemptCount.Should().Be(0,
            "the inner commit was deferred and the outer failure prevented a commit attempt");
    }

    /// <summary>
    /// Issue #533: <c>Result.FailAfterCommit&lt;T&gt;(error)</c> is semantically a failure but
    /// opts in (via <see cref="IPersistOnFailure"/>) to having staged changes committed alongside
    /// the failure. The handler returns the persist-on-failure outcome, the commit runs, the
    /// caller still sees the failure.
    /// </summary>
    [Fact]
    public async Task Handle_fail_after_commit_result_commits_and_returns_failure()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var error = new Error.Conflict(Resource: null, Code: "external.permanent_failure") { Detail = "gateway rejected the payload" };
        var persistOnFailureResult = Result.FailAfterCommit<string>(error);

        // Act
        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(persistOnFailureResult),
            ct);

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeSameAs(error);
        uow.CommitAttemptCount.Should().Be(1,
            "FailAfterCommit must trigger the post-handler commit so the staged failure-state row is persisted");
    }

    /// <summary>
    /// Issue #533: if the commit itself fails on a persist-on-failure outcome, the commit
    /// error is returned (overwriting the original handler error). Intentional — the caller
    /// must learn that the persist-on-failure guarantee was not honored.
    /// </summary>
    [Fact]
    public async Task Handle_fail_after_commit_with_commit_failure_returns_commit_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var commitError = new Error.Conflict(Resource: null, Code: FaultCodes.ConcurrentModification) { Detail = "row was concurrently modified" };
        var uow = new FakeUnitOfWork { CommitResult = Result.Fail(commitError) };
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var handlerError = new Error.Conflict(Resource: null, Code: "external.permanent_failure") { Detail = "gateway rejected" };
        var persistOnFailureResult = Result.FailAfterCommit<string>(handlerError);

        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(persistOnFailureResult),
            ct);

        result.Should().BeFailure();
        result.UnwrapError().Should().BeSameAs(commitError);
        uow.CommitAttemptCount.Should().Be(1);
    }

    /// <summary>
    /// Issue #533: the no-payload <see cref="Result.FailAfterCommit(Error)"/> factory returns a
    /// <see cref="Result{Unit}"/> that follows the same persist-on-failure path as the generic
    /// overload — the no-payload result envelope must also opt in to the commit step.
    /// </summary>
    [Fact]
    public async Task Handle_unit_fail_after_commit_result_commits_and_returns_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeUnitCommand, Result<Unit>>(uow);
        var error = new Error.Conflict(Resource: null, Code: "external.permanent_failure") { Detail = "gateway rejected" };
        var persistOnFailureResult = Result.FailAfterCommit(error);

        var result = await behavior.Handle(
            new FakeUnitCommand(),
            (_, _) => new ValueTask<Result<Unit>>(persistOnFailureResult),
            ct);

        result.Should().BeFailure();
        result.Error.Should().BeSameAs(error);
        uow.CommitAttemptCount.Should().Be(1);
    }

    /// <summary>
    /// Issue #533: a handler that throws (rather than returning a result) must not commit. The
    /// behavior must let the exception propagate so the outer exception-handling pipeline can
    /// react, and the unit-of-work scope must dispose without firing <c>CommitAsync</c>.
    /// </summary>
    [Fact]
    public async Task Handle_throwing_handler_does_not_commit_and_propagates_exception()
    {
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);

        var act = async () => await behavior.Handle(
            new FakeCommand(),
            (_, _) => throw new InvalidOperationException("handler blew up"),
            ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler blew up");
        uow.CommitAttemptCount.Should().Be(0,
            "an exception bypasses the success/persist-on-failure branches; no commit must run");
        using var nextScope = uow.BeginScope();
        nextScope.IsOwner.Should().BeTrue();
    }

    #region Test Infrastructure

    private sealed record FakeCommand : global::Mediator.ICommand<Result<string>>;

    private sealed record FakeUnitCommand : global::Mediator.ICommand<Result<Unit>>;

    /// <summary>
    /// Constructor null-guard test (PR #459-style discipline applied here too).
    /// </summary>
    [Fact]
    public void Constructor_null_unitOfWork_throws_argument_null_exception() =>
        FluentActions
            .Invoking(() => new TransactionalCommandBehavior<FakeCommand, Result<string>>(unitOfWork: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "unitOfWork");

    #endregion
}