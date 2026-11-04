---
package: Trellis.Testing
namespaces: [Trellis.Testing]
types: ["FakeRepository<TAggregate, TId>", "FakeSharedResourceLoader<TResource, TId>", FakeUnitOfWork, TestActorProvider, TestActorScope, ActorIdentityAssertionsExtensions, "ResultAssertions<TValue>", ResultAssertionsExtensions, ResultAssertionsAsyncExtensions, IResultAssertions, IResultAssertionsExtensions, "MaybeAssertions<T>", MaybeAssertionsExtensions, ErrorAssertions, ErrorAssertionsExtensions, ValidationErrorAssertions, ValidationErrorAssertionsExtensions, UnwrapExtensions, UnwrapFailedException, AggregateTestMutator, CallbackRecorder, "CallbackRecorder<TArgument>", "ControlledAsync<T>", ControlledAsync, CanceledToken, TestGate, PendingOperationAssertionsExtensions, PropagationAssertionsExtensions, ArgumentNullAssertionsExtensions, DomainEventAssertionsExtensions, PageAssertionsExtensions, "PageAssertions<T>", SpecificationAssertionsExtensions, "SpecificationAssertions<T>", ActorAssertionsExtensions, ActorAssertions, AuthorizeMessageAssertionsExtensions]
version: v3
last_verified: 2026-11-03
audience: [llm]
agent_usage: onDemand
agent_description: "Open when writing unit or handler tests: Result/Maybe/error, domain-event, page, specification and actor assertions, async-source helpers, fakes and test actors (Trellis.Testing)."
---
# Trellis.Testing — API Reference

- **Package:** `Trellis.Testing`
- **Namespace:** `Trellis.Testing`
- **Purpose:** FluentAssertions extensions, unwrap helpers, and test doubles (FakeRepository, FakeUnitOfWork, TestActorProvider) for Trellis applications.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-10--test-handler-test-using-trellistesting-shouldbe--unwraperror) — recipes using this package.

