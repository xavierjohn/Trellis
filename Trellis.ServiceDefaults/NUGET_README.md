# Trellis.ServiceDefaults

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.ServiceDefaults.svg)](https://www.nuget.org/packages/Trellis.ServiceDefaults)

An ordered composition root for Trellis services. Configure authorization, validation, EF Core transactions, outbox, inbox, idempotency, and integration events in one fluent call.

## Installation

```bash
dotnet add package Trellis.ServiceDefaults
```

## Quick Example

```csharp
builder.Services.AddTrellis(trellis => trellis
    .UseAsp()
    .UseScalarValueValidation()
    .UseProblemDetails()
    .UseMediator()
    .UseFluentValidation(typeof(Program).Assembly)
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```

Call `AddTrellis(...)` once. If omitted, the composition features are intentionally not registered.

## Key Features

- Applies Trellis registrations in canonical pipeline order.
- Keeps feature selection explicit and discoverable at the application composition root.
- Provides slots for ASP.NET Core, authorization, FluentValidation, EF Core unit of work, outbox, inbox, idempotency, and events.
- Preserves package-specific options through focused `UseXxx(...)` methods.
- Leaves vendor stores and transport adapters in their owning packages, avoiding unnecessary SDK dependencies.

For most applications, use the builder instead of mixing standalone `AddTrellis*` calls. Provider-specific stores and adapters remain separate registrations by design.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-servicedefaults.html)
- [Cross-package cookbook](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html)
- [Mediator reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-mediator.html)
- [EF Core reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore.html)
