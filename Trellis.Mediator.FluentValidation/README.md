# Trellis.Mediator.FluentValidation

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Mediator.FluentValidation.svg)](https://www.nuget.org/packages/Trellis.Mediator.FluentValidation)

Mediator pipeline adapter that plugs [FluentValidation](https://github.com/FluentValidation/FluentValidation) validators into the `Trellis.Mediator` validation stage.

Reference this package from an Application or composition-root project. `Trellis.FluentValidation` remains the Mediator-independent package for domain-level validation helpers.

## Installation

```bash
dotnet add package Trellis.Mediator.FluentValidation
```

## Quick Example

```csharp
using FluentValidation;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

builder.Services.AddMediator(options =>
    options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();
builder.Services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

## Key Features

- Registers `FluentValidationMessageValidatorAdapter<TMessage>` through the existing `IMessageValidator<TMessage>` extension point; it does not add a second pipeline behavior.
- Aggregates all validator failures into one `Error.InvalidInput`.
- Normalizes member chains and indexers to RFC 6901 JSON Pointers.
- Supports an AOT-safe parameterless registration with explicit validators.
- Offers an assembly-scanning overload for non-AOT applications.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-mediator-fluentvalidation.md)
- [Cookbook Recipe 2](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html#recipe-2--command--handler--fluentvalidation--ef-persistence)
- [FluentValidation integration guide](https://xavierjohn.github.io/Trellis/articles/integration-fluentvalidation.html)

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Mediator.FluentValidation\tests\Trellis.Mediator.FluentValidation.Tests.csproj -c Release
```
