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

public sealed record PlaceOrderCommand(System.Guid OrderId, decimal Amount, string Currency, string OwnerId)
    : ICommand<Result<OrderId>>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3);
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}

public sealed class PlaceOrderHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand, Result<OrderId>>
{
    public ValueTask<Result<OrderId>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken) =>
        new(Result.Combine(
                OrderId.TryCreate(command.OrderId),
                CurrencyCode.TryCreate(command.Currency).Map(c => new Money(command.Amount, c)),
                ActorId.TryCreate(command.OwnerId))
            .Bind((id, money, ownerId) => Order.TryCreate(id, money, ownerId))
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
                        ResourceRef.For<Order>(id), "already_exists")),
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