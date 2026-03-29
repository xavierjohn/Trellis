# Trellis.Authorization

Lightweight authorization primitives for [Trellis](https://github.com/xavierjohn/Trellis). Depends only on `Trellis.Results` — no mediator, web framework, or other third-party dependency.

## Why a Separate Package?

Authorization is a domain concern, not a CQRS concern. `Actor`, `IActorProvider`, `IAuthorize`, and `IAuthorizeResource<TResource>` are useful in:

- Web services without CQRS (middleware, endpoint filters, service classes)
- Domain services that need to check permissions
- Blazor components
- Background workers

If you use [Trellis.Mediator](../Trellis.Mediator/README.md), it references `Trellis.Authorization` transitively — you don't need to install both.

## Installation

```
dotnet add package Trellis.Authorization
```

## Types

| Type | Purpose |
|------|---------|
| `Actor` | Sealed record representing the current user (`Id` + `Permissions`) |
| `IActorProvider` | Abstraction for resolving the current actor — implement in your API layer |
| `IAuthorize` | Marker interface for static permission requirements (`RequiredPermissions`) |
| `IAuthorizeResource<TResource>` | Resource-based authorization that receives a loaded resource (`Authorize(Actor, TResource)`) |
| `IResourceLoader<TMessage, TResource>` | Loads the resource for `IAuthorizeResource<TResource>` — register as scoped in DI |
| `ResourceLoaderById<TMessage, TResource, TId>` | Convenience base for the common extract-ID-and-load pattern |

## Usage

### Actor

```csharp
using Trellis.Authorization;

var actor = Actor.Create("user-42", new HashSet<string> { "Orders.Read", "Orders.Write" });

actor.HasPermission("Orders.Read");                    // true
actor.HasAllPermissions(["Orders.Read", "Admin"]);     // false
actor.HasAnyPermission(["Orders.Read", "Admin"]);      // true
```

### IActorProvider

Implement in your API layer to provide the current authenticated user:

```csharp
using Trellis.Authorization;

public class HttpActorProvider(IHttpContextAccessor accessor) : IActorProvider
{
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var user = accessor.HttpContext!.User;
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var permissions = user.Claims
            .Where(c => c.Type == "permission")
            .Select(c => c.Value)
            .ToHashSet();
        return Task.FromResult(Actor.Create(id, permissions));
    }
}
```

### Direct Authorization (No CQRS)

Use `Actor` directly in service methods:

```csharp
using Trellis;
using Trellis.Authorization;

public Result<Document> EditDocument(Actor actor, string documentId, string newContent)
{
    var doc = _store.Get(documentId);
    if (doc is null)
        return Result.Failure<Document>(Error.NotFound("Document not found"));

    if (actor.Id != doc.OwnerId && !actor.HasPermission("Documents.EditAny"))
        return Result.Failure<Document>(Error.Forbidden("Only the owner can edit"));

    var updated = doc with { Content = newContent };
    _store.Update(updated);
    return Result.Success(updated);
}
```

### With CQRS (Trellis.Mediator)

Declare authorization on commands — pipeline behaviors enforce it automatically:

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;

// Static permissions
public sealed record PublishCommand(string DocumentId)
    : ICommand<Result<Document>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["Documents.Publish"];
}

// Resource-based authorization with loaded resource
public sealed record EditCommand(string DocumentId, string NewContent)
    : ICommand<Result<Document>>, IAuthorizeResource<Document>
{
    public IResult Authorize(Actor actor, Document document) =>
        actor.Id == document.OwnerId || actor.HasPermission("Documents.EditAny")
            ? Result.Success()
            : Result.Failure(Error.Forbidden("Only the owner can edit"));
}
```

See the [AuthorizationExample](../Examples/AuthorizationExample/) for a complete side-by-side comparison.
