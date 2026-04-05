# Trellis.Asp.Authorization

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.Authorization.svg)](https://www.nuget.org/packages/Trellis.Asp.Authorization)

ASP.NET Core actor providers for turning authenticated requests into Trellis `Actor` objects.

## Installation
```bash
dotnet add package Trellis.Asp.Authorization
```

## Quick Example
```csharp
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();
builder.Services.AddEntraActorProvider();

app.MapGet("/me", [Authorize] async (IActorProvider actors, CancellationToken ct) =>
    Results.Ok(await actors.GetCurrentActorAsync(ct)));
```

## Key Features
- Includes claims-based, Azure Entra, and development actor providers.
- Produces a single `Actor` model that the rest of your app can trust.
- Keeps identity-provider specifics in the API layer instead of handlers and domain code.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-asp-authorization.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
