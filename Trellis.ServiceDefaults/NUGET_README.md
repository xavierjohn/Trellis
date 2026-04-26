# Trellis.ServiceDefaults

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.ServiceDefaults.svg)](https://www.nuget.org/packages/Trellis.ServiceDefaults)

Opinionated composition defaults for Trellis web services.

## Installation
```bash
dotnet add package Trellis.ServiceDefaults
```

## Quick Example
```csharp
using Trellis.ServiceDefaults;

builder.Services.AddTrellis(options => options
    .UseAsp()
    .UseMediator()
    .UseFluentValidation(typeof(Program).Assembly)
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```

`UseEntityFrameworkUnitOfWork<TContext>()` is always applied last so the transactional command behavior runs innermost. `AddDbContext<TContext>(...)` and `AddMediator(...)` remain application-owned registrations.

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
