// Cookbook Recipe 16 — Unit of work in handlers: Add staging vs immediate SaveAsync.
namespace CookbookSnippets.Recipe16;

using System;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.FluentValidation;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;
using Trellis.Testing;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed record Money(decimal Amount);

public sealed record CreateOrderCommand(Money Total) : ICommand<Result<OrderId>>;

public sealed class Order : Aggregate<OrderId>
{
    public Money Total { get; private set; } = default!;

    private Order(OrderId id) : base(id) { }

    public static Result<Order> TryCreate(Money? total) =>
        total.ToResult(Error.InvalidInput.ForField("total", "validation.error", "Total is required."))
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

public sealed class Recipe16DbContext(DbContextOptions<Recipe16DbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
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