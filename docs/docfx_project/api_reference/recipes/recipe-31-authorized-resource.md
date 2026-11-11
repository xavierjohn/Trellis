---
name: recipe-31-authorized-resource
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows IAuthorizedResource so a command authorizes and mutates one resource with a single load. Use when avoiding a duplicate load in pipeline and handler."
---
# Recipe 31 — Avoid duplicate load with `IAuthorizedResource<TCommand, TResource>`

**Requires:** `Trellis.Core`, plus `Trellis.Authorization`, `Trellis.Mediator`.

## Contents

- Problem
- Fix
- Command and loader are unchanged
- Actor/resource parameter alternative
- Via commands
- When NOT to inject the accessor
- Concurrency
- Failure modes
- What it shows
- Related recipes

**Problem.** A command implements `IAuthorizeResource<Order>` (or `IAuthorizeResourceVia<Team>`); the resource-authorization pipeline loads the resource to run `Authorize(actor, resource)`; then the handler loads the **same** resource again from its repository to mutate it. For non-EF stores this is wasted I/O — a doubled CosmosDB read (and doubled RU billing), a doubled Dapper roundtrip, a doubled outbound HTTP call. Even for EF (where the change-tracker identity map returns the same tracked instance) the second LINQ query still fires.

**Fix.** Inject `IAuthorizedResource<TCommand, TResource>` and call `GetRequiredResource()` instead of re-fetching via the repository. The framework returns the **same instance** the loader produced for this dispatch.

```csharp
// ❌ Wrong — handler reloads the resource the pipeline already loaded.
public sealed class CancelOrderHandler(IOrderRepository orders) // duplicate-load source
    : ICommandHandler<CancelOrderCommand, Result<Trellis.Unit>>
{
    public async ValueTask<Result<Trellis.Unit>> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        var found = await orders.FindByIdAsync(cmd.OrderId, ct);   // SECOND lookup — wasteful
        if (!found.TryGetValue(out var order))
            return Result.Fail<Trellis.Unit>(new Error.NotFound(ResourceRef.For<Order>(cmd.OrderId)));
        order.Cancel();
        return Result.Ok(Trellis.Unit.Value);
    }
}

// ✅ Correct — handler reads the loaded resource from the accessor.
public sealed class CancelOrderHandler(IAuthorizedResource<CancelOrderCommand, Order> authorized)
    : ICommandHandler<CancelOrderCommand, Result<Trellis.Unit>>
{
    public ValueTask<Result<Trellis.Unit>> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        // The pipeline loaded this Order to run cmd.Authorize(actor, order); we mutate the
        // SAME instance. No second DB roundtrip; if the loader returned a tracked entity
        // and a unit of work is registered, this mutation is saved when the command's unit-of-work
        // scope commits after the handler succeeds.
        authorized.GetRequiredResource().Cancel();
        return new(Result.Ok(Trellis.Unit.Value));
    }
}
```

**Command and loader are unchanged.** Existing `IAuthorizeResource<Order>` + `IIdentifyResource<Order, OrderId>` + `SharedResourceLoaderById<Order, OrderId>` registrations stay exactly as in [Recipe 7](recipe-07-resource-authorization.md). The accessor is **auto-registered** by `AddResourceAuthorization(...)` for every closed `(TMessage, TResource)` pair the scan sees, and by the explicit `AddResourceAuthorization<TMessage, TResource, TResponse>()` / `AddRelatedResourceAuthorization<...>()` helpers for AOT consumers. No additional composition-root call is required.

**Actor/resource parameter alternative.** When business logic also needs the checked
actor, the accessor constructor can be replaced by a parameterless framework base:

```csharp
public sealed class CancelOrderHandler
    : ActorResourceCommandHandler<CancelOrderCommand, Order, Result<Trellis.Unit>>
{
    protected override ValueTask<Result<Trellis.Unit>> Handle(
        CancelOrderCommand command, Actor actor, Order order, CancellationToken cancellationToken)
    {
        order.Cancel();
        return new(Result.Ok());
    }
}
```

Keep genuine business constructor dependencies; remove only actor/accessor plumbing.
The normal entry checks every declared gate and binds the resource to that exact dispatch,
so an inner handler cannot borrow an outer dispatch's resource. Via commands use the
corresponding `ActorResourceViaCommandHandler<TCommand,TLeaf,TOwner,TResponse>` and
receive the same leaf, not owners. Handler tests send the command through Mediator with
`TestActorProvider` and fake loaders/business dependencies; the actor/resource hook is
protected and cannot be invoked as a public bypass. All mutation-readiness and TOCTOU cautions
below apply equally to base-supplied resources.

