---
name: recipe-18-dto-to-value-object
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Maps primitive DTO fields to value objects in a command without a test-only Unwrap(), including exact monetary input. Use when converting DTOs to commands."
---
# Recipe 18 — DTO primitives to value-object command: no test-only `Unwrap()`

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.Mediator`, `Trellis.Primitives`.

## Contents

- Problem
- Monetary inputs
- What it shows
- Anti-pattern -> fix
- Already-created but nullable values
- Nested collections
- Problem
- Anti-pattern -> fix
- Exact means numeric, not lexical

**Problem.** Request DTOs often carry primitive transport fields (`string email`, `string customerName`), while commands and domain methods should receive Trellis value objects. Each `TryCreate` returns `Result<TVO>`. Do not use `Unwrap()` in production code — it is a `Trellis.Testing` helper for tests.

**Monetary inputs.** For an exact quote match or raw-input identity before rounding, read [the monetary boundary below](#exact-input-monetary-boundaries-before-normalization) before converting that DTO field.

```csharp
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using Trellis.Primitives;

public sealed record CreateCustomerRequest(string Email, string CustomerName);

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed record CustomerResponse(CustomerId Id, string Email, string CustomerName);

[StringLength(200, MinimumLength = 1)]
public sealed partial class CustomerName : RequiredString<CustomerName>;

public sealed record CreateCustomerCommand(EmailAddress Email, CustomerName CustomerName)
    : ICommand<Result<CustomerResponse>>
{
    public static Result<CreateCustomerCommand> TryCreate(CreateCustomerRequest request) =>
        Result.Combine(
                EmailAddress.TryCreate(request.Email, nameof(request.Email)),
                CustomerName.TryCreate(request.CustomerName, nameof(request.CustomerName)))
            .Map((email, customerName) => new CreateCustomerCommand(email, customerName));
}

[ApiController]
[Route("customers")]
public sealed class CustomersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public ValueTask<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken ct) =>
        CreateCustomerCommand.TryCreate(request)
            .BindAsync(command => sender.Send(command, ct))
            .ToHttpResponseAsync()
            .AsActionResultAsync<CustomerResponse>();
}
```

**What it shows.**

- Keep DTOs transport-shaped and commands/domain methods value-object-shaped.
- Pass field names into `TryCreate` so failures point at the request field (`/email`, `/customerName` after pointer normalization at the ASP boundary).
- Use `Result.Combine(...)` to aggregate per-field `Error.InvalidInput` failures into one validation response.
- Stay on the ROP track: invalid input short-circuits before `sender.Send(...)`; valid input creates the command and continues.

**Anti-pattern -> fix.**

```csharp
// WRONG — Unwrap() is test-only and turns validation failures into thrown exceptions.
var command = new CreateCustomerCommand(
    EmailAddress.TryCreate(request.Email).Unwrap(),
    CustomerName.TryCreate(request.CustomerName).Unwrap());

// FIX — aggregate value-object creation results and bind into the command.
var command = Result.Combine(
        EmailAddress.TryCreate(request.Email, nameof(request.Email)),
        CustomerName.TryCreate(request.CustomerName, nameof(request.CustomerName)))
    .Map((email, customerName) => new CreateCustomerCommand(email, customerName));
```

**Already-created but nullable values.** A command factory can also receive value objects
that have already been validated but might be absent. Guard their presence without
re-parsing, returning `Result<Unit>`, or recovering the values with `!`:

```csharp
public static Result<CreateCustomerCommand> RequireValues(
    EmailAddress? email, CustomerName? customerName) =>
    Result.EnsureNotNull(email, "email", "Email is required.")
        .Combine(Result.EnsureNotNull(customerName, "customerName", "Customer name is required."))
        .Map((email, customerName) => new CreateCustomerCommand(email, customerName));

public static Task<Result<CreateCustomerCommand>> RequireValuesAsync(
    Task<EmailAddress?> email, Task<CustomerName?> customerName) =>
    email.EnsureNotNullAsync("email", "Email is required.")
        .CombineAsync(customerName.EnsureNotNullAsync("customerName", "Customer name is required."))
        .MapAsync((email, customerName) => new CreateCustomerCommand(email, customerName));
