namespace Trellis.EntityFrameworkCore.Tests;

using Trellis.Testing;

public class TransactionalCommandBehaviorTests
{
    [Fact]
    public async Task Handle_successful_handler_commits_and_returns_result()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var expected = Result.Success("done");

        // Act
        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(expected),
            ct);

        // Assert
        result.Should().BeSuccess();
        result.Value.Should().Be("done");
        uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_failed_handler_does_not_commit()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var failure = Result.Failure<string>(Error.Domain("bad"));

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
        var uow = new FakeUnitOfWork { CommitResult = Error.Conflict("concurrency") };
        var behavior = new TransactionalCommandBehavior<FakeCommand, Result<string>>(uow);
        var handlerResult = Result.Success("staged");

        // Act
        var result = await behavior.Handle(
            new FakeCommand(),
            (_, _) => new ValueTask<Result<string>>(handlerResult),
            ct);

        // Assert
        result.Should().BeFailure();
        result.Error.Should().BeOfType<ConflictError>();
        uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_unit_result_successful_handler_commits()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var uow = new FakeUnitOfWork();
        var behavior = new TransactionalCommandBehavior<FakeUnitCommand, Result<Unit>>(uow);
        var expected = Result.Success(default(Unit));

        // Act
        var result = await behavior.Handle(
            new FakeUnitCommand(),
            (_, _) => new ValueTask<Result<Unit>>(expected),
            ct);

        // Assert
        result.Should().BeSuccess();
        uow.CommitCount.Should().Be(1);
    }

    #region Test Infrastructure

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public Result<Unit>? CommitResult { get; init; }

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.FromResult(CommitResult ?? Result.Success());
        }
    }

    private sealed record FakeCommand : Mediator.ICommand<Result<string>>;

    private sealed record FakeUnitCommand : Mediator.ICommand<Result<Unit>>;

    #endregion
}
