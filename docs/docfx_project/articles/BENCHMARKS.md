---
title: Benchmarks
package: Trellis (multiple)
topics: [benchmarks, performance, allocations, rop, microbenchmarks]
related_api_reference: [trellis-api-core.md]
last_verified: 2026-11-13
audience: [developer]
---
# Benchmarks

This article is the "show me the numbers" companion to [Performance](performance.md).

If you want the short version, read that article first. If you want benchmark shape, representative timings, and reproduction steps, read on.

> [!NOTE]
> Microbenchmarks are hardware-sensitive. The values below are useful for comparing **patterns inside Trellis**, not for predicting end-to-end request latency.

## Benchmark environment

Latest benchmark report in this repository:

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 9900X 4.40 GHz |
| OS | Windows 11 (25H2), build 26200.9974 |
| .NET | 11.0.0 (SDK 11.0.100), X64 RyuJIT x86-64-v4 |
| GC | Concurrent Workstation |
| BenchmarkDotNet | 0.16.0-preview.2, isolated runner |
| Job | `ShortRun`: 1 launch, 3 warmup iterations, 3 measured iterations |
| Power plan | Balanced; automatic changes disabled |
| Memory diagnostics | Enabled |
| Coverage | 236 parameterized cases across 17 classes |
| Measured | November 12, 2026, source revision `5902bb82` |

