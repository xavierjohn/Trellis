---
title: Performance
package: Trellis (multiple)
topics: [performance, benchmarks, allocations, rop, overhead, optimization]
related_api_reference: [trellis-api-core.md]
last_verified: 2026-11-12
audience: [developer]
---
# Performance

If you're deciding whether Trellis is "too expensive," the short answer is usually **no**.

Trellis adds a very small amount of CPU overhead so you can get explicit errors, composable workflows, and easier-to-review code. In real applications, database calls, HTTP calls, and serialization dominate the timeline long before Trellis does.

> [!TIP]
> Measure Trellis the same way you measure LINQ: compare it to the real work around it, not to an empty method.

## The practical answer

On the November 12, 2026 **.NET 11.0.0 / SDK 11.0.100** run, the simple happy-path means were **90.49 ns** for ROP and **69.78 ns** for imperative code: an observed **20.71 ns** gap. The larger pipeline gap was **94.67 ns**. These are complete workload costs, not a fixed per-step overhead, and the three-sample ShortRun confidence intervals limit timing conclusions.

| Question | Practical answer |
| --- | --- |
| Does Trellis allocate more than equivalent imperative code? | It depends: the basic comparisons allocate **136 B more**; the larger pipeline allocations match. |
| Is the happy path fast? | Yes — common `Map`, `Bind`, and `Tap` calls stay in the low-nanosecond range. |
| Is the failure path expensive? | Usually no — short-circuiting keeps it predictable. |
| Should I optimize Trellis before I optimize I/O? | Almost never. |

## Why the overhead usually does not matter

Microbenchmarks isolate small workloads. Production code adds database, network, serialization, and business-logic costs around those operations.

| Operation | Typical scale |
| --- | ---: |
| `Map` / `Bind` / `Tap` | single-digit to low double-digit ns |
| JSON serialization | microseconds |
| Database query | milliseconds |
| HTTP call | milliseconds to tens of milliseconds |

That means Trellis overhead is usually lost in the noise of:

- database access
- network latency
- disk I/O
- logging and serialization
- your own business logic

## A simple example

This is the kind of code the benchmarks are measuring:

```csharp
using Trellis;
using Trellis.Primitives;

var output = FirstName.TryCreate("Ada")
    .Combine(EmailAddress.TryCreate("ada@example.com"))
    .Match(
        onSuccess: values => $"{values.Item1} <{values.Item2}>",
        onFailure: error => error.Detail);
```

The imperative equivalent is a little closer to the metal, but not by much:

```csharp
using Trellis;
using Trellis.Primitives;

var firstName = FirstName.TryCreate("Ada");
var email = EmailAddress.TryCreate("ada@example.com");

string output;
if (firstName.TryGetValue(out var firstNameValue) && email.TryGetValue(out var emailValue))
    output = $"{firstNameValue} <{emailValue}>";
else
{
    Error? error = null;
    if (firstName.IsFailure)
        error = firstName.Error;
    if (email.IsFailure)
        error = error is null ? email.Error : error.Combine(email.Error);

    output = error!.Detail;
}
```

## Headline numbers from the benchmark suite

These figures come from the benchmark data checked into this repository. Treat them as **directionally useful**, not as universal constants.

### ROP vs imperative

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `RopStyleHappy` | 90.49 ns | 280 B |
| `IfStyleHappy` | 69.78 ns | 144 B |
| `RopStyleSad` | 86.54 ns | 384 B |
| `IfStyleSad` | 59.15 ns | 248 B |

### Core operations

| Operation | Representative result |
| --- | --- |
| `Bind` | ~4.82 ns for a single happy-path bind |
| `Map` | ~4.02 ns for a single happy-path map |
| `Tap` | ~3.86 ns for a single happy-path tap |
| `Ensure` | ~6.33 ns / 56 B for one predicate with an eagerly constructed error |
| `Combine` | ~4.14 ns for two successful results |

> [!NOTE]
> Benchmarks vary by CPU, runtime version, and benchmark shape. Compare **relative cost** and **allocation behavior** more than any single absolute number.

## What actually makes Trellis fast enough

### 1. Success-path operations are tiny

Common pipeline steps are intentionally lightweight. If your chain is mostly validation, mapping, and short-circuiting, Trellis is not likely to be your bottleneck.

### 2. Failure short-circuits

Once a result is failed, downstream success-path work is skipped. That makes failure behavior predictable and often surprisingly cheap.

### 3. The memory story is good

The basic ROP happy/sad comparisons allocate **280 B / 384 B**, versus **144 B / 248 B** for imperative code. The larger pipeline allocates **632 B** in either style. Individual successful `Map`, `Bind`, and `Tap` workloads allocate nothing, but that does not make every complete pipeline allocation-free. Costs can include:

- constructing errors
- allocating strings or objects in your mapping code
- async machinery
- logging or collection growth
- pipeline infrastructure and callback handling

## Performance advice that actually pays off

### Combine validations before you bind

When validations are independent, aggregate them first.

```csharp
using Trellis;
using Trellis.Primitives;

Result<string> CreateDisplayName(string first, string last, string email) =>
    FirstName.TryCreate(first)
        .Combine(LastName.TryCreate(last))
        .Combine(EmailAddress.TryCreate(email))
        .Map((firstName, lastName, emailAddress) =>
            $"{firstName} {lastName} <{emailAddress}>");
```

That is usually clearer *and* more efficient than deeply nested sequential validation.

### Reuse stable errors on hot paths

```csharp
using Trellis;

static class CustomerRules
{
    private static readonly Error MinimumSpendError =
        new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Customer must have spent at least 1000." };

    public static Result<decimal> Validate(decimal totalSpend) =>
        totalSpend >= 1000m
            ? Result.Ok(totalSpend)
            : MinimumSpendError;
}
```

### Prefer `ValueTask` only when it is genuinely helpful

If an async API frequently completes synchronously — for example, cache hits — `ValueTask` can help reduce allocations. If the work is always real I/O, `Task` is usually fine.

### Optimize I/O before you optimize Trellis

If a request is slow, look for:

- N+1 database queries
- repeated HTTP calls
- unnecessary serialization
- chatty logging
- inefficient collection or string processing

Those costs dwarf the framework overhead.

## Running the benchmarks yourself

The current results were captured with an isolated **BenchmarkDotNet 0.16.0-preview.2** runner, preserving the Balanced power plan. The repository's 0.15.8 pin does not recognize .NET 11; use a compatible version before running these commands. See [Benchmarks](BENCHMARKS.md#reproducing-the-results-locally) for the recorded configuration.

Run the full benchmark suite, including tracing:

```powershell
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*'
```

Run one subset:

```powershell
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*Combine*'
```

## When you should care more

There *are* cases where micro-overhead matters:

- extremely hot CPU-bound loops
- high-frequency in-memory processing
- code paths called millions of times per second
- allocation-sensitive low-latency services

If that is your world, benchmark your exact workload. Trellis is still viable in many of those scenarios, but you should verify with production-shaped data.

## Bottom line

Trellis is a trade: lightweight pipeline steps and workload-dependent allocation costs for clearer control flow, explicit errors, and easier composition.

For most systems, that is an excellent trade.

If you want the raw numbers, keep reading: [Benchmarks](BENCHMARKS.md).
