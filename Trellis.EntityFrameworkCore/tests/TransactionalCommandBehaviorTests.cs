using Trellis.Testing;
namespace Trellis.EntityFrameworkCore.Tests;

public class TransactionalCommandBehaviorTests
{
    [Fact]
    public async Task Handle_successful_handler_commits_and_returns_result()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var expected = Result.Ok("done");

        // Act
        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(expected),
            ct);

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().Should().Be("done");
        uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_failed_handler_does_not_commit()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var failure = Result.Fail<string>(new Error.Conflict(null, "domain.violation") { Detail = "bad" });

        // Act
        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(failure),
            ct);

        // Assert
        result.Should().BeFailure();
        uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_commit_failure_returns_commit_error()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork { CommitResult = Result.Fail(new Error.Conflict(null, "conflict") { Detail = "concurrency" }) };
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
        uow.CommitCount.Should().Be(1);
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
        uow.CommitCount.Should().Be(1);
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
        var uow = new ScopeTrackingFakeUnitOfWork();
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
        uow.ActualPersistCount.Should().Be(1,
            "only the outermost scope's commit actually persists changes; nested CommitAsync calls return success without persisting");
    }

    /// <summary>
    /// Regression: when the outer command fails, neither the inner's deferred commit nor the
    /// outer's commit fire — staged changes remain in the change tracker and are discarded
    /// when the scope ends.
    /// </summary>
    [Fact]
    public async Task Handle_nested_outer_failure_after_inner_success_does_not_commit_anything()
    {
        var ct = TestContext.Current.CancellationToken;
        var uow = new ScopeTrackingFakeUnitOfWork();
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
                return Result.Fail<string>(new Error.Conflict(null, "outer.failed") { Detail = "outer rejected" });
            },
            ct);

        result.Should().BeFailure();
        uow.CommitCallCount.Should().Be(1,
            "only the inner's deferred CommitAsync was called; the outer's failure short-circuits before its commit");
        uow.ActualPersistCount.Should().Be(0,
            "the inner commit was deferred and the outer never committed — no persistence happens");
    }

    #region Test Infrastructure

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public Result<Unit>? CommitResult { get; init; }

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.FromResult(CommitResult ?? Result.Ok());
        }
    }

    /// <summary>
    /// Fake unit-of-work that emulates the depth-tracking + deferred-commit semantics of
    /// <see cref="EfUnitOfWork{TContext}"/> so the regression tests above don't depend on a
    /// real <c>DbContext</c>.
    /// </summary>
    private sealed class ScopeTrackingFakeUnitOfWork : IUnitOfWork
    {
        private int _depth;

        public int CommitCallCount { get; private set; }

        public int ActualPersistCount { get; private set; }

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCallCount++;
            if (_depth > 1)
                return Task.FromResult(Result.Ok());

            ActualPersistCount++;
            return Task.FromResult(Result.Ok());
        }

        public IDisposable BeginScope()
        {
            _depth++;
            return new Releaser(this);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly ScopeTrackingFakeUnitOfWork _owner;
            private bool _disposed;

            public Releaser(ScopeTrackingFakeUnitOfWork owner) => _owner = owner;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner._depth--;
            }
        }
    }

    private sealed record FakeCommand : Mediator.ICommand<Result<string>>;

    private sealed record FakeUnitCommand : Mediator.ICommand<Result<Unit>>;

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