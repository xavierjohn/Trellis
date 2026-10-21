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
- `IUnitOfWork.MarkUncommittable(Error reason)` permanently blocks commits after a hook veto, exception, or cancellation. Adapters must reject null, preserve the first reason, and return it before nested deferral or store access; scopes never reset it. This required interface member is an adapter contract change, not an optional no-op. Discard the unit of work and backing context; invalidation does not roll back tracked state.
- `IInboxStore` and `InboxRecord` define transactional message deduplication without selecting a database.
- `IConsumerCheckpointStore` stores an optional pull-consumer resume position.
- All contracts live in the `Trellis` namespace and depend only on `Trellis.Core`.
- EF Core implementations ship in `Trellis.EntityFrameworkCore` and `Trellis.EntityFrameworkCore.Inbox`; adapters for other stores can implement the same contracts directly.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-persistence-abstractions.html)
- [Trellis documentation](https://xavierjohn.github.io/Trellis/)
