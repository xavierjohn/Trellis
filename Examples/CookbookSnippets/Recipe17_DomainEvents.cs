// Cookbook Recipe 17 — Defining custom domain events: OccurredAt is the only timestamp.
namespace CookbookSnippets.Recipe17;

using System;
using System.Collections.Generic;
using Trellis;
using Trellis.Authorization;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed partial class TrackingNumber : RequiredString<TrackingNumber>;

public sealed class Money(decimal amount) : ValueObject
{
    public decimal Amount { get; } = amount;

    protected override void GetEqualityComponents(ref EqualityComponents components)
        => components.Add(Amount);
}

public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new();
    public static readonly OrderStatus Submitted = new();
}

public sealed record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderApproved(OrderId OrderId, ActorId ApprovedBy, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderShipped(OrderId OrderId, TrackingNumber Tracking, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    public Money Total { get; private set; } = default!;
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public DateTimeOffset? SubmittedAt { get; private set; }

    private Order(OrderId id) : base(id) { }

    public static Result<Order> TryCreate(OrderId? id, Money? total) =>
        id.ToResult(Error.InvalidInput.ForField("id", "validation.error", "Order id is required."))
            .Combine(total.ToResult(Error.InvalidInput.ForField("total", "validation.error", "Total is required.")))
            .Map((id, total) => new Order(id) { Total = total });

    public Result<Order> Submit(TimeProvider clock)
    {
        var occurredAt = clock.GetUtcNow();
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft, Error.InvalidInput.ForRule("order.already-submitted", "Already submitted"))
            .Tap(_ =>
            {
                Status = OrderStatus.Submitted;
                SubmittedAt = occurredAt;
                DomainEvents.Add(new OrderSubmitted(Id, Total, occurredAt));
            });
    }
}

internal static class Recipe17Demonstrator
{
    public static void DomainEventSurface(OrderSubmitted submitted, Actor approver, TimeProvider clock)
    {
        DateTimeOffset occurredAt = ((IDomainEvent)submitted).OccurredAt;
        DateTimeOffset now = clock.GetUtcNow();
        // Actors come from the authenticated request, not from a generator. Pass the
        // approver's typed ActorId through directly.
        OrderApproved approved = new(submitted.OrderId, approver.Id, now);

        _ = (occurredAt, approved);
    }
}

#if FALSE
// Wrong — omitting OccurredAt does not implement IDomainEvent.
// public sealed record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset SubmittedAt) : IDomainEvent;

// Wrong — a semantic timestamp duplicates the canonical OccurredAt timestamp.
// public sealed record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset SubmittedAt, DateTimeOffset OccurredAt) : IDomainEvent;
#endif