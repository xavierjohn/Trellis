// Cookbook Recipe 2 — Command + handler + FluentValidation + EF persistence.
namespace CookbookSnippets.Recipe02;

using System.Threading;
using System.Threading.Tasks;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Stubs;
using FluentValidation;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.FluentValidation;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;
using MonetaryAmount = Trellis.Primitives.MonetaryAmount;

public sealed record PlaceOrderRequest(System.Guid OrderId, decimal Amount, string Currency, string OwnerId);

public sealed record PlaceOrderCommand(OrderId OrderId, Money Total, ActorId OwnerId)
    : ICommand<Result<OrderId>>
{
    public static Result<PlaceOrderCommand> TryCreate(PlaceOrderRequest request) =>
        Result.Combine(
                OrderId.TryCreate(request.OrderId, nameof(request.OrderId)),
                MonetaryAmount.TryCreate(request.Amount, nameof(request.Amount)),
                CurrencyCode.TryCreate(request.Currency, nameof(request.Currency)),
                ActorId.TryCreate(request.OwnerId, nameof(request.OwnerId)))
            .Map((orderId, amount, currency, ownerId) =>
                new PlaceOrderCommand(orderId, new Money(amount.Value, currency), ownerId));
}

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator() =>
        RuleFor(x => x.Total.Amount)
            .LessThanOrEqualTo(10_000m)
            .WithMessage("Orders over 10,000 require manual approval.");
}

public sealed class PlaceOrderHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand, Result<OrderId>>
{
    public ValueTask<Result<OrderId>> Handle(PlaceOrderCommand cmd, CancellationToken cancellationToken) =>
        Order.TryCreate(cmd.OrderId, cmd.Total, cmd.OwnerId)
            .Tap(repo.Add)
            .Map(o => o.Id)
            .AsValueTask();
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
                        ResourceRef.For<Order>(id), "already-exists")),
                    none: () => Result.Ok(id)));
}

internal static class Recipe2BehaviorSurface
{
    public static void PipelineBehaviorTypes()
    {
        Type validationBehaviorType = typeof(ValidationBehavior<,>);
        Type messageValidatorType = typeof(IMessageValidator<>);
        Type transactionalBehaviorType = typeof(TransactionalCommandBehavior<,>);

        _ = (validationBehaviorType, messageValidatorType, transactionalBehaviorType);
    }
}