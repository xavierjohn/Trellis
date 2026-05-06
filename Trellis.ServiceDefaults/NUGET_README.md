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

`UseFluentValidation()` and `UseResourceAuthorization()` both support no-assembly calls for explicit, no-scanning composition; pass assemblies only when you want Trellis to discover validators/resource loaders automatically.

## AOT compatibility

`Trellis.ServiceDefaults` is **not** AOT- or trim-compatible. The fluent assembly-scanning methods (`UseFluentValidation(asm)`, `UseResourceAuthorization(asm)`, `UseDomainEvents(asm)`) wrap underlying `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` APIs without propagating the attributes. For AOT consumers, use the per-package direct APIs (`services.AddTrellisFluentValidation()` + explicit validator registrations, `services.AddResourceAuthorization<TMessage, TResource, TResponse>()`, `services.AddDomainEventDispatch()` + `services.AddDomainEventHandler<TEvent, THandler>()`).

The parameterless `o.UseFluentValidation()` / `o.UseResourceAuthorization()` / `o.UseDomainEvents()` overloads are AOT-compatible — they only register the adapter / pipeline behaviors and rely on the consumer's explicit per-type registrations.

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
