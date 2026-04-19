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

## Practical guidance

Use this adapter when you want FluentValidation rules but Trellis flow control.

### Good defaults

- validate requests at the application boundary
- use `InlineValidator<T>` for domain invariants when it keeps the type simpler
- use `ValidateToResultAsync(...)` for rules that touch I/O
- use `ToResult(...)` only when you already have a `ValidationResult`

### What the resulting errors look like

Failures are Trellis validation errors, so they use Trellis error conventions such as the standard `validation.error` code.

> [!WARNING]
> `Trellis.FluentValidation` is an adapter. It does not replace FluentValidation features, rule syntax, or DI registration. You still configure validators the normal FluentValidation way.