**Via commands** (multi-hop authorization via `IAuthorizeResourceVia<TOwner>`) expose the **leaf** through the accessor — the resource the message identifies via `IIdentifyResource<TLeaf, TLeafId>`, which is the typical mutation target. The owner accessor is intentionally **not** exposed; handlers that need owner state read it from their repository.

```csharp
public sealed record UploadScorecardCommand(MatchId MatchId, Scorecard Scorecard)
    : ICommand<Result<Trellis.Unit>>,
      IIdentifyResource<Match, MatchId>,
      IAuthorizeResourceVia<Team>
{
    public MatchId GetResourceId() => MatchId;
    public Trellis.IResult Authorize(Actor actor, IReadOnlyList<Team> teams) =>
        Result.Ensure(teams.Any(t => t.CreatedByActorId == actor.Id),
            static () => new Error.Forbidden("team.not-owner"));
}

public sealed class UploadScorecardHandler(
    IAuthorizedResource<UploadScorecardCommand, Match> match)   // LEAF accessor
    : ICommandHandler<UploadScorecardCommand, Result<Trellis.Unit>>
{
    public ValueTask<Result<Trellis.Unit>> Handle(UploadScorecardCommand cmd, CancellationToken ct)
    {
        match.GetRequiredResource().UploadScorecard(cmd.Scorecard);   // mutate the leaf
        return new(Result.Ok(Trellis.Unit.Value));
    }
}
```

**When NOT to inject the accessor.** The framework cannot enforce mutation-readiness — it just returns whatever the loader returned. If your loader returns any of the following, **do not inject the accessor**; reload via your repository instead:

- A projection type (e.g. `OrderHeader` for cheap authorization, not the full `Order` aggregate).
- A no-tracking EF entity that the handler must mutate (the mutation will not persist).
- A stale read-replica POCO when the handler needs strong consistency.
- An HTTP DTO that cannot be persisted back through any local repository.

In those cases the loader's job is "decide who owns the resource for the authorization check"; the handler's job is "fetch the canonical mutation-ready aggregate". They are different shapes and the accessor would couple them incorrectly.

**Concurrency.** The accessor is scoped to the current dispatch: nested `mediator.Send` calls see their own resource and the outer one is restored afterwards, concurrent `Task.WhenAll` dispatches of the same closed pair within one DI scope do not see each other's resource, and a read from a task that outlives the dispatch throws. Implementation uses a per-async-flow linked frame list with an `IsActive` flag — each push allocates a new frame (no shared mutable state between sibling forks), and dispose flips the frame's `IsActive` flag (visible to orphan child tasks that captured the frame at fork time but outlived the parent dispatch). The framework guarantees an orphan task cannot read the resource after the parent's dispatch ends. (Verified by `AuthorizedResourceHolderTests.ParallelPushes_OfDifferentResources_DoNotCrossContaminate` and `OrphanChildTask_CapturesFrameAtFork_ButReadsNothingAfterParentDispose`.)

**Failure modes.** `GetRequiredResource()` throws `InvalidOperationException` outside a populated dispatch — typical causes:
- the handler was invoked directly (e.g. from a unit test) without going through the mediator pipeline;
- the message lacks resource-authorization registration (`AddResourceAuthorization` was never called for it);
- it is read from a task that outlives the dispatch, or for a message/resource pair the pipeline did not authorize. (Failed authentication, a failed loader and a denied `Authorize` never reach the handler, so they cannot cause this.)

For optional reads use `TryGetResource(out var resource)` which returns `false` instead of throwing.

**What it shows.** Eliminates the duplicate load that motivated the accessor. For non-EF stores (CosmosDB, Dapper, HTTP-backed loaders) it removes the handler's second load of the same resource, which is one fewer round trip per command. For an EF query-based loader it also skips the second LINQ query (the identity map deduplicates entity instances, not the SQL round trip). The framework guarantee is identity, not mutation-readiness — the caution above is the caller's responsibility.

**Related recipes.** [Recipe 7](recipe-07-resource-authorization.md) for the authorization model itself; [Recipe 24](recipe-24-indirect-authorization.md) for the via case the accessor composes with.
