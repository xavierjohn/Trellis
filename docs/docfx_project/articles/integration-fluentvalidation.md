# FluentValidation Integration

**Level:** Intermediate 📚 | **Time:** 20-30 min | **Prerequisites:** [Basics](basics.md)

FluentValidation is great at describing validation rules. Trellis is great at keeping application flow on the success/failure railway. `Trellis.FluentValidation` connects those two worlds so you can validate once and stay inside `Result<T>`.

This article starts with the everyday case first, then covers null handling, async rules, and when `ToResult(...)` is the better fit.

## Table of Contents

- [Quick start](#quick-start)
- [What the adapter gives you](#what-the-adapter-gives-you)
- [Validating in application services](#validating-in-application-services)
- [Validating inside domain factories](#validating-inside-domain-factories)
- [Async validation](#async-validation)
- [Null input behavior](#null-input-behavior)
- [Converting an existing `ValidationResult`](#converting-an-existing-validationresult)
- [Mediator integration: `AddTrellisFluentValidation()`](#mediator-integration-addtrellisfluentvalidation)
- [Practical guidance](#practical-guidance)

## Quick start

If you already use FluentValidation, the adapter adds a very small API surface:

```bash
dotnet add package FluentValidation
dotnet add package Trellis.FluentValidation
```

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

On success, you get `Result.Ok(request)`. On failure, you get a Trellis validation failure with grouped field errors.

## What the adapter gives you

The goal is simple: stop translating `ValidationResult` by hand.

Public helpers:

```csharp
Result<T> ValidateToResult<T>(
    this IValidator<T> validator,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value",
    string? message = null);

Task<Result<T>> ValidateToResultAsync<T>(
    this IValidator<T> validator,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value",
    string? message = null,
    CancellationToken cancellationToken = default);

Result<T> ToResult<T>(
    this ValidationResult validationResult,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value");
```

What those helpers do for you:

- return the validated value on success
- convert FluentValidation failures into `new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = ... }`
- preserve grouped field errors
- use caller argument expressions for better root-level field names
- short-circuit `null` input before FluentValidation runs

> [!NOTE]
> The real methods use `[CallerArgumentExpression]` for `paramName`. That means `validator.ValidateToResult(command)` can automatically report `"command"` as the field name for root-level failures or null input.

## Validating in application services

This is the most common use case: validate a request, then continue with domain logic only if validation succeeded.

```csharp
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

public sealed record RegisterUserRequest(string Email, string FirstName, string LastName);

public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.LastName).NotEmpty();
    }
}

public sealed record User(string Email, string FirstName, string LastName);

public sealed class UserService(RegisterUserRequestValidator validator)
{
    public Result<User> Register(RegisterUserRequest request) =>
        validator.ValidateToResult(request)
            .Map(valid => new User(valid.Email, valid.FirstName, valid.LastName));
}
```

Why this reads well:

- validation stays near the request boundary
- the rest of the method only deals with valid input
- the return type stays `Result<T>` all the way through

## Validating inside domain factories

Sometimes the thing you are validating is not an incoming DTO. It is the aggregate or value object you just constructed.

`InlineValidator<T>` works well for that:

```csharp
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

This keeps invariant validation close to the type that owns the invariant.

## Async validation

Use the async helper when your rules hit the database, a remote API, or any other I/O.

```csharp
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
            .MustAsync(async (email, cancellationToken) =>
                !await repository.EmailExistsAsync(email, cancellationToken))
            .WithMessage("Email is already registered.");
    }
}

public sealed class UserService(RegisterUserRequestValidator validator)
{
    public async Task<Result<RegisterUserRequest>> RegisterAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var validated = await validator.ValidateToResultAsync(
            request,
            cancellationToken: cancellationToken);

        if (validated.IsFailure)
            return Result.Fail<RegisterUserRequest>(validated.Error);

        return Result.Ok(validated.Value);
    }
}
```

> [!TIP]
> Pass the cancellation token through. `ValidateToResultAsync(...)` forwards it to `validator.ValidateAsync(...)`.

## Null input behavior

Null request models are easy to forget because FluentValidation usually assumes you already have an instance. The adapter closes that gap.

```csharp
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

string? alias = null;

var validator = new InlineValidator<string?>();
validator.RuleFor(x => x).NotEmpty();

Result<string?> result = validator.ValidateToResult(
    alias,
    message: "Alias is required.");
```

Important behavior:

- if `value` is `null`, the adapter does **not** call `validator.Validate(...)`
- it returns a validation failure immediately
- the field name comes from the caller expression unless you override the message

## Converting an existing `ValidationResult`

Sometimes you already have a `ValidationResult` because you ran FluentValidation directly or you are integrating with older code. Use `ToResult(...)` in that case.

```csharp
using FluentValidation;
using FluentValidation.Results;
using Trellis;
using Trellis.FluentValidation;

public sealed record CreateUserRequest(string Email);

var validator = new InlineValidator<CreateUserRequest>();
validator.RuleFor(x => x.Email).NotEmpty().EmailAddress();

var request = new CreateUserRequest("invalid-email");
ValidationResult validationResult = validator.Validate(request);

Result<CreateUserRequest> result = validationResult.ToResult(request);
```

That is the right helper when:

- validation already happened elsewhere
- you only need the Trellis conversion step
- you want to preserve the original validated value

## Mediator integration: `AddTrellisFluentValidation()`

If you use [`Trellis.Mediator`](integration-mediator.md), you do **not** need to call `validator.ValidateToResult(...)` by hand inside every handler. `AddTrellisFluentValidation()` plugs FluentValidation into the unified `ValidationBehavior` stage so every `IValidator<TMessage>` registered for the message runs automatically before the handler is invoked, and its failures aggregate with any `IValidate.Validate()` failures into a single `Error.UnprocessableContent` response.

### Wiring

```csharp
using FluentValidation;
using Trellis.FluentValidation;
using Trellis.Mediator;

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();

// Register every validator explicitly (AOT-friendly):
builder.Services.AddScoped<IValidator<SubmitBatchTransfersCommand>, SubmitBatchTransfersValidator>();
```

The parameterless overload registers an open-generic `FluentValidationMessageValidatorAdapter<TMessage>` as `IMessageValidator<>`. This is **AOT-friendly**: open-generic DI registration is a first-class NativeAOT pattern, and no reflection over assemblies happens on the hot path.

For non-AOT apps that prefer assembly scanning:

```csharp
builder.Services.AddTrellisFluentValidation(typeof(SubmitBatchTransfersValidator).Assembly);
```

The scanning overload is annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Use the parameterless overload + explicit `AddScoped<IValidator<T>, ...>()` for trimming/AOT scenarios.

### Composing with `IValidate`

A message can implement `IValidate` (for cross-cutting business invariants) **and** have one or more `IValidator<TMessage>` implementations (for property-shaped rules) registered in DI. The `ValidationBehavior` runs every source and merges all `Error.UnprocessableContent` failures into one response — callers see the full violation list in a single round trip:

```csharp
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
    // Cross-cutting business invariant — awkward in FluentValidation:
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
            : Result.Fail(new Error.UnprocessableContent(EquatableArray.Create([.. violations])));
    }
}

public sealed class SubmitBatchTransfersValidator : AbstractValidator<SubmitBatchTransfersCommand>
{
    public SubmitBatchTransfersValidator()
    {
        // Property-shaped rules — natural fit for FluentValidation:
        RuleFor(c => c.Metadata.Reference)
            .NotEmpty().Matches(@"^BATCH-\d{4}-\d{3}$");

        RuleForEach(c => c.Lines).ChildRules(line =>
            line.RuleFor(l => l.Memo).NotEmpty().MaximumLength(200));
    }
}
```

A request that violates both sources at once produces one 422 with **every** violation aggregated under its proper JSON Pointer.

### JSON Pointer normalization

FluentValidation reports property names in its own dotted-with-brackets shape: `Metadata.Reference`, `Lines[0].Memo`. The Trellis adapter translates those into RFC 6901 JSON Pointers before placing them on `FieldViolation.Pointer`:

| FluentValidation property | `FieldViolation.Pointer` |
| --- | --- |
| `Reference` (root property) | `/Reference` |
| `Metadata.Reference` (nested) | `/Metadata/Reference` |
| `Lines[0].Memo` (indexer) | `/Lines/0/Memo` |
| `Items[0].Tags[2]` (multi-indexer) | `/Items/0/Tags/2` |

Special characters in segments are escaped per RFC 6901 (`~` → `~0`, `/` → `~1`). Property names that already start with `/` are passed through unchanged.

The pre-existing `validationResult.ToResult(value)` helper applies the same normalization, so direct callers benefit from the fix too.

### Failure-source semantics

- All `Error.UnprocessableContent` failures (from `IValidate` and every `IMessageValidator<TMessage>`) merge into a single response.
- Any non-UPC failure (`Error.Conflict`, `Error.Forbidden`, …) returned by any validator short-circuits the stage immediately and is propagated as-is. This lets `IValidate` model "this is a domain rule violation, not a field problem" without losing exit semantics.


### Good defaults

- validate requests at the application boundary
- use `InlineValidator<T>` for domain invariants when it keeps the type simpler
- use `ValidateToResultAsync(...)` for rules that touch I/O
- use `ToResult(...)` only when you already have a `ValidationResult`

### What the resulting errors look like

Failures are Trellis validation errors, so they use Trellis error conventions such as the standard `validation.error` code.

> [!WARNING]
> `Trellis.FluentValidation` is an adapter. It does not replace FluentValidation features, rule syntax, or DI registration. You still configure validators the normal FluentValidation way.
