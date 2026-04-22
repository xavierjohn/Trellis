# Trellis.FluentValidation

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.FluentValidation.svg)](https://www.nuget.org/packages/Trellis.FluentValidation)

A small bridge that turns FluentValidation output into Trellis results.

## Installation
```bash
dotnet add package Trellis.FluentValidation
```

## Quick Example
```csharp
using FluentValidation;
using Trellis.FluentValidation;

public sealed record CreateUserRequest(string Email);

var validator = new InlineValidator<CreateUserRequest>();
validator.RuleFor(x => x.Email).NotEmpty().EmailAddress();

var result = validator.ValidateToResult(new CreateUserRequest("ada@example.com"));
```

## Mediator Integration
Plug FluentValidation into the `Trellis.Mediator` validation stage so it composes with `IValidate` and aggregates all failures into one response:

```csharp
builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();
builder.Services.AddScoped<IValidator<MyCommand>, MyCommandValidator>();
```

The adapter normalizes FluentValidation property names (`Metadata.Reference`, `Lines[0].Memo`) into RFC 6901 JSON Pointers (`/Metadata/Reference`, `/Lines/0/Memo`).

## Key Features
- Convert `ValidationResult` into `Result<T>` with Trellis validation errors.
- Validate inline or through reusable validator classes.
- Plug into the `Trellis.Mediator` pipeline via `AddTrellisFluentValidation()` (AOT-friendly open-generic registration; assembly-scanning overload available for non-AOT scenarios).
- JSON Pointer normalization for nested and indexer property paths.
- Keep third-party validation libraries inside the same Result pipeline as the rest of your app.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-fluentvalidation.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
