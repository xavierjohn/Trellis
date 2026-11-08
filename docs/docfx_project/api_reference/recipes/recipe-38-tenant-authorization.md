---
name: recipe-38-tenant-authorization
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows tenant-scoped resource authorization with a typed actor attribute and a per-command scope check. Use when enforcing tenant isolation on a command."
---
# Recipe 38 — Tenant-scoped resource authorization with a typed actor attribute

**Requires:** `Trellis.Core`, plus `Trellis.Authorization`, `Trellis.Mediator`.

**Problem.** A multi-tenant service must reject any command whose target row belongs to a different tenant than the caller. The check is the same in every command — read the tenant from the actor's claim, compare it to the resource's tenant — so it gets copy-pasted, and each copy re-does the `actor.GetAttribute("tid")` string lookup plus a manual `TenantId.TryCreate(...)`. A shared base class is tempting, but tenant isolation is **policy**, not configuration: a base type either rigidly assumes one scope shape or grows so many hooks it saves nothing, and it hides a security decision inside a hierarchy. Keep the rule explicit per command; remove only the parsing ceremony.

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

// String-backed tenant id, sourced from the actor's "tid" claim. DocumentId and TenantDocument
// (the loaded resource, carrying the tenant it belongs to) are your own types — see Recipe 1.
public sealed partial class TenantId : RequiredString<TenantId>;

public sealed record ArchiveDocumentCommand(DocumentId DocumentId)
    : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<TenantDocument>, IIdentifyResource<TenantDocument, DocumentId>
{
    public DocumentId GetResourceId() => DocumentId;

    // The per-command scope check. No base class — the rule stays explicit and lives with the
    // command. Actor.TryGetAttribute<TenantId> parses the "tid" claim through the VO's IParsable (validating via TryCreate),
    // so the gate deny-closes (Forbidden) on a missing, malformed, or mismatched tenant claim.
    public IResult Authorize(Actor actor, TenantDocument resource) =>
        Result.Ensure(
            actor.TryGetAttribute<TenantId>(ActorAttributes.TenantId, out var tenant) && tenant == resource.TenantId,
            () => new Error.Forbidden(
                Code: "tenant.isolation",
                Resource: ResourceRef.For<TenantDocument>(resource.Id)));
}

// When a handler needs the tenant as a Result to compose with other steps, the Result-returning
// overload carries the value object forward (or a failed Result whose error field is the key):
Result<TenantId> tenant = actor.GetRequiredAttribute<TenantId>(ActorAttributes.TenantId);
```

**What it shows.** `Actor.GetRequiredAttribute<TVo>(key)` and `Actor.TryGetAttribute<TVo>(key, out vo)` parse an actor attribute (an ABAC claim) into a Trellis value object through its `IParsable` implementation — the same validation that guards request input now guards claim-sourced values, with no `GetAttribute(...)` + `TryCreate(...)` boilerplate and no magic strings. `TryGetAttribute` is the natural fit for an authorization gate (deny-close on `false`); `GetRequiredAttribute` returns `Result<TVo>` for railway composition in a handler, failing with an `Error.InvalidInput` whose field is the attribute key when the claim is absent or invalid. The value object can be any source-generated `Required*` VO — `string`-, `Guid`-, or `int`-backed — since the generator makes each one `IParsable`, so a Guid-backed tenant id works as naturally as a string claim. Wiring is identical to [Recipe 7](recipe-07-resource-authorization.md); the tenant check lives in `Authorize(actor, resource)`, which the resource-authorization pipeline runs after the loader produces the resource.

**Why no `TenantScopedCommand` base type.** The variable part of a tenant guard — what "scope" means, which resources are scoped, how the resource exposes its tenant — is domain policy. A reusable base class is either too rigid or needs enough hooks to beat the three explicit lines it replaces, and inheritance hides a security decision. The typed accessor removes the *ceremony* (parsing) while leaving the *policy* (the comparison) visible and per-command.
