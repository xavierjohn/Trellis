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
                var error = new Error.Conflict(null, "test.rejected");
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
