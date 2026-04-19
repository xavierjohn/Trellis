namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for Tap operation testing side effects without transformation.
/// Tap is used for logging, metrics, and other side effects while passing through the result.
/// </summary>
[MemoryDiagnoser]
public class TapBenchmarks
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

    [Benchmark(Baseline = true)]
    public Result<int> Tap_SingleAction_Success()
    {
        return _successResult.Tap(x => _sideEffectCounter++);
    }

    [Benchmark]
    public Result<int> Tap_SingleAction_Failure()
    {
        return _failureResult.Tap(x => _sideEffectCounter++);
    }

    [Benchmark]
    public Result<int> Tap_ThreeActions_Success()
    {
        return _successResult
            .Tap(x => _sideEffectCounter++)
            .Tap(x => _sideEffectCounter += x)
            .Tap(x => _sideEffectCounter *= 2);
    }

    [Benchmark]
    public Result<int> Tap_ThreeActions_Failure()
    {
        return _failureResult
            .Tap(x => _sideEffectCounter++)
            .Tap(x => _sideEffectCounter += x)
            .Tap(x => _sideEffectCounter *= 2);
    }

    [Benchmark]
    public Result<int> Tap_WithLogging_Success()
    {
        return _successResult
            .Tap(x => LogValue(x))
            .Tap(x => IncrementMetric())
            .Tap(x => RecordTimestamp());
    }

    [Benchmark]
    public Result<int> TapError_OnFailure()
    {
        return _failureResult
            .TapOnFailure(err => LogError(err))
            .TapOnFailure(err => IncrementErrorMetric());
    }

    [Benchmark]
    public Result<int> TapError_OnSuccess()
    {
        return _successResult
            .TapOnFailure(err => LogError(err))
            .TapOnFailure(err => IncrementErrorMetric());
    }

    [Benchmark]
    public Result<int> Tap_MixedWithMap_Success()
    {
        return _successResult
            .Tap(x => _sideEffectCounter++)
            .Map(x => x * 2)
            .Tap(x => _sideEffectCounter += x)
            .Map(x => x + 10)
            .Tap(x => _sideEffectCounter *= 2);
    }

    [Benchmark]
    public Result<int> Tap_ComplexSideEffect_Success()
    {
        return _successResult
            .Tap(x => PerformComplexSideEffect(x))
            .Tap(x => UpdateStatistics(x))
            .Tap(x => NotifyObservers(x));
    }

    [Benchmark]
    public Result<int> Tap_FiveActions_Success()
    {
        return _successResult
            .Tap(x => _sideEffectCounter++)
            .Tap(x => _sideEffectCounter += 1)
            .Tap(x => _sideEffectCounter += 2)
            .Tap(x => _sideEffectCounter += 3)
            .Tap(x => _sideEffectCounter += 4);
    }

    [Benchmark]
    public Result<int> Tap_WithBind_Success()
    {
        return _successResult
            .Tap(x => _sideEffectCounter++)
            .Bind(x => Result.Ok(x * 2))
            .Tap(x => _sideEffectCounter += x)
            .Bind(x => Result.Ok(x + 10));
    }

    private void LogValue(int value)
    {
        // Simulate logging overhead
        _sideEffectCounter += value;
    }

    private void IncrementMetric()
    {
        _sideEffectCounter++;
    }

    private void RecordTimestamp()
    {
        _sideEffectCounter += DateTime.UtcNow.Millisecond;
    }

    private void LogError(Error error)
    {
        _sideEffectCounter--;
    }

    private void IncrementErrorMetric()
    {
        _sideEffectCounter++;
    }

    private void PerformComplexSideEffect(int value)
    {
        for (int i = 0; i < 10; i++)
        {
            _sideEffectCounter += value + i;
        }
    }

    private void UpdateStatistics(int value)
    {
        _sideEffectCounter = (_sideEffectCounter + value) / 2;
    }

    private void NotifyObservers(int value)
    {
        _sideEffectCounter += value * 2;
    }
}