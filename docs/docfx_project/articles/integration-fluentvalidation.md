---
title: FluentValidation Integration
package: Trellis.FluentValidation
topics: [validation, fluentvalidation, error-mapping, unprocessable-content, mediator-pipeline, json-pointer, aot]
related_api_reference: [trellis-api-fluentvalidation.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# FluentValidation Integration

`Trellis.FluentValidation` plugs FluentValidation validators into the Trellis Mediator validation stage and converts standalone `ValidationResult` runs into `Result<T>` failures backed by `Error.UnprocessableContent`.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Wire validators into the Mediator pipeline (AOT-safe) | `services.AddTrellisFluentValidation()` + explicit `AddScoped<IValidator<T>, ...>()` | [Mediator integration](#mediator-integration) |
| Wire validators with assembly scanning (non-AOT) | `services.AddTrellisFluentValidation(typeof(...).Assembly)` | [Mediator integration](#mediator-integration) |
| Run a validator outside Mediator and stay in `Result<T>` | `validator.ValidateToResult(value)` | [Standalone validation](#standalone-validation) |
| Same as above for async rules (DB / I/O) | `validator.ValidateToResultAsync(value, cancellationToken: ct)` | [Standalone validation](#standalone-validation) |
| Convert an existing `ValidationResult` you already have | `validationResult.ToResult(value)` | [Converting an existing validationresult](#converting-an-existing-validationresult) |
| Reject a `null` request without invoking FluentValidation | `ValidateToResult` / `ValidateToResultAsync` (built-in null short-circuit) | [Null input](#null-input) |
| Aggregate FluentValidation + `IValidate` failures into one 422 | `AddTrellisFluentValidation()` alongside `IValidate.Validate()` on the message | [Composing with ivalidate](#composing-with-ivalidate) |

## Use this guide when

- You already use FluentValidation and want failures to surface as `Error.UnprocessableContent` instead of exceptions or hand-rolled translations.
- You send messages through `Trellis.Mediator` and want validators to run automatically inside `ValidationBehavior<TMessage,TResponse>` without per-handler boilerplate.
- You need RFC 6901 JSON Pointer paths (`/Lines/0/Memo`) on validation failures so the ASP boundary renders them under the right field.

## Surface at a glance

`Trellis.FluentValidation` exposes one DI extension class, one open-generic adapter, and one set of `Result<T>` extension methods.

| Member | Receiver | Returns | Purpose |
|---|---|---|---|
| `AddTrellisFluentValidation()` | `IServiceCollection` | `IServiceCollection` | Registers the open-generic adapter as `IMessageValidator<>`. AOT/trim-safe, idempotent. |
| `AddTrellisFluentValidation(params Assembly[])` | `IServiceCollection` | `IServiceCollection` | Same as above, then scans assemblies for concrete `IValidator<T>` types and registers each scoped. **Not AOT/trim-safe** (`[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`). |
| `FluentValidationMessageValidatorAdapter<TMessage>` | — (DI-resolved) | `IMessageValidator<TMessage>` | Runs every injected `IValidator<TMessage>` sequentially, aggregates failures into one `Error.UnprocessableContent`. Forwards the ambient `CancellationToken`. |
| `ValidateToResult<T>(value, paramName?, message?)` | `IValidator<T>` | `Result<T>` | Synchronous validate-and-convert. Short-circuits `null` input without invoking FluentValidation. |
| `ValidateToResultAsync<T>(value, paramName?, message?, ct)` | `IValidator<T>` | `Task<Result<T>>` | Asynchronous validate-and-convert. Forwards `CancellationToken` to `ValidateAsync`. |
| `ToResult<T>(value, paramName?)` | `ValidationResult` | `Result<T>` | Converts a pre-computed `ValidationResult` to `Result<T>`; preserves the validated value on success. |

Full signatures: [trellis-api-fluentvalidation.md](../api_reference/trellis-api-fluentvalidation.md).

## Installation

```bash
dotnet add package FluentValidation
dotnet add package Trellis.FluentValidation
```

## Quick start

Run a FluentValidation validator and stay inside `Result<T>`. No DI, no Mediator — just the standalone helper.

```csharp
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

public sealed record CreateUserRequest(string Email, string FirstName, string LastName);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
    }
}

var validator = new CreateUserRequestValidator();
var request = new CreateUserRequest("sam@example.com", "Sam", "Taylor");

Result<CreateUserRequest> result = validator.ValidateToResult(request);
```

On success: `Result.Ok(request)`. On failure: `Result.Fail<CreateUserRequest>(new Error.UnprocessableContent(EquatableArray.Create(violations)))` with one `FieldViolation` per FluentValidation failure.

## Standalone validation

Use the `IValidator<T>` extension methods when validators are not driven by the Mediator pipeline — domain factories, application services, or any code path that already holds an `IValidator<T>` instance.

| Helper | Sync/async | Null input |
|---|---|---|
| `ValidateToResult<T>` | sync | Returns `Fail` without calling `validator.Validate` |
| `ValidateToResultAsync<T>` | async | Returns `Fail` without calling `validator.ValidateAsync` |

Both helpers forward `paramName` from `[CallerArgumentExpression]`, so root-level failures and null-input failures carry the caller's variable name as the field path.

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);
}

public sealed record RegisterUserRequest(string Email);

public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserRequestValidator(IUserRepository repository)
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MustAsync(async (email, ct) => !await repository.EmailExistsAsync(email, ct))
            .WithMessage("Email is already registered.");
    }
}

public sealed class UserService(RegisterUserRequestValidator validator)
{
    public Task<Result<RegisterUserRequest>> RegisterAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken) =>
        validator.ValidateToResultAsync(request, cancellationToken: cancellationToken);
}
```

### Validating inside domain factories

`InlineValidator<T>` keeps invariant rules close to the type that owns them. The factory returns `Result<T>` directly.

```csharp
using System;
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

public sealed class Product : Entity<Guid>
{
    private static readonly InlineValidator<Product> s_validator = CreateValidator();

    public string Name { get; }
    public decimal Price { get; }

    private Product(Guid id, string name, decimal price)
        : base(id)
    {
        Name = name;
        Price = price;
    }

    public static Result<Product> Create(string name, decimal price)
    {
        var product = new Product(Guid.NewGuid(), name, price);
        return s_validator.ValidateToResult(product);
    }

    private static InlineValidator<Product> CreateValidator()
    {
        var validator = new InlineValidator<Product>();
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        validator.RuleFor(x => x.Price).GreaterThan(0);
        return validator;
    }
}
```

### Null input

`ValidateToResult` / `ValidateToResultAsync` reject `null` before invoking FluentValidation. The captured `paramName` becomes the field path; the optional `message` parameter overrides the default `'{paramName}' must not be empty.`.

```csharp
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

string? alias = null;

var validator = new InlineValidator<string?>();
validator.RuleFor(x => x).NotEmpty();

Result<string?> result = validator.ValidateToResult(alias, message: "Alias is required.");
```

`validator.Validate(null!)` is **not** called; the helper synthesizes a single `FieldViolation` for `paramName` with reason code `"validation.error"`.

## Converting an existing `ValidationResult`

When validation already happened (legacy code, custom orchestration, or a manual `validator.Validate(value)` call), use `ToResult(value)` to fold the `ValidationResult` into the railway.

```csharp
using FluentValidation;
using FluentValidation.Results;
using Trellis;
using Trellis.FluentValidation;

public sealed record CreateUserRequest(string Email);

var validator = new InlineValidator<CreateUserRequest>();
validator.RuleFor(x => x.Email).NotEmpty().EmailAddress();

var request = new CreateUserRequest("invalid-email");
ValidationResult validation = validator.Validate(request);

Result<CreateUserRequest> result = validation.ToResult(request);
```

`ToResult` only null-checks `validationResult` itself — it does not reject a `null` `value`. Use `ValidateToResult` when null-input rejection matters.

## Mediator integration

`AddTrellisFluentValidation()` registers `FluentValidationMessageValidatorAdapter<TMessage>` as the open-generic `IMessageValidator<TMessage>`. The existing `ValidationBehavior<TMessage,TResponse>` discovers it automatically — no second pipeline behavior is added.

| Overload | AOT/trim | Behavior |
|---|---|---|
| `AddTrellisFluentValidation()` | Safe | Registers the open-generic adapter once. Idempotent. You register each `IValidator<T>` explicitly. |
| `AddTrellisFluentValidation(params Assembly[])` | **Not safe** (`[RequiresUnreferencedCode]`, `[RequiresDynamicCode]`) | Calls the parameterless overload, then scans the supplied assemblies for concrete `IValidator<T>` types and registers them as scoped. Deduplicates `(serviceType, implementationType)` pairs against existing registrations. Tolerates `ReflectionTypeLoadException`. |

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Trellis.FluentValidation;
using Trellis.Mediator;

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();

builder.Services.AddScoped<IValidator<SubmitBatchTransfersCommand>, SubmitBatchTransfersValidator>();
```

For non-AOT apps, scan instead:

```csharp
builder.Services.AddTrellisFluentValidation(typeof(SubmitBatchTransfersValidator).Assembly);
```

### Adapter behavior

| Situation | Result |
|---|---|
| No `IValidator<TMessage>` registered | `Result.Ok()` — no allocations |
| All registered validators pass | `Result.Ok()` |
| One or more validators report failures | `Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(violations)))` aggregating every failure |
| FluentValidation `PropertyName` is null/whitespace | Pointer derived from `typeof(TMessage).Name` |
| FluentValidation `ErrorCode` is null/whitespace | `FieldViolation.ReasonCode` defaults to `"validation.error"` |
| `CancellationToken` cancelled mid-run | Forwarded to `validator.ValidateAsync`; cancellation propagates |

Validators run **sequentially** and every failure is collected — the adapter does not short-circuit on the first failing validator.

### Composing with `IValidate`

A message can implement `Trellis.Mediator.IValidate` for cross-cutting business invariants and also have one or more `IValidator<TMessage>` implementations registered for property-shaped rules. `ValidationBehavior<TMessage,TResponse>` runs every source and merges all `Error.UnprocessableContent` failures into a single response.

```csharp
using System.Collections.Generic;
using FluentValidation;
using Mediator;
using Trellis;
using Trellis.Mediator;

public sealed record SubmitBatchTransfersCommand(
    AccountId FromId,
    BatchMetadata Metadata,
    IReadOnlyList<BatchTransferLine> Lines)
    : ICommand<Result<BatchTransferReceipt>>, IValidate
{
    public IResult Validate()
    {
        var violations = new List<FieldViolation>();
        if (Lines.Count == 0)
            violations.Add(new FieldViolation(InputPointer.ForProperty(nameof(Lines)), "batch.empty")
            { Detail = "At least one line is required." });
        for (var i = 0; i < Lines.Count; i++)
            if (Lines[i].ToAccountId == FromId)
                violations.Add(new FieldViolation(new InputPointer($"/Lines/{i}/ToAccountId"), "batch.self-transfer")
                { Detail = "A line may not target the source account." });

        return violations.Count == 0
            ? Result.Ok()
            : Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())));
    }
}

public sealed class SubmitBatchTransfersValidator : AbstractValidator<SubmitBatchTransfersCommand>
{
    public SubmitBatchTransfersValidator()
    {
        RuleFor(c => c.Metadata.Reference)
            .NotEmpty().Matches(@"^BATCH-\d{4}-\d{3}$");

        RuleForEach(c => c.Lines).ChildRules(line =>
            line.RuleFor(l => l.Memo).NotEmpty().MaximumLength(200));
    }
}
```

A request that violates both sources at once produces one 422 with **every** violation aggregated under its proper JSON Pointer.

> [!NOTE]
> Any non-`UnprocessableContent` failure (`Error.Conflict`, `Error.Forbidden`, …) returned by `IValidate` or any validator short-circuits the stage immediately and propagates as-is. Aggregation only applies to `UnprocessableContent`.

### JSON Pointer normalization

The adapter and `ToResult` translate FluentValidation property names to RFC 6901 JSON Pointers before constructing `InputPointer`. The same `JsonPointerNormalizer` is used in both code paths.

| FluentValidation `PropertyName` | `FieldViolation.Field` |
|---|---|
| `Email` | `/Email` |
| `Address.PostCode` | `/Address/PostCode` |
| `Items[0].Sku` | `/Items/0/Sku` |
| `Items[0].Tags[2]` | `/Items/0/Tags/2` |

Special characters in segments are escaped per RFC 6901 (`~` → `~0`, `/` → `~1`). Names that already begin with `/` are passed through unchanged.

## Composition

Once a validation step yields `Result<T>`, it composes with the rest of Trellis (`Bind`, `Map`, `Ensure`, async variants). For commands that produce no payload, return `Result<Unit>`.

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

public sealed record RegisterUserRequest(string Email, string FirstName, string LastName);
public sealed record User(string Email, string FirstName, string LastName);

public interface IUserRepository
{
    Task<Result<Unit>> AddAsync(User user, CancellationToken cancellationToken);
}

public sealed class UserService(
    IValidator<RegisterUserRequest> validator,
    IUserRepository repository)
{
    public Task<Result<Unit>> RegisterAsync(RegisterUserRequest request, CancellationToken ct) =>
        validator.ValidateToResultAsync(request, cancellationToken: ct)
            .MapAsync(valid => new User(valid.Email, valid.FirstName, valid.LastName), ct)
            .BindAsync((user, token) => repository.AddAsync(user, token), ct);
}
```

## Practical guidance

- **Prefer the parameterless `AddTrellisFluentValidation()` overload.** It is AOT/trim-safe and idempotent. Register each `IValidator<T>` explicitly with `AddScoped`.
- **Reach for `ValidateToResultAsync` whenever rules touch I/O.** It forwards `CancellationToken` straight to `validator.ValidateAsync`.
- **Use `ToResult` only when you already hold a `ValidationResult`.** For all other paths use `ValidateToResult` / `ValidateToResultAsync` so null input is rejected before FluentValidation runs.
- **Combine `IValidate` and FluentValidation for layered rules.** Cross-field invariants live on `IValidate.Validate()`; per-property rules live in the validator. Both contribute to a single aggregated 422.
- **Let pointer normalization carry structure.** Do not pre-format property names — the adapter handles dotted chains and indexers automatically.
- **Validators do not parse primitives.** Keep primitive-to-value-object parsing at the transport seam; validate already-shaped commands and value objects.

## Cross-references

- API surface: [trellis-api-fluentvalidation.md](../api_reference/trellis-api-fluentvalidation.md)
- `Result<T>`, `Error.UnprocessableContent`, `FieldViolation`, `InputPointer`: [trellis-api-core.md](../api_reference/trellis-api-core.md)
- Mediator validation behavior: [trellis-api-mediator.md](../api_reference/trellis-api-mediator.md)
- ASP.NET 422 rendering: [trellis-api-asp.md](../api_reference/trellis-api-asp.md)
- Cookbook recipes: [trellis-api-cookbook.md](../api_reference/trellis-api-cookbook.md)
