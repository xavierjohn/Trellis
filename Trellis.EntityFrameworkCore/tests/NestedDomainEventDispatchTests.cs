namespace Trellis.EntityFrameworkCore.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator;

public class NestedDomainEventDispatchTests
{
    [Fact]
    public async Task Handle_SuccessfulGrandchildWithIgnoredIntermediateFailure_BelongsToActualOwningCommit()
    {
        using var context = new CommitContext();
        var unitOfWork = new EfUnitOfWork<CommitContext>(context);
        var aggregate = new PendingAggregate();
        var publisher = new RecordingPublisher(context);
        var outer = new DomainEventDispatchBehavior<OuterCommand, Result<string>>(publisher,
            NullLogger<DomainEventDispatchBehavior<OuterCommand, Result<string>>>.Instance);
        var inner = new DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>(publisher,
            NullLogger<DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>>.Instance);
        var outerTransaction = new TransactionalCommandBehavior<OuterCommand, Result<string>>(unitOfWork);
        var innerTransaction = new TransactionalCommandBehavior<InnerCommand, Result<PendingAggregate>>(unitOfWork);

        var result = await outer.Handle(new OuterCommand(), (root, ct) => outerTransaction.Handle(root,
            async (_, token) =>
            {
                var intermediate = await outer.Handle(new OuterCommand(), (middle, middleToken) =>
                    outerTransaction.Handle(middle, async (_, grandchildToken) =>
                    {
                        var nested = await inner.Handle(new InnerCommand(), (command, commandToken) =>
                            innerTransaction.Handle(command, (_, _) =>
                                ValueTask.FromResult(Result.Ok(aggregate)), commandToken), grandchildToken);
                        nested.IsSuccess.Should().BeTrue();
                        return Result.Fail<string>(new Error.Conflict(Resource: null, Code: "ignored"));
                    }, middleToken), token);
                intermediate.IsFailure.Should().BeTrue();
                publisher.Events.Should().BeEmpty();
                return Result.Ok("outer deliberately commits staged work");
            }, ct), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        publisher.Events.Should().ContainSingle("successful deferred work belongs to the actual owner, not an intermediate scope");
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ManuallyOwnedOuterScope_RejectsAutomaticDispatchWithoutClearing()
    {
        using var context = new CommitContext();
        var unitOfWork = new EfUnitOfWork<CommitContext>(context);
        using var manual = unitOfWork.BeginScope();
        manual.IsOwner.Should().BeTrue();
        var aggregate = new PendingAggregate();
        var publisher = new RecordingPublisher(context);
        var dispatch = new DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>(publisher,
            NullLogger<DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>>.Instance);
        var transaction = new TransactionalCommandBehavior<InnerCommand, Result<PendingAggregate>>(unitOfWork);
        var act = async () => await dispatch.Handle(new InnerCommand(), (command, ct) =>
            transaction.Handle(command, (_, _) => ValueTask.FromResult(Result.Ok(aggregate)), ct),
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manually owned*");
        context.Commits.Should().Be(0);
        publisher.Events.Should().BeEmpty();
        aggregate.UncommittedEvents().Should().ContainSingle();
    }

    [Theory]
    [InlineData("success", false)]
    [InlineData("failure", false)]
    [InlineData("throw", false)]
    [InlineData("persist-failure", false)]
    [InlineData("commit-failure", false)]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    [InlineData("throw", true)]
    [InlineData("persist-failure", true)]
    [InlineData("commit-failure", true)]
    public async Task Handle_NestedAggregateWithOuterDto_DispatchesOnlyAfterSuccessfulOwningCommit(string outcome, bool tracked)
    {
        using var context = new CommitContext();
        var unitOfWork = new EfUnitOfWork<CommitContext>(context);
        var first = new PendingAggregate();
        var second = new PendingAggregate();
        var stale = new PendingAggregate();
        var source = new TrackedSource { CommittedAggregates = [stale] };
        context.OnCommit = () => source.CommittedAggregates = [first, second];
        var publisher = new RecordingPublisher(context);
        IPipelineBehavior<OuterCommand, Result<string>> outer = tracked
            ? new TrackedAggregateDomainEventDispatchBehavior<OuterCommand, Result<string>>(source, publisher,
                NullLogger<TrackedAggregateDomainEventDispatchBehavior<OuterCommand, Result<string>>>.Instance)
            : new DomainEventDispatchBehavior<OuterCommand, Result<string>>(publisher,
                NullLogger<DomainEventDispatchBehavior<OuterCommand, Result<string>>>.Instance);
        IPipelineBehavior<InnerCommand, Result<PendingAggregate>> inner = tracked
            ? new TrackedAggregateDomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>(source, publisher,
                NullLogger<TrackedAggregateDomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>>.Instance)
            : new DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>(publisher,
                NullLogger<DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>>.Instance);
        var outerTransaction = new TransactionalCommandBehavior<OuterCommand, Result<string>>(unitOfWork);
        var innerTransaction = new TransactionalCommandBehavior<InnerCommand, Result<PendingAggregate>>(unitOfWork);
        var error = new Error.Conflict(Resource: null, Code: "test.rejected");

        async ValueTask<Result<string>> Handler(OuterCommand _, CancellationToken ct)
        {
            foreach (var aggregate in new[] { first, second, first })
            {
                var response = await inner.Handle(new InnerCommand(),
                    (message, token) => innerTransaction.Handle(message,
                        (_, _) => ValueTask.FromResult(Result.Ok(aggregate)), token), ct);
                response.IsSuccess.Should().BeTrue();
                publisher.Events.Should().BeEmpty("nested commits are deferred");
                aggregate.UncommittedEvents().Should().NotBeEmpty("deferred dispatch cannot clear events");
            }

            if (outcome == "throw")
                throw new InvalidOperationException("outer failed");
            if (outcome == "commit-failure")
                context.FailCommit = true;
            return outcome switch
            {
                "failure" => Result.Fail<string>(error),
                "persist-failure" => Result.FailAfterCommit<string>(error),
                _ => Result.Ok("done"),
            };
        }

        var act = async () => await outer.Handle(new OuterCommand(),
            (message, ct) => outerTransaction.Handle(message, Handler, ct), TestContext.Current.CancellationToken);
        if (outcome == "throw")
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outer failed");
        else
        {
            var result = await act();
            result.IsSuccess.Should().Be(outcome == "success");
        }

        publisher.Events.Should().HaveCount(outcome == "success" ? 2 : 0);
        context.Commits.Should().Be(outcome is "success" or "persist-failure" ? 1 : 0);
        first.UncommittedEvents().Count.Should().Be(outcome == "success" ? 0 : 1);
        stale.UncommittedEvents().Should().ContainSingle("deferred commands cannot consume a previous commit's snapshot");

        context.FailCommit = false;
        var fresh = new PendingAggregate();
        context.OnCommit = () => source.CommittedAggregates = [fresh];
        var next = await inner.Handle(new InnerCommand(),
            (message, ct) => innerTransaction.Handle(message,
                (_, _) => ValueTask.FromResult(Result.Ok(fresh)), ct), TestContext.Current.CancellationToken);
        next.IsSuccess.Should().BeTrue();
        publisher.Events.Should().HaveCount(outcome == "success" ? 3 : 1,
            "a subsequent operation must not inherit the abandoned batch");
    }

    [Fact]
    public async Task Handle_NestedAggregateWithOuterUnit_PreservesPendingDispatch()
    {
        using var context = new CommitContext();
        var unitOfWork = new EfUnitOfWork<CommitContext>(context);
        var aggregate = new PendingAggregate();
        var publisher = new RecordingPublisher(context);
        var outer = new DomainEventDispatchBehavior<UnitCommand, Result<Trellis.Unit>>(publisher,
            NullLogger<DomainEventDispatchBehavior<UnitCommand, Result<Trellis.Unit>>>.Instance);
        var inner = new DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>(publisher,
            NullLogger<DomainEventDispatchBehavior<InnerCommand, Result<PendingAggregate>>>.Instance);
        var outerTransaction = new TransactionalCommandBehavior<UnitCommand, Result<Trellis.Unit>>(unitOfWork);
        var innerTransaction = new TransactionalCommandBehavior<InnerCommand, Result<PendingAggregate>>(unitOfWork);

        var result = await outer.Handle(new UnitCommand(), (message, ct) => outerTransaction.Handle(message,
            async (_, token) =>
            {
                var nested = await inner.Handle(new InnerCommand(), (command, innerToken) =>
                    innerTransaction.Handle(command, (_, _) => ValueTask.FromResult(Result.Ok(aggregate)), innerToken), token);
                nested.IsSuccess.Should().BeTrue();
                publisher.Events.Should().BeEmpty();
                return Result.Ok();
            }, ct), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        publisher.Events.Should().ContainSingle();
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    private sealed record OuterCommand : ICommand<Result<string>>;
    private sealed record UnitCommand : ICommand<Result<Trellis.Unit>>;
    private sealed record InnerCommand : ICommand<Result<PendingAggregate>>;
    private sealed record PendingEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class TrackedSource : ITrackedAggregateSource
    {
        public IReadOnlyList<IAggregate> CommittedAggregates { get; set; } = [];
    }

    private sealed class PendingAggregate : IAggregate
    {
        private readonly List<IDomainEvent> _events = [new PendingEvent(DateTimeOffset.UnixEpoch)];
        public bool IsChanged => _events.Count != 0;
        public string ETag => string.Empty;
        public IReadOnlyList<IDomainEvent> UncommittedEvents() => _events;
        public void AcceptChanges() => _events.Clear();
    }

    private sealed class CommitContext()
        : DbContext(new DbContextOptionsBuilder<CommitContext>().UseSqlite("Data Source=:memory:").Options)
    {
        public int Commits { get; private set; }
        public bool FailCommit { get; set; }
        public Action? OnCommit { get; set; }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (FailCommit)
                throw new DbUpdateConcurrencyException("commit failed");
            Commits++;
            OnCommit?.Invoke();
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingPublisher(CommitContext context) : IDomainEventPublisher
    {
        public List<IDomainEvent> Events { get; } = [];
        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            context.Commits.Should().BeGreaterThan(0, "publication requires an actual commit");
            Events.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }
}