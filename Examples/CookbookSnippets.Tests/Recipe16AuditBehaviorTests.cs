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
    public async Task PreCommitHook_Success_StagesAuditInSameSaveBeforeTrackedDispatch()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)));

        var outcome = result.Unwrap();
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(1);
        var stored = await fixture.Db.AuditEntries.SingleAsync(Token);
        stored.OrderId.Should().Be(outcome.OrderId);
        stored.Action.Should().Be("order.created");
        stored.Should().BeSameAs(outcome.AuditEntries.Single());
        fixture.Probe.SavingCalls.Should().Be(1);
        fixture.Probe.SuccessfulSaves.Should().Be(1);
        fixture.CommittedAggregates.Should().Contain(stored);
        stored.UncommittedEvents().Should().BeEmpty();
        fixture.Publisher.Events.Should().ContainSingle().Which.Should().BeOfType<AuditRecorded>()
            .Which.AuditEntryId.Should().Be(stored.Id);
        fixture.Publisher.StoredAuditsAtDispatch.Should().Equal([1]);
    }

    [Fact]
    public async Task PreCommitHook_MissingAudit_RejectsBeforeAnySave()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), (message, _) =>
        {
            var order = Order.TryCreate(message.Total).Unwrap();
            fixture.Db.Orders.Add(order);
            return Result.Ok(new AuditedOrderOutcome(order.Id, [])).AsValueTask();
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
    public async Task PreCommitHook_OrdinaryHandlerFailure_SavesNeitherBusinessNorAuditRows()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), (message, _) =>
        {
            fixture.Db.Orders.Add(Order.TryCreate(message.Total).Unwrap());
            return Result.Fail<AuditedOrderOutcome>(new Error.Conflict("order.stale")).AsValueTask();
        });

        result.Should().HaveErrorCode("order.stale");
        fixture.Probe.SavingCalls.Should().Be(0);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(0);
        (await fixture.Db.AuditEntries.CountAsync(Token)).Should().Be(0);
        fixture.Publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task PreCommitHook_SecondInsertFails_RollsBackBothRowsAndSuppressesDispatch()
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
    public async Task PreCommitHook_SequentialCommands_KeepsEachDispatchAuditSeparate()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = (await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)))).Unwrap();
        var second = (await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(20m)))).Unwrap();

        first.OrderId.Should().NotBe(second.OrderId);
        (await fixture.Db.Orders.CountAsync(Token)).Should().Be(2);
        (await fixture.Db.AuditEntries.Select(entry => entry.OrderId).ToListAsync(Token))
            .Should().BeEquivalentTo([first.OrderId, second.OrderId]);
        fixture.Probe.SuccessfulSaves.Should().Be(2);
        fixture.Publisher.Events.Should().HaveCount(2);
        fixture.Publisher.StoredAuditsAtDispatch.Should().Equal([1, 2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCommitHook_NestedCommands_WaitsForOwningOutcomeBeforeSavingAndDispatching(bool outerSucceeds)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(10m)), async (message, token) =>
        {
            var nested = (await fixture.SendAsync(new CreateAuditedOrderCommand(new Money(20m)))).Unwrap();
            nested.AuditEntries.Single().UncommittedEvents().Should().ContainSingle();
            fixture.Probe.SavingCalls.Should().Be(0);
            fixture.Publisher.Events.Should().BeEmpty();
            return outerSucceeds
                ? await fixture.Handler.Handle(message, token)
                : Result.Fail<AuditedOrderOutcome>(new Error.Conflict("outer.rejected"));
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
        public SaveProbe Probe { get; } = probe;
        public RecordingPublisher Publisher => scope.ServiceProvider.GetRequiredService<RecordingPublisher>();
        public ICommandHandler<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>> Handler =>
            scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>>>();
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

        public ValueTask<Result<AuditedOrderOutcome>> SendAsync(
            CreateAuditedOrderCommand message,
            MessageHandlerDelegate<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>>? handler = null)
        {
            MessageHandlerDelegate<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>> next = handler ?? Handler.Handle;
            foreach (var behavior in scope.ServiceProvider
                         .GetServices<IPipelineBehavior<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>>>().Reverse())
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
