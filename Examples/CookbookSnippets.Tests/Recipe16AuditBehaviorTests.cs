namespace CookbookSnippets.Tests;

using CookbookSnippets.Recipe16;
using global::Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Testing;

public class Recipe16AuditBehaviorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InsideTransactionBehavior_Success_StagesAuditInSameSaveBeforeTrackedDispatch()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)));

        var order = result.Unwrap();
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(1);
        var stored = await fixture.Db.AuditEntries.SingleAsync(Token);
        stored.OrderId.Should().Be(order.Id);
        stored.Action.Should().Be("order.created");
        fixture.Db.Orders.Local.Should().ContainSingle().Which.Should().BeSameAs(order);
        fixture.Probe.SavingCalls.Should().Be(1);
        fixture.Probe.SuccessfulSaves.Should().Be(1);
        fixture.CommittedAggregates.Should().Contain(stored);
        stored.UncommittedEvents().Should().BeEmpty();
        fixture.Publisher.Events.Should().ContainSingle().Which.Should().BeOfType<AuditRecorded>()
            .Which.AuditEntryId.Should().Be(stored.Id);
        fixture.Publisher.StoredAuditsAtDispatch.Should().Equal([1]);
    }

    [Fact]
    public async Task InsideTransactionBehavior_MissingAudit_RejectsBeforeAnySave()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), (message, _) =>
        {
            var order = Order.TryCreate(message.Total).Unwrap();
            fixture.Db.Orders.Add(order);
            return Result.Ok(order).AsValueTask();
        });

        var error = result.Should().BeFailureOfType<Error.Unexpected>().Which;
        error.Code.Should().Be("audit.not-recorded");
        error.FaultId.Should().NotBeNullOrWhiteSpace();
        fixture.Probe.SavingCalls.Should().Be(0);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(0);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(0);
        fixture.Db.AuditEntries.Local.Should().BeEmpty();
        fixture.Publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_PropagatedNestedFailure_SkipsOwningCommit()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), async (message, token) =>
        {
            var nested = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(20m)), (inner, _) =>
            {
                var order = Order.TryCreate(inner.Total).Unwrap();
                fixture.Db.Orders.Add(order);
                return Result.Ok(order).AsValueTask();
            });
            nested.Should().HaveErrorCode("audit.not-recorded");
            return nested;
        });

        result.Should().HaveErrorCode("audit.not-recorded");
        fixture.Recorder.Recorded.Should().BeEmpty();
        fixture.Probe.SavingCalls.Should().Be(0);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(0);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(0);
        fixture.Publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_IgnoredNestedFailure_DoesNotPermanentlyRejectOwner()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), async (message, token) =>
        {
            var nested = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(20m)), (inner, _) =>
            {
                var order = Order.TryCreate(inner.Total).Unwrap();
                fixture.Db.Orders.Add(order);
                return Result.Ok(order).AsValueTask();
            });
            nested.Should().HaveErrorCode("audit.not-recorded");
            return await fixture.Handler.Handle(message, token);
        });

        result.Should().BeSuccess();
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(2);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(1);
        fixture.Probe.SuccessfulSaves.Should().Be(1);
        fixture.Recorder.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_OrdinaryHandlerFailure_SavesNeitherBusinessNorAuditRows()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), (message, _) =>
        {
            fixture.Db.Orders.Add(Order.TryCreate(message.Total).Unwrap());
            return Result.Fail<Order>(new Error.Conflict("order.stale")).AsValueTask();
        });

        result.Should().HaveErrorCode("order.stale");
        fixture.Probe.SavingCalls.Should().Be(0);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(0);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(0);
        fixture.Publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_OrdinaryFailure_DiscardsPendingAuditBeforeNextCommand()
    {
        await using var fixture = await Fixture.CreateAsync();
        var message = new CreateAuditedOrderCommand(new Money(10m));
        var failed = await fixture.SendAsync(message, async (command, token) =>
        {
            (await fixture.Handler.Handle(command, token)).Should().BeSuccess();
            return Result.Fail<Order>(new Error.Conflict("order.rejected"));
        });
        failed.Should().HaveErrorCode("order.rejected");

        var next = await fixture.SendAsync(message, (command, _) =>
        {
            var order = Order.TryCreate(command.Total).Unwrap();
            fixture.Db.Orders.Add(order);
            return Result.Ok(order).AsValueTask();
        });

        next.Should().HaveErrorCode("audit.not-recorded");
        fixture.Probe.SavingCalls.Should().Be(0);
        fixture.Db.AuditEntries.Local.Should().BeEmpty();
        fixture.Publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_PersistOnFailure_WithPendingAudit_CommitsAndPreservesIntent()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), async (command, token) =>
        {
            (await fixture.Handler.Handle(command, token)).Should().BeSuccess();
            return Result.FailAfterCommit<Order>(new Error.Conflict("order.rejected"));
        });

        result.Should().HaveErrorCode("order.rejected");
        (result is IPersistOnFailure { PersistOnFailure: true }).Should().BeTrue();
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(1);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(1);
        fixture.Probe.SuccessfulSaves.Should().Be(1);
        fixture.Publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_PersistOnFailureWithoutAudit_ReturnsOrdinaryFailureWithoutSave()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), (message, _) =>
        {
            fixture.Db.Orders.Add(Order.TryCreate(message.Total).Unwrap());
            return Result.FailAfterCommit<Order>(new Error.Conflict("order.rejected")).AsValueTask();
        });

        result.Should().HaveErrorCode("audit.not-recorded");
        (result is IPersistOnFailure { PersistOnFailure: true }).Should().BeFalse();
        fixture.Probe.SavingCalls.Should().Be(0);
        fixture.Recorder.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_SecondInsertFails_RollsBackBothRowsAndSuppressesDispatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER reject_order_after_audit BEFORE INSERT ON Orders
            WHEN EXISTS (SELECT 1 FROM AuditEntries WHERE OrderId = NEW.Id)
            BEGIN SELECT RAISE(ABORT, 'second insert rejected'); END;
            CREATE TRIGGER reject_audit_after_order BEFORE INSERT ON AuditEntries
            WHEN EXISTS (SELECT 1 FROM Orders WHERE Id = NEW.OrderId)
            BEGIN SELECT RAISE(ABORT, 'second insert rejected'); END;
            """, Token);

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)));

        result.Should().BeFailure();
        fixture.Probe.SavingCalls.Should().Be(1);
        fixture.Probe.SuccessfulSaves.Should().Be(0);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(0);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(0);
        fixture.CommittedAggregates.Should().BeEmpty();
        fixture.Publisher.Events.Should().BeEmpty();
        fixture.Db.AuditEntries.Local.Should().ContainSingle().Which.UncommittedEvents().Should().ContainSingle();
    }

    [Fact]
    public async Task InsideTransactionBehavior_SameMessageNestedDispatch_PreservesParentCheckpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        var message = new CreateAuditedOrderCommand(new Money(10m));

        var result = await fixture.SendAsync(message, async (command, token) =>
        {
            var outer = await fixture.Handler.Handle(command, token);
            var pending = fixture.Recorder.Recorded.Single();
            pending.OrderId.Should().Be(outer.Unwrap().Id);

            (await fixture.SendAsync(command)).Should().BeSuccess();

            fixture.Recorder.Recorded.Should().ContainSingle().Which.Should().BeSameAs(pending);
            return outer;
        });

        result.Should().BeSuccess();
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(2);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(2);
        fixture.Probe.SuccessfulSaves.Should().Be(1);
        fixture.Recorder.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task InsideTransactionBehavior_SequentialCommands_KeepsEachDispatchAuditSeparate()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = (await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)))).Unwrap();
        var second = (await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(20m)))).Unwrap();

        first.Id.Should().NotBe(second.Id);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(2);
        (await fixture.Db.AuditEntries.Select(entry => entry.OrderId).ToListAsync(Token))
            .Should().BeEquivalentTo([first.Id, second.Id]);
        fixture.Probe.SuccessfulSaves.Should().Be(2);
        fixture.Publisher.Events.Should().HaveCount(2);
        fixture.Publisher.StoredAuditsAtDispatch.Should().Equal([1, 2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsideTransactionBehavior_NestedCommands_WaitsForOwningOutcomeBeforeSavingAndDispatching(bool outerSucceeds)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), async (message, token) =>
        {
            var nested = (await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(20m)))).Unwrap();
            fixture.Db.AuditEntries.Local.Single(entry => entry.OrderId == nested.Id)
                .UncommittedEvents().Should().ContainSingle();
            fixture.Probe.SavingCalls.Should().Be(0);
            fixture.Publisher.Events.Should().BeEmpty();
            return outerSucceeds
                ? await fixture.Handler.Handle(message, token)
                : Result.Fail<Order>(new Error.Conflict("outer.rejected"));
        });

        result.IsSuccess.Should().Be(outerSucceeds);
        fixture.Probe.SuccessfulSaves.Should().Be(outerSucceeds ? 1 : 0);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(outerSucceeds ? 2 : 0);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(outerSucceeds ? 2 : 0);
        fixture.Publisher.Events.Should().HaveCount(outerSucceeds ? 2 : 0);
        if (!outerSucceeds)
            fixture.Db.AuditEntries.Local.Should().ContainSingle().Which.UncommittedEvents().Should().ContainSingle();
    }

    private sealed class Fixture(
        SqliteConnection connection, ServiceProvider provider, AsyncServiceScope scope, SaveProbe probe)
        : IAsyncDisposable
    {
        public Recipe16DbContext Db => scope.ServiceProvider.GetRequiredService<Recipe16DbContext>();
        public IOrderAuditRecorder Recorder => scope.ServiceProvider.GetRequiredService<IOrderAuditRecorder>();
        public SaveProbe Probe { get; } = probe;
        public RecordingPublisher Publisher => scope.ServiceProvider.GetRequiredService<RecordingPublisher>();
        public ICommandHandler<CreateAuditedOrderCommand, Result<Order>> Handler =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateAuditedOrderCommand, Result<Order>>>();
        public IReadOnlyList<IAggregate> CommittedAggregates =>
            scope.ServiceProvider.GetRequiredService<ITrackedAggregateSource>().CommittedAggregates;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(Token);
            var probe = new SaveProbe();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<Recipe16DbContext>(options =>
                options.UseSqlite(connection).AddTrellisInterceptors().AddInterceptors(probe));
            Recipe16Demonstrator.AuditRegistration(services);
            services.AddTrackedAggregateDomainEventDispatch();
            services.AddScoped<RecordingPublisher>();
            services.AddScoped<IDomainEventPublisher>(provider => provider.GetRequiredService<RecordingPublisher>());
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
            var scope = provider.CreateAsyncScope();
            var fixture = new Fixture(connection, provider, scope, probe);
            await fixture.Db.Database.EnsureCreatedAsync(Token);
            return fixture;
        }

        public ValueTask<Result<Order>> SendAsync(
            CreateAuditedOrderCommand message,
            MessageHandlerDelegate<CreateAuditedOrderCommand, Result<Order>>? handler = null)
        {
            MessageHandlerDelegate<CreateAuditedOrderCommand, Result<Order>> next = handler ?? Handler.Handle;
            foreach (var behavior in scope.ServiceProvider
                         .GetServices<IPipelineBehavior<CreateAuditedOrderCommand, Result<Order>>>().Reverse())
            {
                var inner = next;
                next = (command, token) => behavior.Handle(command, inner, token);
            }

            return next(message, Token);
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class SaveProbe : SaveChangesInterceptor
    {
        public int SavingCalls { get; private set; }
        public int SuccessfulSaves { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SavingCalls++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            SuccessfulSaves++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingPublisher(Recipe16DbContext db) : IDomainEventPublisher
    {
        public List<IDomainEvent> Events { get; } = [];
        public List<int> StoredAuditsAtDispatch { get; } = [];

        public async ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            cancellationToken.Should().Be(CancellationToken.None);
            StoredAuditsAtDispatch.Add(await db.AuditEntries.CountAsync(cancellationToken));
            Events.Add(domainEvent);
        }
    }
}
