// Cookbook Recipe 16 — Unit of work in handlers: Add staging vs immediate SaveAsync.
namespace CookbookSnippets.Recipe16;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.FluentValidation;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;
using Trellis.ServiceDefaults;
using Trellis.Testing;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed record Money(decimal Amount);

public sealed record CreateOrderCommand(Money Total) : ICommand<Result<OrderId>>;

public sealed class Order : Aggregate<OrderId>
{
    public Money Total { get; private set; } = default!;

    private Order(OrderId id) : base(id) { }

    public static Result<Order> TryCreate(Money? total) =>
        Result.EnsureNotNull(total, "total", "Total is required.")
            .Map(t => new Order(OrderId.NewUniqueV4()) { Total = t });
}

public interface IOrderRepository
{
    void Add(Order order);
    void Remove(Order order);
    Task<Result<Trellis.Unit>> RemoveByIdAsync(OrderId id, CancellationToken ct = default);
}

public sealed class EfOrderRepository(DbContext context) : RepositoryBase<Order, OrderId>(context), IOrderRepository;

public sealed class CreateOrderHandler(IOrderRepository repo)
    : ICommandHandler<CreateOrderCommand, Result<OrderId>>
{
    public ValueTask<Result<OrderId>> Handle(CreateOrderCommand command, CancellationToken cancellationToken) =>
        Order.TryCreate(command.Total)
            .Tap(repo.Add)
            .Map(o => o.Id)
            .AsValueTask();
}

public sealed partial class AuditEntryId : RequiredGuid<AuditEntryId>;

public sealed record AuditRecorded(AuditEntryId AuditEntryId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class AuditEntry : Aggregate<AuditEntryId>
{
    public OrderId OrderId { get; private set; } = default!;
    public string Action { get; private set; } = string.Empty;

    private AuditEntry(AuditEntryId id) : base(id) { }

    public static AuditEntry ForOrderCreated(OrderId orderId, TimeProvider clock)
    {
        var entry = new AuditEntry(AuditEntryId.NewUniqueV7(clock))
        {
            OrderId = orderId,
            Action = "order.created",
        };
        entry.DomainEvents.Add(new AuditRecorded(entry.Id, clock.GetUtcNow()));
        return entry;
    }
}

public sealed record AuditedOrderOutcome(OrderId OrderId, IReadOnlyList<AuditEntry> AuditEntries);

public sealed record CreateAuditedOrderCommand(Money Total) : ICommand<Result<AuditedOrderOutcome>>;

public sealed class CreateAuditedOrderHandler(IOrderRepository orders, TimeProvider clock)
    : ICommandHandler<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>>
{
    public ValueTask<Result<AuditedOrderOutcome>> Handle(
        CreateAuditedOrderCommand command, CancellationToken cancellationToken) =>
        Order.TryCreate(command.Total)
            .Tap(orders.Add)
            .Map(order => new AuditedOrderOutcome(order.Id, [AuditEntry.ForOrderCreated(order.Id, clock)]))
            .AsValueTask();
}

public interface IAuditEntryRepository
{
    void Add(AuditEntry entry);
}

public sealed class EfAuditEntryRepository(DbContext context)
    : RepositoryBase<AuditEntry, AuditEntryId>(context), IAuditEntryRepository;

public sealed partial class CreateOrderAuditHook(
    IAuditEntryRepository audits, ILogger<CreateOrderAuditHook> logger)
    : ICommandPreCommitHook<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>>
{
    public ValueTask<Result<Trellis.Unit>> BeforeCommitAsync(
        CreateAuditedOrderCommand message,
        Result<AuditedOrderOutcome> response,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.TryGetValue(out var outcome, out var error))
            return Result.Fail(error).AsValueTask();

        if (outcome.AuditEntries.Count == 0)
        {
            var faultId = Guid.NewGuid().ToString("N");
            LogMissingAudit(logger, faultId);
            return Result.Fail(new Error.Unexpected("audit.not-recorded", faultId)
            {
                Detail = "An unexpected error occurred while processing the request.",
            }).AsValueTask();
        }

        foreach (var entry in outcome.AuditEntries)
            audits.Add(entry);

        return Result.Ok().AsValueTask();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order creation recorded no audit entry; commit refused. Fault {FaultId}.")]
    private static partial void LogMissingAudit(ILogger logger, string faultId);
}

public sealed class Recipe16DbContext(DbContextOptions<Recipe16DbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<Recipe16DbContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>().Property(order => order.Total)
            .HasConversion(total => total.Amount, amount => new Money(amount));
}

internal static class Recipe16Demonstrator
{
    public static void RepositoryBaseStagesWithoutSaving(EfOrderRepository repo, Order order)
    {
        repo.Add(order);
        repo.Remove(order);
        Task<Result<Trellis.Unit>> removed = repo.RemoveByIdAsync(order.Id);

        _ = removed;
    }

    public static Task<Result<int>> ExplicitDbContextSaveResult(Recipe16DbContext db, CancellationToken ct) =>
        db.SaveChangesResultAsync(ct);

    public static IServiceCollection UnitOfWorkRegistration(IServiceCollection services) =>
        services
            .AddTrellisBehaviors()
            .AddTrellisFluentValidation(typeof(Recipe16Demonstrator).Assembly)
            .AddTrellisUnitOfWork<Recipe16DbContext>()
            .AddScoped<IOrderRepository, EfOrderRepository>();

    public static IServiceCollection AuditRegistration(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IOrderRepository>(provider =>
            new EfOrderRepository(provider.GetRequiredService<Recipe16DbContext>()));
        services.AddScoped<IAuditEntryRepository>(provider =>
            new EfAuditEntryRepository(provider.GetRequiredService<Recipe16DbContext>()));
        services.AddScoped<ICommandHandler<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>>,
            CreateAuditedOrderHandler>();
        return services.AddTrellis(options => options
            .UseCommandPreCommitHook<CreateAuditedOrderCommand, Result<AuditedOrderOutcome>, CreateOrderAuditHook>()
            .UseEntityFrameworkUnitOfWork<Recipe16DbContext>());
    }

    public static void TransactionalBehaviorTypePin()
    {
        Type behavior = typeof(TransactionalCommandBehavior<CreateOrderCommand, Result<OrderId>>);
        _ = behavior;
    }

    public static async Task FakeRepositorySetupAndConflictResult(Order order)
    {
        FakeRepository<Order, OrderId> orders =
            new FakeRepository<Order, OrderId>()
                .WithUniqueConstraint(o => o.Total);

        orders.Add(order);

        Result<Trellis.Unit> conflictResult = await orders.SaveAsync(order);
        _ = conflictResult;
    }
}

#if FALSE
// Wrong — explicit SaveChangesAsync in the handler bypasses TransactionalCommandBehavior.
// Wrong — SaveAsync is a FakeRepository test convenience; EF repositories should not expose it.
#endif