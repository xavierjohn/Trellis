---
name: recipe-02-command-handler
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows a command handler with FluentValidation and EF Core persistence. Use when writing a handler that validates and saves, or returning a synchronous Result chain from a Task API."
---
# Recipe 2 — Command + handler + FluentValidation + EF persistence

**Problem.** Wire a `PlaceOrderCommand` end-to-end: validation via FluentValidation, mediator handler that uses an EF repository, transactional commit on success.

```csharp
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;
using Trellis.Primitives;

public sealed record PlaceOrderRequest(Guid OrderId, decimal Amount, string Currency, string OwnerId);

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
    public PlaceOrderValidator()
    {
        RuleFor(x => x.Total.Amount)
            .LessThanOrEqualTo(10_000m)
            .WithMessage("Orders over 10,000 require manual approval.");
    }
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

[ApiController]
[Route("orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public ValueTask<ActionResult<OrderId>> Place([FromBody] PlaceOrderRequest request, CancellationToken ct) =>
        PlaceOrderCommand.TryCreate(request)
            .BindAsync(command => sender.Send(command, ct))
            .ToHttpResponseAsync()
            .AsActionResultAsync<OrderId>();
}

// Composition root
public static class OrdersDi
{
    public static IServiceCollection AddOrdersFeature(this IServiceCollection services) =>
        services
            .AddTrellisBehaviors()                              // canonical mediator behaviors (exception, tracing, logging, authorization, validation)
            .AddTrellisFluentValidation(typeof(PlaceOrderValidator).Assembly)
            .AddTrellisUnitOfWork<AppDbContext>()               // Innermost: commits on success
            .AddScoped<IOrderRepository, EfOrderRepository>();
}
```

**What it shows.** The mediator pipeline already runs `ValidationBehavior<TMessage, TResponse>` before the handler — `AddTrellisFluentValidation` plugs every `IValidator<T>` into it via the open-generic `IMessageValidator<T>` adapter. `AddTrellisUnitOfWork<TContext>` registers `TransactionalCommandBehavior<,>` *after* the others, so it lands innermost and commits only when the handler returns success. The handler itself is pure: no `try`/`catch`, no primitive parsing, no `await db.SaveChangesAsync()` — that's the unit of work's job.

> **Multiple independent resources in the handler?** Reach for [Recipe 21](recipe-21-parallel-loads.md) when the loads hit *different* stores (HTTP + DB, two upstream services, factory-created `DbContext`s). For two repository reads against the same scoped `DbContext`, stay sequential — that case races EF Core. The Recipe explains the rule and shows both shapes.

> **Validation ownership.** Primitive→VO conversion happens at the transport seam. FluentValidation validates VO-shaped commands for cross-field rules and business invariants. Handlers receive value-object-shaped commands and must not parse primitives. See [Recipe 18](recipe-18-dto-to-value-object.md) for the canonical controller-seam adapter.

**Anti-pattern → fix (TRLS010).**

```csharp
// WRONG — sync-over-async (.Result deadlocks) + throwing inside the Result chain.
.Bind(id => repo.FindAsync(id, ct).Result is { HasValue: true }
    ? throw new InvalidOperationException("already exists")  // TRLS010 + TRLS005
    : Result.Ok(id))

// FIX — MatchAsync awaits the Maybe carrier and dispatches without leaving the Result chain.
.BindAsync(id => repo.FindAsync(id, ct)
    .MatchAsync(
        some: _  => Result.Fail<OrderId>(new Error.Conflict(Resource: ResourceRef.For<Order>(id), Code: "already-exists")),
        none: () => Result.Ok(id)))
```
