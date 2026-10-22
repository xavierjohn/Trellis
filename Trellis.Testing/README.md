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
- **Result assertions** — `BeSuccess()`, `BeFailure()`, `BeFailureOfType<T>()`, `HaveErrorCode()`, async variants
- **Maybe assertions** — `HaveValue()`, `BeNone()`, `HaveValueEqualTo()`, `HaveValueMatching()`
- **Error assertions** — `HaveCode()`, `HaveDetail()`, `BeOfType<T>()`
- **Error.InvalidInput assertions** — `HaveFieldError()`, `HaveFieldErrorWithDetail()`, `HaveFieldCount()`
- **Unwrap helpers** — `Unwrap()` for extracting values in test code without analyzer warnings
- **FakeRepository** — In-memory aggregate store with domain event tracking and unique constraints
- **FakeUnitOfWork** — Configurable commit outcomes and counters with owner/nested-scope commit deferral
- **TestActorProvider** — Mutable actor/identity provider with shared `AsyncLocal` scoping for authorization tests
- **Identity agreement** — `await actorProvider.ShouldHaveConsistentActorIdentityAsync(identityProvider, ct)` asserts the full actor's ID matches identity-only resolution, sequentially and with provider-specific diagnostics
- **AggregateTestMutator** — Reflection helpers for setting `Maybe<T>` backing fields in tests

## FakeUnitOfWork

Use `FakeUnitOfWork` when a test exercises the real transactional command behavior
without a database:

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var unitOfWork = new FakeUnitOfWork
{
    CommitResult = Result.Fail(new Error.Conflict("test.commit-failed"))
};

using var scope = unitOfWork.BeginScope();
(await unitOfWork.CommitAsync()).Should().BeFailureOfType<Error.Conflict>();
unitOfWork.CommitCallCount.Should().Be(1);
unitOfWork.CommitAttemptCount.Should().Be(1);
```

`CommitResult` defaults to success. `CommitCallCount` includes every call;
`CommitAttemptCount` excludes deferred nested calls but includes failed and canceled
attempts. Nested commits return success until only the owning scope remains.
Scope disposal never commits. An already-canceled token cancels a non-deferred call.

The fake depends only on provider-neutral persistence abstractions, not EF Core or
Mediator. It does not coordinate repositories, persist data, or roll back changes.
Use one instance per test/DI scope, dispose nested scopes in reverse order, and keep
real-provider integration tests for atomicity.

## ASP.NET Core Integration Tests

For WebApplicationFactory helpers, DI service replacement, and MSAL token acquisition, use the companion package:

```bash
dotnet add package Trellis.Testing.AspNetCore
```

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-testing.html)
- [Package API reference](../docs/docfx_project/api_reference/trellis-api-testing-reference.md)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Testing\tests\Trellis.Testing.Tests.csproj -c Release
```
