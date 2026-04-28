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
        _failureResult = Result.Fail<int>(new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "Something went wrong" });
        _validationFailure = Result.Fail<int>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Invalid value" })));
    }

    [Benchmark(Baseline = true)]
    public Result<int> MapOnFailure_OnSuccess()
    {
        return _successResult
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Wrapped: {e.Detail}" });
    }

    [Benchmark]
    public Result<int> MapOnFailure_OnFailure()
    {
        return _failureResult
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Wrapped: {e.Detail}" });
    }

    [Benchmark]
    public Result<int> MapOnFailure_ChangeErrorType()
    {
        return _validationFailure
            .MapOnFailure(e => new Error.Conflict(null, "conflict") { Detail = $"Conflict: {e.Detail}" });
    }

    [Benchmark]
    public Result<int> MapOnFailure_ChainedOnSuccess()
    {
        return _successResult
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Level 1: {e.Detail}" })
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Level 2: {e.Detail}" });
    }

    [Benchmark]
    public Result<int> MapOnFailure_ChainedOnFailure()
    {
        return _failureResult
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Level 1: {e.Detail}" })
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Level 2: {e.Detail}" });
    }

    [Benchmark]
    public Result<int> MapOnFailure_InPipeline_Success()
    {
        return _successResult
            .Ensure(v => v > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" })
            .Map(v => v * 2)
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Pipeline failed: {e.Detail}" });
    }

    [Benchmark]
    public Result<int> MapOnFailure_InPipeline_Failure()
    {
        return _failureResult
            .Ensure(v => v > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Must be positive" })
            .Map(v => v * 2)
            .MapOnFailure(e => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"Pipeline failed: {e.Detail}" });
    }
}