> **ASP.NET Core integration test helpers** (WebApplicationFactory, DI replacement, fake time, MSAL tokens, and `.http` replay) are in a separate package: [`Trellis.Testing.AspNetCore`](trellis-api-testing-aspnetcore.md#use-this-file-when).

## Use this file when

- You are writing unit/handler/domain tests for Trellis `Result`, `Maybe`, errors, or mediator handlers.
- You need FluentAssertions extensions for success/failure/error-shape assertions.
- You need test-only unwrap helpers, fake repositories, fake unit-of-work commits, or test actor providers.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Assert a generic result succeeded | `result.Should().BeSuccess()` / `.HaveValue(...)` | [`ResultAssertions<TValue>`](#resultassertionstvalue) |
| Assert a result failed with a specific error case | `result.Should().BeFailureOfType<TError>()` | [`ResultAssertions<TValue>`](#resultassertionstvalue) |
| Assert error code/detail | `.HaveErrorCode(...)`, `.HaveErrorDetail(...)`, `.HaveErrorDetailContaining(...)` | [`ResultAssertions<TValue>`](#resultassertionstvalue) |
| Assert a full validation pointer/reason or exact typed violation payloads | `.HaveFieldViolation(pointer, code)`, `.HaveOnlyFieldViolations(expected)` and corresponding rule assertions | [`ValidationErrorAssertions`](#validationerrorassertions) |
| Assert persistence intent without claiming a database commit | `.HavePersistOnFailure(expected)` on typed or interface result assertions | [Persistence intent](#persistence-intent) |
| Record callback counts and ordered arguments | `CallbackRecorder` / `CallbackRecorder<TArgument>` | [Callback recorders](#callback-recorders) |
| Control a genuinely pending Task/ValueTask receiver | `ControlledAsync<T>` | [`ControlledAsync<T>`](#controlledasynct) |
| Bound an async assertion without canceling its source | `BeSuccessAsync(timeout, cancellationToken)` and failure variants | [Bounded async assertions](#bounded-async-assertions) |
| Prove a ValueTask consumer reads its result exactly once | `ControlledAsync.SingleUse<T>()`, then `ResultReadCount` | [`ControlledAsync<T>`](#controlledasynct) |
| Assert an operation is still pending, then release it and await with a bound | `operation.ShouldBePendingThenReleaseAsync(release, timeout, token, whilePending)` | [Pending-operation assertions](#pending-operation-assertions) |
| Assert the original exception or cancellation token propagates unchanged | `operation.ShouldPropagateExceptionAsync(exception)` / `.ShouldPropagateCancellationAsync(token)` | [Propagation assertions](#propagation-assertions) |
| Pause code under test at a point, assert about the paused state, then release it (rendezvous or barrier) | `using var gate = new TestGate();` with `gate.ArriveAsync()` in the code under test, then `await gate.WaitForArrivalAsync(timeout, token)` and `gate.Release()` | [`TestGate`](#testgate) |
| Create an already-canceled token with its own identity | `CanceledToken.Create()` with `Token`, `ToTask`, `ToValueTask` | [`CanceledToken`](#canceledtoken) |
| Assert a null guard names the right parameter, or fires before awaiting a pending source | `act.ShouldRejectNull(paramName)`, `.ShouldRejectNullAsync(...)`, `.ShouldRejectNullBeforeAwaitAsync(...)` | [Argument-null assertions](#argument-null-assertions) |
| Assert which domain events an aggregate or `FakeRepository` raised | `aggregate.UncommittedEvents().Should().HaveRaised<TEvent>()` and siblings | [Domain-event assertions](#domain-event-assertions) |
| Assert a `Page<T>`: items, cursors and applied limits | `page.Should().HaveItems(...)`, `.HaveNextCursor()`, `.BeLastPage()`, `.BeCapped()` | [`PageAssertions<T>`](#pageassertionst) |
| Assert a specification accepts or rejects candidates | `spec.Should().BeSatisfiedBy(x)` / `.NotBeSatisfiedBy(x)` | [`SpecificationAssertions<T>`](#specificationassertionst) |
| Assert an actor's effective permissions, identity or attributes | `actor.Should().HavePermission(...)`, `.NotHavePermission(...)`, `.BeOwnerOf(...)` | [`ActorAssertions`](#actorassertions) |
| Test denied paths for an `IAuthorize` message without the pipeline | `message.ShouldBeAuthorizedFor(actor)` / `.ShouldNotBeAuthorizedFor(actor)` | [`IAuthorize` message assertions](#iauthorize-message-assertions) |
| Test the unauthenticated path | `TestActorProvider.Anonymous()` or `provider.WithoutActor()` | [`TestActorProvider`](#testactorprovider) |
| Assert against `IResult` (e.g., `IAuthorizeResource<T>.Authorize` return value) | `result.Should().BeSuccess()` / `.BeFailureOfType<TError>()` — same surface, no `.HaveValue` since the non-generic interface carries no typed value | [`IResultAssertions`](#iresultassertions) |
| Extract success value in tests only | `result.Unwrap()` | [Usage notes](#usage-notes) |
| Extract error in tests only | `result.UnwrapError()` | [Usage notes](#usage-notes) |
| Provide an actor in Mediator-dispatch handler tests | Register `TestActorProvider` as `IActorProvider` with the normal pipeline and fake dependencies | [`TestActorProvider`](#testactorprovider) |
| Assert full actor and identity-only providers agree | `await actorProvider.ShouldHaveConsistentActorIdentityAsync(identityProvider, token)` | [`ActorIdentityAssertionsExtensions`](#actoridentityassertionsextensions) |
| Stub repository behavior | `FakeRepository<TAggregate,TId>` | [`FakeRepository<TAggregate,TId>`](#fakerepositorytaggregate-tid) |
| Test commit decisions and nested scopes without a database | `FakeUnitOfWork` | [`FakeUnitOfWork`](#fakeunitofwork) |

## Common traps

- `Unwrap()` and `UnwrapError()` are test helpers. Do not copy them into production code or documentation snippets for application logic.
- Test both the success path and the expected error branch; a compiling handler that never asserts failure semantics can still miss Trellis behavior.
- Actor-aware handlers require Mediator dispatch even in tests. Their actor/resource `Handle` overload is protected; there is no public business-only seam. `TestActorProvider` supplies identity but does not create an authorized frame. Keep the normal pipeline/resource registrations and substitute fake business, loader, commit, and event dependencies as appropriate. See [Recipe 10](trellis-api-cookbook.md#recipe-10--test-handler-test-using-trellistesting-shouldbe--unwraperror).
- ASP.NET Core integration helpers are in [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md#use-this-file-when), not this package.
- **Trellis analyzers in test code.** Idiomatic assertions already satisfy `TRLS001` (Result not handled): a FluentAssertions chain (`result.Should().BeSuccess()`) and an explicit discard (`_ = await repo.AddAsync(...)`) both count as *handled*. For a deliberate fire-and-forget call in *arrange*/seeding, prefer the discard (`_ = ...`) over a blanket `<NoWarn>TRLS001</NoWarn>` so the rule keeps protecting the rest of the project. Seeding through `DbContext.SaveChangesAsync()` trips `TRLS015`; call `SaveChangesResultAsync()` (then assert or discard it), or relax just that one rule in the test project if you intentionally seed with the raw EF method.
- **Relax a rule across test projects with one shared file, not a per-project `<NoWarn>`.** In a Clean Architecture layout the test projects are scattered (`Domain/tests/`, `Application/tests/`, …) with no common folder, so a per-directory `.editorconfig` is awkward. Keep a single global analyzer config and apply it to every `*.Tests` project from the root `Directory.Build.props`; a severity override (rather than `<NoWarn>`) keeps the rule visible as guidance:

  ```ini
  # eng/Trellis.Tests.globalconfig
  is_global = true
  dotnet_diagnostic.TRLS001.severity = suggestion
  dotnet_diagnostic.TRLS015.severity = none
  ```

  ```xml
  <!-- Directory.Build.props (repo root) — auto-applies to *.Tests projects, no per-project edits -->
  <ItemGroup Condition="$(MSBuildProjectName.EndsWith('.Tests'))">
    <EditorConfigFiles Include="$(MSBuildThisFileDirectory)eng/Trellis.Tests.globalconfig" />
  </ItemGroup>
  ```

## Types

### Namespace `Trellis.Testing`

#### `ResultAssertionsExtensions`
```csharp
public static class ResultAssertionsExtensions
{
    public static ResultAssertions<TValue> Should<TValue>(this Result<TValue> result);
}
```

#### `ResultAssertions<TValue>`
```csharp
public class ResultAssertions<TValue> : ReferenceTypeAssertions<Result<TValue>, ResultAssertions<TValue>>
{
    public ResultAssertions(Result<TValue> result);

    public AndWhichConstraint<ResultAssertions<TValue>, TValue> BeSuccess(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<ResultAssertions<TValue>, Error> BeFailure(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<ResultAssertions<TValue>, TError> BeFailureOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error;

    public AndConstraint<ResultAssertions<TValue>> HaveValue(
        TValue expectedValue,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveValueMatching(
        Func<TValue, bool> predicate,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveValueEquivalentTo(
        TValue expectedValue,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveErrorCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveErrorDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveErrorDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> Be(
        Result<TValue> expected,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> NotBe(
        Result<TValue> unexpected,
        string because = "",
        params object[] becauseArgs);
}
```

#### `IResultAssertionsExtensions`

```csharp
public static class IResultAssertionsExtensions
{
    public static IResultAssertions Should(this IResult? result);
}
```

#### `IResultAssertions`

Entry-point for assertions against members typed as the non-generic `IResult` interface — most commonly `IAuthorizeResource<TResource>.Authorize` and `IAuthorizeResourceVia<TOwner>.Authorize`, which both return `IResult` by contract. Mirrors the failure-side surface of `ResultAssertions<TValue>` (no `.HaveValue` because the non-generic interface carries no typed value).

Overload resolution: a concrete `Result<T>` always binds to the typed `Should<T>(this Result<T>)` extension instead (struct is more specific than the `IResult` interface). The non-generic entry point fires for any receiver statically typed as `IResult` — including `IResult<TValue>`, generic type parameters constrained to `IResult`, and custom concrete types implementing `IResult` — because no typed extension exists for those receivers. Consumers holding `IResult<TValue>` who want the typed `.HaveValue` surface should narrow the receiver to a concrete `Result<TValue>` first.

```csharp
public class IResultAssertions : ReferenceTypeAssertions<IResult, IResultAssertions>
{
    public IResultAssertions(IResult? result);

    public AndConstraint<IResultAssertions> BeSuccess(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<IResultAssertions, Error> BeFailure(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<IResultAssertions, TError> BeFailureOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error;

    public AndConstraint<IResultAssertions> HaveErrorCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<IResultAssertions> HaveErrorDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<IResultAssertions> HaveErrorDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs);
}
```

#### `UnwrapError(this Result<Unit>)`
The `UnwrapError` extension also has a `Result<Unit>`-specific overload at `Trellis.Testing.UnwrapExtensions.UnwrapError(this Result<Unit>)` for tests asserting on no-payload results.

#### Persistence intent

Both `ResultAssertions<TValue>` and `IResultAssertions` provide `HavePersistOnFailure`:

```csharp
public AndConstraint<ResultAssertions<TValue>> HavePersistOnFailure(
    bool expected, string because = "", params object[] becauseArgs);
public AndConstraint<IResultAssertions> HavePersistOnFailure(
    bool expected, string because = "", params object[] becauseArgs);
```

Checks the per-instance `IPersistOnFailure.PersistOnFailure` flag, not merely whether the interface
exists. An `IResult` without that optional interface has no opt-in (`false`); a null receiver is an
assertion failure, including inside `AssertionScope`. The assertion does not assert success/failure
or prove a commit, rollback, or database atomicity. Chain a state assertion separately.

#### `ResultAssertionsAsyncExtensions`
```csharp
public static class ResultAssertionsAsyncExtensions
{
    public static Task<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static Task<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static Task<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
        where TError : Error;

    public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
        where TError : Error;
}
```

#### Bounded async assertions

The existing names also accept a required timeout **and** cancellation token. Requiring both leaves
existing single-argument `because`/`default` calls unambiguous. These overloads are extensions on the
awaitable, not on `.Should()`.

```csharp
public static Task<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
    this Task<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
    string because = "", params object[] becauseArgs);
public static Task<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
    this Task<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
    string because = "", params object[] becauseArgs);
public static Task<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
    this Task<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
    string because = "", params object[] becauseArgs) where TError : Error;
public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
    this ValueTask<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
    string because = "", params object[] becauseArgs);
public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
    this ValueTask<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
    string because = "", params object[] becauseArgs);
public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
    this ValueTask<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
    string because = "", params object[] becauseArgs) where TError : Error;
```

`TimeoutException`, source exceptions and cancellation propagate unchanged. The token and timeout
bound only the assertion wait; they do not complete or cancel the source operation. Use `finally`
to release controlled sources. Timeout validation follows `Task.WaitAsync` (including infinite
timeouts). Each ValueTask is converted to Task once; do not consume the original again. No xUnit,
test-runner token, or global timeout is inferred by the library.

#### `MaybeAssertionsExtensions`
```csharp
public static class MaybeAssertionsExtensions
{
    public static MaybeAssertions<T> Should<T>(this Maybe<T> maybe)
        where T : notnull;
}
```

#### `MaybeAssertions<T>`
```csharp
public class MaybeAssertions<T> : ReferenceTypeAssertions<Maybe<T>, MaybeAssertions<T>>
    where T : notnull
{
    public MaybeAssertions(Maybe<T> maybe);

    public AndWhichConstraint<MaybeAssertions<T>, T> HaveValue(
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> BeNone(
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> HaveValueEqualTo(
        T expectedValue,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> HaveValueMatching(
        Func<T, bool> predicate,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> HaveValueEquivalentTo(
        T expectedValue,
        string because = "",
        params object[] becauseArgs);
}
```

When a value assertion is evaluated inside a FluentAssertions `AssertionScope`, a `None`
receiver records the normal "Expected Maybe to have a value..." assertion failure and
does not dereference `Maybe<T>.Value`.

#### `ActorIdentityAssertionsExtensions`

```csharp
public static class ActorIdentityAssertionsExtensions
{
    public static Task ShouldHaveConsistentActorIdentityAsync(
        this IActorProvider actorProvider,
        IActorIdentityProvider identityProvider,
        CancellationToken cancellationToken = default);
}
```

Resolves identity first and full actor second **sequentially**, so scoped repositories
are not used concurrently. When an actor resolves, asserts that identity is present and
equals `Actor.Id`. Both absent, or identity present with actor absent, are valid.
Failures name both provider types and both outcomes, including explicit `None`; a
FluentAssertions `AssertionScope` collects the failure without unwrapping absence.
Null arguments throw `ArgumentNullException`; provider failures and cancellation
propagate unchanged. Use interfaces resolved from the same scope to test registration too.

`TestActorProvider` also implements `IActorIdentityProvider`: its
`GetCurrentActorIdAsync` reads the active `WithActor` scope's canonical ID (or the default
actor's ID) without invoking full actor resolution, and honors cancellation. Scope
restoration and parallel-test isolation apply to both contracts.

#### `ErrorAssertionsExtensions`
```csharp
public static class ErrorAssertionsExtensions
{
    public static ErrorAssertions Should(this Error? error);
}
```

#### `ErrorAssertions`
```csharp
public class ErrorAssertions : ReferenceTypeAssertions<Error, ErrorAssertions>
{
    public ErrorAssertions(Error? error);

    public AndConstraint<ErrorAssertions> Be(
        Error expected,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ErrorAssertions> HaveCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ErrorAssertions> HaveDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ErrorAssertions> HaveDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs);

    public new AndWhichConstraint<ErrorAssertions, TError> BeOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error;
}
```

> **Note:** The `HaveInstance(...)` assertion was removed. `Error.Instance` <!-- doc-member-ok: Error.Instance; historical removed member, not current API --> is no longer part of the closed-ADT base — the ASP wire layer populates `ProblemDetails.Instance` from the server-relative request path+query, and typed payloads expose `ResourceRef` (e.g. `Error.NotFound.Resource`) directly for assertion via `BeOfType<Error.NotFound>().Which.Resource`.

#### `ValidationErrorAssertionsExtensions`
```csharp
public static class ValidationErrorAssertionsExtensions
{
    // Bound to Error.InvalidInput (the replacement for the previous validation error class).
    // Method names preserved for source-compat at test sites.
    public static ValidationErrorAssertions Should(this Error.InvalidInput error);
}
```

#### `ValidationErrorAssertions`
```csharp
public class ValidationErrorAssertions : ReferenceTypeAssertions<Error.InvalidInput, ValidationErrorAssertions>
{
    public ValidationErrorAssertions(Error.InvalidInput error);

    public AndConstraint<ValidationErrorAssertions> HaveFieldError(
        string fieldName,                              // accepted as either "email" or "/email" — normalized via InputPointer.ForProperty
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ValidationErrorAssertions> HaveFieldErrorWithDetail(
        string fieldName,
        string expectedDetail,                         // matches FieldViolation.Detail exactly
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ValidationErrorAssertions> HaveFieldCount(
        int expectedCount,                             // counts distinct field paths
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<ValidationErrorAssertions, FieldViolation> HaveFieldViolation(
        InputPointer field, string reasonCode, string because = "", params object[] becauseArgs);
    public AndWhichConstraint<ValidationErrorAssertions, RuleViolation> HaveRuleViolation(
        string reasonCode, string because = "", params object[] becauseArgs);
    public AndConstraint<ValidationErrorAssertions> HaveFieldViolationCount(
        int expectedCount, string because = "", params object[] becauseArgs);
    public AndConstraint<ValidationErrorAssertions> HaveRuleViolationCount(
        int expectedCount, string because = "", params object[] becauseArgs);
    public AndConstraint<ValidationErrorAssertions> HaveOnlyFieldViolations(
        IEnumerable<FieldViolation> expected, string because = "", params object[] becauseArgs);
    public AndConstraint<ValidationErrorAssertions> HaveOnlyRuleViolations(
        IEnumerable<RuleViolation> expected, string because = "", params object[] becauseArgs);
}
```

`HaveFieldViolation` matches the entire `InputPointer`, including `In`, and the reason code;
`HaveRuleViolation` matches the reason code. Both require **exactly one** match and expose the
original object through `.Which`, so its typed `Args`, related `Fields` and optional `Detail` can
be asserted without reconstructing the error. They do not assert that other violations are absent.

`HaveFieldViolationCount` and `HaveRuleViolationCount` count all entries, including duplicates.
Count mismatches report the validation-error context, category, expected/actual counts and that category's violations.
The existing `HaveFieldCount` still counts distinct paths and ignores input location.

`HaveOnlyFieldViolations` and `HaveOnlyRuleViolations` compare **unordered multisets**, preserving
duplicate multiplicity, even when FluentAssertions is globally configured for strict ordering.
They use the violation records' domain equality: full pointers/locations,
reason codes, typed argument contents and `Detail` all matter; a number is not equal to text.
Related pointers *inside* a rule retain their domain-significant order. Each method asserts only
its own category; assert both categories (including an empty collection) to exclude all extras.
Null and empty argument dictionaries follow domain equality and compare equal. Constructing
expected violation records still records validation metrics, just as ordinary record construction
does; create expectations before starting a measurement probe when testing telemetry.

All six structured assertions report a null subject through FluentAssertions rather than
dereferencing it, including inside `AssertionScope`. Failed unique-match assertions return a
default `.Which` inside a collecting scope: do not dereference it after a failed prerequisite.
Null expected collections throw `ArgumentNullException`.

#### `UnwrapExtensions`
```csharp
public static class UnwrapExtensions
{
    public static T Unwrap<T>(this Result<T> result);

    public static Error UnwrapError<T>(this Result<T> result);

    public static Error UnwrapError(this Result<Unit> result);

    public static T Unwrap<T>(this Maybe<T> maybe)
        where T : notnull;

    public static Task<T> UnwrapAsync<T>(this Task<Result<T>> resultTask);

    public static ValueTask<T> UnwrapAsync<T>(this ValueTask<Result<T>> resultTask);
}
```

#### `UnwrapFailedException`
```csharp
public sealed class UnwrapFailedException : Exception
{
    public UnwrapFailedException();
    public UnwrapFailedException(string message);
    public UnwrapFailedException(string message, Exception innerException);
}
```

#### Callback recorders

```csharp
public sealed class CallbackRecorder
{
    public CallbackRecorder();
    public int CallCount { get; }
    public void Record();
    public Action Wrap(Action callback);
    public Func<TResult> Wrap<TResult>(Func<TResult> callback);
}

public sealed class CallbackRecorder<TArgument>
{
    public CallbackRecorder();
    public int CallCount { get; }
    public IReadOnlyList<TArgument> Arguments { get; }
    public void Record(TArgument argument);
    public Action<TArgument> Wrap(Action<TArgument> callback);
    public Func<TArgument, TResult> Wrap<TResult>(Func<TArgument, TResult> callback);
}
```

Recording happens at callback entry, even when the callback throws. Returned values and original
exceptions pass through unchanged; Task/ValueTask results are **not** awaited or consumed. Recording
is thread-safe, but callbacks are not serialized. `Arguments` is a snapshot in recording order,
not completion order; argument objects are not cloned. Use a tuple argument for multiple inputs.
Use one recorder per test; there is no shared global state or reset lifecycle. Null callbacks
throw `ArgumentNullException` before recording.

#### `ControlledAsync<T>`

```csharp
public sealed class ControlledAsync<T>
{
    public ControlledAsync();                       // Task-backed, shared outcome
    public bool IsSingleUse { get; }
    public Task<T> Task { get; }                    // throws InvalidOperationException when single-use
    public ValueTask<T> ValueTask { get; }
    public int ResultReadCount { get; }             // single-use reads; always 0 when Task-backed
    public bool TryComplete(T value);
    public bool TryFail(Exception exception);
    public bool TryCancel(CancellationToken cancellationToken = default);
}

public static class ControlledAsync
{
    public static ControlledAsync<T> SingleUse<T>();
}
```

One type, two modes. `new ControlledAsync<T>()` is Task-backed: every receiver shares one task, which may be
awaited repeatedly. `ControlledAsync.SingleUse<T>()` backs `ValueTask` with an `IValueTaskSource`: a second
consumption throws `InvalidOperationException` and is counted, so `ResultReadCount` proves an implementation
consumed the ValueTask exactly once. Reading `Task` on a single-use source throws; use `ValueTask`.

Both modes start genuinely pending, run continuations asynchronously, and share one terminal outcome: the first
successful transition returns true; later transitions return false without changing it, including under
concurrent attempts. Values, exception objects and cancellation tokens are preserved. `TryCancel` associates a
token with the canceled source; it does not cancel the caller's token. `TryFail(null)` always throws
`ArgumentNullException`. No implicit timeout, disposal or cleanup is performed. Release pending sources in
`finally` (or use `ShouldBePendingThenReleaseAsync`) and bound assertion waits explicitly.

#### Pending-operation assertions

```csharp
public static class PendingOperationAssertionsExtensions
{
    public static Task<TResult> ShouldBePendingThenReleaseAsync<TResult>(
        this Task<TResult> operation, Action release, TimeSpan timeout, CancellationToken cancellationToken,
        Action? whilePending = null);
    public static Task ShouldBePendingThenReleaseAsync(
        this Task operation, Action release, TimeSpan timeout, CancellationToken cancellationToken,
        Action? whilePending = null);
    public static ValueTask<TResult> ShouldBePendingThenReleaseAsync<TResult>(
        this ValueTask<TResult> operation, Action release, TimeSpan timeout, CancellationToken cancellationToken,
        Action? whilePending = null);
}
```

Asserts the operation has not completed, runs `whilePending` checks (callback counts, source reads), then **always**
runs `release` (even when the pending assertion or a check fails, so a failed check cannot leave the gate
closed) and awaits the operation with `timeout`/`cancellationToken`, returning its result. When the pending
assertion or a check fails, the failure propagates immediately: the released operation is not awaited, so its
own outcome is not observed. `TimeoutException`,
operation exceptions and cancellation propagate unchanged; the bound never cancels the operation. A ValueTask is
consumed exactly once. This replaces the hand-written `try { assert pending } finally { release }` scaffold.

#### Propagation assertions

```csharp
public static class PropagationAssertionsExtensions
{
    public static Task ShouldPropagateExceptionAsync(
        this Task operation, Exception expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    public static Task ShouldPropagateExceptionAsync(
        this ValueTask operation, Exception expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    public static Task ShouldPropagateExceptionAsync<T>(
        this ValueTask<T> operation, Exception expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    public static Task ShouldPropagateCancellationAsync(
        this Task operation, CancellationToken expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    public static Task ShouldPropagateCancellationAsync(
        this ValueTask operation, CancellationToken expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    public static Task ShouldPropagateCancellationAsync<T>(
        this ValueTask<T> operation, CancellationToken expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
```

`timeout` and `cancellationToken` bound only the assertion's wait. With neither supplied the operation is awaited
**without a bound**, so pass them whenever the operation could be stranded on a pending source: an expired
`timeout` fails the assertion ("did not complete within ...") instead of hanging, and a canceled
`cancellationToken` throws `OperationCanceledException`. Neither completes or cancels the operation.

`ShouldPropagateExceptionAsync` requires the operation to fault with exactly the `expected` instance
(reference equality, no wrapping); an equal-looking different instance fails. `ShouldPropagateCancellationAsync`
requires cancellation carrying exactly the expected token. Failures name what happened instead (a different
exception type, a different token, a fault where cancellation was expected, or successful completion). A
`Task<T>` binds to the `Task` overload.

#### `CanceledToken`

```csharp
public sealed class CanceledToken : IDisposable
{
    public static CanceledToken Create();
    public CancellationToken Token { get; }
    public Task ToTask();
    public Task<T> ToTask<T>();
    public ValueTask ToValueTask();
    public ValueTask<T> ToValueTask<T>();
    public void Dispose();
}
```

Each `Create()` owns a distinct `CancellationTokenSource`, so a test can prove an operation preserved *this*
token rather than substituting another canceled one (all `new CancellationToken(true)` values are equal). The
`ToTask`/`ToValueTask` methods return canceled awaitables carrying `Token`. Dispose it with `using`.

#### `TestGate`

```csharp
public sealed class TestGate : IDisposable
{
    public TestGate(bool permanentRelease = true);
    public bool ReleaseIsPermanent { get; }
    public int ArrivalCount { get; }

    // Called by the code under test
    public void Signal();
    public Task WaitUntilReleasedAsync(CancellationToken cancellationToken = default);
    public Task ArriveAsync(CancellationToken cancellationToken = default);   // Signal + WaitUntilReleased

    // Called by the test
    public Task WaitForArrivalAsync(TimeSpan timeout, CancellationToken cancellationToken);
    public Task WaitForArrivalsAsync(int count, TimeSpan timeout, CancellationToken cancellationToken);
    public void Release();
    public void Dispose();
}
```

A rendezvous between code under test and the test: the code under test reaches a point and waits there
(`ArriveAsync`), the test waits for the arrival, asserts about the paused state, then calls `Release()`. It
replaces the hand-written `entered` / `release` `TaskCompletionSource` pair, and fixes that pair's recurring
defects: waiting for an arrival **requires a timeout and token**, so a missed arrival fails with a
`TimeoutException` naming how many arrivals were seen instead of hanging; arrivals are **counted and never
throw** when repeated; and `Dispose()` releases the gate, so `using var gate = new TestGate();` cannot strand a
background flow when an assertion fails. Declare the gate after any fixture it blocks so it is disposed first.

`Release()` is **permanent by default** (`ReleaseIsPermanent` is true): later arrivals pass straight through, so
a flow that re-enters after release cannot hang. Construct with `permanentRelease: false` for a gate that frees
only the callers currently waiting and closes again for later arrivals. `Dispose()` always opens the gate
permanently. `Signal()` records an arrival without waiting, and `WaitUntilReleasedAsync` waits without
recording one. Together with `WaitForArrivalsAsync(count, ...)` they also express a barrier: each of N
concurrent flows calls `Signal()` and then `WaitForArrivalsAsync(N, ...)`, replacing a counter plus a
`TaskCompletionSource`. A canceled token cancels only that wait, and a canceled `ArriveAsync` still counts as an
arrival. `WaitForArrivalsAsync` rejects a `count` below one. All members are thread-safe.

#### Argument-null assertions

```csharp
public static class ArgumentNullAssertionsExtensions
{
    public static void ShouldRejectNull(this Action act, string paramName);
    public static Task ShouldRejectNullAsync(this Func<Task> act, string paramName);
    public static Task ShouldRejectNullBeforeAwaitAsync(this Func<Task> start, string paramName);
    public static Task ShouldRejectNullBeforeAwaitAsync(this Task operation, string paramName);
}
```

All three require `ArgumentNullException` whose `ParamName` is exactly `paramName`, so a guard cannot pass
for the wrong parameter. `ShouldRejectNullAsync` accepts a synchronous throw or a faulted task.
`ShouldRejectNullBeforeAwaitAsync` additionally proves the guard is eager, so a still-pending source was never
awaited; if the returned task is still pending the assertion fails immediately without waiting. The `Func<Task>`
form accepts a synchronous throw **or** an already-faulted task. The `Task` form takes an operation you already
started and requires the error to arrive through that returned task: a synchronous throw while starting it escapes
the test and fails it. Prefer the `Task` form for Trellis async extensions, which promise to report argument
errors through the returned awaitable.

#### Domain-event assertions

```csharp
public static class DomainEventAssertionsExtensions
{
    public static AndWhichConstraint<GenericCollectionAssertions<IDomainEvent>, TEvent> HaveRaised<TEvent>(
        this GenericCollectionAssertions<IDomainEvent> assertions,
        Func<TEvent, bool>? predicate = null, string because = "", params object[] becauseArgs)
        where TEvent : IDomainEvent;
    public static AndConstraint<GenericCollectionAssertions<IDomainEvent>> NotHaveRaised<TEvent>(
        this GenericCollectionAssertions<IDomainEvent> assertions, string because = "", params object[] becauseArgs)
        where TEvent : IDomainEvent;
    public static AndConstraint<GenericCollectionAssertions<IDomainEvent>> HaveRaisedCount<TEvent>(
        this GenericCollectionAssertions<IDomainEvent> assertions, int expected,
        string because = "", params object[] becauseArgs)
        where TEvent : IDomainEvent;
    public static AndConstraint<GenericCollectionAssertions<IDomainEvent>> HaveRaisedExactlyInOrder(
        this GenericCollectionAssertions<IDomainEvent> assertions, params Type[] eventTypes);
}
```

These extend the collection assertions from `aggregate.UncommittedEvents().Should()` and
`FakeRepository.PublishedEvents.Should()`. There is deliberately no `Should()` on aggregates, so existing
`aggregate.Should()` calls keep binding to FluentAssertions. `HaveRaised<TEvent>` requires **exactly one**
matching event (optionally narrowed by `predicate`) and exposes it through `.Which`; zero or several matches fail
and list every raised event type. `HaveRaisedCount` asserts an exact count, `NotHaveRaised` absence, and
`HaveRaisedExactlyInOrder` the exact runtime-type sequence. `HaveRaised`, `NotHaveRaised` and
`HaveRaisedCount` match with `OfType<TEvent>()`, so subtypes and interface implementations count;
`HaveRaisedExactlyInOrder` compares exact runtime types and takes no `because` arguments. To also assert payloads, keep
`UncommittedEvents().Should().Equal(...)` (record equality). Use `BeEmpty()` for "nothing raised".

#### `PageAssertions<T>`

```csharp
public static class PageAssertionsExtensions
{
    public static PageAssertions<T> Should<T>(this Page<T> page);
}

public class PageAssertions<T> : ReferenceTypeAssertions<Page<T>, PageAssertions<T>>
{
    public AndConstraint<PageAssertions<T>> Be(Page<T> expected, string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> HaveItems(IEnumerable<T> expected, string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> HaveCount(int expected, string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> BeEmpty(string because = "", params object[] becauseArgs);
    public AndWhichConstraint<PageAssertions<T>, Cursor> HaveNextCursor(string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> HaveNoNextCursor(string because = "", params object[] becauseArgs);
    public AndWhichConstraint<PageAssertions<T>, Cursor> HavePreviousCursor(string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> HaveNoPreviousCursor(string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> BeLastPage(string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> BeFirstPage(string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> HaveRequestedLimit(int expected, string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> HaveAppliedLimit(int expected, string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> BeCapped(string because = "", params object[] becauseArgs);
    public AndConstraint<PageAssertions<T>> NotBeCapped(string because = "", params object[] becauseArgs);
}
```

For the domain `Page<T>` returned by query handlers (not the HTTP wire shape). `HaveItems` compares in order;
`HaveCount` uses `DeliveredCount`. `HaveNextCursor`/`HavePreviousCursor` expose the original `Cursor` through
`.Which` (cursor tokens are opaque; assert presence or pass it into the next `PageRequest`). `BeLastPage` means no
next cursor and `BeFirstPage` no previous cursor. `BeCapped` means the applied limit is below the requested limit.
`Be` compares by record equality. A null page reports an assertion failure.

#### `SpecificationAssertions<T>`

```csharp
public static class SpecificationAssertionsExtensions
{
    public static SpecificationAssertions<T> Should<T>(this Specification<T> specification);
}

public class SpecificationAssertions<T> : ReferenceTypeAssertions<Specification<T>, SpecificationAssertions<T>>
{
    public AndConstraint<SpecificationAssertions<T>> BeSatisfiedBy(T candidate, string because = "", params object[] becauseArgs);
    public AndConstraint<SpecificationAssertions<T>> NotBeSatisfiedBy(T candidate, string because = "", params object[] becauseArgs);
    public AndConstraint<SpecificationAssertions<T>> BeSatisfiedByAll(IEnumerable<T> candidates, string because = "", params object[] becauseArgs);
    public AndConstraint<SpecificationAssertions<T>> BeSatisfiedByNone(IEnumerable<T> candidates, string because = "", params object[] becauseArgs);
    public AndConstraint<SpecificationAssertions<T>> SelectExactly(IEnumerable<T> candidates, IEnumerable<T> expected, string because = "", params object[] becauseArgs);
}
```

Replaces `spec.IsSatisfiedBy(x).Should().BeTrue()`. Failures name the candidate and the specification's
expression; `BeSatisfiedByAll` and `BeSatisfiedByNone` list **every** offending candidate, not just the first.
`SelectExactly` asserts the candidates the specification accepts equal `expected` in candidate order. Composed
specifications (`And`, `Or`, `Not`) are asserted like any other. A null `candidates` argument throws
`ArgumentNullException`.

#### `ActorAssertions`

```csharp
public static class ActorAssertionsExtensions
{
    public static ActorAssertions Should(this Actor? actor);
}

public class ActorAssertions : ReferenceTypeAssertions<Actor, ActorAssertions>
{
    public AndConstraint<ActorAssertions> Be(Actor expected, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HavePermission(string permission, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HaveScopedPermission(string permission, string scope, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> NotHavePermission(string permission, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HaveAllPermissions(IEnumerable<string> permissions, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HaveAnyPermission(IEnumerable<string> permissions, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HaveId(ActorId expected, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HaveId(string expected, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> BeOwnerOf(ActorId resourceOwnerId, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HaveAttribute(string key, string because = "", params object[] becauseArgs);
    public AndConstraint<ActorAssertions> HaveAttributeValue(string key, string value, string because = "", params object[] becauseArgs);
}
```

Permission assertions follow the pipeline rule (`Actor.HasPermission`): an explicitly forbidden permission
overrides a granted one. `HavePermission` failures say whether the permission was **forbidden** or **never
granted**; `HaveAllPermissions` lists every missing permission. `HaveScopedPermission` builds
`permission:scope` using `Actor.PermissionScopeSeparator`. `NotHavePermission` passes for both absent and
forbidden permissions. Scope, attribute-value and permission methods have distinct names on purpose: an overload
`HaveAttribute(key, value)` would silently bind to the `because` parameter.

#### `IAuthorize` message assertions

```csharp
public static class AuthorizeMessageAssertionsExtensions
{
    public static void ShouldRequirePermissions(this IAuthorize message, params string[] permissions);
    public static void ShouldBeAuthorizedFor(this IAuthorize message, Actor actor);
    public static void ShouldNotBeAuthorizedFor(this IAuthorize message, Actor actor);
}
```

`ShouldRequirePermissions` asserts the message requires exactly that set of permissions, ignoring order, and
reports missing and unexpected entries. `ShouldBeAuthorizedFor` and `ShouldNotBeAuthorizedFor` apply the same
`HasAllPermissions` rule as the Mediator authorization behavior, so denied paths need no pipeline. These are
explicit methods rather than a `Should()` entry point so commands and queries keep binding to FluentAssertions.
Resource-based checks return `IResult`; assert them with `IResultAssertions`.

> **Source-breaking `Should()` overloads.** `Should()` on `Page<T>`, `Specification<T>` and `Actor` now binds to
> `PageAssertions<T>`, `SpecificationAssertions<T>` and `ActorAssertions` instead of FluentAssertions' object
> assertions. `PageAssertions<T>` and `ActorAssertions` keep `Be`, but `BeEquivalentTo`, `NotBe` and other
> object-assertion members are not available on these types; compare the members you care about, or use
> `Should().BeSameAs(...)`, which `ReferenceTypeAssertions` provides.

#### `AggregateTestMutator`
```csharp
public static class AggregateTestMutator
{
    [RequiresUnreferencedCode("Uses reflection to set source-generated backing fields. Not AOT-compatible — test-only.")]
    public static TEntity SetMaybeField<TEntity, TValue>(
        this TEntity entity,
        Expression<Func<TEntity, Maybe<TValue>>> propertySelector,
        TValue? value)
        where TEntity : class
        where TValue : notnull;

    [RequiresUnreferencedCode("Uses reflection to set source-generated backing fields. Not AOT-compatible — test-only.")]
    public static TEntity ClearMaybeField<TEntity, TValue>(
        this TEntity entity,
        Expression<Func<TEntity, Maybe<TValue>>> propertySelector)
        where TEntity : class
        where TValue : notnull;
}
```

> **AOT/trim incompatibility.** `AggregateTestMutator` uses reflection to set source-generated `Maybe<T>` backing fields. Both methods carry `[RequiresUnreferencedCode]`; AOT-published consumers will receive IL2026 / IL3050 warnings at the call site. The helpers are intentionally test-only — do not call them from production code.

#### `FakeRepository<TAggregate, TId>`
```csharp
public class FakeRepository<TAggregate, TId>
    where TAggregate : Aggregate<TId>
    where TId : notnull
{
    public IReadOnlyList<IDomainEvent> PublishedEvents { get; }
    public int Count { get; }

    public FakeRepository<TAggregate, TId> WithUniqueConstraint(Func<TAggregate, object?> propertySelector);

    public Task<Result<TAggregate>> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    public Task<Maybe<TAggregate>> FindByIdAsync(TId id, CancellationToken cancellationToken = default);

    // Read surface mirroring RepositoryBase<TAggregate, TId>. Use these from test
    // repository adapters that expose specification-based queries.
    public Task<IReadOnlyList<TAggregate>> QueryAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default);
    public Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);
    public Task<bool> ExistsAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default);
    public Task<int> CountAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default);

    // Setup surface — mirrors RepositoryBase<TAggregate, TId>. Use these in handlers and
    // in test setup so the same IRepository contract works in both the EF and fake paths.
    // Both Add and Remove (and DeleteAsync below) capture aggregate.UncommittedEvents()
    // into PublishedEvents and call AcceptChanges, so deletion-related domain events
    // are observable through PublishedEvents.
    public void Add(TAggregate aggregate);
    public void Remove(TAggregate aggregate);
    public Task<Result<Unit>> RemoveByIdAsync(TId id, CancellationToken cancellationToken = default);

    // Result-shape surface — only on the fake. Reserve for tests that explicitly assert
    // on Result-of-save shape (e.g., conflict-result handling). NOT part of RepositoryBase.
    public Task<Result<Unit>> SaveAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
    public Task<Result<Unit>> DeleteAsync(TId id, CancellationToken cancellationToken = default);

    public void Clear();
    public bool Exists(TId id);
    public TAggregate? Get(TId id);
    public IEnumerable<TAggregate> GetAll();

    // Predicate/specification-based local helpers. Prefer the RepositoryBase-shaped
    // QueryAsync/ExistsAsync/CountAsync above when building same-contract test adapters;
    // these remain available for legacy test code and ad-hoc Func-based filtering.
    public Task<Maybe<TAggregate>> FindAsync(Func<TAggregate, bool> predicate);
    public Task<IReadOnlyList<TAggregate>> WhereAsync(Func<TAggregate, bool> predicate);
    public Task<IReadOnlyList<TAggregate>> WhereAsync(Specification<TAggregate> specification);
}
```

> **Cancellation token observability.** Repository-shaped `*Async` methods on `FakeRepository<TAggregate, TId>` accept a `CancellationToken` parameter for source-compat with `RepositoryBase<TAggregate, TId>` but **do not observe it** — the fake completes synchronously. The local `FindAsync` helper and both `WhereAsync` overloads have no token parameter. Tests that rely on cancellation behavior need a different test double; this fake intentionally trades cancellation-observability for the simpler synchronous semantics that DDD aggregate tests typically need.

> **Null guards.** `Add`, `Remove`, `SaveAsync`, `WithUniqueConstraint`, `QueryAsync`, `ExistsAsync(Specification<TAggregate>)`, `CountAsync`, `FindAsync`, and `WhereAsync` all `ArgumentNullException.ThrowIfNull(...)` their reference-type parameters. `GetByIdAsync`, `FindByIdAsync`, `RemoveByIdAsync`, `DeleteAsync`, and `ExistsAsync(TId)` rely on the `TId : notnull` constraint at compile time.

#### `FakeSharedResourceLoader<TResource, TId>`
```csharp
public class FakeSharedResourceLoader<TResource, TId> : SharedResourceLoaderById<TResource, TId>
    where TResource : Aggregate<TId>
    where TId : notnull
{
    public FakeSharedResourceLoader(FakeRepository<TResource, TId> repository);

    public override Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken);
}
```

#### `FakeUnitOfWork`

```csharp
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public FakeUnitOfWork();

    public Result<Unit> CommitResult { get; set; }
    public int CommitCallCount { get; }
    public int CommitAttemptCount { get; }

    public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default);
    public IUnitOfWorkScope BeginScope();
}
```

Provider-neutral test double for the [`IUnitOfWork` contract](trellis-api-persistence-abstractions.md#iunitofwork).
`Trellis.Testing` references `Trellis.Persistence.Abstractions`, not EF Core or Mediator.
Pass the fake to the real `TransactionalCommandBehavior` when testing commit decisions;
an ordinary handler-only test does not need a unit of work.

| Member / scenario | Behavior |
| --- | --- |
| `CommitResult` | Defaults to `Result.Ok()`. Each non-deferred, uncanceled call returns the current configured result unchanged; changing it affects subsequent attempts. A failed attempt does not permanently reject later attempts. |
| `CommitCallCount` | Counts every `CommitAsync` invocation, including deferred nested calls and canceled attempts. Starts at zero. |
| `CommitAttemptCount` | Counts calls at scope depth zero or one, including success, failure, and cancellation. Deferred calls do not increment it. This is not a successful-write count. |
| Nested commit (depth > 1) | Returns success without using `CommitResult` or observing cancellation, matching the EF adapter's deferred-commit behavior. |
| Cancellation at depth zero or one | An already-canceled token produces a canceled task carrying that token, instead of the configured result. Normal calls complete synchronously; no in-flight I/O or cancellation is simulated. |
| `BeginScope` | Returns `IUnitOfWorkScope` with fixed `IsOwner`: true only for the outermost scope. Dispose scopes in reverse opening order. Disposal is idempotent, restores depth, and never commits. |

Use one instance per test or DI scope and serial nested commands only. The fake does not
coordinate `FakeRepository`, track aggregates, persist data, or simulate rollback.
Repository changes remain immediately visible even when a commit fails. Keep real-provider
integration tests for persistence atomicity, rollback, and post-commit aggregate tracking.

#### `TestActorProvider`
```csharp
public sealed class TestActorProvider : IActorProvider, IActorIdentityProvider
{
    public TestActorProvider(Actor actor);
    public TestActorProvider(string userId, params string[] permissions);

    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default);
    public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default);

    public TestActorScope WithActor(Actor actor);
    public TestActorScope WithActor(string userId, params string[] permissions);

    public static TestActorProvider Anonymous();
    public TestActorScope WithoutActor();
}
```

`Anonymous()` creates a provider that resolves no actor and no identity (`Maybe.None` from both
`GetCurrentActorAsync` and `GetCurrentActorIdAsync`) until a `WithActor` scope is opened, for testing
unauthenticated and 401 paths. `WithoutActor()` makes the current async flow anonymous for the scope's
duration, then restores the previous actor; it nests with `WithActor` and is isolated across parallel flows.

#### `TestActorScope`
```csharp
public sealed class TestActorScope : IAsyncDisposable, IDisposable
{
    public ValueTask DisposeAsync();
    public void Dispose();
}
```

## Usage notes

### Assertions

- Synchronous assertions start from `Result<T>` or `Maybe<T>`:
  - `result.Should().BeSuccess()`
  - `result.Should().BeFailureOfType<Error.InvalidInput>()`
  - `maybe.Should().HaveValue()`
- **Async assertions are extension methods on `Task<Result<T>>` and `ValueTask<Result<T>>`, not on `ResultAssertions<T>`.**
  - Correct: `await resultTask.BeSuccessAsync();`
  - Correct: `await valueTaskResult.BeFailureAsync();`
  - Wrong: `await result.Should().BeSuccessAsync();`

### FakeRepository

- **Setup surface** (mirrors `RepositoryBase<TAggregate, TId>` in `Trellis.EntityFrameworkCore`) — use these from handlers and test setup so the same `IRepository` contract works in both worlds:
  - `void Add(TAggregate)` — stages an insert; in the fake, immediately visible. Throws `InvalidOperationException` on unique-constraint violation (setup mistakes should fail loud at the call site).
  - `void Remove(TAggregate)` — stages a delete; no-op if the aggregate is not in the store.
  - `Task<Result<Unit>> RemoveByIdAsync(TId)` — looks up by ID and removes; returns `Error.NotFound` if missing.
- **Result-shape surface** (only on the fake — `RepositoryBase` does not expose these) — use only when the test specifically asserts on the `Result` of the persistence call:
  - `Task<Result<Unit>> SaveAsync(TAggregate)` — returns `Error.Conflict` on unique-constraint violation. Use to test conflict-handling code paths.
  - `Task<Result<Unit>> DeleteAsync(TId)` — returns `Error.NotFound` on missing. Use to test not-found handling. (`RemoveByIdAsync` is the staging-API-named alias.)
- `WithUniqueConstraint(Func<TAggregate, object?> propertySelector)` — fluent constraint registration; checked eagerly by `Add` (throws) and at-call by `SaveAsync` (returns `Result`).
- `Clear()`, `Exists(TId id)`, `Get(TId id)`, `GetAll()`, `Count` — direct inspection helpers
- `GetByIdAsync` / `DeleteAsync` / `RemoveByIdAsync` return `Error.NotFound` details in the EF-runtime format:
  - `"{AggregateTypeName} with ID '{id}' not found."`
- Unique-constraint conflicts return `Error.Conflict` with `Code` `FaultCodes.DuplicateKey` (`duplicate.key`), matching the EF runtime, and detail:
  - `"A {AggregateTypeName} with the same value already exists."`

> See cookbook **Recipe 16 — Unit of work in handlers** for guidance on which surface to use from where, and the pitfall of accidentally calling `SaveAsync` from a production-shaped repository contract.

## Compilable examples

### Result assertions

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var success = Result.Ok(42);
success.Should().BeSuccess().Which.Should().Be(42);

var notFound = Result.Fail<int>(new Error.NotFound(ResourceRef.For("Order", "123")) { Detail = "Order 123 not found" });
notFound.Should().BeFailure()
    .Which.Detail.Should().Be("Order 123 not found");
```

### Async assertions

```csharp
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Trellis.Testing;

Task<Result<int>> resultTask = Task.FromResult(Result.Ok(42));
ValueTask<Result<int>> valueTaskResult = ValueTask.FromResult(Result.Ok(7));

(await resultTask.BeSuccessAsync()).Which.Should().Be(42);
(await valueTaskResult.BeSuccessAsync()).Which.Should().Be(7);
```

### FakeRepository

```csharp
using System;
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var repo = new FakeRepository<Order, OrderId>()
    .WithUniqueConstraint(order => order.Id);

var order = new Order(new OrderId(Guid.NewGuid()));

await repo.SaveAsync(order).BeSuccessAsync();
(await repo.GetByIdAsync(order.Id)).Should().BeSuccess().Which.Should().BeSameAs(order);
repo.Exists(order.Id).Should().BeTrue();
repo.Count.Should().Be(1);

public sealed record OrderId(Guid Value);

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id) : base(id) { }
}
```
