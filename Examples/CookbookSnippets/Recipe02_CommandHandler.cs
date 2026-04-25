// Cookbook Recipe 2 — Command + handler + FluentValidation + EF persistence.
namespace CookbookSnippets.Recipe02;

using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Stubs;
using Trellis.EntityFrameworkCore;
using Trellis.FluentValidation;
using Trellis.Mediator;

public sealed record PlaceOrderCommand(System.Guid OrderId, decimal Amount, string Currency)
    : ICommand<Result<OrderId>>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3);
    }
}

public sealed class PlaceOrderHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand, Result<OrderId>>
{
    public ValueTask<Result<OrderId>> Handle(PlaceOrderCommand cmd, CancellationToken ct) =>
        new(OrderId.TryCreate(cmd.OrderId)
            .BindZip(id => CurrencyCode.TryCreate(cmd.Currency).Map(c => new Money(cmd.Amount, c)))
            .Bind(t => Order.Create(t.Item1, t.Item2))
            .Tap(repo.Add)
            .Map(o => o.Id));
}

// Composition root
public static class OrdersDi
{
    public static IServiceCollection AddOrdersFeature(this IServiceCollection services) =>
        services
            .AddTrellisBehaviors()
            .AddTrellisFluentValidation(typeof(PlaceOrderValidator).Assembly)
            .AddTrellisUnitOfWork<AppDbContext>()
            .AddScoped<IOrderRepository, EfOrderRepository>();
}

#if FALSE
// WRONG — sync-over-async (.Result deadlocks) + throwing inside the Result chain.
// Kept here for documentation only. Demonstrates TRLS010 and TRLS005.
internal static class AntiPattern
{
    public static Result<OrderId> Wrong(IOrderRepository repo, OrderId id, CancellationToken ct) =>
        Result.Ok(id)
            .Bind(id => repo.FindAsync(id, ct).Result is { HasValue: true }
                ? throw new System.InvalidOperationException("already exists")
                : Result.Ok(id));
}
#endif

// FIX — MatchAsync awaits the Maybe carrier and dispatches without leaving the Result chain.
public static class FixPattern
{
    public static Task<Result<OrderId>> EnsureNotExisting(
        IOrderRepository repo, OrderId id, CancellationToken ct) =>
        Task.FromResult(Result.Ok(id))
            .BindAsync(id => repo.FindAsync(id, ct)
                .MatchAsync(
                    some: _ => Result.Fail<OrderId>(new Error.Conflict(
                        new ResourceRef("Order", id.Value.ToString()), "already_exists")),
                    none: () => Result.Ok(id)));
}
