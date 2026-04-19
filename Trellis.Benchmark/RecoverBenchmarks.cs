namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for RecoverOnFailure operation testing error recovery and fallback mechanisms.
/// RecoverOnFailure is used to provide alternative paths when operations fail.
/// </summary>
[MemoryDiagnoser]
public class RecoverBenchmarks
{
    private Result<int> _successResult = default!;
    private Result<int> _failureResult = default!;
    private Result<int> _notFoundFailure = default!;
    private Result<int> _validationFailure = default!;

    [GlobalSetup]
    public void Setup()
    {
        _successResult = Result.Ok(42);
        _failureResult = Result.Fail<int>(Error.Unexpected("Unexpected error"));
        _notFoundFailure = Result.Fail<int>(Error.NotFound("Resource not found"));
        _validationFailure = Result.Fail<int>(Error.Validation("Validation failed"));
    }

    [Benchmark(Baseline = true)]
    public Result<int> RecoverOnFailure_OnSuccess()
    {
        return _successResult
            .RecoverOnFailure(() => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_OnFailure()
    {
        return _failureResult
            .RecoverOnFailure(() => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_OnFailure_WithErrorAccess()
    {
        return _failureResult
            .RecoverOnFailure(error => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_WithPredicate_Match()
    {
        return _notFoundFailure
            .RecoverOnFailure(
                predicate: error => error is NotFoundError,
                func: () => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_WithPredicate_NoMatch()
    {
        return _validationFailure
            .RecoverOnFailure(
                predicate: error => error is NotFoundError,
                func: () => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_WithPredicate_AndErrorAccess_Match()
    {
        return _notFoundFailure
            .RecoverOnFailure(
                predicate: error => error is NotFoundError,
                func: error => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_Chain_TwoLevels()
    {
        return _failureResult
            .RecoverOnFailure(() => Result.Fail<int>(Error.NotFound("Still failing")))
            .RecoverOnFailure(() => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_Chain_ThreeLevels()
    {
        return _failureResult
            .RecoverOnFailure(() => Result.Fail<int>(Error.NotFound("Fail 1")))
            .RecoverOnFailure(() => Result.Fail<int>(Error.Validation("Fail 2")))
            .RecoverOnFailure(() => Result.Ok(100));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_WithComplexRecovery()
    {
        return _failureResult
            .RecoverOnFailure(error => RecoverFromError(error));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_Multiple_DifferentErrorTypes()
    {
        return _failureResult
            .RecoverOnFailure(error => error is NotFoundError, () => Result.Ok(10))
            .RecoverOnFailure(error => error is ValidationError, () => Result.Ok(20))
            .RecoverOnFailure(error => error is UnexpectedError, () => Result.Ok(30));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_WithExpensiveRecovery()
    {
        return _failureResult
            .RecoverOnFailure(error => PerformExpensiveRecovery());
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_MixedWithBind_Success()
    {
        return _successResult
            .Bind(x => Result.Ok(x * 2))
            .RecoverOnFailure(() => Result.Ok(0))
            .Bind(x => Result.Ok(x + 10));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_MixedWithBind_Failure()
    {
        return _failureResult
            .Bind(x => Result.Ok(x * 2))
            .RecoverOnFailure(() => Result.Ok(0))
            .Bind(x => Result.Ok(x + 10));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_WithDefaultValue()
    {
        return _failureResult
            .RecoverOnFailure(() => Result.Ok(0));
    }

    [Benchmark]
    public Result<string> RecoverOnFailure_TypeTransformation()
    {
        var failureString = Result.Fail<string>(Error.NotFound("String not found"));
        return failureString
            .RecoverOnFailure(() => Result.Ok("Default Value"));
    }

    [Benchmark]
    public Result<int> RecoverOnFailure_NestedPredicates()
    {
        return _failureResult
            .RecoverOnFailure(
                predicate: error => error is NotFoundError || error is ValidationError,
                func: () => Result.Ok(50))
            .RecoverOnFailure(
                predicate: error => error is UnexpectedError,
                func: () => Result.Ok(100));
    }

    private static Result<int> RecoverFromError(Error error)
    {
        return error switch
        {
            NotFoundError => Result.Ok(10),
            ValidationError => Result.Ok(20),
            UnexpectedError => Result.Ok(30),
            _ => Result.Ok(0)
        };
    }

    private static Result<int> PerformExpensiveRecovery()
    {
        var sum = 0;
        for (int i = 0; i < 100; i++)
        {
            sum += i * i;
        }

        return Result.Ok(sum);
    }
}