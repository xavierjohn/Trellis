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
The Mediator wire-up moved to the dedicated [`Trellis.Mediator.FluentValidation`](https://www.nuget.org/packages/Trellis.Mediator.FluentValidation) package in v3 so consumers that only need the standalone helpers don't take a Mediator dependency. Install that package to plug FluentValidation into the `Trellis.Mediator` validation stage:

```bash
dotnet add package Trellis.Mediator.FluentValidation
```

```csharp
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();
builder.Services.AddScoped<IValidator<MyCommand>, MyCommandValidator>();
```

The adapter normalizes FluentValidation property names (`Metadata.Reference`, `Lines[0].Memo`) into RFC 6901 JSON Pointers (`/metadata/reference`, `/lines/0/memo`) using `JsonPointerNormalizer`, which is now part of the public surface of `Trellis.FluentValidation`.

## Key Features
- Convert `ValidationResult` into `Result<T>` with Trellis validation errors.
- Validate inline or through reusable validator classes.
- `JsonPointerNormalizer.ToJsonPointer(...)` projects FluentValidation property names into RFC 6901 JSON Pointers — public so third-party adapters can reuse the normalization rules.
- Plug into the `Trellis.Mediator` pipeline via the companion [`Trellis.Mediator.FluentValidation`](https://www.nuget.org/packages/Trellis.Mediator.FluentValidation) package.
- Keep third-party validation libraries inside the same Result pipeline as the rest of your app.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-fluentvalidation.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
