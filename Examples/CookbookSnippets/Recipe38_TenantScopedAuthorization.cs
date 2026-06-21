// Cookbook Recipe 38 — Tenant-scoped resource authorization with a typed actor attribute.
namespace CookbookSnippets.Recipe38;

using global::Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

// String-backed tenant identifier, sourced from the actor's "tid" claim. Because it is a
// RequiredString<TenantId>, Actor.GetRequiredAttribute<TenantId>/TryGetAttribute<TenantId> can
// parse the claim string through its TryCreate, applying the same validation as request input.
public sealed partial class TenantId : RequiredString<TenantId>;

public sealed partial class DocumentId : RequiredGuid<DocumentId>;

// The loaded resource the authorization pipeline hands to Authorize(actor, resource). In a real
// app this is the aggregate (or a projection) produced by the resource loader; it carries the
// tenant the row belongs to.
public sealed class TenantDocument
{
    public required DocumentId Id { get; init; }
    public required TenantId TenantId { get; init; }
}

public sealed record ArchiveDocumentCommand(DocumentId DocumentId)
    : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<TenantDocument>, IIdentifyResource<TenantDocument, DocumentId>
{
    public DocumentId GetResourceId() => DocumentId;

    // TENANT ISOLATION — the per-command scope check that would otherwise be copy-pasted across
    // every command in every service. There is deliberately no base class: the rule stays explicit
    // and lives with the command. The typed accessor removes the GetAttribute(...) + TenantId.TryCreate(...)
    // ceremony, and the gate deny-closes (Forbidden) on a missing, malformed, or mismatched tenant claim.
    public Trellis.IResult Authorize(Actor actor, TenantDocument resource) =>
        actor.TryGetAttribute<TenantId>(ActorAttributes.TenantId, out var tenant) && tenant == resource.TenantId
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden(
                PolicyId: "tenant.isolation",
                Resource: ResourceRef.For<TenantDocument>(resource.Id)));
}

internal static class Recipe38TenantSurface
{
    // When a handler needs the tenant as a Result to compose with other steps (railway style),
    // GetRequiredAttribute returns Result<TenantId> instead of a bool — a missing or invalid claim
    // becomes a failed Result whose error field is the attribute key.
    public static Result<TenantId> ReadTenant(Actor actor) =>
        actor.GetRequiredAttribute<TenantId>(ActorAttributes.TenantId);
}
