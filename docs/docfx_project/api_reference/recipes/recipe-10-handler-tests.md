---
name: recipe-10-handler-tests
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows handler tests with Trellis.Testing: Should().Be(...) and UnwrapError(). Use when writing handler or domain tests."
---
# Recipe 10 — Test: handler test using `Trellis.Testing` `Should().Be(...)` / `UnwrapError()`

**Requires:** `Trellis.Core`, plus `Trellis.Authorization`, `Trellis.Primitives`, `Trellis.Testing`.

**Problem.** Unit-test the `PlaceOrderHandler` from [Recipe 2](recipe-02-command-handler.md) using FluentAssertions extensions from `Trellis.Testing`.

For an actor-aware base, send the command/query through Mediator with `TestActorProvider`
and fake business dependencies, then use the same Result assertions below. Keep the normal
authorization, validation, and resource-loader registrations; supply fake commit/event
dependencies when those stages are enabled. Assert denied/anonymous paths as well as
business outcomes, ownership, existence hiding, and actor/resource snapshot identity.
The actor/resource `Handle` overload is protected business logic, not a test seam.
Calling the public two-argument `Handle` without an authorized dispatch throws before
business logic, and registering a test actor provider alone does not create that dispatch.
For pipeline-free unit tests, exercise aggregates, policies, or application services
directly rather than unsealing a handler or accessing its protected hook by reflection.
The direct test below is for [Recipe 2](recipe-02-command-handler.md)'s ordinary handler, not an actor-aware base.

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.Testing;
using Xunit;

public class PlaceOrderHandlerTests
{
    [Fact]
    public async Task PlaceOrder_returns_id_on_success()
    {
        var repo = new InMemoryOrderRepository();
        var sut  = new PlaceOrderHandler(repo);

        var command = new PlaceOrderCommand(
            OrderId.TryCreate(Guid.NewGuid()).Unwrap(),
            new Money(100m, CurrencyCode.TryCreate("USD").Unwrap()),
            ActorId.TryCreate("alice").Unwrap());

        var result = await sut.Handle(command, CancellationToken.None);

        result.Should().BeSuccess();
        result.Should().HaveValue(repo.Last().Id);                  // asserts the Result<T> value equals
    }

    [Fact]
    public void PlaceOrder_request_adapter_fails_when_currency_invalid()
    {
        var request = new PlaceOrderRequest(Guid.NewGuid(), 100m, "US", "alice"); // 2 chars, not 3

        var result = PlaceOrderCommand.TryCreate(request);

        result.Should().BeFailureOfType<Error.InvalidInput>()
            .Which.Should().HaveFieldError("currency");
    }
}
```

**What it shows.** `ResultAssertions<TValue>.HaveValue(expected)` asserts the success value **equals** `expected` (use `HaveValueEquivalentTo` for member-wise structural equivalence); `UnwrapError()` is the safe accessor that *only* returns the error and is intended for use after `Should().BeFailure...`. Calling `.Should()` on an `Error.InvalidInput` returns the specialized `ValidationErrorAssertions` (with `HaveFieldError`, `HaveFieldErrorWithDetail`, `HaveFieldCount`). Async assertions have two valid shapes: await the pipeline first and assert the resulting `Result<T>`, or call `BeSuccessAsync` / `BeFailureAsync` directly on `Task<Result<T>>` or `ValueTask<Result<T>>`. The unsupported shape is `await result.Should().BeSuccessAsync()` because `.Should()` returns synchronous `ResultAssertions<TValue>`.

Beyond Result assertions, `Trellis.Testing` covers the other things handler and domain tests check:
`aggregate.UncommittedEvents().Should().HaveRaised<TEvent>()` for raised domain events,
`page.Should().HaveNextCursor()` / `.BeLastPage()` for a `Page<T>`, `spec.Should().BeSatisfiedBy(x)` for
specifications, and `actor.Should().HavePermission(...)` plus `message.ShouldNotBeAuthorizedFor(actor)` for
denied paths without the pipeline. `TestActorProvider.Anonymous()` supplies the unauthenticated case. See the
[Testing reference](../trellis-api-testing-reference.md#patterns-index) for every helper.
