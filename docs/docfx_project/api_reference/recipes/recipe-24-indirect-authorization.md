---
name: recipe-24-indirect-authorization
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows authorization against a related resource several navigation hops away. Use when access depends on owner chains or fan-out."
---
# Recipe 24 — Indirect (multi-hop) resource authorization

**Requires:** `Trellis.Core`, plus `Trellis.Authorization`, `Trellis.Mediator`.

## Contents

- [Cricket fan-out — end to end](#cricket-fan-out--end-to-end)
- [Chain — `Match → Team → Tournament`](#chain--match--team--tournament)
- [AOT / explicit registration](#aot--explicit-registration)
- [What the framework rejects at startup](#what-the-framework-rejects-at-startup)
- [TOCTOU note](#toctou-note)

**Problem.** A command identifies a "leaf" resource by id (e.g. `MatchId`) but ownership/authorization is determined by a different resource one or more navigation hops away. Examples:

- **Cricket fan-out**: actor must own home OR away team to upload a scorecard. `Match → {HomeTeam, AwayTeam}`, OR-ownership.
- **Owner chain**: `Match → Team → Tournament` — authorize against tournament owner.
- **Org hierarchy**: `Document → Folder` where `Folder` carries the org-unit owning the document.

The naive workaround is a per-handler ownership guard (e.g. cricket's old `MatchOwnershipGuard`). It runs inside the handler body **after** the framework authorization pipeline has already passed, must be remembered in every handler, and is easy to forget — making it a recurring source of authorization gaps. `IAuthorizeResourceVia<TOwner>` moves the check into the framework pipeline as a declarative interface.

**Decision table.**

| Scenario | Recipe |
|---|---|
| **Owner-by-id check on the command's resource (the 90% case)** | `IAuthorizeResource<T>` + `IIdentifyResource<T, TId>` → framework reuses `SharedResourceLoaderById<T, TId>` ([Recipe 7](recipe-07-resource-authorization.md)) |
| Authorize against the resource the command identifies (custom loader) | `IAuthorizeResource<T>` + custom `IResourceLoader<TMessage, T>` ([Recipe 7](recipe-07-resource-authorization.md)) |
| Authorize against a single related resource one FK hop away | `IAuthorizeResourceVia<TOwner>` + `IIdentifyRelatedResource<TOwner, TOwnerId>` on the leaf |
| Authorize against a set of related resources (cricket fan-out, OR-ownership) | `IAuthorizeResourceVia<TOwner>` + `IIdentifyRelatedResources<TOwner, TOwnerId>` on the leaf |
| Authorize against a resource at the end of a chain (`Match → Team → Tournament`) | `IAuthorizeResourceVia<TFinalOwner>` + `IIdentifyRelatedResource<,>` declared on each intermediate entity |
| Authorize against a recursive hierarchy (org-unit ancestors, comment-thread parents) | Materialize the ancestor chain on the loaded aggregate (closure-table projection) and use zero-hop `IAuthorizeResource<T>`; OR a custom `IResourceLoader<TMessage, TProjection>` |
| Authorize against a composite shape (joins, projections, conditional paths, plural-in-middle) | `IResourceLoader<TMessage, TProjection>` returning a custom projection; put `IAuthorizeResource<TProjection>` on the command |

## Cricket fan-out — end to end

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed partial class MatchId : RequiredGuid<MatchId>;
public sealed partial class TeamId  : RequiredGuid<TeamId>;

public sealed class Match : Aggregate<MatchId>, IIdentifyRelatedResources<Team, TeamId>
{
    public TeamId HomeTeamId { get; }
    public TeamId AwayTeamId { get; }
    public IReadOnlyList<TeamId> GetRelatedResourceIds() => [HomeTeamId, AwayTeamId];
}

public sealed class Team : Aggregate<TeamId>
{
    public ActorId CreatedByActorId { get; }
}

public sealed record UploadScorecardCommand(MatchId MatchId, /* fields */)
    : ICommand<Result<Unit>>,
      IAuthorizeResourceVia<Team>,
      IIdentifyResource<Match, MatchId>
{
    public MatchId GetResourceId() => MatchId;

    public IResult Authorize(Actor actor, IReadOnlyList<Team> owners) =>
        Result.Ensure(
            owners.Any(t => t.CreatedByActorId == actor.Id),
            static () => new Error.Forbidden("match.upload-scorecard")
                { Detail = "Actor does not own either match team." });
}

// Composition root — assembly scan registers everything.
services.AddTrellisBehaviors();
services.AddResourceAuthorization(typeof(UploadScorecardCommand).Assembly);
```

The pipeline:
1. Resolves the actor.
2. Loads the `Match` via the existing `SharedResourceLoaderById<Match, MatchId>` (bridged automatically because the command implements `IIdentifyResource<Match, MatchId>`).
3. Calls `match.GetRelatedResourceIds()` → `[home, away]`, deduplicates, loads each via `SharedResourceLoaderById<Team, TeamId>`.
4. Calls `command.Authorize(actor, [homeTeam, awayTeam])`.
5. On any leaf-load failure, the loader's error bubbles. On any **intermediate** or owner-load failure, the pipeline collapses to `Error.Forbidden` (no existence leak). Empty ID list at any hop short-circuits to `Forbidden` without invoking `Authorize`.

**Handler parameters.** `ActorResourceViaCommandHandler<UploadScorecardCommand,Match,Team,Result<Trellis.Unit>>`
passes the checked actor and loaded **match** to protected `Handle`, not either team or
the owner collection. Keep `owners.Any(...)` unchanged: owning the away team alone still
allows an upload. For `Document -> Folder`, use
`ActorResourceViaQueryHandler<ReadDocumentQuery,Document,Folder,Result<Document>>`;
the business body receives the document while the folder remains an authorization input.
All static/resource stages share one provider resolution per dispatch.
These bases do not require static `IAuthorize`, and the registered leaf must agree with
`TLeaf` or the normal handler entry throws diagnostically. Existing leaf accessors remain
available when the business body does not need an actor.

## Chain — `Match → Team → Tournament`

```csharp
public sealed class Match : Aggregate<MatchId>, IIdentifyRelatedResource<Team, TeamId>
{
    public TeamId TeamId { get; }
    public TeamId GetRelatedResourceId() => TeamId;
}

public sealed class Team : Aggregate<TeamId>, IIdentifyRelatedResource<Tournament, TournamentId>
{
    public TournamentId TournamentId { get; }
    public TournamentId GetRelatedResourceId() => TournamentId;
}

public sealed record CancelMatchCommand(MatchId MatchId)
    : ICommand<Result<Unit>>,
      IAuthorizeResourceVia<Tournament>,
      IIdentifyResource<Match, MatchId>
{
    public MatchId GetResourceId() => MatchId;

    public IResult Authorize(Actor actor, IReadOnlyList<Tournament> owners) =>
        Result.Ensure(
            owners[0].OwnerActorId == actor.Id,
            static () => new Error.Forbidden("match.cancel"));
}
```

The resolver discovers the path `Match → Team → Tournament` at registration time using the entity-side `IIdentifyRelatedResource<,>` declarations. **Singular chains always pass `IReadOnlyList<TOwner>` of size 1** — index `[0]` is safe.

## AOT / explicit registration

`AddResourceAuthorization(Assembly[])` uses reflection. For Native AOT or trimming-strict deployments, register each via-command explicitly. The single-hop overload covers a leaf with one foreign key to its owner (singular extractor). It does **not** support fan-out — for that, drop to the hand-built `ResolvedAuthorizationPath` overload.

```csharp
// Single-hop scenario: Match has one Team FK; the actor must own that team.
public sealed class Match : Aggregate<MatchId>, IIdentifyRelatedResource<Team, TeamId>
{
    public TeamId TeamId { get; }
    public TeamId GetRelatedResourceId() => TeamId;
}

public sealed record DeleteMatchCommand(MatchId MatchId)
    : ICommand<Result<Unit>>,
      IAuthorizeResourceVia<Team>,
      IIdentifyResource<Match, MatchId>
{
    public MatchId GetResourceId() => MatchId;

    public IResult Authorize(Actor actor, IReadOnlyList<Team> owners) =>
        Result.Ensure(
            owners[0].CreatedByActorId == actor.Id,
            static () => new Error.Forbidden("match.delete"));
}

services.AddRelatedResourceAuthorization<
    DeleteMatchCommand, Match, MatchId, Team, TeamId, Result<Unit>>(
    extractOwnerId: match => match.TeamId);  // single-hop selector
```

For chains (`Match → Team → Tournament`) or fan-out (cricket `Match → {HomeTeam, AwayTeam}`), the single-hop overload cannot express the path. Build a `ResolvedAuthorizationPath` manually and use the `(this IServiceCollection, ResolvedAuthorizationPath)` overload — the hand-built path can carry multiple hops and a plural terminal hop.

## What the framework rejects at startup

- **Dual-mode commands** — implementing both `IAuthorizeResource<T>` and `IAuthorizeResourceVia<TOwner>` on one command. Security primitives are never silently composed.
- **Via-command without `IIdentifyResource<TLeaf, TLeafId>`** — the scanner cannot infer the leaf, so a silent skip would leave the via-marker unprotected. Throws naming the offending command.
- **Multiple distinct simple paths** from leaf to owner (ambiguous). Throws listing every discovered path; disambiguate by removing an `IIdentifyRelatedResource[s]` declaration or by switching to the explicit `IResourceLoader<TMessage, TProjection>` escape hatch.
- **Plural hop in a non-terminal position** — fan-out cartesian expansion is not supported.
- **No path** from leaf to owner — declare an `IIdentifyRelatedResource[s]<TOwner, ...>` somewhere along the chain or use the escape hatch.
- **Missing `SharedResourceLoaderById<TTo, TToId>`** registration for any hop — throws `InvalidOperationException` at request time (deployment bug, not authorization denial).

## TOCTOU note

Resource authorization loads happen before the handler, including loads performed by a custom `IResourceLoader<TMessage, TProjection>`. `TransactionalCommandBehavior` does not open a database transaction around either the loader or the handler: its scope coordinates the eventual `SaveChanges` commit. Multi-hop widens the window in which ownership can change after authorization. Merely switching loader implementations does not close it. Enforce ownership in the repository's conditional mutation predicate (and reject a zero-row update), or explicitly establish an appropriate transaction/isolation strategy that covers both authorization reads and writes.
