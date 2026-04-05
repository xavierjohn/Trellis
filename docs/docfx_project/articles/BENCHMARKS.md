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
| OS | Windows 11 (25H2) |
| .NET | 10.0.3 (SDK 10.0.103) |
| BenchmarkDotNet | 0.15.8 |
| Job | `ShortRun` |
| Memory diagnostics | Enabled |

The benchmark project is:

```text
Trellis.Benchmark\Trellis.Benchmark.csproj
```

## How to read the results

A few rules make the numbers easier to interpret:

1. **Look at relative difference first.** A 4 ns gap is real in a microbenchmark and often irrelevant in a web request.
2. **Look at allocations next.** Allocation-free success paths matter more than tiny timing differences.
3. **Treat benchmark families differently.** `Map`, `Tap`, and `Bind` measure framework overhead. Async and object-creation benchmarks measure framework cost plus your workload shape.

## Headline results

### ROP vs imperative code

| Method | Mean | Allocated |
| --- | ---: | ---: |
| `RopStyleHappy` | 98.32 ns | 296 B |
| `IfStyleHappy` | 93.86 ns | 296 B |
| `RopStyleSad` | 65.63 ns | 336 B |
| `IfStyleSad` | 75.08 ns | 336 B |
| `RopSample1` | 635.27 ns | 3848 B |
| `IfSample1` | 630.80 ns | 3848 B |

### What that means

- The basic happy path shows Trellis roughly **4-5 ns** slower in this run.
- The sad path is effectively a wash and was slightly faster for Trellis in this run.
- Larger pipelines keep the same overall story: **very small framework cost, identical allocations**.

> [!TIP]
> If you ever see a different number in older benchmark comments or documents, that is expected. CPUs, runtime builds, and JIT behavior change. The consistent conclusion in this repo is that Trellis overhead stays very small.

## Operation-by-operation results

### `Bind`

Use `Bind` when the next step also returns a `Result<T>`.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Bind_SingleChain_Success` | 4.85 ns | 0 B |
| `Bind_SingleChain_Failure` | 3.75 ns | 0 B |
| `Bind_ThreeChains_AllSuccess` | 14.79 ns | 0 B |
| `Bind_FiveChains_Success` | 33.84 ns | 0 B |
| `Bind_ThreeChains_FailAtSecond` | 34.65 ns | 152 B |

**Takeaway:** `Bind` scales cleanly and short-circuits immediately on failure.

### `Map`

Use `Map` when you are transforming the value but keeping success/failure structure unchanged.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Map_SingleTransformation_Success` | 3.24 ns | 0 B |
| `Map_ThreeTransformations_Success` | 12.13 ns | 0 B |
| `Map_FiveTransformations_Success` | 28.74 ns | 0 B |
| `Map_ComplexTransformation` | 21.48 ns | 80 B |
| `Map_ToComplexObject` | 27.10 ns | 144 B |

**Takeaway:** plain mapping is extremely cheap. Allocations usually come from the object you create, not from Trellis itself.

### `Tap`

Use `Tap` for side effects that should not change the value.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Tap_SingleAction_Success` | 2.88 ns | 0 B |
| `Tap_ThreeActions_Success` | 14.84 ns | 64 B |
| `Tap_WithLogging_Success` | 33.03 ns | 64 B |
| `Tap_FiveActions_Success` | 23.90 ns | 128 B |

**Takeaway:** `Tap` is cheap enough for ordinary logging and bookkeeping.

### `Ensure`

Use `Ensure` to keep a success value only when a predicate passes.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Ensure_SinglePredicate_Pass` | 12.06 ns | 152 B |
| `Ensure_SinglePredicate_Fail` | 11.98 ns | 152 B |
| `Ensure_ThreePredicates_AllPass` | 54.83 ns | 456 B |
| `Ensure_FivePredicates_AllPass` | 106.16 ns | 760 B |

**Takeaway:** `Ensure` is still fast, but it is the place where error allocation becomes visible — which is exactly what you would expect from validation code.

### `Combine`

Use `Combine` when several validations can run independently and you want all failures back together.

| Benchmark | Mean | Allocated |
| --- | ---: | ---: |
| `Combine_TwoResults_BothSuccess` | 7.27 ns | 0 B |
| `Combine_TwoResults_BothFailure` | 15.41 ns | 32 B |
| `Combine_ThreeResults_AllSuccess` | 14.68 ns | 0 B |
| `Combine_FiveResults_AllSuccess` | 58.08 ns | 0 B |
| `Combine_FiveResults_MultipleFailures` | 628.96 ns | 2536 B |

**Takeaway:** success is very cheap; the expensive cases are the ones doing real work to aggregate many failures.

### `Maybe<T>` and zero-cost helpers

The benchmark suite also shows `Maybe<T>` helper operations and simple actor checks effectively disappearing into JIT noise.

That is exactly what you want from infrastructure primitives: they should not dominate the profile.

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

Async benchmark numbers are always larger because they include `Task` / `ValueTask` machinery and often the shape of the delegate being executed.

That does **not** mean Trellis suddenly became slow. It means async has a baseline cost even before your database or HTTP client does anything useful.

## Reproducing the results locally

Run everything:

```bash
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release
```

Run a focused slice:

```bash
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter *Map*
```

A few good habits when you compare your own runs:

- use **Release** builds
- close other heavy workloads if possible
- compare results on the **same machine**
- keep the benchmark shape constant while you experiment

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

- Trellis pipeline operations are **small and predictable**.
- Success paths are often **allocation-free**.
- Failure paths pay for the errors they create, which is expected.
- Compared to real application I/O, the framework cost is usually negligible.

If you want decision guidance instead of raw numbers, go back to [Performance](performance.md).
