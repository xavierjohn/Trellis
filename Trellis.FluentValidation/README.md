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

## Key Features
- Convert `ValidationResult` into `Result<T>` with Trellis validation errors.
- Validate inline or through reusable validator classes.
- Keep third-party validation libraries inside the same Result pipeline as the rest of your app.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-fluentvalidation.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
