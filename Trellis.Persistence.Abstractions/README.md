# Trellis.Persistence.Abstractions

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Persistence.Abstractions.svg)](https://www.nuget.org/packages/Trellis.Persistence.Abstractions)

Store-neutral persistence contracts for Trellis unit-of-work and idempotent-consumer integrations.

## Installation

```bash
dotnet add package Trellis.Persistence.Abstractions
```

## Quick Example

```csharp
using Trellis;

static async Task<Result<Unit>> CommitAsync(
    IUnitOfWork unitOfWork,
    CancellationToken cancellationToken)
{
    using var scope = unitOfWork.BeginScope();
    return await unitOfWork.CommitAsync(cancellationToken);
}
```

## Key Features

- `IUnitOfWork` and `IUnitOfWorkScope` define a nested, ownership-aware commit boundary.
- `IInboxStore` and `InboxRecord` define transactional message deduplication without selecting a database.
- `IConsumerCheckpointStore` stores an optional pull-consumer resume position.
- All contracts live in the `Trellis` namespace and depend only on `Trellis.Core`.
- EF Core implementations ship in `Trellis.EntityFrameworkCore` and `Trellis.EntityFrameworkCore.Inbox`; adapters for other stores can implement the same contracts directly.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-persistence-abstractions.md)
- [Trellis documentation](https://xavierjohn.github.io/Trellis/)

## Development

This package has no dedicated test project. Build it from the repository root:

```powershell
dotnet build Trellis.Persistence.Abstractions\src\Trellis.Persistence.Abstractions.csproj -c Release
```
