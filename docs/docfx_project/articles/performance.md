---
title: Performance
package: Trellis (multiple)
topics: [performance, benchmarks, allocations, rop, mediator, overhead, optimization]
related_api_reference: [trellis-api-core.md, trellis-api-mediator.md]
last_verified: 2026-10-21
audience: [developer]
---
# Performance

If you're deciding whether Trellis is "too expensive," the short answer is usually **no**.

Trellis adds a very small amount of CPU overhead so you can get explicit errors, composable workflows, and easier-to-review code. In real applications, database calls, HTTP calls, and serialization dominate the timeline long before Trellis does.

> [!TIP]
> Measure Trellis the same way you measure LINQ: compare it to the real work around it, not to an empty method.

## The practical answer

On the latest benchmark run captured in this repository, the simple happy-path comparison showed Trellis adding roughly **4-5 ns** over imperative code on a fast desktop CPU. Older runs on different hardware landed in the **11-16 ns** range. The exact number moves with hardware and JIT behavior, but the conclusion does not: **the overhead is tiny**.

| Question | Practical answer |
| --- | --- |
| Does Trellis allocate more than equivalent imperative code? | Usually **no** on the same path. |
| Is the happy path fast? | Yes — common `Map`, `Bind`, and `Tap` calls stay in the low-nanosecond range. |
| Is the failure path expensive? | Usually no — short-circuiting keeps it predictable. |
| Should I optimize Trellis before I optimize I/O? | Almost never. |

## Why the overhead usually does not matter

A few nanoseconds sounds real in a benchmark because the benchmark is isolating the framework cost. Your production code usually is not.

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
| `RopStyleHappy` | 98.32 ns | 296 B |
| `IfStyleHappy` | 93.86 ns | 296 B |
| `RopStyleSad` | 65.63 ns | 336 B |
| `IfStyleSad` | 75.08 ns | 336 B |

### Core operations

| Operation | Representative result |
| --- | --- |
| `Bind` | ~4.85 ns for a single happy-path bind |
| `Map` | ~3.24 ns for a single happy-path map |
| `Tap` | ~2.88 ns for a single happy-path tap |
| `Ensure` | ~12.06 ns for one predicate |
| `Combine` | ~7.27 ns for two successful results |

> [!NOTE]
> Benchmarks vary by CPU, runtime version, and benchmark shape. Compare **relative cost** and **allocation behavior** more than any single absolute number.

### Transactional command frames

The Core ROP figures above are not an allocation promise for optional integration
stages. Every transactional command opens a `CommandDispatchState` frame, even when
no hooks are registered or no entries are added: an unused child must still hide its
parent's entries. The entry dictionary remains lazy.

Before measuring, the review budget was fixed at **500 ns and 256 B incremental
per single unused-state dispatch** versus a matched no-frame baseline. This is an
investigation trigger, not a portable CI timing assertion.

`CommandDispatchStateBenchmarks` measures the real internal frame's complete lifecycle
through benchmark-only friend access, with identical depth-aware scopes and synchronously
completed fake handler/commit work. Used-state cases add, read, and remove one typed entry;
their baseline carries the same payload locally. This isolates frame/accessor overhead,
not full Mediator throughput, asynchronous I/O, or database commit latency.

The 2026-10-21 run used BenchmarkDotNet 0.15.8 ShortRun (one launch, three warmups and
three measured iterations), .NET 10.0.13 x64 Workstation GC, Windows 11, and an AMD
Ryzen 9 9900X:

| Frames | State per frame | No-frame mean | Framed mean | Incremental time | Incremental allocation |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | Unused | 29.07 ns | 52.58 ns | 23.51 ns | 144 B |
| 1 | One entry | 29.24 ns | 98.39 ns | 69.15 ns | 360 B |
| 2 | Unused | 61.12 ns | 118.82 ns | 57.70 ns | 360 B |
| 2 | One entry | 64.83 ns | 208.67 ns | 143.84 ns | 792 B |

The two-frame rows measure the **whole outer/inner pair**, not a per-dispatch cost.
For one unused frame, total allocation changed from 32 B to 176 B; the incremental
23.51 ns / 144 B is within the predeclared budget. No activation flag, hook-count
shortcut, or frame-elision optimization was justified. These machine-specific figures
do not predict allocations for every `AsyncLocal` execution-context shape.

## What actually makes Trellis fast enough

### 1. Success-path operations are tiny

Common pipeline steps are intentionally lightweight. If your chain is mostly validation, mapping, and short-circuiting, Trellis is not likely to be your bottleneck.

### 2. Failure short-circuits

Once a result is failed, downstream success-path work is skipped. That makes failure behavior predictable and often surprisingly cheap.

### 3. The memory story is good

For equivalent logic, the benchmark suite repeatedly shows **matching allocations** between Trellis and imperative code. When allocations do appear, they usually come from:

- constructing errors
- allocating strings or objects in your mapping code
- async machinery
- logging or collection growth

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

Run the full benchmark suite:

```bash
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release
```

Run one subset:

```bash
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter *Combine*
```

Run the dispatch-frame matrix (one/two frames, unused/used state, with/without frames):

```powershell
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*CommandDispatchStateBenchmarks*'
```

The benchmark job marks its generated executables nonpackable so they do not try to
restore a nonexistent published package baseline. Normal library package validation
remains enabled.

## When you should care more

There *are* cases where micro-overhead matters:

- extremely hot CPU-bound loops
- high-frequency in-memory processing
- code paths called millions of times per second
- allocation-sensitive low-latency services

If that is your world, benchmark your exact workload. Trellis is still viable in many of those scenarios, but you should verify with production-shaped data.

## Bottom line

Trellis is a trade: a few nanoseconds for clearer control flow, explicit errors, and easier composition.

For most systems, that is an excellent trade.

If you want the raw numbers, keep reading: [Benchmarks](BENCHMARKS.md).
