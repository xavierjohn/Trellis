---
name: recipe-32-hide-existence
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows AuthFailureExposurePolicy.HideAsNotFound for one public NotFound across missing, removed and withheld resources. Use when hiding the existence of sensitive resources."
---
# Recipe 32 — Hide existence with `AuthFailureExposurePolicy.HideAsNotFound`

**Problem.** A `Forbidden` response on `GET /incidents/{id}` tells the unauthorized caller "this resource exists and you may not access it". For some resources — incident reports, security findings, internal correspondence, private profiles — that disclosure is itself the leak. The boundary needs to return 404 (indistinguishable from "the resource does not exist") to unauthorized actors.

**Fix.** Opt the resource into `AuthFailureExposurePolicy.HideAsNotFound` via `ResourceAuthorizationOptions`. Resource-stage root `Error.NotFound`, `Error.Gone`, `Error.Forbidden`, and `Error.AuthenticationRequired` become one fresh public NotFound. Its type and ID come from configuration and the request, never the original error; original code, detail, and cause are not copied. Other direct-resource / via-leaf errors pass through unchanged.

```csharp
// Composition root.
builder.Services.AddTrellis(options => options
    .UseResourceAuthorization<GetIncidentQuery, Incident, Result<IncidentDto>>()
    .UseResourceAuthorization(o => o.HideExistence<Incident>()));
```

```csharp
// Command and loader are unchanged from Recipe 7.
public sealed record GetIncidentQuery(IncidentId Id)
    : IQuery<Result<IncidentDto>>,
      IAuthorizeResource<Incident>,
      IIdentifyResource<Incident, IncidentId>
{
    public IncidentId GetResourceId() => Id;
    public IResult Authorize(Actor actor, Incident incident) =>
        Result.Ensure(incident.AssigneeId == actor.Id || actor.HasPermission("incidents:read-any"),
            static () => new Error.Forbidden("incidents.read-denied"));
}
```

**Public metadata.** For the same request ID, missing, removed (`Gone`), denied, and anonymous resource-stage outcomes all yield NotFound with resource `{ Type: "Incident", Id: "inc-42" }`, code `error.unspecified`, and the standard NotFound display message. The default ASP mapper produces matching 404 ProblemDetails metadata, including `instance`; only per-request trace identifiers may vary. No original storage resource or cause reaches this public error.

**Fixed public code/detail.** Both `HideExistence` forms accept optional static metadata. Use the same configured reason for every concealed failure, not different missing/denied reasons:

```csharp
.UseResourceAuthorization(o => o.HideExistence<Incident>(
    code: "incident.not-found",
    detail: "Incident not found."));
```

Omitted/null/empty/whitespace code uses `error.unspecified`; null detail uses the standard display message. The last call for a resource replaces its configuration; parameterless `HideExistence` resets code/detail to defaults.

**Multiple resources.** Chain calls in one callback or contribute separate callbacks; callbacks compose in registration order.

```csharp
.UseResourceAuthorization(o => o
    .HideExistence<Incident>()
    .HideExistence<SecurityFinding>()
    .HideExistence<PrivateProfile>())
```

**Default is `Propagate`.** Resources that do not opt in retain their original errors. Set `DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound` for service-wide hiding with default public metadata, then use `Propagate<TResource>()` for safe-to-disclose resources.

```csharp
.UseResourceAuthorization(o =>
{
    o.DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound;
    o.Propagate<PublicProfile>();      // genuinely public resource — leak is harmless
});
```

**Projection-loader overload.** When the loader returns an internal projection for authorization but the wire-public type is different, use the two-type overload:

```csharp
.UseResourceAuthorization(o => o.HideExistence<IncidentOwnership, Incident>(
    code: "incident.not-found", detail: "Incident not found."));
```

The pipeline extracts the ID from `IIdentifyResource<Incident, IncidentId>` first, falling back to the projection's identifier interface when necessary. `NotFound.Resource.Type` is the public type name, for missing projections as well as denials.

**Via commands** key on `TLeaf`. `HideExistence<Match>()` covers `IAuthorizeResourceVia<Team>` + `IIdentifyResource<Match,MatchId>`. Missing leaves and withheld outcomes use the same public Match error, never an owner error. The projection form can select a separate public leaf type.

**Pipeline interaction caveat.** When a command implements both `IAuthorize` (static permissions) and `IAuthorizeResource<T>`, the canonical pipeline runs `AuthorizationBehavior` **before** `ResourceAuthorizationBehavior`. An unauthenticated caller fails the static gate first — that `AuthenticationRequired` is **not** translated to `NotFound`, because `AuthorizationBehavior` has no concept of the resource it's protecting. Commands that need full existence-hiding (anonymous probes return 404, not 401) must omit `IAuthorize` and let `HideAsNotFound` cover the resource-authorization branch alone.

`ActorResourceQueryHandler<GetIncidentQuery,Incident,Result<IncidentDto>>` and the
corresponding direct-command/via bases preserve this resource-only shape: they do **not**
force `IAuthorize`. Their protected `Handle` receives the authenticated, resource-authorized
actor and loaded incident/leaf only after the resource gate succeeds.

**Cache safety.** A shared cache can serve an unauthorized actor's synthetic 404 to a later authorized actor, regardless of whether their error bodies match. Mark responses for hidden resources with `Cache-Control: private` or `no-store`:

```csharp
endpoints.MapGet("/incidents/{id}", async (...) =>
    (await mediator.Send(new GetIncidentQuery(...)))
        .ToHttpResponse(o => o.WithCacheControl(CacheControl.NoStore())));   // safe under HideAsNotFound
```

**Observability.** Every translation emits a structured log at `Information`:

```
EventId: 1 (EventName "ExistenceHidden")
Resource-authorization failure hidden as NotFound for GetIncidentQuery: original Kind=forbidden Code=incidents.read-denied → public resource Incident
```

The log retains the **original input** `Kind` and `Code` for private diagnostics, including missing-resource codes. They are not attached to the returned error or its cause. Example SIEM query (KQL):

```kusto
Trellis_Logs
| where EventName == "ExistenceHidden"
| summarize count() by MessageName, OriginalCode, PublicResourceType, bin(TimeGenerated, 5m)
```

**Normalization scope.** Only resource-stage root NotFound/Gone/Forbidden/AuthenticationRequired normalize, including the Forbidden generated for a loader's null-success contract violation. `Gone` uses the same public 404 so tombstones cannot reveal previous existence. Direct-resource / via-leaf `Unexpected`, `Unavailable`, transport faults, and other root kinds remain unchanged. Aggregates and unrelated handler failures are outside the policy. Application response customization must not reintroduce private distinctions; response timing is not equalized.

**Via intermediate/owner failures.** Every intermediate/owner load failure collapses to `Forbidden("resource.authorization-via.load-failed")` before normalization, regardless of underlying kind. Under hiding, even owner `Unavailable` becomes the public NotFound. `ExistenceHidden` sees that collapsed code, not the downstream cause. Use direct authorization with a custom projection loader when your application needs to classify related-resource operational failures itself.

**Related recipes.** [Recipe 7](recipe-07-resource-authorization.md) for the authorization model; [Recipe 24](recipe-24-indirect-authorization.md) for via commands; [Recipe 31](recipe-31-authorized-resource.md) for the resource-handoff accessor that composes with this policy.
