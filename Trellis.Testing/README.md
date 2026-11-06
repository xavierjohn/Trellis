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

## Structured validation and pipeline assertions

Assert stable payloads rather than rendered English messages:

```csharp
var field = InputPointer.ForBody("/items/0/quantity");
var error = Error.InvalidInput.ForField("quantity.minimum", field,
    args: ValidationArgs.Of("min", 2));

error.Should().HaveFieldViolation(field, "quantity.minimum")
    .Which.Args.Should().Equal(ValidationArgs.Of("min", 2));
error.Should().HaveFieldViolationCount(1).And.HaveOnlyRuleViolations([]);
Result.FailAfterCommit<int>(error).Should().HavePersistOnFailure(true);
```

`HaveFieldViolation` and `HaveRuleViolation` return a unique original match. The
`HaveOnlyFieldViolations` / `HaveOnlyRuleViolations` assertions compare exact unordered
payload multisets, including typed arguments, input locations, detail and duplicate
multiplicity. `HaveFieldCount` keeps its existing distinct-path meaning; the new
violation-count assertions count every entry. Persistence intent is not proof of a commit.

## Async test helpers

```csharp
var calls = new CallbackRecorder();
var source = new ControlledAsync<Maybe<int>>();

// Assert the operation is pending, always release the gate, then await with a bound.
var result = await source.Task
    .ToResultAsync(calls.Wrap<Error>(() => new Error.Forbidden("test.absent")))
    .ShouldBePendingThenReleaseAsync(
        () => source.TryComplete(Maybe<int>.None),
        TimeSpan.FromSeconds(5),
        TestContext.Current.CancellationToken,
        whilePending: () => calls.CallCount.Should().Be(0));

result.Should().BeFailure();
calls.CallCount.Should().Be(1);

// Prove the original exception or token comes through, not a wrapper or a lookalike.
await Task.FromException<Maybe<int>>(failure).ToResultAsync(error).ShouldPropagateExceptionAsync(failure);
using var canceled = CanceledToken.Create();
await canceled.ToTask<Maybe<int>>().ToResultAsync(error).ShouldPropagateCancellationAsync(canceled.Token);

// Prove a ValueTask consumer reads its result exactly once.
var single = ControlledAsync.SingleUse<int>();
single.TryComplete(42);
(await consumer(single.ValueTask)).Should().Be(42);
single.ResultReadCount.Should().Be(1);

// A null guard must name the right parameter, and fire before a pending source is awaited.
// Use a fresh source that is still pending: a completed one would let a misplaced guard pass.
var pending = new ControlledAsync<Maybe<int>>();
try
{
    var started = pending.Task.ToResultAsync((Func<Error>)null!);   // a synchronous throw here fails the test
    await started.ShouldRejectNullBeforeAwaitAsync("ferror");
}
finally
{
    pending.TryComplete(Maybe<int>.None);
}
```

`ControlledAsync<T>` is one type with two modes: Task-backed by default, or
`ControlledAsync.SingleUse<T>()`, whose `ValueTask` throws on a second consumption and counts reads in
`ResultReadCount`. `CallbackRecorder<TArgument>` records ordered argument snapshots (`Arguments`).
Assert counts with `calls.Should().HaveBeenCalledOnce()` (also `NotHaveBeenCalled`, `HaveBeenCalledAtLeastOnce`,
`HaveBeenCalledTimes`), and use `calls.Forbidden<T>("reason")` for a callback that must never run.
Bounded success/failure assertions take a timeout and token; they stop only the assertion wait, never the
operation. The helpers are test-runner-agnostic.

## Pausing code under test: `TestGate`

```csharp
using var gate = new TestGate();                    // Dispose() releases, so a failed assertion never strands the flow
fixture.Probe.BeforeRead = async (number, token) =>
{
    if (number == 1)
        await gate.ArriveAsync(token);              // code under test: signal arrival, then wait for release
};

var check = fixture.CheckAsync();
await gate.WaitForArrivalAsync(TimeSpan.FromSeconds(30), token);   // bounded: a missed arrival fails, never hangs
check.IsCompleted.Should().BeFalse();
gate.Release();                                     // permanent by default; new TestGate(permanentRelease: false) re-closes
```

`TestGate` replaces the hand-written `entered` / `release` `TaskCompletionSource` pair. Arrivals are counted and
never throw when repeated, `WaitForArrivalsAsync(n, ...)` plus `Signal()` makes an N-way barrier, and waiting for
an arrival always requires a timeout and token.

## Domain event, page, specification and actor assertions

```csharp
order.UncommittedEvents().Should().HaveRaised<OrderPlaced>().Which.OrderId.Should().Be(id);
repository.PublishedEvents.Should().NotHaveRaised<OrderShipped>();

page.Should().HaveItems([first, second]).And.HaveRequestedLimit(50).And.BeCapped();
page.Should().HaveNextCursor().Which.Token.Should().NotBeEmpty();

new OverdueSpecification(today).Should().BeSatisfiedByAll(overdueOrders);

actor.Should().HavePermission("Orders.Read").And.NotHavePermission("Orders.Write");
new CancelOrderCommand(id).ShouldNotBeAuthorizedFor(readOnlyActor);

var anonymous = TestActorProvider.Anonymous();               // resolves no actor and no identity
await using var scope = actorProvider.WithoutActor();        // or switch an existing provider
```

Event assertions extend `aggregate.UncommittedEvents().Should()` rather than adding `Should()` to
aggregates. Permission assertions follow the pipeline rule: an explicitly forbidden permission overrides a
granted one, and the failure message says which. `ShouldBeAuthorizedFor` applies the same all-permissions
rule as the Mediator authorization behavior, so denied paths need no pipeline.

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
