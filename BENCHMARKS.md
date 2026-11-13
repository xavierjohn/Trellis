# Performance Benchmarks

This document provides detailed performance analysis of the Trellis library using [BenchmarkDotNet](https://benchmarkdotnet.org/).

## Table of Contents

- [Overview](#overview)
- [Key Findings](#key-findings)
- [Benchmark Results](#benchmark-results)
  - [Railway-Oriented Programming vs Imperative Style](#railway-oriented-programming-vs-imperative-style)
  - [Bind Operations](#bind-operations)
  - [Map Operations](#map-operations)
  - [Tap Operations](#tap-operations)
  - [Ensure Operations](#ensure-operations)
  - [Match Operations](#match-operations)
  - [MapOnFailure Operations](#maponfailure-operations)
  - [Combine Operations](#combine-operations)
  - [Recover Operations](#recover-operations)
  - [Async Operations](#async-operations)
  - [Maybe Operations](#maybe-operations)
  - [Error Handling](#error-handling)
  - [Value Object Operations](#value-object-operations)
  - [Money Operations](#money-operations)
  - [Specification Operations](#specification-operations)
  - [Actor / Authorization](#actor--authorization)
  - [Tracing Overhead](#tracing-overhead)
- [Running Benchmarks](#running-benchmarks)
- [Interpreting Results](#interpreting-results)

## Overview

The Trellis library is designed with performance in mind. All benchmarks are run using BenchmarkDotNet with memory diagnostics enabled to track both execution time and memory allocations.

**Test Environment:**
- **Measured**: November 12, 2026, source revision `5902bb82`
- **CPU**: AMD Ryzen 9 9900X 4.40GHz (12 physical / 24 logical cores)
- **OS**: Windows 11 (25H2), build 26200.9974
- **.NET**: 11.0.0 (SDK 11.0.100), X64 RyuJIT x86-64-v4
- **GC**: Concurrent Workstation
- **BenchmarkDotNet**: v0.16.0-preview.2, isolated runner compiling the unchanged benchmark workloads
- **Job**: ShortRun (3 iterations, 1 launch, 3 warmup)
- **Power plan**: Balanced; automatic power-plan changes disabled
- **Memory Diagnostics**: Enabled (Gen0, Gen1, Gen2, Allocations)
- **Coverage**: 236 parameterized cases across 17 benchmark classes

The run used a shared host and three measured iterations per case, so treat small timing differences cautiously. `Error`, where shown, is the half-width of the 99.9% confidence interval; `Gen0` is collections per 1,000 operations, and `-` under `Allocated` means no managed allocation was measured.

## Key Findings

### ✅ Low Single-Step Cost
Single successful `Bind`, `Map`, and `Tap` benchmarks measure **~4-5 ns** with no managed allocations. Complete pipeline costs depend on validation, callbacks, tracing, and the objects being created.

### ✅ Explicit Allocation Costs
The basic ROP comparisons allocate **136 B more** than their imperative equivalents. The larger pipeline comparison allocates **632 B** in both styles. Allocation-free primitive operations do not imply that every complete pipeline is allocation-free.

### ✅ Success Path Optimizations
Success path operations are highly optimized with minimal allocations and fast execution times. Most operations (Map, Tap, Bind) allocate zero bytes on success paths.

### ✅ Error Path Short-Circuiting
Failure paths skip downstream success callbacks. Error creation and aggregation still have measurable costs: combining two existing failures takes **22.09 ns / 96 B**, while the five-result multiple-failure benchmark takes **85.37 ns / 448 B**.

### ✅ Lightweight Optionality and Ownership Checks
Basic `Maybe<T>` operations measure **~0.9 ns** with zero allocations. `Actor.IsOwner` measures **~0.92-0.94 ns**, also without allocations. Sub-nanosecond results are benchmark observations, not a guarantee of zero execution cost.

## Benchmark Results

### Railway-Oriented Programming vs Imperative Style

Comparison of ROP style vs traditional if-style code for the same logic.

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|
| RopStyleHappy | 90.49 ns | 25.816 ns | 1.415 ns | 0.0167 | 280 B |
| IfStyleHappy | 69.78 ns | 11.771 ns | 0.645 ns | 0.0086 | 144 B |
| RopStyleSad | 86.54 ns | 24.633 ns | 1.350 ns | 0.0229 | 384 B |
| IfStyleSad | 59.15 ns | 0.967 ns | 0.053 ns | 0.0148 | 248 B |
| RopSample1 | 308.12 ns | 45.790 ns | 2.510 ns | 0.0377 | 632 B |
| IfSample1 | 213.45 ns | 36.471 ns | 1.999 ns | 0.0377 | 632 B |

**Analysis:**
- The observed happy-path mean gap is **20.71 ns** (90.49 ns ROP vs 69.78 ns imperative).
- The observed sad-path mean gap is **27.39 ns** (86.54 ns vs 59.15 ns).
- Basic ROP cases allocate **136 B more**: 280 B vs 144 B on success, and 384 B vs 248 B on failure.
- The larger pipeline means are **308.12 ns vs 213.45 ns**, a **94.67 ns** gap, with **632 B** allocated by each.
- These are complete workload measurements, not a fixed per-operation overhead. ShortRun confidence intervals limit conclusions about timing differences.

**Example Code:**

```csharp
// ROP Style
FirstName.TryCreate("Xavier")
    .Combine(EmailAddress.TryCreate("xavier@somewhere.com"))
    .Match(
        onSuccess: ok => ok.Item1 + " " + ok.Item2,
        onFailure: error => error.Detail
    );

// Imperative Style (equivalent logic)
var rFirstName = FirstName.TryCreate("Xavier");
var rEmailAddress = EmailAddress.TryCreate("xavier@somewhere.com");
if (rFirstName.TryGetValue(out var firstName) && rEmailAddress.TryGetValue(out var emailAddress))
    return firstName + " " + emailAddress;

Error? error = null;
if (rFirstName.IsFailure)
    error = rFirstName.Error;
if (rEmailAddress.IsFailure)
    error = error is null ? rEmailAddress.Error : error.Combine(rEmailAddress.Error);
return error!.Detail;
```

### Bind Operations

Testing sequential operations with transformations.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| Bind_SingleChain_Success | 4.82 ns | - | - |
| Bind_SingleChain_Failure | 4.14 ns | - | - |
| Bind_ThreeChains_AllSuccess | 16.95 ns | - | - |
| Bind_ThreeChains_FailAtFirst | 11.72 ns | - | - |
| Bind_ThreeChains_FailAtSecond | 16.16 ns | 0.0033 | 56 B |
| Bind_TypeTransformation | 13.17 ns | 0.0024 | 40 B |
| Bind_FiveChains_Success | 39.50 ns | - | - |
| Bind_WithComplexOperation_Success | 18.24 ns | - | - |
| Bind_WithComplexOperation_Failure | 12.28 ns | - | - |

**Key Insights:**
- **Single Bind**: 4.82 ns with zero allocations
- **Chaining**: Three successful Binds take 16.95 ns; five take 39.50 ns
- **Short-circuit on failure**: Failed results propagate immediately without executing downstream functions
- **Type transformation**: The int-to-string workload takes 13.17 ns and allocates 40 B
- **Failure creation**: Failing at the second step takes 16.16 ns and allocates 56 B

### Map Operations

Testing value transformations without changing the Result context.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| Map_SingleTransformation_Success | 4.02 ns | - | - |
| Map_SingleTransformation_Failure | 4.95 ns | - | - |
| Map_ThreeTransformations_Success | 13.01 ns | - | - |
| Map_ThreeTransformations_Failure | 12.06 ns | - | - |
| Map_TypeConversion_IntToString | 4.66 ns | - | - |
| Map_ComplexTransformation | 18.21 ns | 0.0048 | 80 B |
| Map_MathematicalOperations | 17.65 ns | - | - |
| Map_FiveTransformations_Success | 26.94 ns | - | - |
| Map_StringManipulation | 23.78 ns | 0.0048 | 80 B |
| Map_WithComplexCalculation | 15.15 ns | - | - |
| Map_ToComplexObject | 31.88 ns | 0.0086 | 144 B |

**Key Insights:**
- **Single transformation**: 4.02 ns with zero allocations
- **Chaining**: Three successful maps take 13.01 ns; five take 26.94 ns
- **Type conversion**: The measured int-to-string workload takes 4.66 ns with no allocation
- **Complex transformations**: Object-creating workloads take 18.21-31.88 ns and allocate 80-144 B
- **Allocation depends on the delegate**: Simple value transformations allocate nothing in these benchmarks
- Ideal for type conversions and simple transformations in hot paths

### Tap Operations

Testing side effects without changing the Result.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| Tap_SingleAction_Success | 3.86 ns | - | - |
| Tap_SingleAction_Failure | 3.87 ns | - | - |
| Tap_ThreeActions_Success | 12.46 ns | - | - |
| Tap_ThreeActions_Failure | 10.28 ns | - | - |
| Tap_WithLogging_Success | 31.22 ns | - | - |
| TapOnFailure_OnFailure | 7.79 ns | - | - |
| TapOnFailure_OnSuccess | 7.65 ns | - | - |
| Tap_MixedWithMap_Success | 24.45 ns | - | - |
| Tap_ComplexSideEffect_Success | 16.16 ns | - | - |
| Tap_FiveActions_Success | 24.89 ns | - | - |
| Tap_WithBind_Success | 19.88 ns | - | - |

**Key Insights:**
- **Single action**: 3.86 ns on success and 3.87 ns on failure
- **Zero allocations in this family**: All synchronous Tap workloads in this table allocate 0 B
- **Logging-shaped workload**: 31.22 ns; this does not measure a real logging sink or I/O
- **Composition**: Mixed with Map at 24.45 ns and Bind at 19.88 ns
- **Workload matters**: The separate tracing benchmark's capturing Tap callback allocates even without a Trellis listener
- Ideal for debugging, logging, caching, and notification scenarios without impacting main flow

**Use Case:**
```csharp
await GetUserAsync(userId)
    .TapAsync(user => _logger.LogInformation("Retrieved user {UserId}", user.Id))
    .TapAsync(user => _cache.SetAsync(user.Id, user))
    .TapOnFailureAsync(error => _logger.LogError("Failed to get user: {Error}", error));
```

### Ensure Operations

Testing validation/guard clauses.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| Ensure_SinglePredicate_Pass | 6.33 ns | 0.0033 | 56 B |
| Ensure_SinglePredicate_Fail | 6.44 ns | 0.0033 | 56 B |
| Ensure_SinglePredicate_OnFailureResult | 6.21 ns | 0.0033 | 56 B |
| Ensure_ThreePredicates_AllPass | 19.55 ns | 0.0100 | 168 B |
| Ensure_ThreePredicates_FailAtSecond | 18.96 ns | 0.0100 | 168 B |
| Ensure_ComplexPredicate_Pass | 6.38 ns | 0.0033 | 56 B |
| Ensure_ComplexPredicate_Fail | 6.32 ns | 0.0033 | 56 B |
| Ensure_WithExpensiveValidation_Pass | 32.91 ns | 0.0033 | 56 B |
| Ensure_WithExpensiveValidation_Fail | 34.10 ns | 0.0033 | 56 B |
| Ensure_ComplexObject_MultipleRules | 23.21 ns | 0.0124 | 208 B |
| Ensure_FivePredicates_AllPass | 41.73 ns | 0.0167 | 280 B |
| Ensure_MixedWithMapAndBind | 25.46 ns | 0.0067 | 112 B |

**Key Insights:**
- **Single validation**: 6.33 ns on pass, 6.44 ns on failure, with 56 B allocated in either case
- **Eager errors**: These benchmarks construct errors before calling `Ensure`, so even passing or already-failed results pay that allocation
- **Chaining**: Three passing predicates take 19.55 ns / 168 B; five take 41.73 ns / 280 B
- **Expensive predicates**: The loop-based validations take 32.91-34.10 ns
- **Composition**: Mixed with Map and Bind at 25.46 ns / 112 B
- **Short-circuiting**: Failed validations skip subsequent predicates, not eagerly evaluated error arguments

**Use Case:**
```csharp
customer.CanBePromoted()
    .Ensure(c => c.TotalPurchases > 1000, Error.InvalidInput.ForRule(code: "customer.minimum-purchase", detail: "Minimum purchase requirement"))
    .Ensure(c => c.AccountAge > TimeSpan.FromDays(90), Error.InvalidInput.ForRule(code: "customer.account-age", detail: "Account age requirement"))
    .Tap(c => c.Promote());
```

### Match Operations

Testing pattern matching on success/failure paths.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| Match_Success | 9.88 ns | 0.0067 | 112 B |
| Match_Failure | 9.85 ns | 0.0067 | 112 B |
| Match_Success_TypePreserved | 9.46 ns | 0.0067 | 112 B |
| Match_Failure_TypePreserved | 10.22 ns | 0.0067 | 112 B |
| Switch_Success | 13.01 ns | 0.0134 | 224 B |
| Switch_Failure | 12.95 ns | 0.0134 | 224 B |
| Match_AfterPipeline_Success | 32.36 ns | 0.0124 | 208 B |
| Match_AfterPipeline_Failure | 22.91 ns | 0.0134 | 224 B |

**Key Insights:**
- **Match workloads**: 9.46-10.22 ns with 112 B allocated in these cases
- **Switch workloads**: 12.95-13.01 ns with 224 B allocated
- **Pipeline integration**: 22.91-32.36 ns with 208-224 B allocated
- **Not allocation-free**: The current measurements include the complete callback and result-consumption paths
- Ideal for final result consumption at the end of ROP pipelines

### MapOnFailure Operations

Testing error transformation on the failure track.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| MapOnFailure_OnSuccess | 4.31 ns | - | - |
| MapOnFailure_OnFailure | 47.90 ns | 0.0129 | 216 B |
| MapOnFailure_ChangeErrorType | 7.38 ns | 0.0048 | 80 B |
| MapOnFailure_ChainedOnSuccess | 8.26 ns | - | - |
| MapOnFailure_ChainedOnFailure | 99.66 ns | 0.0272 | 456 B |
| MapOnFailure_InPipeline_Success | 13.02 ns | 0.0033 | 56 B |
| MapOnFailure_InPipeline_Failure | 59.12 ns | 0.0172 | 288 B |

**Key Insights:**
- **No-op on success**: 4.31 ns with zero allocation
- **Error transformation**: The basic failure transformation takes 47.90 ns / 216 B; changing the error type takes 7.38 ns / 80 B
- **Chaining**: The chained failure workload takes 99.66 ns / 456 B
- Perfect for translating domain errors to API-layer errors at composition boundaries

### Combine Operations

Testing parallel result aggregation for validation scenarios.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| Combine_TwoResults_BothSuccess | 4.14 ns | - | - |
| Combine_TwoResults_FirstFailure | 5.60 ns | - | - |
| Combine_TwoResults_SecondFailure | 5.16 ns | - | - |
| Combine_TwoResults_BothFailure | 22.09 ns | 0.0057 | 96 B |
| Combine_ThreeResults_AllSuccess | 16.39 ns | - | - |
| Combine_ThreeResults_OneFailure | 18.51 ns | - | - |
| Combine_ThreeResults_TwoFailures | 33.93 ns | 0.0057 | 96 B |
| Combine_DifferentTypes_BothSuccess | 5.26 ns | - | - |
| Combine_DifferentTypes_OneFailure | 8.31 ns | - | - |
| Combine_WithBind_AllSuccess | 37.56 ns | 0.0033 | 56 B |
| Combine_WithBind_OneFailure | 11.86 ns | - | - |
| Combine_ValueObjects_AllValid | 120.26 ns | 0.0105 | 176 B |
| Combine_ValueObjects_OneInvalid | 121.54 ns | 0.0100 | 168 B |
| Combine_ValueObjects_AllInvalid | 101.59 ns | 0.0167 | 280 B |
| Combine_FiveResults_AllSuccess | 34.15 ns | - | - |
| Combine_FiveResults_OneFailure | 50.62 ns | 0.0033 | 56 B |
| Combine_FiveResults_MultipleFailures | 85.37 ns | 0.0267 | 448 B |
| CombineAsync_TwoResults_BothSuccess | 28.73 ns | 0.0153 | 256 B |
| CombineAsync_ThreeResults_AllSuccess | 42.36 ns | 0.0206 | 344 B |
| Combine_ComplexObject_AllValid | 129.59 ns | 0.0105 | 176 B |
| Combine_ComplexObject_WithValidation | 135.29 ns | 0.0138 | 232 B |
| Combine_WithUnit_Success | 5.25 ns | - | - |

**Key Insights:**
- **Success aggregation**: Two results combine in 4.14 ns; three in 16.39 ns; five in 34.15 ns, all without allocations
- **Failure aggregation**: Two failures take 22.09 ns / 96 B; the five-result multiple-failure workload takes 85.37 ns / 448 B
- **Value object validation**: The all-valid workload takes 120.26 ns / 176 B
- **Async variants**: Two and three successful results take 28.73 ns / 256 B and 42.36 ns / 344 B
- **No-payload support**: `Combine_WithUnit_Success` takes 5.25 ns with no allocation
- Object-creating success workloads allocate too; zero allocation applies to the simple existing-result combinations

**Use Case:**
```csharp
// Validate user registration
FirstName.TryCreate(request.FirstName)
    .Combine(LastName.TryCreate(request.LastName))
    .Combine(EmailAddress.TryCreate(request.Email))
    .Combine(Password.TryCreate(request.Password))
    .Bind((first, last, email, pwd) =>
        User.TryCreate(first, last, email, pwd));
```

### Recover Operations

Testing error recovery patterns.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| RecoverOnFailure_OnSuccess | 3.97 ns | - | - |
| RecoverOnFailure_OnFailure | 4.75 ns | - | - |
| RecoverOnFailure_OnFailure_WithErrorAccess | 4.95 ns | - | - |
| RecoverOnFailure_WithPredicate_Match | 5.18 ns | - | - |
| RecoverOnFailure_WithPredicate_NoMatch | 4.52 ns | - | - |
| RecoverOnFailure_WithPredicate_AndErrorAccess_Match | 5.38 ns | - | - |
| RecoverOnFailure_Chain_TwoLevels | 13.19 ns | 0.0033 | 56 B |
| RecoverOnFailure_Chain_ThreeLevels | 21.01 ns | 0.0067 | 112 B |
| RecoverOnFailure_WithComplexRecovery | 5.16 ns | - | - |
| RecoverOnFailure_Multiple_DifferentErrorTypes | 22.41 ns | - | - |
| RecoverOnFailure_WithExpensiveRecovery | 25.55 ns | - | - |
| RecoverOnFailure_MixedWithBind_Success | 13.90 ns | - | - |
| RecoverOnFailure_MixedWithBind_Failure | 13.79 ns | - | - |
| RecoverOnFailure_WithDefaultValue | 5.48 ns | - | - |
| RecoverOnFailure_TypeTransformation | 7.65 ns | 0.0033 | 56 B |
| RecoverOnFailure_NestedPredicates | 12.14 ns | - | - |

**Key Insights:**
- **No-op on success**: 3.97 ns with zero allocation
- **Simple recovery**: 4.75 ns, or 4.95 ns with error access
- **Predicate-based recovery**: 5.18 ns when the predicate matches and 4.52 ns when it does not
- **Mostly zero-allocation**: Only chained recovery and type transformations allocate
- **Selective recovery**: 22.41 ns for the multi-error-type workload
- Ideal for graceful degradation and fallback patterns

### Async Operations

Testing asynchronous operation performance.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| BindAsync_SingleChain_Success | 25.53 ns | 0.0143 | 240 B |
| BindAsync_SingleChain_Failure | 21.47 ns | 0.0095 | 160 B |
| BindAsync_ThreeChains_Success | 75.26 ns | 0.0430 | 720 B |
| BindAsync_ThreeChains_FailAtSecond | 77.70 ns | 0.0416 | 696 B |
| MapAsync_SingleTransformation_Success | 24.62 ns | 0.0139 | 232 B |
| MapAsync_ThreeTransformations_Success | 72.18 ns | 0.0416 | 696 B |
| TapAsync_SingleAction_Success | 317.72 ns | 0.0281 | 472 B |
| TapAsync_ThreeActions_Success | 923.80 ns | 0.0849 | 1422 B |
| EnsureAsync_SinglePredicate_Pass | 21.61 ns | 0.0129 | 216 B |
| EnsureAsync_ThreePredicates_AllPass | 55.03 ns | 0.0291 | 488 B |
| Mixed_AsyncOperations_Success | 512.65 ns | 0.0639 | 1069 B |
| BindAsync_WithDelay_Success | 15,466,286.98 ns | - | 784 B |
| BindAsync_FiveChains_Success | 124.42 ns | 0.0715 | 1200 B |
| BindAsync_TypeTransformation | 58.79 ns | 0.0339 | 568 B |
| TaskResult_BindAsync_Success | 64.06 ns | 0.0381 | 640 B |
| RecoverOnFailureAsync_OnFailure | 22.56 ns | 0.0143 | 240 B |
| FinallyAsync_OnSuccess | 17.72 ns | 0.0153 | 256 B |

**Key Insights:**
- **BindAsync/MapAsync**: 25.53 ns / 240 B and 24.62 ns / 232 B for single successful operations
- **Chaining**: Three successful async Binds take 75.26 ns; five take 124.42 ns
- **TapAsync**: The awaitable side-effect workloads take 317.72 ns for one action and 923.80 ns for three
- **FinallyAsync**: 17.72 ns / 256 B for this result-consumption workload
- **Delayed work**: `BindAsync_WithDelay_Success` takes 15.47 ms; that synthetic delay dominates the measured pipeline
- These totals include Task machinery and delegate work, not just the ROP operation

**Use Case:**
```csharp
await GetStudentAsync(studentId)
    .ParallelAsync(GetGradesAsync(studentId))
    .ParallelAsync(GetAttendanceAsync(studentId))
    .WhenAllAsync()
    .BindAsync((student, grades, attendance) =>
        GenerateReportAsync(student, grades, attendance));
```

### Maybe Operations

Testing optional value handling — `Maybe<T>` is a `readonly struct`.

| Method | Mean | Allocated |
|---|---|---|
| HasValue_WithValue | 0.91 ns | - |
| HasValue_Empty | 0.91 ns | - |
| HasNoValue_WithValue | 0.91 ns | - |
| HasNoValue_Empty | 0.91 ns | - |
| GetValueOrDefault_WithValue | 0.91 ns | - |
| GetValueOrDefault_Empty | 0.91 ns | - |
| TryGetValue_WithValue | 0.90 ns | - |
| TryGetValue_Empty | 0.91 ns | - |
| From_Value | 0.91 ns | - |
| None_Creation | 0.91 ns | - |
| ImplicitConversion_FromValue | 0.91 ns | - |
| ImplicitConversion_FromNull | 0.91 ns | - |
| Equality_BothWithSameValue | 0.90 ns | - |
| Equality_BothEmpty | 0.90 ns | - |
| Equality_OneEmptyOneWithValue | 0.92 ns | - |
| GetHashCode_WithValue | 0.91 ns | - |
| GetHashCode_Empty | 0.91 ns | - |
| ToString_WithValue | 0.91 ns | - |
| ToString_Empty | 0.93 ns | - |
| ToResult_WithValue | 3.79 ns | 56 B |
| ToResult_Empty | 3.88 ns | 56 B |
| Optional_WithNullValue | 0.91 ns | - |
| Optional_WithValue | 0.92 ns | - |
| Equals_WithObject | 0.92 ns | - |
| Equals_WithSameValue | 0.91 ns | - |
| CreateComplexMaybe | 21.51 ns | 40 B |

**Key Insights:**
- **Basic operations**: Approximately 0.90-0.93 ns in this run
- **True zero-allocation**: All basic operations (HasValue, equality, GetHashCode) allocate nothing
- **ToResult workloads**: 3.79-3.88 ns with 56 B allocated
- **Complex creation**: `CreateComplexMaybe` takes 21.51 ns / 40 B
- Treat sub-nanosecond timings as observations of these workloads, not a promise that every use is free

### Error Handling

Testing structured error creation, equality, hashing, and formatting. Error aggregation is covered by the Combine benchmarks.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| CreateValidationError_Simple | 26.03 ns | 0.0134 | 224 B |
| CreateNotFoundError | 2.44 ns | 0.0033 | 56 B |
| CreateUnauthorizedError | 2.24 ns | 0.0029 | 48 B |
| CreateConflictError | 3.29 ns | 0.0048 | 80 B |
| CreateInternalServerError | 40.66 ns | 0.0081 | 136 B |
| CreateValidationError_MultipleFields | 58.93 ns | 0.0263 | 440 B |
| CreateValidationError_SingleFieldMultipleMessages | 59.92 ns | 0.0263 | 440 B |
| ErrorEquality_SameKind | 0.91 ns | - | - |
| ErrorEquality_DifferentKind | 2.20 ns | - | - |
| GetHashCode_ValidationError | 43.42 ns | - | - |
| GetHashCode_ComplexValidationError | 92.39 ns | - | - |
| ToString_SimpleError | 81.65 ns | 0.0803 | 1344 B |
| ToString_ComplexValidationError | 83.06 ns | 0.0803 | 1344 B |
| CreateErrorFromException | 43.18 ns | 0.0081 | 136 B |
| IsErrorType_UnprocessableContent | 0.92 ns | - | - |
| IsErrorType_NotFound | 0.92 ns | - | - |

**Key Insights:**
- **Simple errors**: NotFound, Unauthorized, and Conflict creation take 2.24-3.29 ns and allocate 48-80 B
- **Validation workloads**: 26.03 ns / 224 B for the simple case, and 58.93-59.92 ns / 440 B for multi-violation cases
- **Equality**: 0.91-2.20 ns with no allocation
- **Type checks**: Approximately 0.92 ns with no allocation
- **Formatting**: `ToString` workloads take 81.65-83.06 ns and allocate 1,344 B
- Error creation, hashing, and formatting have different costs; measure the path your application actually uses

### Value Object Operations

Testing DDD `ValueObject` comparison and sorting.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| CompareTo_Equal | 21.07 ns | - | - |
| CompareTo_Different | 12.70 ns | - | - |
| Sort_100_ValueObjects | 9,981.23 ns | 0.0458 | 824 B |

**Key Insights:**
- **ValueObject.CompareTo**: 21.07 ns for equal values and 12.70 ns for different values, with no allocation
- **Sorting 100 objects**: 9.98 microseconds / 824 B for the complete sorting workload
- The sorting total includes many comparisons and collection work; it is not a per-comparison timing

### Money Operations

Testing `Money` value object arithmetic (multi-currency with ISO 4217).

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| Create_Valid | 46.90 ns | 0.0186 | 312 B |
| Create_ZeroDecimalCurrency | 51.40 ns | 0.0186 | 312 B |
| Add_SameCurrency | 50.65 ns | 0.0186 | 312 B |
| Subtract_SameCurrency | 56.05 ns | 0.0186 | 312 B |
| Multiply_Decimal | 51.07 ns | 0.0186 | 312 B |
| Multiply_Integer | 48.85 ns | 0.0186 | 312 B |
| Divide_Decimal | 73.47 ns | 0.0186 | 312 B |
| Divide_Integer | 49.03 ns | 0.0186 | 312 B |
| Add_DifferentCurrency_Fails | 98.05 ns | 0.0362 | 608 B |
| Subtract_DifferentCurrency_Fails | 122.61 ns | 0.0367 | 616 B |
| IsGreaterThan | 5.71 ns | - | - |
| IsLessThan | 5.70 ns | - | - |
| Allocate_ThreeWay | 99.11 ns | 0.0210 | 352 B |
| Allocate_EvenSplit | 138.58 ns | 0.0238 | 400 B |
| ArithmeticPipeline | 154.19 ns | 0.0415 | 696 B |

**Key Insights:**
- **Creation**: 46.90-51.40 ns / 312 B
- **Arithmetic**: 48.85-73.47 ns / 312 B for successful arithmetic workloads
- **Currency mismatch**: 98.05 ns / 608 B for addition and 122.61 ns / 616 B for subtraction
- **Comparison**: Approximately 5.70 ns with no allocation
- **Allocation workloads**: Three-way split at 99.11 ns / 352 B; even split at 138.58 ns / 400 B
- **Full pipeline**: 154.19 ns / 696 B

### Specification Operations

Testing DDD Specification pattern (expression trees, composition).

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| IsSatisfiedBy_Simple_Pass | 3.31 ns | - | - |
| IsSatisfiedBy_Simple_Fail | 3.38 ns | - | - |
| IsSatisfiedBy_And_Pass | 6.82 ns | - | - |
| IsSatisfiedBy_And_Fail | 4.57 ns | - | - |
| IsSatisfiedBy_Or_Pass | 4.56 ns | - | - |
| IsSatisfiedBy_Or_Fail | 6.32 ns | - | - |
| IsSatisfiedBy_Not | 3.13 ns | - | - |
| IsSatisfiedBy_Complex | 6.02 ns | - | - |
| ToExpression_Simple | 241.07 ns | 0.0501 | 840 B |
| ToExpression_And | 844.26 ns | 0.1507 | 2536 B |
| ToExpression_Complex | 2,038.67 ns | 0.3586 | 6032 B |
| Filter_100_Orders | 385,763.66 ns | - | 6536 B |
| Filter_100_Orders_Composed | 452,977.99 ns | 0.4883 | 13176 B |

**Key Insights:**
- **Cached evaluation**: `IsSatisfiedBy` reuses a lazily compiled delegate by default. Steady-state evaluations take 3.13-6.82 ns with no allocation; first-use compilation is not represented by these warmed-up means.
- **Expression construction**: `ToExpression` takes 241.07 ns / 840 B for the simple case and 2.04 microseconds / 6,032 B for the complex case.
- **Filtering workloads**: 385.76-452.98 microseconds for 100 records. These benchmarks explicitly call `ToExpression().Compile()` on every invocation, so compilation is included.
- **Hot-loop guidance**: Reuse specification instances for cached `IsSatisfiedBy` evaluation, or reuse a compiled delegate rather than repeatedly compiling it.
- **EF Core boundary**: Passing the expression to EF Core avoids this explicit in-memory `Compile` call; query translation and execution are not measured here.

### Actor / Authorization

Testing `Actor` permission checking (HashSet-backed).

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| HasPermission_Found | 2.01 ns | - | - |
| HasPermission_NotFound | 1.51 ns | - | - |
| HasPermission_Forbidden | 1.91 ns | - | - |
| HasPermission_Scoped | 15.16 ns | 0.0038 | 64 B |
| HasAllPermissions_AllPresent | 5.78 ns | - | - |
| HasAllPermissions_SomeMissing | 11.33 ns | 0.0057 | 96 B |
| HasAnyPermission_OnePresent | 10.21 ns | 0.0057 | 96 B |
| HasAnyPermission_NonePresent | 13.01 ns | 0.0057 | 96 B |
| HasPermission_LargeSet_50_Found | 5.78 ns | - | - |
| HasPermission_LargeSet_500_Found | 5.65 ns | - | - |
| HasPermission_LargeSet_500_NotFound | 4.82 ns | - | - |
| IsOwner_Match | 0.92 ns | - | - |
| IsOwner_NoMatch | 0.94 ns | - | - |
| Create_Simple | 103.83 ns | 0.0449 | 752 B |

**Key Insights:**
- **Simple permission lookup**: 2.01 ns found and 1.51 ns not found, with no allocation
- **Larger sets**: Found lookups take 5.78 ns for 50 permissions and 5.65 ns for 500 permissions
- **Ownership checks**: 0.92-0.94 ns with no allocation
- **Scoped permissions**: 15.16 ns / 64 B
- **Batch checks**: 5.78-13.01 ns; the missing/any-permission workloads allocate 96 B
- **Actor creation**: 103.83 ns / 752 B
- Zero allocations for all simple permission checks

### Tracing Overhead

Testing five workloads at depths 1, 5, and 10, with and without a Trellis activity listener. A separate ambient request activity is present in every case. `ListenerOn=True` samples every Trellis activity; exporter and network costs are not included.

| Method | ListenerOn | Depth | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|---|---|
| JustOk | False | 1 | 4.81 ns | 0.269 ns | 0.015 ns | - | - |
| BindChain_AllSuccess | False | 1 | 9.95 ns | 2.044 ns | 0.112 ns | - | - |
| BindChain_FailAtFirst | False | 1 | 7.46 ns | 0.640 ns | 0.035 ns | - | - |
| MapChain_AllSuccess | False | 1 | 7.36 ns | 0.559 ns | 0.031 ns | - | - |
| TapChain_AllSuccess | False | 1 | 11.19 ns | 0.438 ns | 0.024 ns | 0.0053 | 88 B |
| JustOk | False | 5 | 17.49 ns | 2.352 ns | 0.129 ns | - | - |
| BindChain_AllSuccess | False | 5 | 41.27 ns | 11.609 ns | 0.636 ns | - | - |
| BindChain_FailAtFirst | False | 5 | 27.16 ns | 5.095 ns | 0.279 ns | - | - |
| MapChain_AllSuccess | False | 5 | 27.13 ns | 3.866 ns | 0.212 ns | - | - |
| TapChain_AllSuccess | False | 5 | 33.44 ns | 5.857 ns | 0.321 ns | 0.0052 | 88 B |
| JustOk | False | 10 | 33.92 ns | 1.272 ns | 0.070 ns | - | - |
| BindChain_AllSuccess | False | 10 | 86.05 ns | 10.164 ns | 0.557 ns | - | - |
| BindChain_FailAtFirst | False | 10 | 57.41 ns | 3.887 ns | 0.213 ns | - | - |
| MapChain_AllSuccess | False | 10 | 56.30 ns | 2.171 ns | 0.119 ns | - | - |
| TapChain_AllSuccess | False | 10 | 67.06 ns | 9.482 ns | 0.520 ns | 0.0052 | 88 B |
| JustOk | True | 1 | 4.79 ns | 0.855 ns | 0.047 ns | - | - |
| BindChain_AllSuccess | True | 1 | 94.34 ns | 9.618 ns | 0.527 ns | 0.0238 | 400 B |
| BindChain_FailAtFirst | True | 1 | 113.28 ns | 11.650 ns | 0.639 ns | 0.0310 | 520 B |
| MapChain_AllSuccess | True | 1 | 87.40 ns | 7.896 ns | 0.433 ns | 0.0238 | 400 B |
| TapChain_AllSuccess | True | 1 | 90.99 ns | 5.686 ns | 0.312 ns | 0.0291 | 488 B |
| JustOk | True | 5 | 17.25 ns | 5.251 ns | 0.288 ns | - | - |
| BindChain_AllSuccess | True | 5 | 487.04 ns | 61.727 ns | 3.383 ns | 0.1192 | 2000 B |
| BindChain_FailAtFirst | True | 5 | 564.98 ns | 148.420 ns | 8.135 ns | 0.1545 | 2600 B |
| MapChain_AllSuccess | True | 5 | 457.38 ns | 16.815 ns | 0.922 ns | 0.1192 | 2000 B |
| TapChain_AllSuccess | True | 5 | 445.85 ns | 17.538 ns | 0.961 ns | 0.1245 | 2088 B |
| JustOk | True | 10 | 33.76 ns | 14.615 ns | 0.801 ns | - | - |
| BindChain_AllSuccess | True | 10 | 970.75 ns | 93.517 ns | 5.126 ns | 0.2384 | 4000 B |
| BindChain_FailAtFirst | True | 10 | 1,124.20 ns | 93.228 ns | 5.110 ns | 0.3090 | 5200 B |
| MapChain_AllSuccess | True | 10 | 914.93 ns | 76.966 ns | 4.219 ns | 0.2384 | 4000 B |
| TapChain_AllSuccess | True | 10 | 868.52 ns | 130.131 ns | 7.133 ns | 0.2441 | 4088 B |

**Key Insights:**
- With no Trellis listener, depth-10 successful Bind and Map chains take 86.05 ns and 56.30 ns without allocations.
- Sampling every activity raises those means to 970.75 ns and 914.93 ns, with 4,000 B allocated by each depth-10 chain.
- `JustOk` allocates nothing with either listener setting; it measures construction under a foreign ambient activity, not creation of a Trellis activity.
- The Tap workload captures a local counter and allocates 88 B even without a Trellis listener.

## Running Benchmarks

The recorded run used an isolated runner with **BenchmarkDotNet 0.16.0-preview.2**, which recognizes .NET 11. The repository still pins 0.15.8, which does not recognize this runtime; use a .NET 11-compatible BenchmarkDotNet version before using the commands below.

The isolated runner compiled the same benchmark source files and used the same `ShortRun` job, with a mutator to preserve the host's Balanced power plan:

```csharp
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.ShortRun)
    .AddJob(Job.Default.DontEnforcePowerPlan().AsMutator());
```

Once using a compatible runner:

```powershell
# Run all classes, including parameterized tracing benchmarks
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*'

# Run specific benchmark
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*Combine*'

# Run with specific options
dotnet run --project Trellis.Benchmark\Trellis.Benchmark.csproj -c Release -- --filter '*ROP*' --memory
```

**Benchmark Classes:**
- `BenchmarkROP` — Core ROP vs imperative comparisons
- `BindBenchmarks` — Sequential transformations
- `MapBenchmarks` — Value transformations
- `TapBenchmarks` — Side effects
- `EnsureBenchmarks` — Validation operations
- `MatchBenchmarks` — Pattern matching
- `MapOnFailureBenchmarks` — Error transformation
- `CombineBenchmarks` — Result aggregation
- `RecoverBenchmarks` — Error recovery patterns
- `AsyncBenchmarks` — Asynchronous operations
- `MaybeBenchmarks` — Optional value handling
- `ErrorBenchmarks` — Error creation and aggregation
- `ValueObjectBenchmarks` — DDD value object comparison
- `MoneyBenchmarks` — Multi-currency arithmetic
- `SpecificationBenchmarks` — DDD specification pattern
- `ActorBenchmarks` — Authorization permission checking
- `TracingOverheadBenchmarks` — Activity listener and chain-depth costs (30 parameterized cases)

## Interpreting Results

### What the Numbers Mean

**Execution Time:**
- **< 100 ns**: Excellent — negligible overhead
- **100-500 ns**: Very good — minimal impact
- **500-1000 ns**: Good — reasonable for most scenarios
- **> 1000 ns**: Context-dependent — compare to your I/O operations

**Memory Allocations:**
- **< 100 B**: Excellent — minimal heap pressure
- **100-500 B**: Very good — acceptable for most operations
- **500-1000 B**: Good — watch for high-frequency operations
- **> 1000 B**: Context-dependent — consider pooling for hot paths

### Real-World Context

**Typical Operation Costs:**
- Database query: **1,000,000-10,000,000 ns** (1-10 ms)
- HTTP request: **10,000,000-100,000,000 ns** (10-100 ms)
- File I/O: **100,000-1,000,000 ns** (0.1-1 ms)
- Observed ROP vs imperative mean gaps in this run: **21-95 ns**

**Conclusion:** The observed pipeline mean gaps are small beside millisecond-scale I/O. They are not end-to-end request measurements or a fixed overhead for every ROP operation.

### Performance Tips

1. **Use `Combine` for parallel validation** — More efficient than sequential checks
2. **Leverage short-circuiting** — Failed results don't execute subsequent operations
3. **Prefer `Map` over `Bind`** — When you don't need to change the Result context
4. **Use `ParallelAsync`** — For independent async operations
5. **Reuse specifications** — `IsSatisfiedBy` caches compilation by default; avoid repeatedly calling `ToExpression().Compile()` in hot loops
6. **Don't over-optimize** — Focus on I/O and business logic optimization first

## Conclusion

The .NET 11 results show low-nanosecond primitive operations, with complete pipeline costs determined by validation, callbacks, allocations, and tracing. Trellis offers:
- **Code clarity** — Railway-oriented style is more readable
- **Error handling** — Explicit error propagation and aggregation
- **Testability** — Pure functions are easier to test
- **Maintainability** — Composable operations reduce complexity

Single successful Bind, Map, and Tap workloads take about **4-5 ns** without allocation. The complete ROP comparisons have observed mean gaps of **21-95 ns**, and the basic cases allocate more than their imperative equivalents. Benchmark your complete workload when those costs matter.

**Performance Summary by Operation:**
| Category | Range | Allocations |
|----------|-------|-------------|
| Map | 4.02-31.88 ns | 0-144 B |
| Match / Switch | 9.46-32.36 ns | 112-224 B |
| Tap | 3.86-31.22 ns | 0 B |
| Bind | 4.14-39.50 ns | 0-56 B |
| Combine | 4.14-135.29 ns | 0-448 B |
| Ensure | 6.21-41.73 ns | 56-280 B (eager errors) |
| Recover | 3.97-25.55 ns | 0-112 B |
| MapOnFailure | 4.31-99.66 ns | 0-456 B |
| Maybe | 0.90-21.51 ns | 0-56 B |
| Actor | 0.92-103.83 ns | 0-752 B |
| Money | 5.70-154.19 ns | 0-696 B |
| Specification | 3.13 ns-452.98 microseconds | 0-13,176 B |
| Errors | 0.91-92.39 ns | 0-1,344 B |
| Async | 17.72 ns-15.47 ms | 160-1,422 B |
| Value objects | 12.70 ns-9.98 microseconds | 0-824 B |
| Tracing workloads | 4.79 ns-1.12 microseconds | 0-5,200 B |

---

**Last Updated:** November 12, 2026
**Benchmark Tool:** [BenchmarkDotNet](https://benchmarkdotnet.org/) v0.16.0-preview.2 (isolated runner)
**Environment:** .NET 11.0.0 (SDK 11.0.100), Release Configuration, AMD Ryzen 9 9900X, Windows 11 (25H2), Balanced power plan
