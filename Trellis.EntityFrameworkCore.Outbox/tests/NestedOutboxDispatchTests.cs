namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using global::Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator;

public class NestedOutboxDispatchTests
{
    [Theory]
    [InlineData("success", false)]
    [InlineData("failure", false)]
    [InlineData("persist-failure", false)]
    [InlineData("commit-failure", false)]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    [InlineData("persist-failure", true)]
    [InlineData("commit-failure", true)]
    public async Task Handle_NestedAggregate_OutboxAloneCapturesOwningCommitEvents(string outcome, bool tracked)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseSqlite(connection).AddTrellisInterceptors().AddTrellisOutboxInterceptor().Options;
        await using var context = new OutboxTestDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);
        var id = ThingId.NewUniqueV7();
        if (outcome == "commit-failure")
        {
            context.Things.Add(Thing.Create(id, "existing", DateTimeOffset.UnixEpoch));
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();
        }

        var originalRows = await context.Set<OutboxMessage>().CountAsync(ct);
        var unitOfWork = new EfUnitOfWork<OutboxTestDbContext>(context);
        var publisher = new RecordingPublisher();
        var thing = Thing.Create(id, "nested", DateTimeOffset.UnixEpoch);
        IPipelineBehavior<OuterCommand, Result<Trellis.Unit>> outer = tracked
            ? new TrackedAggregateDomainEventDispatchBehavior<OuterCommand, Result<Trellis.Unit>>(unitOfWork, publisher,
                NullLogger<TrackedAggregateDomainEventDispatchBehavior<OuterCommand, Result<Trellis.Unit>>>.Instance)
            : new DomainEventDispatchBehavior<OuterCommand, Result<Trellis.Unit>>(publisher,
                NullLogger<DomainEventDispatchBehavior<OuterCommand, Result<Trellis.Unit>>>.Instance);
        IPipelineBehavior<InnerCommand, Result<Thing>> inner = tracked
            ? new TrackedAggregateDomainEventDispatchBehavior<InnerCommand, Result<Thing>>(unitOfWork, publisher,
                NullLogger<TrackedAggregateDomainEventDispatchBehavior<InnerCommand, Result<Thing>>>.Instance)
            : new DomainEventDispatchBehavior<InnerCommand, Result<Thing>>(publisher,
                NullLogger<DomainEventDispatchBehavior<InnerCommand, Result<Thing>>>.Instance);
        var outerTransaction = new TransactionalCommandBehavior<OuterCommand, Result<Trellis.Unit>>(unitOfWork);
        var innerTransaction = new TransactionalCommandBehavior<InnerCommand, Result<Thing>>(unitOfWork);

        var result = await outer.Handle(new OuterCommand(), (message, token) => outerTransaction.Handle(message,
            async (_, innerToken) =>
            {
                var nested = await inner.Handle(new InnerCommand(), (command, commandToken) =>
                    innerTransaction.Handle(command, (_, _) =>
                    {
                        context.Things.Add(thing);
                        return ValueTask.FromResult(Result.Ok(thing));
                    }, commandToken), innerToken);
                nested.IsSuccess.Should().BeTrue();
                thing.UncommittedEvents().Should().ContainSingle();
                publisher.Count.Should().Be(0);
                (await context.Set<OutboxMessage>().CountAsync(innerToken)).Should().Be(originalRows);
                var error = new Error.Conflict(Resource: null, Code: "test.rejected");
                return outcome switch
                {
                    "failure" => Result.Fail<Trellis.Unit>(error),
                    "persist-failure" => Result.FailAfterCommit<Trellis.Unit>(error),
                    _ => Result.Ok(),
                };
            }, token), ct);

        result.IsSuccess.Should().Be(outcome == "success");
        publisher.Count.Should().Be(0, "durable outbox capture must remain the only dispatch path");
        var committed = outcome is "success" or "persist-failure";
        (await context.Set<OutboxMessage>().CountAsync(ct)).Should().Be(originalRows + (committed ? 1 : 0));
        thing.UncommittedEvents().Count.Should().Be(committed ? 0 : 1);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("hook-failure")]
    [InlineData("persist-failure")]
    [InlineData("commit-failure")]
    public async Task Handle_PreCommitHook_CapturesIntroducedAggregateOnlyWithSuccessfulCommit(string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseSqlite(connection).AddTrellisInterceptors().AddTrellisOutboxInterceptor().Options;
        await using var context = new OutboxTestDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);
        var id = ThingId.NewUniqueV7();
        if (outcome == "commit-failure")
        {
            context.Things.Add(Thing.Create(id, "existing", DateTimeOffset.UnixEpoch));
            (await context.SaveChangesResultUnitAsync(ct)).IsSuccess.Should().BeTrue();
            context.ChangeTracker.Clear();
        }

        var originalRows = await context.Set<OutboxMessage>().CountAsync(ct);
        var originalThings = await context.Things.CountAsync(ct);
        var unitOfWork = new EfUnitOfWork<OutboxTestDbContext>(context);
        var publisher = new RecordingPublisher();
        var handlerAggregate = Thing.Create(id, "handler-added", DateTimeOffset.UnixEpoch);
        var hookAggregate = Thing.Create(ThingId.NewUniqueV7(), "hook-added", DateTimeOffset.UnixEpoch);
        var hook = new StagingHook(context, hookAggregate, outcome == "hook-failure");
        var transaction = new TransactionalCommandBehavior<OuterCommand, Result<Trellis.Unit>>(unitOfWork, [hook]);
        var dispatch = new TrackedAggregateDomainEventDispatchBehavior<OuterCommand, Result<Trellis.Unit>>(
            unitOfWork, publisher,
            NullLogger<TrackedAggregateDomainEventDispatchBehavior<OuterCommand, Result<Trellis.Unit>>>.Instance);

        var result = await dispatch.Handle(new OuterCommand(), (message, token) => transaction.Handle(message,
            (_, _) =>
            {
                context.Things.Add(handlerAggregate);
                return ValueTask.FromResult(outcome == "persist-failure"
                    ? Result.FailAfterCommit(new Error.Conflict("command.rejected"))
                    : Result.Ok());
            }, token), ct);

        var committed = outcome is "success" or "persist-failure";
        result.IsSuccess.Should().Be(outcome == "success");
        (await context.Things.CountAsync(ct)).Should().Be(originalThings + (committed ? 2 : 0));
        var rows = await context.Set<OutboxMessage>().AsNoTracking().ToListAsync(ct);
        rows.Should().HaveCount(originalRows + (committed ? 2 : 0));
        rows.Count(row => row.Payload.Contains("hook-added", StringComparison.Ordinal)).Should().Be(committed ? 1 : 0);
        handlerAggregate.UncommittedEvents().Count.Should().Be(committed ? 0 : 1);
        hookAggregate.UncommittedEvents().Count.Should().Be(committed ? 0 : 1);
        publisher.Count.Should().Be(0, "outbox capture must remain the only dispatch path");
        if (committed)
            ((ITrackedAggregateSource)unitOfWork).CommittedAggregates.Should().Contain(hookAggregate);
        else
            ((ITrackedAggregateSource)unitOfWork).CommittedAggregates.Should().BeEmpty();
    }

    private sealed class StagingHook(OutboxTestDbContext context, Thing aggregate, bool reject)
        : ICommandPreCommitHook<OuterCommand, Result<Trellis.Unit>>
    {
        public ValueTask<Result<Trellis.Unit>> BeforeCommitAsync(
            OuterCommand message, Result<Trellis.Unit> response, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Things.Add(aggregate);
            return ValueTask.FromResult(reject
                ? Result.FailAfterCommit(new Error.Conflict("audit.rejected"))
                : Result.Ok());
        }
    }

    private sealed record OuterCommand : ICommand<Result<Trellis.Unit>>;
    private sealed record InnerCommand : ICommand<Result<Thing>>;

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public int Count { get; private set; }
        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }
}