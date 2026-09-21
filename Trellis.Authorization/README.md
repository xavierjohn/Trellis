# Trellis.Authorization

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Authorization.svg)](https://www.nuget.org/packages/Trellis.Authorization)

Lightweight authorization primitives for permissions, actors, and resource-based checks.

## Installation
```bash
dotnet add package Trellis.Authorization
```

## Quick Example
```csharp
using System.Collections.Generic;
using Trellis;
using Trellis.Authorization;

var actor = Actor.Create("user-1", new HashSet<string> { "orders:read" });

IResult result = actor.HasPermission("orders:read")
    ? Result.Ok()
    : Result.Fail(new Error.Forbidden("policy.id") { Detail = "Missing permission." });
```

## Key Features
- `AddSharedResourceAuthorization<TMessage,TResource,TId,TResponse>()` from `Trellis.Mediator` registers the typed behavior, accessor, and shared-loader bridge together; register your `SharedResourceLoaderById<TResource,TId>` implementation separately. The lower-level `AddResourceAuthorization<TMessage,TResource,TResponse>()` still leaves all loader registration to the caller.
- `ActorId` opts into `[Trim, NotDefault]`; trimming and blank rejection are not the unannotated `RequiredString<T>` defaults.
- Defines `Actor`, `ActorId`, `IActorProvider`, `IAuthorize`, and resource authorization interfaces.
- `actorProvider.RequireActorAsync(ct)` returns the actor when presence is already guaranteed, throwing `InvalidOperationException` if that invariant is broken. It performs no authorization or caching; ordinary unauthenticated flows still use `GetCurrentActorAsync` and handle absence.
- `Actor.Id` is the strongly-typed `ActorId` value object — reuse it on consumer aggregate boundaries (`Order.CreatedByActorId`, `Document.LastModifiedByActorId`) for type-checked principal-identity comparisons.
- Works without ASP.NET Core, Mediator, or any web dependency.
- Keeps permission rules inside the same Result-based workflow as the rest of your application.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-db-permissions.html)
- [Package API reference](../docs/docfx_project/api_reference/trellis-api-authorization.md)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Authorization\tests\Trellis.Authorization.Tests.csproj -c Release
```
