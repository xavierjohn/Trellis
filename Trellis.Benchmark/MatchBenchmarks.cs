namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for Match and Switch extension methods.
/// Match converts a Result to another type; Switch performs side effects on both tracks.
/// </summary>
[MemoryDiagnoser]
public class MatchBenchmarks
{
    private Result<int> _successResult = default!;
    private Result<int> _failureResult = default!;
    private int _sideEffectCounter;

    [GlobalSetup]
    public void Setup()
    {
        _successResult = Result.Ok(42);
        _failureResult = Result.Fail<int>(Error.Validation("Test error"));
        _sideEffectCounter = 0;
    }

    #region Match

    [Benchmark(Baseline = true)]
    public string Match_Success()
    {
        return _successResult.Match(
            v => v.ToString(),
            e => e.Detail);
    }

    [Benchmark]
    public string Match_Failure()
    {
        return _failureResult.Match(
            v => v.ToString(),
            e => e.Detail);
    }

    [Benchmark]
    public int Match_Success_TypePreserved()
    {
        return _successResult.Match(
            v => v * 2,
            _ => -1);
    }

    [Benchmark]
    public int Match_Failure_TypePreserved()
    {
        return _failureResult.Match(
            v => v * 2,
            _ => -1);
    }

    #endregion

    #region Switch

    [Benchmark]
    public void Switch_Success()
    {
        _successResult.Switch(
            v => _sideEffectCounter += v,
            _ => _sideEffectCounter--);
    }

    [Benchmark]
    public void Switch_Failure()
    {
        _failureResult.Switch(
            v => _sideEffectCounter += v,
            _ => _sideEffectCounter--);
    }

    #endregion

    #region Chained Match

    [Benchmark]
    public string Match_AfterPipeline_Success()
    {
        return _successResult
            .Map(v => v * 2)
            .Ensure(v => v > 0, Error.Validation("Must be positive"))
            .Match(
                v => $"Value: {v}",
                e => $"Error: {e.Detail}");
    }

    [Benchmark]
    public string Match_AfterPipeline_Failure()
    {
        return _failureResult
            .Map(v => v * 2)
            .Ensure(v => v > 0, Error.Validation("Must be positive"))
            .Match(
                v => $"Value: {v}",
                e => $"Error: {e.Detail}");
    }

    #endregion
}