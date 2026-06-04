# Migration Guide: v2.x → v3.0

> [!IMPORTANT]
> This guide documents the historical FunctionalDDD v2.x → Trellis v3.0 migration (renamed failure-track operations: `TapError` → `TapOnFailure`, `Compensate` → `RecoverOnFailure`, etc.). The advice below still applies for projects upgrading from FunctionalDDD v2.x.
>
> **Trellis V2 (the current major release) introduces a separate, larger breaking change**: the `Error` type is now a closed discriminated-union ADT. The current case set is documented in [`docs/docfx_project/articles/error-handling.md`](docs/docfx_project/articles/error-handling.md). The section [Error union DDD realignment](#error-union-ddd-realignment) below covers the latest rename pass; the [CHANGELOG](CHANGELOG.md#breaking-changes--trelliscoreerror-union-ddd-realignment) carries the canonical rename and slug-change tables.

## End-to-end migration playbook (FunctionalDdd 2.x → Trellis 3.0)

The detail sections in this file each cover one piece of the migration. For consumers upgrading a real codebase from `FunctionalDdd 2.1.x` to `Trellis 3.0`, the **order** below minimizes churn. In practice the mechanical work (Steps 1–4) usually lands as one commit — production code builds clean after Step 4; test code typically stays red until Step 5 handles the `Result<T>.Value` removal. Step 6 is an optional opt-in commit on top.

**Real-world reference.** [`xavierjohn/BuberDinner` `upgrade/trellis-v3`](https://github.com/xavierjohn/BuberDinner/tree/upgrade/trellis-v3) migrated from `FunctionalDdd 2.1.10` to `Trellis 3.0.0-alpha.337` (net8 → net10) in 4 commits, 32 files changed, +1,478 / −165 LoC, zero net test regression. Each step below corresponds to a discrete commit on that branch you can read in isolation.

### Step 1 — Package mapping

Swap every `FunctionalDdd.*` package for the Trellis equivalent. This is the single biggest mechanical change in the migration.

| FunctionalDdd 2.x | Trellis 3.0 |
|---|---|
| `FunctionalDdd.DomainDrivenDesign` | `Trellis.Core` (DDD primitives collapsed in) |
| `FunctionalDdd.RailwayOrientedProgramming` | `Trellis.Core` (ROP operators collapsed in) |
| `FunctionalDdd.CommonValueObjectGenerator` | `Trellis.Core` (bundled at `analyzers/dotnet/cs/` — no separate generator package) |
| `FunctionalDdd.CommonValueObjects` | `Trellis.Primitives` |
| `FunctionalDdd.FluentValidation` | `Trellis.FluentValidation` |
| `FunctionalDdd.Asp` | `Trellis.Asp` |
| (not previously available) | `Trellis.Mediator` — optional, see Step 6 |

Update `Directory.Build.props`'s global `<Using>` entries (`FunctionalDdd` → `Trellis`) and `Directory.Packages.props` package versions. Every `.csproj` `<PackageReference>` updates accordingly.

**One-time housekeeping with Central Package Management.** If you use CPM (`<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`) and your dev machines have multiple package sources configured in the user-level `NuGet.Config`, `dotnet restore` may fail with `NU1507: There are X package sources defined in your configuration`. Add a project-local `nuget.config` pinning `nuget.org` as the only source. One file, no source code touched. Independent of the framework swap but typically needed before it.

### Step 2 — Mechanical renames

After the package swap, the compiler surfaces several mechanical renames. None carry semantic risk:

| FunctionalDdd 2.x | Trellis 3.0 | Notes |
|---|---|---|
| `Result.Success(...)` / `Result.Success<T>(...)` | `Result.Ok(...)` / `Result.Ok<T>(...)` | Mechanical find-and-replace |
| `Result.Failure(...)` / `Result.Failure<T>(...)` | `Result.Fail(...)` / `Result.Fail<T>(...)` | Mechanical find-and-replace |
| `.TapError(...)` | `.TapOnFailure(...)` | See [Renamed Operations](#renamed-operations) for the full failure-track rename table |
| `.MapError(...)` | `.MapOnFailure(...)` | Same |
| `.Compensate(...)` | `.RecoverOnFailure(...)` | Same |
| `.ToActionResultAsync(this)` | `.ToHttpResponseAsync().AsActionResultAsync<T>()` | See [Trellis.Asp v3 - legacy response verbs removed](#trellisasp-v3---legacy-response-verbs-removed) |
| `MyId.NewUnique()` | `MyId.NewUniqueV4()` or `MyId.NewUniqueV7()` | Explicit choice forced — `NewUniqueV7()` is the right default for primary keys (time-ordered, monotonic, index-friendly) |

The `Result.Success` / `Failure` → `Ok` / `Fail` renames are simple find-and-replace; the failure-track operator renames (`TapError`, `MapError`, `Compensate`) are covered by the [Automated Migration](#automated-migration) PowerShell script below.

### Step 3 — CRTP primitives

Every `Required*` base now takes the derived type as a generic parameter:

```csharp
// v2.x
public sealed class UserId : RequiredString { }

// v3
public sealed partial class UserId : RequiredString<UserId> { }
```

Apply this shape to every `RequiredString`, `RequiredGuid`, `RequiredInt`, `RequiredLong`, `RequiredDecimal`, `RequiredBool`, `RequiredDateTime`, `RequiredDateTimeOffset`, and `RequiredEnum` derivative. Add the `partial` keyword if it isn't already present — the source generator now emits `TryCreate` / `Create` / `Parse` / `TryParse` / `JsonConverter` into the partial class.

The generator is bundled inside `Trellis.Core.nupkg` at `analyzers/dotnet/cs/`, so the `FunctionalDdd.CommonValueObjectGenerator` package reference can be dropped — installing `Trellis.Core` attaches the generator automatically.

**Strict-by-default attributes.** `Required*<TSelf>` is now strict-by-default in v3 — `null`, sentinel values, and (for `RequiredString`) `""` / whitespace-only input are rejected without any opt-in attribute. The pre-v3 `[NotDefault]` and `[Trim]` attributes are now vestigial no-ops (the generator emits informational diagnostics `TRLS046` / `TRLS047`). See [`Required<T>` defaults flip](#requiredt-defaults-flip-strict-by-default-with-per-type-opt-outs) for the full strict-default rules and the per-type opt-outs (`[AllowEmpty]`, `[AllowWhitespace]`, `[NoTrim]`, `[AllowZero]`, `[AllowMinValue]`).

### Step 4 — Closed Error ADT port

The open `Error` hierarchy (`UnauthorizedError`, `ConflictError`, `NotFoundError`, `ValidationError`, etc.) is gone. v3 ships a closed 12-case discriminated union under `Trellis.Error`. Replace the open subclass references with the closed-ADT case constructors:

```csharp
// v2.x
return new UnauthorizedError("Invalid credentials.", "Authentication.InvalidCredentials");
return new ConflictError("Email already in use.", "Email", "user@example.com");
return new NotFoundError("User", userId);
return new ValidationError(new[] { new FieldError("email", "invalid format") });

// v3
return new Error.AuthenticationRequired(Scheme: "Bearer", ReasonCode: "Authentication.InvalidCredentials")
    { Detail = "Invalid credentials." };
return new Error.Conflict(ResourceRef.For<User>(userId), "duplicate_email")
    { Detail = "Email already in use." };
return new Error.NotFound(ResourceRef.For<User>(userId));
return Error.InvalidInput.ForField("email", "invalid_format", "Email is not a valid address.");
```

The full case set and slug-change table is in the [Error union DDD realignment](#error-union-ddd-realignment) section below. The pre-v3 `FieldError(name, [details])` shape now uses RFC 6901 JSON Pointers (`InputPointer`) for field paths. The shortcut `Error.InvalidInput.ForField("email", ...)` calls `InputPointer.ForProperty("email")` which produces `"/email"` (RFC 6901 escapes for `~` and `/` are applied, but `.` is preserved as-is — so `ForField("Address.City", ...)` produces `"/Address.City"`, a single token). For nested paths build the pointer explicitly via `new InputPointer("/Address/City")` or the `InputPointer` overload of `ForField`. FluentValidation member chains (`Address.City`, `Items[0].Sku`) ARE auto-normalized to JSON Pointers (`"/Address/City"`, `"/Items/0/Sku"`) by the `Trellis.Mediator.FluentValidation` adapter using `JsonPointerNormalizer` from `Trellis.FluentValidation`. Tests asserting on field-error shape need updating; see [`Trellis.Testing` `Error.InvalidInput` assertions](docs/docfx_project/api_reference/trellis-api-testing-reference.md#validationerrorassertions) (`HaveFieldError`, `HaveFieldErrorWithDetail`, `HaveFieldCount`) for the v3 shape.

### Step 5 — DTO and test cleanup (`Result<T>.Value` removed)

`Result<T>.Value` was removed in v3 because the ambient throwing accessor was the primary cause of unsafe value access. Replace the v2.x access patterns:

| v2.x access | v3 replacement |
|---|---|
| `result.Value` (after explicit `IsSuccess` check) | `result.TryGetValue(out var value)` or `var (ok, value, error) = result;` |
| `result.Value` inside a chain | `.Map(value => ...)` / `.Bind(value => ...)` |
| `result.Value` at a persistence DTO → entity rehydration seam | `result.GetValueOrThrow("context message")` — see [cookbook Recipe 30](docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-30--rehydrating-entities-from-persistence-fail-loud-vs-result-track) |
| `result.Value` in test arrangement | `result.Unwrap()` (from `Trellis.Testing`, test-only) |

`GetValueOrThrow(string? errorMessage = null)` ships in `Trellis.Core` and mirrors the existing `Maybe<T>.GetValueOrThrow(string? errorMessage = null)` precedent. It throws `InvalidOperationException` on failure, which bubbles through `ExceptionBehavior` to a wire `new Error.Unexpected("unhandled_exception", faultId)` (HTTP 500) with the row-identifying message in operator-side logs.

Implicit `T → Result<T>` is also gone. Factory methods that previously returned a bare value now require an explicit `Result.Ok(...)` wrap:

```csharp
// v2.x — implicit lift
public static Result<UserId> TryCreate(Guid id) => new UserId(id);

// v3 — explicit lift
public static Result<UserId> TryCreate(Guid id) => Result.Ok(new UserId(id));
```

### Step 6 — Wire `AddTrellisBehaviors` (optional, recommended)

The final step opts into the Trellis Mediator pipeline behaviors (Exception, Tracing, Logging — plus optional Authorization and Validation):

```csharp
// In your composition root
using Trellis.Mediator;

services.AddTrellisBehaviors();
```

Authorization and Validation behaviors register too but pay no per-request cost until their pre-conditions are met. The Validation behavior runs for every message but no-ops when no `IMessageValidator<T>` is registered (the open-generic `IEnumerable<IMessageValidator<TMessage>>` injection resolves to empty). The Authorization behavior runs only for messages that implement `IAuthorize` — projects without any such message yet pay no cost; the first `IAuthorize` message you ship then requires an `IActorProvider` registration (the provider can return `Maybe<Actor>.None` to surface a typed `Error.AuthenticationRequired` on the wire). Lets a project consume Exception + Tracing + Logging immediately without committing to actor-provider scaffolding or moving validation from the DTO layer to the command boundary. Adopt incrementally.

The [`AddTrellisBehaviors` reference](docs/docfx_project/api_reference/trellis-api-mediator.md#trellismediatorservicecollectionextensions) covers the full set of registered behaviors and pipeline order; the [canonical pipeline order](docs/docfx_project/api_reference/trellis-api-mediator.md#canonical-pipeline-order) documents how the behaviors compose.

### After the playbook

The rest of this file is reference material organized by change area: error-ADT realignment, the operator-rename table from v2.9 → v3.0, `Maybe<T>` notnull constraint, Trellis.Asp response-verb removal, `Required<T>` strict-flip details, and (in the appendix) a methodology for [verifying behavior after upgrade](#appendix--verifying-behavior-after-upgrade) using the auto-deposited API ref docs in `.github/`.

---

## Error union DDD realignment

The `Trellis.Core.Error` discriminated union is now transport-neutral. HTTP-specific failures (`405`, `406`, `412`, `413`, `415`, `416`, `428`) live in the closed `HttpError` union in the new `Trellis.Http.Abstractions` package and flow through `Result<T>` via the `Error.TransportFault(ITransportFault Fault)` envelope.

The closed union now has 12 cases: `InvalidInput`, `InvariantViolation`, `NotFound`, `Forbidden`, `Conflict`, `Gone`, `AuthenticationRequired`, `Unavailable`, `RateLimited`, `Unexpected`, `Aggregate`, `TransportFault`.

The [CHANGELOG entry](CHANGELOG.md#breaking-changes--trelliscoreerror-union-ddd-realignment) is the authoritative rename and slug-change reference; the examples below show the common before/after shapes.

### Field validation

```csharp
// Before
return new Error.UnprocessableContent(EquatableArray.Create(
    new FieldViolation(InputPointer.ForProperty("email"), "invalid_format")
    {
        Detail = "Email is not a valid address.",
    }));

// After
return Error.InvalidInput.ForField("email", "invalid_format", "Email is not a valid address.");
```

### Rule (cross-field / object-level) violation

```csharp
// Before
return new Error.BadRequest("passwords_must_match") { Detail = "Password and confirmation differ." };

// After
return Error.InvalidInput.ForRule("passwords_must_match", "Password and confirmation differ.");
```

### Aggregate invariant violated outside the inbound-validation pipeline

```csharp
// New case — was previously shoe-horned into UnprocessableContent or Conflict
return new Error.InvariantViolation(
    "cross_aggregate_uniqueness",
    ResourceRef.For<Order>(orderId))
{
    Detail = "Order number is already in use by another tenant.",
};
```

### Concurrency conflict

```csharp
// Before
return new Error.Conflict(ResourceRef.For<Order>(orderId), "concurrency_conflict")
{
    Detail = "Order was modified by another request.",
};

// After — same call shape; Conflict is unchanged.
return new Error.Conflict(ResourceRef.For<Order>(orderId), "concurrency_conflict")
{
    Detail = "Order was modified by another request.",
};
```

### Authentication challenge

```csharp
// Before
return new Error.Unauthorized();

// After
return new Error.AuthenticationRequired(Scheme: "Bearer");
```

The boundary still emits `WWW-Authenticate` (from `Error.AuthenticationRequired.Scheme` or the registered `IAuthenticationSchemeProvider` fallback).

**Preserving v2 `UnauthorizedError(message, code)` semantics.** FunctionalDdd v2 callers that distinguished invalid-credentials from missing-credentials via the `code` argument (`new UnauthorizedError("Invalid credentials.", "Authentication.InvalidCredentials")`) should carry the machine-readable code forward via the optional `ReasonCode` parameter on `Error.AuthenticationRequired`:

```csharp
// V2
return new UnauthorizedError("Invalid credentials.", "Authentication.InvalidCredentials");

// V3 — ReasonCode preserves the per-cause machine code; Code returns it instead of Kind.
return new Error.AuthenticationRequired(Scheme: "Bearer", ReasonCode: "Authentication.InvalidCredentials")
    { Detail = "Invalid credentials." };
```

The boundary renderer (`Trellis.Asp.ResponseFailureWriter`) projects `Code` into `ProblemDetails.Extensions["code"]`, which ASP.NET Core serializes as the top-level Problem Details extension member `code` (alongside `type`, `title`, `status`, `detail`, and `instance` — RFC 9457 §3.2). Dashboards and client-side branching that previously keyed off the v2 `code` argument can continue to key off the same top-level field:

```json
{
  "title": "Unauthorized",
  "status": 401,
  "detail": "Invalid credentials.",
  "code": "Authentication.InvalidCredentials",
  "kind": "unauthorized"
}
```

### Rate limiting / dependency unavailable

```csharp
// Before
return new Error.TooManyRequests();
return new Error.ServiceUnavailable();

// After
return new Error.RateLimited(new RetryAdvice(After: TimeSpan.FromSeconds(30)));
return new Error.Unavailable("payment_gateway_offline", new RetryAdvice(After: TimeSpan.FromSeconds(120)));
```

`RetryAdvice(TimeSpan? After, DateTimeOffset? At)` is a new transport-neutral type in `Trellis.Core`. The boundary translates it to the `Retry-After` header.

### Unexpected failure with fault id

```csharp
// Before
return new Error.InternalServerError(faultId) { Detail = "DB write failed." };

// After
return new Error.Unexpected("db_write_failed", faultId) { Detail = "DB write failed." };
```

The required `ReasonCode` makes the failure addressable in telemetry. `Error.Unexpected { ReasonCode == "not_implemented" }` is special-cased at the boundary to `501 Not Implemented`.

### Aggregate of multiple errors

```csharp
// New first-class case (was previously a merged `UnprocessableContent`)
return new Error.Aggregate(EquatableArray.Create<Error>(
    Error.InvalidInput.ForField("email", "required"),
    new Error.Conflict(ResourceRef.For<User>(userId), "duplicate_email")));
```

`Combine` still merges multiple `InvalidInput` failures into a single `InvalidInput`; mixed-type combinations now produce `Error.Aggregate`.

### Transport fault — construction (server) and unwrapping (client)

```csharp
// Server: reject PATCH on a resource that only supports GET / PUT
return new Error.TransportFault(
    new HttpError.MethodNotAllowed(EquatableArray.Create("GET", "PUT")));

// Server: precondition required (RFC 6585)
return new Error.TransportFault(
    new HttpError.PreconditionRequired(PreconditionKind.IfMatch));
```

```csharp
// Client: pattern-match a wrapped HttpError
return result.Error switch
{
    Error.TransportFault { Fault: HttpError.MethodNotAllowed allowed }
        => Log("Allowed methods: " + string.Join(", ", allowed.Allow.Items)),
    Error.TransportFault { Fault: HttpError.PreconditionFailed pf }
        => Log($"Precondition {pf.Condition} failed on {pf.Resource}"),
    _ => Log("Other error: " + result.Error),
};
```

`HttpError` lives in `Trellis.Http.Abstractions`. `Trellis.Asp` and `Trellis.Http` reference it transitively; add an explicit `<PackageReference Include="Trellis.Http.Abstractions" .../>` only when your boundary glue constructs or pattern-matches these types directly.

### Wire format unchanged

The HTTP boundary (`Trellis.Asp.ResponseFailureWriter`) preserves the historical problem-details `kind` extension tokens (`unprocessable-content`, `unauthorized`, `too-many-requests`, `service-unavailable`, `internal-server-error`, `not-implemented`) verbatim. External HTTP API consumers parsing problem-details see no change.

Telemetry consumers that switch on the domain `Error.Kind` slug do need updates — the new slugs are `invalid-input`, `invariant-violation`, `authentication-required`, `rate-limited`, `unavailable`, `unexpected`, `aggregate`, `transport-fault`.

---

## Breaking Changes Summary

FunctionalDDD v3.0 (now Trellis) introduces clearer naming for failure track operations to make Railway-Oriented Programming more explicit and easier to learn. All **failure track operations** now have an `OnFailure` suffix.

**Success track operations remain unchanged** - this is NOT a complete rewrite, just clearer naming for error handling.

---

## Renamed Operations

### Failure Track Operations (Breaking Changes)

| v2.x Method | v3.0 Method | Track | Find & Replace |
|-------------|-------------|-------|----------------|
| `TapError` | **`TapOnFailure`** | 🔴 Failure | `.TapError(` → `.TapOnFailure(` |
| `TapErrorAsync` | **`TapOnFailureAsync`** | 🔴 Failure | `.TapErrorAsync(` → `.TapOnFailureAsync(` |
| `MapError` | **`MapOnFailure`** | 🔴 Failure | `.MapError(` → `.MapOnFailure(` |
| `MapErrorAsync` | **`MapOnFailureAsync`** | 🔴 Failure | `.MapErrorAsync(` → `.MapOnFailureAsync(` |
| `Compensate` | **`RecoverOnFailure`** | 🔴 Failure | `.Compensate(` → `.RecoverOnFailure(` |
| `CompensateAsync` | **`RecoverOnFailureAsync`** | 🔴 Failure | `.CompensateAsync(` → `.RecoverOnFailureAsync(` |

### Success Track Operations (No Changes) ✅

These methods are **unchanged** - no migration needed:

- `Bind`, `BindAsync` - Chain operations that can fail
- `Map`, `MapAsync` - Transform success values
- `Tap`, `TapAsync` - Execute side effects on success
- `Ensure`, `EnsureAsync` - Validate conditions (can switch tracks)
- `When`, `WhenAsync`, `Unless`, `UnlessAsync` - Conditional execution

### Universal/Terminal Operations (No Changes) ✅

These methods are **unchanged**:

- `Combine` - Merge multiple results
- `Match`, `MatchAsync` - Pattern match success/failure
- *(removed in Trellis V2: `MatchError` superseded by exhaustive `switch` on the closed `Error` ADT)*
- `ToResult`, `ToResultAsync` - Convert nullables to Result

---

## Why This Change?

### Problem: Track Behavior Wasn't Obvious

```csharp
// v2.x - Which track do these run on?
.Tap(user => Log(user))          // Success? Not obvious
.TapError(err => LogError(err))  // Failure? "Error" hints at it
.Map(user => user.Name)          // Success? Not obvious
.MapError(err => AddContext(err)) // Failure? "Error" hints at it
.Compensate(err => GetDefault()) // Failure? Not obvious at all
```

### Solution: Explicit `OnFailure` Suffix

```csharp
// v3.0 - Crystal clear track indicators
.Tap(user => Log(user))                    // 🟢 Success (no suffix)
.TapOnFailure(err => LogError(err))       // 🔴 Failure (OnFailure suffix)
.Map(user => user.Name)                   // 🟢 Success (no suffix)
.MapOnFailure(err => AddContext(err))     // 🔴 Failure (OnFailure suffix)
.RecoverOnFailure(err => GetDefault())    // 🔴 Failure (OnFailure suffix)
```

**Pattern:**
- **Success track** = No suffix
- **Failure track** = `OnFailure` suffix

---

## Automated Migration

### Visual Studio / Rider

1. **Edit** → **Find and Replace** → **Replace in Files**
2. **Match case:** ✅ Enabled
3. **Match whole word:** ✅ Enabled  
4. **Use regular expressions:** ❌ Disabled

Apply these replacements **in order**:

```
Find: .TapError(
Replace: .TapOnFailure(

Find: .TapErrorAsync(
Replace: .TapOnFailureAsync(

Find: .MapError(
Replace: .MapOnFailure(

Find: .MapErrorAsync(
Replace: .MapOnFailureAsync(

Find: .Compensate(
Replace: .RecoverOnFailure(

Find: .CompensateAsync(
Replace: .RecoverOnFailureAsync(
```

### VS Code

1. **Edit** → **Find in Files** (Ctrl+Shift+F / Cmd+Shift+F)
2. Enable **Match Case** (Aa button)
3. Enable **Match Whole Word** (Ab| button)
4. Apply replacements from table above

### Command Line (PowerShell)

```powershell
# Navigate to your solution directory
cd C:\MyProject

$utf8Bom = New-Object System.Text.UTF8Encoding $true

# Replace TapError → TapOnFailure
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName) -replace '\.TapError\(', '.TapOnFailure('
    [System.IO.File]::WriteAllText($_.FullName, $content, $utf8Bom)
}

# Replace TapErrorAsync → TapOnFailureAsync  
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName) -replace '\.TapErrorAsync\(', '.TapOnFailureAsync('
    [System.IO.File]::WriteAllText($_.FullName, $content, $utf8Bom)
}

# Replace MapError → MapOnFailure
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName) -replace '\.MapError\(', '.MapOnFailure('
    [System.IO.File]::WriteAllText($_.FullName, $content, $utf8Bom)
}

# Replace MapErrorAsync → MapOnFailureAsync
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName) -replace '\.MapErrorAsync\(', '.MapOnFailureAsync('
    [System.IO.File]::WriteAllText($_.FullName, $content, $utf8Bom)
}

# Replace Compensate → RecoverOnFailure
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName) -replace '\.Compensate\(', '.RecoverOnFailure('
    [System.IO.File]::WriteAllText($_.FullName, $content, $utf8Bom)
}

# Replace CompensateAsync → RecoverOnFailureAsync
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName) -replace '\.CompensateAsync\(', '.RecoverOnFailureAsync('
    [System.IO.File]::WriteAllText($_.FullName, $content, $utf8Bom)
}
```

---

## Migration Examples

### Example 1: Simple Error Logging

#### Before (v2.x)
```csharp
public async Task<IActionResult> GetUser(string id)
{
    return await UserId.TryCreate(id)
        .BindAsync(GetUserAsync)
        .TapError(err => _logger.LogWarning("Failed to get user: {Error}", err))
        .Match(
            onSuccess: user => Ok(user),
            onFailure: error => NotFound(error.Detail)
        );
}
```

#### After (v3.0)
```csharp
public async Task<IActionResult> GetUser(string id)
{
    return await UserId.TryCreate(id)
        .BindAsync(GetUserAsync)
        .TapOnFailure(err => _logger.LogWarning("Failed to get user: {Error}", err)) // ✅ Changed
        .Match(
            onSuccess: user => Ok(user),
            onFailure: error => NotFound(error.Detail)
        );
}
```

### Example 2: Error Recovery with Fallback

#### Before (v2.x)
```csharp
public async Task<Result<User>> GetUserWithFallback(UserId userId)
{
    return await GetUserFromCache(userId)
        .Compensate(() => GetUserFromDatabase(userId))
        .Compensate(() => GetGuestUser())
        .TapError(err => _metrics.RecordFailure("user.get", err.Code));
}
```

#### After (v3.0)
```csharp
public async Task<Result<User>> GetUserWithFallback(UserId userId)
{
    return await GetUserFromCache(userId)
        .RecoverOnFailure(() => GetUserFromDatabase(userId))        // ✅ Changed
        .RecoverOnFailure(() => GetGuestUser())                     // ✅ Changed
        .TapOnFailure(err => _metrics.RecordFailure("user.get", err.Code)); // ✅ Changed
}
```

### Example 3: Complex Error Handling Pipeline

#### Before (v2.x)
```csharp
public async Task<IActionResult> ProcessOrder(CreateOrderRequest request)
{
    return await CustomerId.TryCreate(request.CustomerId)
        .Combine(ProductId.TryCreate(request.ProductId))
        .Combine(Quantity.TryCreate(request.Quantity))
        
        .BindAsync((customerId, productId, qty) => 
            CreateOrderAsync(customerId, productId, qty))
        .Tap(order => _logger.LogInformation("Order created: {OrderId}", order.Id))
        .TapError(err => _logger.LogWarning("Order creation failed: {Error}", err))
        
        .EnsureAsync(order => HasInventoryAsync(order.ProductId, order.Quantity),
            new Error.Conflict(null, "inventory.insufficient") { Detail = "Insufficient inventory" })
        .TapError(err => _metrics.RecordFailure("order.create", err.Code))
        
        .Compensate(err => err is ConflictError 
            ? SuggestAlternativeProductsAsync(request.ProductId)
            : Result.Fail<Order>(err))
        
        .MapError(err => Error.Domain($"Order processing failed: {err.Detail}"))
        
        .TapAsync(order => SaveOrderAsync(order))
        .TapAsync(order => PublishOrderCreatedEventAsync(order))
        
        .Match(
            onSuccess: order => Created($"/orders/{order.Id}", order),
            onFailure: error => error.ToHttpResult()
        );
}
```

#### After (v3.0)
```csharp
public async Task<IActionResult> ProcessOrder(CreateOrderRequest request)
{
    return await CustomerId.TryCreate(request.CustomerId)
        .Combine(ProductId.TryCreate(request.ProductId))
        .Combine(Quantity.TryCreate(request.Quantity))
        
        .BindAsync((customerId, productId, qty) => 
            CreateOrderAsync(customerId, productId, qty))
        .Tap(order => _logger.LogInformation("Order created: {OrderId}", order.Id))
        .TapOnFailure(err => _logger.LogWarning("Order creation failed: {Error}", err)) // ✅ Changed
        
        .EnsureAsync(order => HasInventoryAsync(order.ProductId, order.Quantity),
            new Error.Conflict(null, "inventory.insufficient") { Detail = "Insufficient inventory" })
        .TapOnFailure(err => _metrics.RecordFailure("order.create", err.Code)) // ✅ Changed
        
        .RecoverOnFailure(err => err is Error.Conflict                          // ✅ Changed
            ? SuggestAlternativeProductsAsync(request.ProductId)
            : Result.Fail<Order>(err))
        
        .MapOnFailure(err => new Error.Unexpected("order_processing_failed")        // ✅ Changed
        {
            Detail = $"Order processing failed: {err.Detail}",
            Cause = err
        })
        
        .TapAsync(order => SaveOrderAsync(order))
        .TapAsync(order => PublishOrderCreatedEventAsync(order))
        
        .ToHttpResponseAsync(o => o.Created(order => $"/orders/{order.Id}"))
        .AsActionResultAsync<Order>();
}
```

---

## Testing Migration

### Update Test Methods

Test method names should also be updated for clarity:

#### Before (v2.x)
```csharp
[Fact]
public void TapError_WithAction_FailureResult_ExecutesAction()
{
    var result = Result.Fail<int>(new Error.InternalServerError("test") { Detail = "Error" });
    
    var actual = result.TapError(() => _actionExecuted = true);
    
    _actionExecuted.Should().BeTrue();
}
```

#### After (v3.0)
```csharp
[Fact]
public void TapOnFailure_WithAction_FailureResult_ExecutesAction()  // ✅ Test name changed
{
    var result = Result.Fail<int>(new Error.Unexpected("test_failure") { Detail = "Error" });
    
    var actual = result.TapOnFailure(() => _actionExecuted = true);  // ✅ Method changed
    
    _actionExecuted.Should().BeTrue();
}
```

---

## Validation After Migration

### Compile Your Solution

```bash
dotnet build
```

All compile errors will point to missed renames. The compiler is your friend!

### Common Compile Errors

```
error CS1061: 'Result<User>' does not contain a definition for 'TapError'
```

**Fix:** Replace with `TapOnFailure`

```
error CS1061: 'Result<Order>' does not contain a definition for 'Compensate'  
```

**Fix:** Replace with `RecoverOnFailure`

### Run Your Tests

```bash
dotnet test
```

If tests fail, check for:
- Test method names referencing old operation names
- Assertions checking for old method behavior

---

## Benefits of v3.0 Naming

### 1. Self-Documenting Code

```csharp
// Track behavior is obvious from method names
.Bind(...)              // Runs on success
.TapOnFailure(...)      // Runs on failure - explicit!
.RecoverOnFailure(...)  // Recovery on failure - clear!
```

### 2. Easier to Learn

New developers can understand track behavior **without reading documentation**.

### 3. IDE Support

The new `[RailwayTrack]` attribute enables future IDE tooling:
- Inline hints showing track behavior
- Code analysis and suggestions
- Better IntelliSense grouping

### 4. Consistent Pattern

**Rule:** Failure track = `OnFailure` suffix, Success track = no suffix

Easy to remember, easy to teach.

---

## Rollback Plan

If you need to temporarily roll back to v2.x:

```bash
# Downgrade to last v2.x version
dotnet remove package FunctionalDDD.RailwayOrientedProgramming
dotnet add package FunctionalDDD.RailwayOrientedProgramming --version 2.9.0
```

Then revert your code changes using source control:

```bash
git checkout main -- .
```

---

## Getting Help

- **Documentation:** [https://xavierjohn.github.io/Trellis/](https://xavierjohn.github.io/Trellis/)
- **Issues:** [https://github.com/xavierjohn/Trellis/issues](https://github.com/xavierjohn/Trellis/issues)
- **Discussions:** [https://github.com/xavierjohn/Trellis/discussions](https://github.com/xavierjohn/Trellis/discussions)

---

## Maybe<T> `notnull` Constraint

### Breaking Change

`Maybe<T>` now has a `where T : notnull` constraint, preventing it from wrapping nullable types. This makes `Maybe<T>` a proper domain-level optionality type — you use `Maybe<T>` instead of `T?`, not alongside it.

### What Changed

```csharp
// v2.x — allowed
Maybe<string?> name;        // Compiled
Maybe<int?> count;           // Compiled

// v3.0 — compiler errors
Maybe<string?> name;         // ❌ CS8714: notnull constraint
Maybe<int?> count;            // ❌ CS8714: notnull constraint
```

### New API Methods

| Method | Purpose | Example |
|--------|---------|---------|
| `Map<TResult>` | Transform inner value | `maybe.Map(url => url.Value)` → `Maybe<string>` |
| `Match<TResult>` | Pattern match | `maybe.Match(url => url.Value, () => "none")` → `string` |
| Implicit operator | Natural assignment | `Maybe<Url> m = url;` |

### How to Migrate

**1. Remove nullable wrappers**

```csharp
// v2.x
Maybe<string?> nickname;

// v3.0
Maybe<string> nickname;
```

**2. Replace `null` assignments with `default`**

```csharp
// v2.x
Maybe<Url> website = null;

// v3.0
Maybe<Url> website = default;      // Maybe.None
Maybe<Url> website = Maybe.None<Url>();  // Explicit
```

**3. Use `Maybe<T>` for optional properties instead of `T?`**

```csharp
// v2.x — nullable value object
public Url? Website { get; init; }

// v3.0 — domain-level optionality
public Maybe<Url> Website { get; init; }
```

**4. ASP.NET Core DTOs — automatic support**

`Maybe<T>` properties in DTOs are automatically handled by the JSON converter and model binder when `AddScalarValueValidation()` is configured:

```csharp
public record RegisterUserDto
{
    public FirstName FirstName { get; init; } = null!;        // Required
    public EmailAddress Email { get; init; } = null!;          // Required
    public Maybe<Url> Website { get; init; }                   // Optional — null in JSON → Maybe.None
}
```

---

## Summary Checklist

- [ ] Update NuGet package to v3.0
- [ ] Run find & replace for all 6 renamed methods
- [ ] Migrate `Maybe<T?>` to `Maybe<T>` (remove nullable wrappers)
- [ ] Replace `Url? Website` with `Maybe<Url> Website` in DTOs
- [ ] Compile solution and fix any errors
- [ ] Update test method names
- [ ] Run all tests
- [ ] Update any documentation/comments in your code
- [ ] Commit changes with message: "Migrate to Trellis v3.0"

**Estimated migration time:** 5-15 minutes for most projects (depending on size)

---

## Trellis.Asp v3 - legacy response verbs removed

As part of Phase 3 of the v2 redesign, the seven extension classes listed below (previously marked `[Obsolete]`) have been **deleted**. Code that still calls any of them will not compile against v3.

| Removed verb | Replacement |
|--------------|-------------|
| `result.ToActionResult(controller)` (MVC) | `result.ToHttpResponse(...).AsActionResult<T>()` |
| `result.ToHttpResult(options)` (Minimal API) | `result.ToHttpResponse(configure)` |
| `result.ToCreatedAtActionResult(...)` | `result.ToHttpResponse(body, opts => opts.CreatedAtAction(...))` |
| `result.ToCreatedAtRouteHttpResult(...)` | `result.ToHttpResponse(body, opts => opts.CreatedAtRoute(...))` |
| `result.ToCreatedHttpResult(httpContext, locationFn, metadataSelector, map)` | `result.ToHttpResponse(map, opts => opts.Created(locationFn).WithETag(...))` |
| `result.ToUpdatedActionResult / ToUpdatedHttpResult` | `result.ToHttpResponse(...)` with `WriteOutcome<T>.Updated` (Prefer handling is built-in) |
| `result.ToPagedActionResult / ToPagedHttpResult` | `result.ToHttpResponse(nextUrlBuilder, body, configure)` |
| `outcome.ToActionResult / ToHttpResult` (`WriteOutcome<T>`) | Return `Result<WriteOutcome<T>>` from workflows; call `.ToHttpResponse(configure)` |

**Removed classes:** `ActionResultExtensions`, `ActionResultExtensionsAsync`, `HttpResultExtensions`, `HttpResultExtensionsAsync`, `PageActionResultExtensions`, `PageHttpResultExtensions`, `WriteOutcomeExtensions`.

**Kept (not obsolete):** `OptionalETagAsync` / `RequireETagAsync`, `EntityTagValue`, `AggregateETagExtensions`, `RepresentationMetadata`, `WriteOutcome<T>`, `PagedResponse<T>` / `PageLink` (moved alongside `PagedResponseBuilder`). Note: as part of the v3 error union DDD realignment, `EntityTagValue`, `AggregateETagExtensions` (with `OptionalETagAsync` / `RequireETagAsync`), `RepresentationMetadata`, and `WriteOutcome<T>` moved from `Trellis.Core` to the new `Trellis.Http.Abstractions` package. Their CLR namespace stays `Trellis`, so no `using` change is required — only the package reference.

See [`docs/docfx_project/articles/asp-tohttpresponse.md`](docs/docfx_project/articles/asp-tohttpresponse.md) for canonical examples of every pattern.

---

## Trellis.Asp v3 — `AddTrellisAsp()` no longer auto-registers scalar-value validation

`AddTrellisAsp()` previously made one silent side-effect call to `AddScalarValueValidation()`, which mutates global `MvcOptions` and `JsonOptions` (model binders, JSON converters, `SuppressModelStateInvalidFilter` flip). The mutation was invisible from the `AddTrellisAsp` call site and surprised consumers who had already configured their own converters / naming policies.

In v3, `AddTrellisAsp()` registers ONLY:
- `TrellisAspOptions` (error-to-status-code mapping)
- `ResourceCollectionNameRegistry`
- The composition contract for layered `MapError<TError>` configuration

Scalar-value validation is now an explicit opt-in. Three migration shapes:

| Before (v2.x) | After (v3) | When to use |
|---|---|---|
| `services.AddTrellisAsp();` | `services.AddTrellisAspWithScalarValidation();` | **One-line behavior-preserving migration** for greenfield controller hosts that bind value-object DTOs. |
| `services.AddTrellisAsp();` | `services.AddTrellisAsp();`<br>`services.AddScalarValueValidation();` | Same effect as the convenience helper, but makes the two registrations visible at the call site. |
| `services.AddTrellisAsp();` (host doesn't bind VO DTOs) | `services.AddTrellisAsp();` (no scalar validation) | MVC sites that don't bind value-object DTOs from JSON/route/query. Drops the unused binder / converter mutation. |

For the `TrellisServiceBuilder` composition root (`services.AddTrellis(o => ...)`), the same split applies via a new slot:

```csharp
// Before
services.AddTrellis(options => options
    .UseAsp()       // implicitly registered scalar-value validation
    .UseMediator());

// After — behavior-preserving migration
services.AddTrellis(options => options
    .UseAsp()
    .UseScalarValueValidation()   // explicit opt-in
    .UseMediator());
```

`UseScalarValueValidation()` is independent of `UseAsp()` and idempotent.

**How to spot affected sites in your repo:** search for `AddTrellisAsp(` or `.UseAsp(` and audit each call. If the host binds endpoints that receive Trellis value objects from JSON / route / query (or the `Maybe<T>` of those), use the `AddTrellisAspWithScalarValidation()` / `.UseScalarValueValidation()` form. If the host only uses error-to-status mapping (e.g. raw `string` / `int` parameters), `AddTrellisAsp()` alone is sufficient.

**Mechanical fix.** A grep-and-replace of the form `s/services\.AddTrellisAsp\(/services\.AddTrellisAspWithScalarValidation(/g` (and the same for `options.UseAsp()` → `options.UseAsp().UseScalarValueValidation()`) is a safe no-behavior-change migration; tighten individual call sites later.

---

## Trellis.FluentValidation — Mediator integration moved to `Trellis.Mediator.FluentValidation`

In v3, the Mediator adapter (`AddTrellisFluentValidation()` + `FluentValidationMessageValidatorAdapter<TMessage>`) moved to a new dedicated package — `Trellis.Mediator.FluentValidation` — so that Domain projects can take a dependency on the standalone `Trellis.FluentValidation` helpers (`ValidationResult → Result<T>`, `JsonPointerNormalizer`) without transitively pulling in `Trellis.Mediator`. The helpers themselves and their behavior are unchanged; only the package and namespace for the adapter moved.

### What stays in `Trellis.FluentValidation`

- `FluentValidationResultExtensions` — `ToResult<T>(...)`, `ValidateToResult<T>(...)`, `ValidateToResultAsync<T>(...)` and all their behavior (null-input short-circuit, cancellation observation, RFC 6901 pointer normalization).
- `JsonPointerNormalizer` — **promoted from `internal` to `public`** so the new package can call across the boundary. Third-party FluentValidation adapters can also use `JsonPointerNormalizer.ToJsonPointer(...)` directly. Its behavior is unchanged.

### What moved to `Trellis.Mediator.FluentValidation`

| Member | v2.x location | v3 location |
|---|---|---|
| `AddTrellisFluentValidation()` (and assembly-scanning overload) | `Trellis.FluentValidation` namespace / package | `Trellis.Mediator.FluentValidation` namespace / package |
| `FluentValidationMessageValidatorAdapter<TMessage>` | `Trellis.FluentValidation` namespace / package | `Trellis.Mediator.FluentValidation` namespace / package |
| `FluentValidationServiceCollectionExtensions` | `Trellis.FluentValidation` namespace / package | `Trellis.Mediator.FluentValidation` namespace / package |

Behavior is preserved bit-for-bit. The diagnostic log category emitted by the scanning overload is still `"Trellis.FluentValidation"`, so existing logging filters keep working without change.

### Migration steps

For each Application / composition-root project that previously called `services.AddTrellisFluentValidation()`:

1. Add the new package reference (the old `Trellis.FluentValidation` reference can stay if the project also uses the standalone helpers; otherwise replace it):

   ```bash
   dotnet add package Trellis.Mediator.FluentValidation
   ```

2. Update the `using` directive for the Mediator wire-up:

   ```diff
   - using Trellis.FluentValidation;
   + using Trellis.Mediator.FluentValidation;
   ```

3. Leave `using Trellis.FluentValidation;` in place anywhere the project also calls `ValidateToResult`, `ValidateToResultAsync`, `ToResult`, or `JsonPointerNormalizer.ToJsonPointer`. Those helpers stay in their original namespace and package.

### Examples

**Before (v2.x):**

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Trellis.FluentValidation;
using Trellis.Mediator;

services.AddTrellisBehaviors();
services.AddTrellisFluentValidation();
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

**After (v3):**

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

services.AddTrellisBehaviors();
services.AddTrellisFluentValidation();
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
```

For a project that uses both — Domain helpers + Mediator adapter — both `using` directives appear at the same call site, mirroring the split between the packages.

### `TrellisServiceBuilder` consumers

If your composition root uses `services.AddTrellis(o => o.UseFluentValidation(...))`, no changes are needed — `TrellisServiceBuilder` now depends on `Trellis.Mediator.FluentValidation` instead of `Trellis.FluentValidation` for the adapter, and `UseFluentValidation(...)` forwards to the same `AddTrellisFluentValidation(...)` method at the same call site shape.

### `JsonPointerNormalizer` promotion to `public`

`JsonPointerNormalizer.ToJsonPointer(string?)` previously sat behind `internal`. Promoting it has no consumer-breaking effect on its own (no existing consumer could reach it). The promotion makes it usable from third-party FluentValidation adapters that need to project property names into `InputPointer` values without re-implementing the RFC 6901 escape + dotted-chain segmentation rules. See [`trellis-api-fluentvalidation.md`](docs/docfx_project/api_reference/trellis-api-fluentvalidation.md#jsonpointernormalizer) for the public signature.

---

## Required<T> defaults flip: strict-by-default with per-type opt-outs

### Rationale

`Required<T>` value-object bases now follow the principle of least astonishment: a type named "Required" rejects its CLR sentinel value by default. V2-era `Required*<T>` classes were lenient unless consumers opted into strict validation with `[NotDefault]` and, for strings, `[Trim]`. In v3, strict validation is the default and boundary / legacy-data shapes opt out explicitly with attributes whose names describe the sentinel they allow.

### Defaults and opt-outs

`null` remains rejected by every `Required*<T>` base and has no opt-out. The opt-outs below only affect the non-null sentinel listed for that base.

| Base | Default rejects | Opt-out |
|---|---|---|
| `RequiredString<T>` | `null`, `""`, whitespace-only | `[AllowEmpty]`, `[AllowWhitespace]`, `[NoTrim]` |
| `RequiredGuid<T>` | `null`, `Guid.Empty` | `[AllowEmpty]` |
| `RequiredDateTime<T>` | `null`, `DateTime.MinValue` | `[AllowMinValue]` |
| `RequiredDateTimeOffset<T>` | `null`, `DateTimeOffset.MinValue` | `[AllowMinValue]` |
| `RequiredInt<T>` | `null`, `0` | `[AllowZero]` |
| `RequiredLong<T>` | `null`, `0` | `[AllowZero]` |
| `RequiredDecimal<T>` | `null`, `0m` | `[AllowZero]` |
| `RequiredBool<T>` | `null` | (no opt-out — degenerate) |
| `RequiredEnum<T>` | `null`, undeclared members | (smart-enum) |

### `RequiredString<T>` validation order

1. **Null check** (no opt-out): reject if input is `null`.
2. **Whitespace-only check on raw input** (skipped by `[AllowWhitespace]`): reject if `value.Length > 0` and every character satisfies `char.IsWhiteSpace`.
3. **Trim** (skipped by `[NoTrim]`): `value = value.Trim()`.
4. **Empty check on final input** (skipped by `[AllowEmpty]`, or when the raw value was whitespace-only and `[AllowWhitespace]` is present): reject if `value.Length == 0`.
5. **User-supplied constraints** such as `[StringLength]` and `ValidateAdditional`.

The whitespace check is Unicode-aware because it uses `char.IsWhiteSpace`.

### `RequiredString<T>` truth table

| Attribute(s) | `null` | `""` | `"   "` | `" a "` | `"a"` |
|---|---|---|---|---|---|
| (none) | reject | reject | reject | accept `"a"` | accept `"a"` |
| `[AllowEmpty]` | reject | accept `""` | reject | accept `"a"` | accept `"a"` |
| `[AllowWhitespace]` | reject | reject | accept `""` | accept `"a"` | accept `"a"` |
| `[NoTrim]` | reject | reject | reject | accept `" a "` | accept `"a"` |
| `[AllowEmpty, AllowWhitespace]` | reject | accept `""` | accept `""` | accept `"a"` | accept `"a"` |
| `[AllowEmpty, NoTrim]` | reject | accept `""` | reject | accept `" a "` | accept `"a"` |
| `[AllowWhitespace, NoTrim]` | reject | reject | accept `"   "` | accept `" a "` | accept `"a"` |
| `[AllowEmpty, AllowWhitespace, NoTrim]` | reject | accept `""` | accept `"   "` | accept `" a "` | accept `"a"` |

`[AllowWhitespace]` alone accepts whitespace-only input, but trim still normalizes the stored value to `""`. Combine `[AllowWhitespace]` with `[NoTrim]` when preserving whitespace verbatim is part of the contract.

### Mechanical migration recipe

1. Remove `[NotDefault]` and `[Trim]` from existing classes. They are vestigial no-ops under v3 strict defaults; the generator ignores them and reports informational diagnostics TRLS046 / TRLS047.
2. For value objects that legitimately accept the CLR sentinel (boundary types, legacy-data rehydration, or other compatibility seams), add the per-type opt-out: `[AllowEmpty]` for post-trim-empty `RequiredString<T>` values, `[AllowEmpty]` for `RequiredGuid<T>`, `[AllowMinValue]` for date bases, or `[AllowZero]` for numeric bases.
3. For `RequiredString<T>` fixtures that need lenient handling of whitespace input or skip-trim behavior, also add `[AllowWhitespace]` and/or `[NoTrim]` according to the truth table above.

### Worked example

A typical domain email value object becomes simpler because strictness and trimming are the default:

```csharp
// Before v3
[Trim]
[NotDefault]
[EmailAddress]
public sealed partial class Email : RequiredString<Email>;

// After v3
[EmailAddress]
public sealed partial class Email : RequiredString<Email>;
```

A boundary or legacy-data value object that intentionally accepts empty / whitespace comment bodies opts out explicitly:

```csharp
// Before v3 — lenient by default
public sealed partial class CommentBody : RequiredString<CommentBody>;

// After v3 — leniency is explicit
[AllowEmpty]
[AllowWhitespace]
[NoTrim]
public sealed partial class CommentBody : RequiredString<CommentBody>;
```

If the comment body should accept `""` after trimming but still reject whitespace-only input, use `[AllowEmpty]` without `[AllowWhitespace]`. If it should accept whitespace-only input but normalize it to `""`, use `[AllowWhitespace]` without `[NoTrim]`.

### What about `[AllowDefault]`?

`[AllowDefault]` was deleted before v3 shipped. No consumers existed, and the generic name was replaced by per-type names that make the allowed sentinel obvious at the declaration site: `[AllowEmpty]`, `[AllowMinValue]`, and `[AllowZero]`.

### New conflict diagnostics

| ID | Severity | Trigger |
|---|---|---|
| TRLS046 | Info | `[NotDefault]` is vestigial under the v3 strict defaults |
| TRLS047 | Info | `[Trim]` is vestigial under the v3 strict defaults |
| TRLS048 | Error | `[AllowZero]` on a non-numeric Required base |
| TRLS049 | Error | `[AllowEmpty]` on a numeric / date Required base |
| TRLS050 | Error | `[AllowMinValue]` on a non-date Required base |
| TRLS051 | Error | `[AllowWhitespace]` on a non-string Required base |
| TRLS052 | Error | `[NoTrim]` on a non-string Required base |
| TRLS053 | Error | Contradictory combination, for example `[AllowZero]` + `[Positive]` |

---

## Appendix — Verifying behavior after upgrade

After upgrading a Trellis package, several authoritative sources can answer "what does this version of the API actually do?" They are not equally trustworthy at any given moment. Use the highest tier first; fall through only when a tier leaves the question unanswered.

1. **Published nuspec on nuget.org** — confirms what version of what package is actually installed. The lock for "what's in the binary you just restored."
2. **Auto-deposited API ref docs in `.github/`** — every Trellis package packs a `trellis-api-<name>.md` reference file (declared via `<TrellisApiRefName>` in the `.csproj`). `Trellis.ApiReference.targets` copies the files into the consuming project's `.github/` directory at restore time. **This is the canonical surface that ships with the binary.** When a deposited doc and a source-tree snapshot disagree, the deposited doc is right.
3. **Behavior probe via a one-off test** — write a single test that exercises the API in the shape your code uses it. Compiles against the actually-installed package; behavior is observable directly. Faster than reading source; conclusive when the doc reads ambiguously.
4. **Framework source** — only when (1)–(3) leave a question unanswered. Local clones can lag the published package, especially during alpha development; treat as the source of last resort.

### Concrete example — caught mistakes

The methodology emerged from a real `FunctionalDdd 2.x → Trellis 3.0.0-alpha.337` migration where it caught two mistakes before they shipped:

- **False-positive correctness regression**, caught at step (2). The author wrote up a "`RequiredString<T>` silently accepts empty strings — silent semantic change from v2.x" finding, drafted an upstream issue, and added `[NotDefault, Trim]` to six value objects to "preserve v2.x semantics." An audit pass against `.github/trellis-api-core.md` proved strict-by-default ships in alpha.337 — the attributes were vestigial no-ops (`TRLS046`, `TRLS047`). Issue retracted before filing. Without the auto-deposited docs this would have shipped as a public framework-team report carrying a false correctness claim.
- **Real correctness bug**, also caught at step (2). Porting removed `Result<T>.Value` to inline `.Match(v => v, e => throw …)` for nested DTO conversion preserved the throwing semantic locally but surfaced as HTTP 500 instead of HTTP 422 with field violations. An audit of the cookbook against the actual API behavior found `TraverseAll` — the canonical accumulating combinator for exactly this pattern.

### Why this order

The published nuspec is the lock. The deposited refs are the framework's own claim about its current surface — shipped alongside the binary, versioned together, never out of sync with the installed package. A behavior probe verifies the binary directly without trusting any doc. The framework source is the last resort because it can drift from the published package during alpha development. Trellis ships `trellis-api-*.md` files with every package precisely so consumers don't need to clone the framework source to audit upgrades.

### AI tooling note

When an AI assistant proposes a Trellis pattern after an upgrade, point it at the `.github/trellis-api-*.md` files first. The deposited docs are the source of truth your AI tools and your CI both verify against — keeping consumer-side AI audits grounded against the framework's shipped surface rather than a search-engine cached generic answer.
