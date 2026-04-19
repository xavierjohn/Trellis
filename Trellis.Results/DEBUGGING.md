# Debugging Railway Oriented Programming

Chained functional pipelines can be hard to debug. This guide covers the built-in debugger support, stepping behavior, debug extensions, and practical strategies for diagnosing failures in ROP chains.

## Debugger Display

The library's core types have `[DebuggerDisplay]` and `[DebuggerTypeProxy]` attributes so that
hovering over a variable or inspecting it in the Watch/Locals window shows meaningful information
instead of raw struct fields.

### Result\<T\>

```
Display:  Success, Value = John, Error = <none>
          Failure, Value = <null>, Error = validation.error
```

Expanding a `Result<T>` in the debugger shows a structured proxy view:

| Success | Failure |
|---------|---------|
| `Value` — the wrapped value | `Error` — the `Error` object |
| | `Code` — e.g. `"validation.error"` |
| | `Detail` — e.g. `"Email is invalid"` |
| | `ErrorType` — e.g. `"Error.UnprocessableContent"` |
| | `Instance` — optional correlation ID |

### Error

```
Display:  Email address is not valid.
```

The base `Error` type shows its `Detail` property directly.

### Error.UnprocessableContent

```
Display:  Validation: 2 field(s) — Email address is not valid; First name is required.
```

Expanding shows each field as a structured entry:

```
▶ email: Email address is not valid
▶ firstName: First name is required
```

Each field entry expands to its `Details` array of individual error messages.

### Error.Aggregate

```
Display:  Aggregate: 3 error(s)
```

Expanding shows each contained error with its type:

```
▶ Error.UnprocessableContent: Email is invalid
▶ Error.NotFound: User 123 not found
▶ Error.Conflict: Order already exists
```

### Maybe\<T\>

```
Display:  Some(hello)
          None
```

## Stepping Through ROP Chains

All ROP extension methods (`Bind`, `Map`, `Ensure`, `EnsureAll`, `Tap`, `Match`, `Combine`, `When`,
`Traverse`, `MapOnFailure`, `TapOnFailure`, `RecoverOnFailure`, `Recover`, `ToMaybe`, and their async variants)
are marked with `[DebuggerStepThrough]`.

With **"Just My Code"** enabled (the default in Visual Studio), pressing **F11 (Step Into)** on a
chained call like `.Bind(user => CreateOrder(user))` skips the library plumbing and lands
directly inside your lambda. This makes stepping through a pipeline feel natural:

```csharp
var result = GetUser(id)          // F11 → steps into GetUser
    .Ensure(u => u.IsActive, …)   // F11 → steps into the lambda (u => u.IsActive)
    .Bind(u => CreateOrder(u))    // F11 → steps into CreateOrder
    .Map(o => o.Total);           // F11 → steps into the lambda (o => o.Total)
```

The `Debug` extension methods (`Debug`, `DebugDetailed`, `DebugOnSuccess`, `DebugOnFailure`)
are **not** marked `[DebuggerStepThrough]` — you can step into them to inspect values.

> **Tip:** If you need to step into the library code itself (for example, to diagnose
> an issue with error aggregation in `Combine`), disable "Just My Code" temporarily via
> **Debug → Options → Debugging → General → Enable Just My Code**.

## Debug Extension Methods

The library includes `Debug` extension methods that execute only in **DEBUG** builds and become
zero-cost no-ops in RELEASE builds. Debug information is emitted as OpenTelemetry `Activity`
spans, making it visible in .NET Aspire, Application Insights, Jaeger, and similar tools.

### Quick reference

| Method | What it does |
|--------|-------------|
| `.Debug("label")` | Logs status + value/error as Activity tags |
| `.DebugDetailed("label")` | Same as `Debug` plus error type, `Error.UnprocessableContent` fields, `Error.Aggregate` contents |
| `.DebugWithStack("label")` | Same as `Debug` plus up to 10 stack frames |
| `.DebugOnSuccess(v => …)` | Runs a custom action (only on success) |
| `.DebugOnFailure(err => …)` | Runs a custom action (only on failure) |

All methods have `Task` and `ValueTask` async variants (`DebugAsync`, `DebugDetailedAsync`, etc.).

### Example

```csharp
var result = GetUser(id)
    .Debug("After GetUser")
    .Ensure(u => u.IsActive, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Inactive" })
    .Debug("After Ensure")
    .Bind(ProcessUser)
    .DebugDetailed("Final result");
```

```csharp
// Custom actions
var result = GetUser(id)
    .DebugOnSuccess(user => Console.WriteLine($"User: {user.Id}"))
    .DebugOnFailure(error => Console.WriteLine($"Error: {error.Code} - {error.Detail}"));
```

```csharp
// Async chains
var result = await GetUserAsync(id)
    .DebugAsync("After GetUser")
    .BindAsync(u => GetOrdersAsync(u.Id))
    .DebugDetailedAsync("After GetOrders");
```

## Diagnosing Chain Failures

### Which step failed?

**Use `Tap` / `TapOnFailure` to add logging at each step:**

```csharp
var result = await GetUserAsync(id)
    .TapAsync(u => _logger.LogDebug("Found user: {Id}", u.Id))
    .EnsureAsync(u => u.IsActive, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "User inactive" })
    .TapOnFailureAsync(err => _logger.LogWarning("Validation failed: {Error}", err.Detail))
    .BindAsync(u => GetOrdersAsync(u.Id))
    .TapAsync(orders => _logger.LogDebug("Found {Count} orders", orders.Count))
    .MapAsync(orders => orders.Sum(o => o.Total));
```

