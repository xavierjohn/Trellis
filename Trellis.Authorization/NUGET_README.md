# Trellis.Authorization

Lightweight authorization primitives for [Trellis](https://github.com/xavierjohn/Trellis).

Provides `Actor`, `IActorProvider`, `IAuthorize`, and `IAuthorizeResource<TResource>` types that integrate with the Trellis `Result<T>` type system. No dependency on any mediator or web framework.

## Types

| Type | Purpose |
|------|---------|
| `Actor` | Represents the current user with `Id` and `Permissions` |
| `IActorProvider` | Abstraction for resolving the current actor (implement in API layer) |
| `IAuthorize` | Marker for static permission-based authorization |
| `IAuthorizeResource<TResource>` | Resource-based authorization with a loaded resource (ownership checks) |
| `IResourceLoader<TMessage, TResource>` | Loads the resource required for `IAuthorizeResource<TResource>` |
| `ResourceLoaderById<TMessage, TResource, TId>` | Convenience base class for ID-based resource loading |

## Usage

```csharp
using Trellis.Authorization;

// Implement IActorProvider in your API layer
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

See the [full documentation](https://xavierjohn.github.io/Trellis/) for details.
