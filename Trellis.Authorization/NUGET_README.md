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
    : Result.Fail(Error.Forbidden("Missing permission."));
```

## Key Features
- Defines `Actor`, `IActorProvider`, `IAuthorize`, and resource authorization interfaces.
- Works without ASP.NET Core, Mediator, or any web dependency.
- Keeps permission rules inside the same Result-based workflow as the rest of your application.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-db-permissions.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