The runner compiled the unchanged repository benchmark workloads. Full tables, including all 30 tracing cases, are in the [repository benchmark report](https://github.com/xavierjohn/Trellis/blob/main/BENCHMARKS.md).

The benchmark project is:

```text
Trellis.Benchmark\Trellis.Benchmark.csproj
```

## How to read the results

A few rules make the numbers easier to interpret:

1. **Account for uncertainty.** Three-sample ShortRun means are useful observations, but small timing gaps can fall within the confidence intervals.
2. **Look at allocations next.** Allocation-free success paths matter more than tiny timing differences.
3. **Treat benchmark families differently.** `Map`, `Tap`, and `Bind` measure framework overhead. Async and object-creation benchmarks measure framework cost plus your workload shape.

## Headline results

### ROP vs imperative code

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `RopStyleHappy` | 90.49 ns | 280 B |
| `IfStyleHappy` | 69.78 ns | 144 B |
| `RopStyleSad` | 86.54 ns | 384 B |
| `IfStyleSad` | 59.15 ns | 248 B |
| `RopSample1` | 308.12 ns | 632 B |
| `IfSample1` | 213.45 ns | 632 B |

### What that means

- The observed happy-path mean gap is **20.71 ns**; the sad-path gap is **27.39 ns**.
- Basic ROP cases allocate **136 B more** than their imperative counterparts.
- The larger pipeline mean gap is **94.67 ns**, with **632 B** allocated by either style.
- These are complete workloads, not a universal per-step overhead; timing differences need to be read alongside ShortRun uncertainty.

> [!TIP]
> Benchmark the code you actually run. Callback work, error creation, and tracing can change both time and allocations even when individual pipeline operations are cheap.

## Operation-by-operation results

### `Bind`

Use `Bind` when the next step also returns a `Result<T>`.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Bind_SingleChain_Success` | 4.82 ns | 0 B |
| `Bind_SingleChain_Failure` | 4.14 ns | 0 B |
| `Bind_ThreeChains_AllSuccess` | 16.95 ns | 0 B |
| `Bind_FiveChains_Success` | 39.50 ns | 0 B |
| `Bind_ThreeChains_FailAtSecond` | 16.16 ns | 56 B |

**Takeaway:** `Bind` scales cleanly and short-circuits immediately on failure.

### `Map`

Use `Map` when you are transforming the value but keeping success/failure structure unchanged.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Map_SingleTransformation_Success` | 4.02 ns | 0 B |
| `Map_ThreeTransformations_Success` | 13.01 ns | 0 B |
| `Map_FiveTransformations_Success` | 26.94 ns | 0 B |
| `Map_ComplexTransformation` | 18.21 ns | 80 B |
| `Map_ToComplexObject` | 31.88 ns | 144 B |

**Takeaway:** plain mapping is extremely cheap. Allocations usually come from the object you create, not from Trellis itself.

### `Tap`

Use `Tap` for side effects that should not change the value.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Tap_SingleAction_Success` | 3.86 ns | 0 B |
| `Tap_ThreeActions_Success` | 12.46 ns | 0 B |
| `Tap_WithLogging_Success` | 31.22 ns | 0 B |
| `Tap_FiveActions_Success` | 24.89 ns | 0 B |

**Takeaway:** `Tap` is cheap enough for ordinary logging and bookkeeping.

### `Ensure`

Use `Ensure` to keep a success value only when a predicate passes.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Ensure_SinglePredicate_Pass` | 6.33 ns | 56 B |
| `Ensure_SinglePredicate_Fail` | 6.44 ns | 56 B |
| `Ensure_ThreePredicates_AllPass` | 19.55 ns | 168 B |
| `Ensure_FivePredicates_AllPass` | 41.73 ns | 280 B |

**Takeaway:** these workloads eagerly construct errors before calling `Ensure`, so passing predicates also allocate. They do not measure a lazy error factory.

### `Combine`

Use `Combine` when several validations can run independently and you want all failures back together.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Combine_TwoResults_BothSuccess` | 4.14 ns | 0 B |
| `Combine_TwoResults_BothFailure` | 22.09 ns | 96 B |
| `Combine_ThreeResults_AllSuccess` | 16.39 ns | 0 B |
| `Combine_FiveResults_AllSuccess` | 34.15 ns | 0 B |
| `Combine_FiveResults_MultipleFailures` | 85.37 ns | 448 B |

**Takeaway:** success is very cheap; the expensive cases are the ones doing real work to aggregate many failures.

### `Maybe<T>` and lightweight helpers

Basic `Maybe<T>` operations measure about **0.90-0.93 ns** with no allocation. Actor ownership checks measure **0.92-0.94 ns**, also without allocation.

Treat sub-nanosecond results as observations of these benchmark shapes, not a guarantee of zero execution cost.

## A benchmark-shaped example

This is the kind of code benchmarked by the ROP vs imperative comparison:

```csharp
using Trellis;
using Trellis.Primitives;

string RopStyle() =>
    FirstName.TryCreate("Xavier")
        .Combine(EmailAddress.TryCreate("xavier@somewhere.com"))
        .Match(
            onSuccess: values => values.Item1 + " " + values.Item2,
            onFailure: error => error.Detail);
```

## Why async numbers look bigger

Async benchmark numbers include `Task` / `ValueTask` machinery and the shape of the delegate being executed. Single successful `BindAsync` and `MapAsync` workloads measure **25.53 ns / 240 B** and **24.62 ns / 232 B**, respectively.

That does **not** mean Trellis suddenly became slow. It means async has a baseline cost even before your database or HTTP client does anything useful.

## Reproducing the results locally

The recorded run used **BenchmarkDotNet 0.16.0-preview.2** in an isolated runner. The checked-in benchmark project now uses that same version and preserves the host's active power plan with a `DontEnforcePowerPlan()` job mutator. Its benchmark toolchain builds are non-packable to avoid applying NuGet package-baseline checks to the generated executable; normal repository package validation is unchanged. Run the commands below from the repository root without changing dependencies. The recorded run used the Balanced power plan.

Run everything, including the parameterized tracing benchmarks:

```powershell
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*'
```

Run a focused slice:

```powershell
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*Map*'
```

A few good habits when you compare your own runs:

- use **Release** builds
- close other heavy workloads if possible
- compare results on the **same machine**
- keep the benchmark shape constant while you experiment
- keep power settings and listener configuration consistent

## What to optimize first

If your app is slow, start here before blaming Trellis:

1. database round trips
2. network calls
3. serialization
4. repeated allocations in your own code
5. logging volume

Only after that should you spend time shaving nanoseconds off pipeline composition.

## Bottom line

The benchmark suite tells a consistent story:

- Single successful `Bind`, `Map`, and `Tap` workloads measure **about 4-5 ns**.
- Success paths are often **allocation-free**.
- Complete pipelines can allocate on either track; the basic ROP comparisons allocate more than the imperative cases.
- Compared to real application I/O, the framework cost is usually negligible.

If you want decision guidance instead of raw numbers, go back to [Performance](performance.md).
