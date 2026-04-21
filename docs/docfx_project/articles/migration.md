# Migrating from FunctionalDDD

Migrating from `FunctionalDdd.*` to `Trellis.*` is mostly a **rename exercise**, not a redesign.

That is good news: you usually do **not** need to rethink your domain model or rewrite your pipelines. You need to update package names, namespaces, and a few integration method names.

> [!TIP]
> Start with a mechanical rename, then let the compiler and analyzers show you the handful of places that still need attention.

## What changes and what does not

### What changes

- NuGet package names
- namespaces
- a few instrumentation method names
- references to the old Ardalis specification package

### What does not

- core `Result<T>` / `Maybe<T>` usage
- `Bind`, `Map`, `Tap`, `Ensure`, `Combine`, `Match`
- aggregate and entity patterns
- most application-layer code shape

## Package mapping

| Old package | New package |
| --- | --- |
| `FunctionalDdd.RailwayOrientedProgramming` | `Trellis.Core` |
| `FunctionalDdd.DomainDrivenDesign` | `Trellis.DomainDrivenDesign` |
| `FunctionalDdd.PrimitiveValueObjects` | `Trellis.Primitives` |
| `FunctionalDdd.PrimitiveValueObjectGenerator` | `Trellis.Core.Generator` |
| `FunctionalDdd.Asp` | `Trellis.Asp` |
| `FunctionalDdd.Http` | `Trellis.Http` |
| `FunctionalDdd.FluentValidation` | `Trellis.FluentValidation` |
| `FunctionalDdd.ArdalisSpecification` | Remove and use native `Specification<T>` from `Trellis.DomainDrivenDesign` |

Optional new packages with no direct one-to-one predecessor include:

- `Trellis.Analyzers`
- `Trellis.Testing`
- `Trellis.Stateless`

## Step 1: update package references

If you centralize package versions in `Directory.Packages.props`, this is the fastest path.

### Before

```xml
<ItemGroup>
  <PackageVersion Include="FunctionalDdd.RailwayOrientedProgramming" Version="2.x.x" />
  <PackageVersion Include="FunctionalDdd.DomainDrivenDesign" Version="2.x.x" />
  <PackageVersion Include="FunctionalDdd.Asp" Version="2.x.x" />
  <PackageVersion Include="FunctionalDdd.PrimitiveValueObjects" Version="2.x.x" />
  <PackageVersion Include="FunctionalDdd.PrimitiveValueObjectGenerator" Version="2.x.x" />
</ItemGroup>
```

### After

```xml
<ItemGroup>
  <PackageVersion Include="Trellis.Core" Version="3.x.x" />
  <PackageVersion Include="Trellis.DomainDrivenDesign" Version="3.x.x" />
  <PackageVersion Include="Trellis.Asp" Version="3.x.x" />
  <PackageVersion Include="Trellis.Primitives" Version="3.x.x" />
  <PackageVersion Include="Trellis.Core.Generator" Version="3.x.x" />
  <PackageVersion Include="Trellis.Analyzers" Version="3.x.x" />
</ItemGroup>
```

If you do not use central package management, update each project file directly.

## Step 2: update namespaces

The most important namespace change is this:

| Old | New |
| --- | --- |
| `using FunctionalDdd;` | `using Trellis;` |
| `using FunctionalDdd.PrimitiveValueObjects;` | `using Trellis.Primitives;` |

### Why this matters

Core result and DDD primitives live in `Trellis`.

Ready-to-use value objects such as `EmailAddress`, `FirstName`, and `Money` live in `Trellis.Primitives`.

A common migration pattern is therefore:

```csharp
using Trellis;
using Trellis.Primitives;
```

## Step 3: update instrumentation names

If you enabled OpenTelemetry integration, rename these extension methods:

| Old | New |
| --- | --- |
| `.AddFunctionalDddRopInstrumentation()` | `.AddResultsInstrumentation()` |
| `.AddFunctionalDddCvoInstrumentation()` | `.AddPrimitiveValueObjectInstrumentation()` |

## Step 4: replace Ardalis specification usage

If you were using `FunctionalDdd.ArdalisSpecification`, migrate to native Trellis specifications.

```csharp
using System.Linq.Expressions;
using Trellis;

public sealed class Customer
{
    public bool IsActive { get; init; }
}

public sealed class ActiveCustomerSpecification : Specification<Customer>
{
    public override Expression<Func<Customer, bool>> ToExpression() =>
        customer => customer.IsActive;
}
```

## Step 5: fix the small but important gotchas

### `Unit.Value` does not exist

If older code or snippets refer to `Unit.Value`, replace that usage with either:

```csharp
using Trellis;

var unit1 = default(Unit);
var unit2 = new Unit();
var success = Result.Ok();
```

### Error-code comparisons should use the current Trellis codes

Trellis error codes use the standard `.error` suffix for the built-in error families, for example:

- `validation.error`
- `not.found.error`
- `forbidden.error`
- `unexpected.error`

### Built-in value objects belong in `Trellis.Primitives`

If migration leaves you with missing type errors for `EmailAddress`, `FirstName`, `Money`, and similar types, add the correct namespace import rather than forcing everything into `Trellis`.

## A simple before/after example

### Before

```csharp
using FunctionalDdd;
using FunctionalDdd.PrimitiveValueObjects;

var result = FirstName.TryCreate(firstName)
    .Combine(EmailAddress.TryCreate(email));
```

### After

```csharp
using Trellis;
using Trellis.Primitives;

var result = FirstName.TryCreate(firstName)
    .Combine(EmailAddress.TryCreate(email));
```

The code shape stays the same. That is the pattern you should expect throughout the migration.

## Recommended migration workflow

1. Update package references.
2. Replace namespaces.
3. Fix instrumentation names.
4. Replace any old specification package usage.
5. Build.
6. Run tests.
7. Add `Trellis.Analyzers` if you want the compiler to help enforce current patterns.

## Verification

After the rename, run your normal verification commands:

```bash
dotnet build
dotnet test
```

## Bottom line

For most codebases, the migration is intentionally boring:

- rename packages
- rename namespaces
- fix a few integration points
- keep your domain logic largely unchanged

That is exactly what you want from a rebrand-style migration.
