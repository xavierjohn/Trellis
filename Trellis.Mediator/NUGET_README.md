# Trellis.Mediator

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Mediator.svg)](https://www.nuget.org/packages/Trellis.Mediator)

Result-aware pipeline behaviors for [Mediator](https://github.com/martinothamar/Mediator) that keep handlers focused on business work.

## Installation
```bash
dotnet add package Trellis.Mediator
```

## Quick Example
```csharp
using Mediator;
using Trellis;
using Trellis.Mediator;

public sealed record GetOrderQuery(string Id) : IQuery<Result<string>>, IValidate
{
    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Id)
            ? Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(Id)), "validation.error") { Detail = "Order ID is required." })))
            : Result.Ok();
}

builder.Services.AddMediator();
builder.Services.AddTrellisBehaviors();
```

## Key Features
- Adds validation, authorization, tracing, logging, and exception behaviors that understand `Result<T>`.
- Short-circuits failures before handlers do unnecessary work.
- Supports resource authorization with explicit or assembly-scanned registration.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-mediator.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