**Use descriptive error messages with context:**

```csharp
var result = await GetUserAsync(id)
    .ToResultAsync(new Error.NotFound(new ResourceRef("Resource")) { Detail = $"User {id} not found" })
    .EnsureAsync(u => u.IsActive, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("isActive"), "validation.error") { Detail = "User account is inactive" })))
    .BindAsync(u => GetOrdersAsync(u.Id))
    .EnsureAsync(orders => orders.Any(), new Error.NotFound(new ResourceRef("Resource")) { Detail = $"No orders for user {id}" });
```

**Break the chain into named variables for breakpoints:**

```csharp
var userResult = await GetUserAsync(id)
    .ToResultAsync(new Error.NotFound(new ResourceRef("Resource")) { Detail = "User not found" });

var activeResult = userResult
    .Ensure(u => u.IsActive, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Inactive" });

var ordersResult = await activeResult
    .BindAsync(u => GetOrdersAsync(u.Id));
```

### Inspecting values mid-chain

Use `Tap` with a breakpoint inside the lambda — the debugger will stop there because
`Tap` is `[DebuggerStepThrough]` but your lambda is not:

```csharp
var result = await GetUserAsync(id)
    .TapAsync(user =>
    {
        // Set breakpoint here — inspect 'user' in Locals/Watch
        _ = user;
    })
    .BindAsync(u => ProcessUserAsync(u));
```

### Combine error inspection

`Combine` aggregates all errors. The debugger proxy makes inspection easy —
expand the error to see if it's a `Error.UnprocessableContent` (merged fields) or an
`Error.Aggregate` (mixed types). You can also log them:

```csharp
var result = EmailAddress.TryCreate(email)
    .Combine(FirstName.TryCreate(firstName))
    .Combine(LastName.TryCreate(lastName))
    .TapOnFailure(error =>
    {
        if (error is Error.Aggregate agg)
            foreach (var err in agg.Errors)
                _logger.LogWarning("{Type}: {Detail}", err.GetType().Name, err.Detail);
        else if (error is Error.UnprocessableContent val)
            foreach (var field in val.FieldErrors)
                _logger.LogWarning("{Field}: {Details}", field.FieldName, string.Join(", ", field.Details));
        else
            _logger.LogWarning("{Detail}", error.Detail);
    });
```

## OpenTelemetry Tracing

Every ROP extension method creates an `Activity` span automatically. That can be very useful when
you need to reconstruct an entire pipeline, but it also means trace volume grows with every `Ensure`,
`Bind`, `Map`, `Tap`, `Match`, and tuple helper in the chain.

Treat Results tracing as a **break-glass debugging tool**:

- Use it when you need full pipeline forensics for a hard-to-reproduce issue
- Prefer it in development, test environments, or short-lived troubleshooting sessions
- Avoid assuming it will be a low-noise production default for most applications

If you want a lower-noise observability surface for normal diagnostics, primitive value object tracing
is usually the better fit because it fires at clearer domain boundaries like parse/validate/create.

Enable Results tracing when you need to see the full pipeline as a distributed trace:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Trellis.Results")
        .AddOtlpExporter());
```

Each span records the method name, result status (`Ok` / `Error`), and error code on failure.
This gives you a waterfall view of the entire pipeline in your tracing dashboard.

## Testing Strategies

### Capture values with Tap

```csharp
[Fact]
public async Task Should_Process_Valid_User()
{
    User? captured = null;

    var result = await GetUserAsync("123")
        .TapAsync(user => captured = user)
        .BindAsync(ProcessUserAsync);

    captured.Should().NotBeNull();
    captured!.Id.Should().Be("123");
    result.IsSuccess.Should().BeTrue();
}
```

### Assert on error types

```csharp
[Fact]
public void Should_Fail_With_Validation_Error()
{
    var result = ProcessOrder(invalidOrder);

    result.IsFailure.Should().BeTrue();
    result.Error.Should().BeOfType<Error.UnprocessableContent>();
    result.Error.Detail.Should().Contain("invalid quantity");
}
```

### Extract and test individual steps

```csharp
// Compose from testable pieces
public Result<User> GetActiveUser(string id) =>
    GetUser(id)
        .Ensure(u => u.IsActive, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "User is inactive" });

public Result<User> ValidateEmail(User user) =>
    user.Email.Value.Contains('@')
        ? Result.Ok(user)
        : Result.Fail<User>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Invalid email" })));

// Compose
public Result<User> ValidateAndProcess(string id) =>
    GetActiveUser(id)
        .Bind(ValidateEmail)
        .Tap(UpdateLastLogin);
```

## Checklist

1. Hover over `Result<T>` in the debugger — the display and proxy show value or error details
2. Press F11 on `.Bind()` / `.Map()` / `.Ensure()` — it steps directly into your lambda
3. Insert `.Debug("label")` calls during development — they vanish in Release builds
4. Use `Tap` / `TapOnFailure` to add logging without breaking the chain
5. Include `fieldName` in validation errors for clear diagnostics
6. Break long chains into named variables when you need breakpoints on each step
7. Enable OpenTelemetry tracing to see the pipeline as a distributed trace
