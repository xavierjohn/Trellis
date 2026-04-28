namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for Bind operation testing various chain depths and success/failure scenarios.
/// Bind is critical for railway-oriented programming as it chains operations that can fail.
/// </summary>
[MemoryDiagnoser]
public class BindBenchmarks
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
    public Result<int> Bind_SingleChain_Success()
    {
        return _successResult
            .Bind(x => Result.Ok(x * 2));
    }

    [Benchmark]
    public Result<int> Bind_SingleChain_Failure()
    {
        return _failureResult
            .Bind(x => Result.Ok(x * 2));
    }

    [Benchmark]
    public Result<int> Bind_ThreeChains_AllSuccess()
    {
        return _successResult
            .Bind(x => Result.Ok(x * 2))
            .Bind(x => Result.Ok(x + 10))
            .Bind(x => Result.Ok(x - 5));
    }

    [Benchmark]
    public Result<int> Bind_ThreeChains_FailAtFirst()
    {
        return _failureResult
            .Bind(x => Result.Ok(x * 2))
            .Bind(x => Result.Ok(x + 10))
            .Bind(x => Result.Ok(x - 5));
    }

    [Benchmark]
    public Result<int> Bind_ThreeChains_FailAtSecond()
    {
        return _successResult
            .Bind(x => Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Failed at step 2" }))
            .Bind(x => Result.Ok(x + 10))
            .Bind(x => Result.Ok(x - 5));
    }

    [Benchmark]
    public Result<string> Bind_TypeTransformation()
    {
        return _successResult
            .Bind(x => Result.Ok(x.ToString()))
            .Bind(s => Result.Ok($"Value: {s}"));
    }

    [Benchmark]
    public Result<int> Bind_FiveChains_Success()
    {
        return _successResult
            .Bind(x => Result.Ok(x + 1))
            .Bind(x => Result.Ok(x * 2))
            .Bind(x => Result.Ok(x + 10))
            .Bind(x => Result.Ok(x - 5))
            .Bind(x => Result.Ok(x / 2));
    }

    [Benchmark]
    public Result<int> Bind_WithComplexOperation_Success()
    {
        return _successResult
            .Bind(x => ComputeComplexOperation(x))
            .Bind(x => ValidateResult(x))
            .Bind(x => TransformResult(x));
    }

    [Benchmark]
    public Result<int> Bind_WithComplexOperation_Failure()
    {
        return _failureResult
            .Bind(x => ComputeComplexOperation(x))
            .Bind(x => ValidateResult(x))
            .Bind(x => TransformResult(x));
    }

    private static Result<int> ComputeComplexOperation(int value)
    {
        var result = value * value + value - 10;
        return Result.Ok(result);
    }

    private static Result<int> ValidateResult(int value)
    {
        return value > 0
            ? Result.Ok(value)
            : Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value must be positive" });
    }

    private static Result<int> TransformResult(int value)
    {
        return Result.Ok(value % 100);
    }
}