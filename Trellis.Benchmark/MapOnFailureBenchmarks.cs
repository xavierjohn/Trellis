namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for MapOnFailure extension method.
/// MapOnFailure transforms the error on the failure track without affecting success values.
/// </summary>
[MemoryDiagnoser]
public class MapOnFailureBenchmarks
{
    private Result<int> _successResult = default!;
    private Result<int> _failureResult = default!;
    private Result<int> _validationFailure = default!;

    [GlobalSetup]
    public void Setup()
    {
        _successResult = Result.Ok(42);
        _failureResult = Result.Fail<int>(Error.Unexpected("Something went wrong"));
        _validationFailure = Result.Fail<int>(Error.Validation("Invalid value", "field"));
    }

    [Benchmark(Baseline = true)]
    public Result<int> MapOnFailure_OnSuccess()
    {
        return _successResult
            .MapOnFailure(e => Error.Unexpected($"Wrapped: {e.Detail}"));
    }

    [Benchmark]
    public Result<int> MapOnFailure_OnFailure()
    {
        return _failureResult
            .MapOnFailure(e => Error.Unexpected($"Wrapped: {e.Detail}"));
    }

    [Benchmark]
    public Result<int> MapOnFailure_ChangeErrorType()
    {
        return _validationFailure
            .MapOnFailure(e => Error.Conflict($"Conflict: {e.Detail}"));
    }

    [Benchmark]
    public Result<int> MapOnFailure_ChainedOnSuccess()
    {
        return _successResult
            .MapOnFailure(e => Error.Unexpected($"Level 1: {e.Detail}"))
            .MapOnFailure(e => Error.Unexpected($"Level 2: {e.Detail}"));
    }

    [Benchmark]
    public Result<int> MapOnFailure_ChainedOnFailure()
    {
        return _failureResult
            .MapOnFailure(e => Error.Unexpected($"Level 1: {e.Detail}"))
            .MapOnFailure(e => Error.Unexpected($"Level 2: {e.Detail}"));
    }

    [Benchmark]
    public Result<int> MapOnFailure_InPipeline_Success()
    {
        return _successResult
            .Ensure(v => v > 0, Error.Validation("Must be positive"))
            .Map(v => v * 2)
            .MapOnFailure(e => Error.Unexpected($"Pipeline failed: {e.Detail}"));
    }

    [Benchmark]
    public Result<int> MapOnFailure_InPipeline_Failure()
    {
        return _failureResult
            .Ensure(v => v > 0, Error.Validation("Must be positive"))
            .Map(v => v * 2)
            .MapOnFailure(e => Error.Unexpected($"Pipeline failed: {e.Detail}"));
    }
}