```

These methods belong on the command type above. Success carries the exact
non-null references; `Combine` still reports every missing field. Nullable structs
are unwrapped by the same guard, and the async forms also accept `ValueTask<T?>`.
The field/detail overloads construct standard `value.not-null` violations only on
failure. To choose another error, prefer a lazy `Func<Error>` that constructs it
inside the callback; pass `Error` when an existing instance is intentionally reused. For a query
where absence means not found rather than invalid input, choose a lazy
`Error.NotFound` factory instead of a required-field violation.

Use `Error.InvalidInput.Required(fieldName, detail)` for conditional requiredness or
collection-element rules that are not a plain null check. Its `InputPointer` form
preserves input location and can be used lazily:
`Result.EnsureNotNull(value, () => Error.InvalidInput.Required(inputPointer, detail))`.
Composite validators can use
`FieldViolation.Required(fieldName, detail)` or `FieldViolation.Required(inputPointer, detail)`
to retain an indexed path and input location. Use these guards for required values,
`Result.Ok(value)` for deliberate success wrapping, and `maybe.ToResult(errorFactory)`
when ordinary absence becomes a failure. TRLS066 flags `Result.Ensure(x is not null, error)`
and suggests `Result.EnsureNotNull(x, error)` instead.

**Nested collections.** When `CreateCustomerRequest` carries a `List<AddressDto>` whose items each need to become value objects, the `Result.Combine` shape above doesn't generalize to the collection — use [Recipe 20](recipe-20-fail-fast-vs-accumulating.md) (`TraverseAll`) to validate every row and accumulate per-item failures into one `Error.InvalidInput`. Inlining `.Select(item => item.ToCommand().Match(c => c, e => throw …))` throws on the first invalid row and surfaces as HTTP 500 instead of HTTP 422 with field violations.

## Exact-input monetary boundaries before normalization

**Problem.** An application requires a submitted payment amount to match a trusted quote exactly. `MonetaryAmount.TryCreate(1.004m)` succeeds with `1.00m`, so comparing only the resulting value object with a `1.00m` quote would accept a different submitted amount. The factory is behaving correctly: non-negative values are normalized to two decimal places using `MidpointRounding.AwayFromZero`.

Keep the request field as a primitive `decimal?` until the application's raw-input checks finish. Binding it directly to `MonetaryAmount` would normalize it before those checks could run. Guard presence, compare the original parsed numeric value with the already-resolved quote, and only then `Bind` to the non-throwing factory:

```csharp
using System;
using Trellis;
using Trellis.Primitives;

internal static class PaymentAmountBoundary
{
    public static Result<MonetaryAmount> TryCreateMatchingQuote(
        decimal? amount, MonetaryAmount quotedAmount)
    {
        ArgumentNullException.ThrowIfNull(quotedAmount);

        return Result.EnsureNotNull(amount, "amount", "Amount is required.")
            .Ensure(value => value == quotedAmount.Value,
                static _ => Error.InvalidInput.ForField(
                    code: "payment.amount-mismatch", field: "amount",
                    detail: "Amount must match the quote exactly."))
            .Bind(static value => MonetaryAmount.TryCreate(value, "amount"));
    }
}
```

`Bind` deliberately keeps `MonetaryAmount.TryCreate` as the single construction path, even though the equality guard has already restricted the amount to the trusted quote's normalized value.

This is a **sample-local application policy**, not a new Trellis API or a stricter factory mode. A missing request amount produces `ValidationCodes.ValueNotNull`; a present amount that differs from the quote produces the application-owned `payment.amount-mismatch` at `/amount`. A null trusted quote is programming misuse and throws before entering the Result chain. Quote lookup and payment-specific rules remain application-owned. Applications that intentionally accept rounding can call `MonetaryAmount.TryCreate` directly.

**Anti-pattern -> fix.** Do not normalize first and then use equality as evidence that the raw submission matched. Moving the exact-match `Ensure` after `Bind` loses the distinction between `1.004m` and `1.00m`. Likewise, deduplicating solely by the normalized value object collapses distinct raw inputs such as `1.001m` and `1.004m`. If raw numeric values are part of business identity, retain the validated raw value and an appropriate business/idempotency identifier; the rounded amount alone cannot represent that identity.

**Exact means numeric, not lexical.** Parsed `decimal` equality accepts `20.2500m` for a `20.25m` quote. Parsing itself can round away digits beyond `decimal`'s roughly 28-29 significant digits: both `decimal.Parse` and `Utf8JsonReader.GetDecimal()` read `20.2500000000000000000000000000001` as `20.25m`, before this guard runs. This example does not require a particular encoded scale, preserve original JSON text, or provide a lossless number lexer. Keep the raw JSON number text when true lexical exactness or an audit/identity contract needs the original representation. `MonetaryAmount` is for a single-currency context; `Money` uses currency-dependent precision, not always two decimal places.

The complete example is compiled in `Examples/CookbookSnippets/Recipe18_ExactMonetaryBoundary.cs`. `Examples/CookbookSnippets.Tests/Recipe18MonetaryBoundaryTests.cs` covers exact and zero matches, equivalent decimal scales, precision loss in both parsing paths, rounding collisions, missing/mismatched inputs, normalized hash-set identity, and the unchanged midpoint/negative-input behavior. The factory's midpoint, normalized-identity, and sub-cent-negative contracts are also pinned in `Trellis.Primitives/tests/MonetaryAmountTests.cs`.
