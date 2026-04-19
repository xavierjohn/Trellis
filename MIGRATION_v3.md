# Migration Guide: v2.x → v3.0

> [!IMPORTANT]
> This guide documents the historical FunctionalDDD v2.x → Trellis v3.0 migration (renamed failure-track operations: `TapError` → `TapOnFailure`, `Compensate` → `RecoverOnFailure`, etc.). The advice below still applies for projects upgrading from FunctionalDDD v2.x.
>
> **Trellis V2 (the current major release) introduces a separate, larger breaking change**: the `Error` type is now a closed discriminated-union ADT with 18 nested `sealed record` cases (no static factory methods, typed payloads). See [ADR-001](docs/adr/ADR-001-result-api-surface.md) and the [Error Handling guide](docs/docfx_project/articles/error-handling.md) for the full surface and migration patterns. The error snippets in this document have been updated in-place to use the V2 closed-ADT syntax.

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
        
        .RecoverOnFailure(err => err is ConflictError                           // ✅ Changed
            ? SuggestAlternativeProductsAsync(request.ProductId)
            : Result.Fail<Order>(err))
        
        .MapOnFailure(err => Error.Domain($"Order processing failed: {err.Detail}")) // ✅ Changed
        
        .TapAsync(order => SaveOrderAsync(order))
        .TapAsync(order => PublishOrderCreatedEventAsync(order))
        
        .Match(
            onSuccess: order => Created($"/orders/{order.Id}", order),
            onFailure: error => error.ToHttpResult()
        );
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
    var result = Result.Fail<int>(new Error.InternalServerError("test") { Detail = "Error" });
    
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
