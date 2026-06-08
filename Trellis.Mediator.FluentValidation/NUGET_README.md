# Trellis.Mediator.FluentValidation

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Mediator.FluentValidation.svg)](https://www.nuget.org/packages/Trellis.Mediator.FluentValidation)

Mediator pipeline adapter that plugs [FluentValidation](https://github.com/FluentValidation/FluentValidation) validators into the `Trellis.Mediator` validation stage.

## Why this package exists

`Trellis.FluentValidation` is a **Domain-layer** concern — it converts FluentValidation `ValidationResult` to `Result<T>` for use in any application layer. `Trellis.Mediator` is an **application / composition-layer** concern — it provides the mediator pipeline behaviors (validation, authorization, logging, etc.).

This package bridges the two without forcing Domain projects to take a transitive Mediator dependency. Reference it from your **Application** (or composition root) project; keep `Trellis.FluentValidation` in your Domain project.

## Installation

```bash
dotnet add package Trellis.Mediator.FluentValidation
```

## Quick example

```csharp
using FluentValidation;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();
builder.Services.AddScoped<IValidator<MyCommand>, MyCommandValidator>();
```

`AddTrellisFluentValidation()` registers `FluentValidationMessageValidatorAdapter<TMessage>` as the open-generic `IMessageValidator<TMessage>` implementation. Every `IValidator<T>` registered for the message in DI runs inside the existing `ValidationBehavior<TMessage, TResponse>` and contributes its failures to an aggregated `Error.InvalidInput` response. FluentValidation property names with member chains (`Address.City`) or indexers (`Items[0].Sku`) are translated to camelCase RFC 6901 JSON Pointers (`/address/city`, `/items/0/sku`).

## AOT / trim story

The parameterless `AddTrellisFluentValidation()` overload is AOT- and trim-safe — it uses open-generic DI registration with no reflection. Validators must be registered explicitly:

```csharp
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

The `AddTrellisFluentValidation(params Assembly[])` overload scans assemblies for `IValidator<T>` implementations via reflection and is annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` — use the parameterless form in AOT scenarios.

## Documentation

- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-fluentvalidation.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
