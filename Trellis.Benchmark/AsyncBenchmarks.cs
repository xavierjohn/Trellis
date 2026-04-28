namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for async operations testing BindAsync, MapAsync, TapAsync, and EnsureAsync.
/// Async operations are critical for I/O-bound scenarios and service calls.
/// </summary>
[MemoryDiagnoser]
public class AsyncBenchmarks
{
    private Result<int> _successResult = default!;
    private Result<int> _failureResult = default!;

    [GlobalSetup]
    public void Setup()
    {
        _successResult = Result.Ok(42);
        _failureResult = Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Test error" });
    }

    [Benchmark(Baseline = true)]
    public async Task<Result<int>> BindAsync_SingleChain_Success()
    {
        return await _successResult
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)));
    }

    [Benchmark]
    public async Task<Result<int>> BindAsync_SingleChain_Failure()
    {
        return await _failureResult
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)));
    }

    [Benchmark]
    public async Task<Result<int>> BindAsync_ThreeChains_Success()
    {
        return await _successResult
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)))
            .BindAsync(x => Task.FromResult(Result.Ok(x + 10)))
            .BindAsync(x => Task.FromResult(Result.Ok(x - 5)));
    }

    [Benchmark]
    public async Task<Result<int>> BindAsync_ThreeChains_FailAtSecond()
    {
        return await _successResult
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)))
            .BindAsync(x => Task.FromResult(Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Failed" })))
            .BindAsync(x => Task.FromResult(Result.Ok(x - 5)));
    }

    [Benchmark]
    public async Task<Result<int>> MapAsync_SingleTransformation_Success()
    {
        return await _successResult
            .MapAsync(x => Task.FromResult(x * 2));
    }

    [Benchmark]
    public async Task<Result<int>> MapAsync_ThreeTransformations_Success()
    {
        return await _successResult
            .MapAsync(x => Task.FromResult(x * 2))
            .MapAsync(x => Task.FromResult(x + 10))
            .MapAsync(x => Task.FromResult(x - 5));
    }

    [Benchmark]
    public async Task<Result<int>> TapAsync_SingleAction_Success()
    {
        var counter = 0;
        return await _successResult
            .TapAsync(x => Task.Run(() => counter++));
    }

    [Benchmark]
    public async Task<Result<int>> TapAsync_ThreeActions_Success()
    {
        var counter = 0;
        return await _successResult
            .TapAsync(x => Task.Run(() => counter++))
            .TapAsync(x => Task.Run(() => counter += x))
            .TapAsync(x => Task.Run(() => counter *= 2));
    }

    [Benchmark]
    public async Task<Result<int>> EnsureAsync_SinglePredicate_Pass()
    {
        return await _successResult
            .EnsureAsync(x => Task.FromResult(x > 0),
                        new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" });
    }

    [Benchmark]
    public async Task<Result<int>> EnsureAsync_ThreePredicates_AllPass()
    {
        return await _successResult
            .EnsureAsync(x => Task.FromResult(x > 0), new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" })
            .EnsureAsync(x => Task.FromResult(x < 100), new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be less than 100" })
            .EnsureAsync(x => Task.FromResult(x % 2 == 0), new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be even" });
    }

    [Benchmark]
    public async Task<Result<int>> Mixed_AsyncOperations_Success()
    {
        return await _successResult
            .MapAsync(x => Task.FromResult(x * 2))
            .BindAsync(x => Task.FromResult(Result.Ok(x + 10)))
            .TapAsync(x => Task.Run(() => { }))
            .EnsureAsync(x => Task.FromResult(x > 50), new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be > 50" });
    }

    [Benchmark]
    public async Task<Result<int>> BindAsync_WithDelay_Success()
    {
        return await _successResult
            .BindAsync((x) =>
            {
                return Task.Run(async () =>
                {
                    await Task.Delay(1);
                    return Result.Ok(x * 2);
                });
            });
    }

    [Benchmark]
    public async Task<Result<int>> BindAsync_FiveChains_Success()
    {
        return await _successResult
            .BindAsync(x => Task.FromResult(Result.Ok(x + 1)))
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)))
            .BindAsync(x => Task.FromResult(Result.Ok(x + 10)))
            .BindAsync(x => Task.FromResult(Result.Ok(x - 5)))
            .BindAsync(x => Task.FromResult(Result.Ok(x / 2)));
    }

    [Benchmark]
    public async Task<Result<string>> BindAsync_TypeTransformation()
    {
        return await _successResult
            .BindAsync(x => Task.FromResult(Result.Ok(x.ToString())))
            .BindAsync(s => Task.FromResult(Result.Ok($"Value: {s}")));
    }

    [Benchmark]
    public async Task<Result<int>> TaskResult_BindAsync_Success()
    {
        return await Task.FromResult(_successResult)
            .BindAsync(x => Task.FromResult(Result.Ok(x * 2)))
            .BindAsync(x => Task.FromResult(Result.Ok(x + 10)));
    }

    [Benchmark]
    public async Task<Result<int>> RecoverOnFailureAsync_OnFailure()
    {
        return await _failureResult
            .RecoverOnFailureAsync(() => Task.FromResult(Result.Ok(100)));
    }

    [Benchmark]
    public async Task<int> FinallyAsync_OnSuccess()
    {
        var result = _successResult;
        return await Task.FromResult(
            result.Match(
                onSuccess: ok => ok * 2,
                onFailure: err => -1));
    }
}