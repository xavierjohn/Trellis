# Trellis.Testing

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Testing.svg)](https://www.nuget.org/packages/Trellis.Testing)

FluentAssertions extensions and test doubles that make Result and Maybe tests read like intent instead of plumbing.

## Installation
```bash
dotnet add package Trellis.Testing
```

## Quick Example
```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var result = Result.Ok(42);
var maybe = Maybe.From("Ada");

result.Should().BeSuccess().Which.Should().Be(42);
maybe.Should().HaveValue().Which.Should().Be("Ada");
```

## Key Features
- **Result assertions** — `BeSuccess()`, `BeFailure()`, `BeFailureOfType<T>()`, async variants
- **Maybe assertions** — `HaveValue()`, `BeNone()`, `HaveValueEqualTo()`
- **Error/Error.InvalidInput assertions** — `HaveCode()`, `HaveFieldError()`
- **Unwrap helpers** — Extract values in test code without analyzer warnings
- **FakeRepository** — In-memory aggregate store with domain event tracking
- **FakeUnitOfWork** — Configurable commit results, call/attempt counters, and deferred nested commits
- **TestActorProvider** — Mutable actor/identity provider with shared async-flow scoping
- **Identity agreement** — `await actorProvider.ShouldHaveConsistentActorIdentityAsync(identityProvider, ct)` compares sequential resolutions with provider-specific diagnostics
- **Structured validation** — Match full pointers/reason codes, count violations, or assert exact field/rule payload multisets with typed arguments
- **Persistence intent** — `HavePersistOnFailure(bool)` on typed and interface result assertions
- **Callback recording** — `CallbackRecorder` / `CallbackRecorder<TArgument>` record counts and ordered argument snapshots without changing callback outcomes
- **Controlled async sources** — `ControlledAsync<T>` exposes pending Task/Task-backed ValueTask receivers with explicit completion, fault and cancellation
- **Bounded async assertions** — Existing success/failure assertions accept an explicit timeout and cancellation token without canceling the source operation

`FakeUnitOfWork` implements provider-neutral `IUnitOfWork` without EF Core or Mediator.
It tests commit decisions and scope ownership, not persistence atomicity or rollback.
See the full documentation for setup and counter semantics.

For ASP.NET Core integration test helpers (WebApplicationFactory, DI replacement), see [Trellis.Testing.AspNetCore](https://www.nuget.org/packages/Trellis.Testing.AspNetCore).

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-testing.html)
- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-testing-reference.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
