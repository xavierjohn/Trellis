namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for Ensure operation testing validation predicates.
/// Ensure is critical for business rule validation in railway-oriented programming.
/// </summary>
[MemoryDiagnoser]
public class EnsureBenchmarks
{
    private Result<int> _successResult = default!;
    private Result<int> _failureResult = default!;

    [GlobalSetup]
    public void Setup()
    {
        _successResult = Result.Ok(42);
        _failureResult = Result.Fail<int>(Error.Validation("Test error"));
    }

    [Benchmark(Baseline = true)]
    public Result<int> Ensure_SinglePredicate_Pass()
    {
        return _successResult
            .Ensure(x => x > 0, Error.Validation("Value must be positive"));
    }

    [Benchmark]
    public Result<int> Ensure_SinglePredicate_Fail()
    {
        return _successResult
            .Ensure(x => x > 100, Error.Validation("Value must be greater than 100"));
    }

    [Benchmark]
    public Result<int> Ensure_SinglePredicate_OnFailureResult()
    {
        return _failureResult
            .Ensure(x => x > 0, Error.Validation("Value must be positive"));
    }

    [Benchmark]
    public Result<int> Ensure_ThreePredicates_AllPass()
    {
        return _successResult
            .Ensure(x => x > 0, Error.Validation("Must be positive"))
            .Ensure(x => x < 100, Error.Validation("Must be less than 100"))
            .Ensure(x => x % 2 == 0, Error.Validation("Must be even"));
    }

    [Benchmark]
    public Result<int> Ensure_ThreePredicates_FailAtSecond()
    {
        return _successResult
            .Ensure(x => x > 0, Error.Validation("Must be positive"))
            .Ensure(x => x > 100, Error.Validation("Must be greater than 100"))
            .Ensure(x => x % 2 == 0, Error.Validation("Must be even"));
    }

    [Benchmark]
    public Result<int> Ensure_ComplexPredicate_Pass()
    {
        return _successResult
            .Ensure(x => x > 0 && x < 100 && x % 2 == 0,
                   Error.Validation("Complex validation failed"));
    }

    [Benchmark]
    public Result<int> Ensure_ComplexPredicate_Fail()
    {
        return _successResult
            .Ensure(x => x > 100 && x < 200 && x % 3 == 0,
                   Error.Validation("Complex validation failed"));
    }

    [Benchmark]
    public Result<int> Ensure_WithExpensiveValidation_Pass()
    {
        return _successResult
            .Ensure(x => IsExpensiveValidationPassed(x),
                   Error.Validation("Expensive validation failed"));
    }

    [Benchmark]
    public Result<int> Ensure_WithExpensiveValidation_Fail()
    {
        return _successResult
            .Ensure(x => IsExpensiveValidationFailed(x),
                   Error.Validation("Expensive validation failed"));
    }

    [Benchmark]
    public Result<Person> Ensure_ComplexObject_MultipleRules()
    {
        var person = new Person(42, "John Doe", 30, "john@example.com");
        return Result.Ok(person)
            .Ensure(p => p.Age >= 18, Error.Validation("Must be adult"))
            .Ensure(p => p.Name.Length > 0, Error.Validation("Name required"))
            .Ensure(p => p.Email.Contains('@'), Error.Validation("Valid email required"));
    }

    [Benchmark]
    public Result<int> Ensure_FivePredicates_AllPass()
    {
        return _successResult
            .Ensure(x => x > 0, Error.Validation("Must be positive"))
            .Ensure(x => x < 100, Error.Validation("Must be less than 100"))
            .Ensure(x => x % 2 == 0, Error.Validation("Must be even"))
            .Ensure(x => x >= 40, Error.Validation("Must be at least 40"))
            .Ensure(x => x <= 50, Error.Validation("Must be at most 50"));
    }

    [Benchmark]
    public Result<int> Ensure_MixedWithMapAndBind()
    {
        return _successResult
            .Map(x => x * 2)
            .Ensure(x => x > 50, Error.Validation("Must be greater than 50"))
            .Bind(x => Result.Ok(x + 10))
            .Ensure(x => x < 200, Error.Validation("Must be less than 200"));
    }

    private static bool IsExpensiveValidationPassed(int value)
    {
        // Simulate expensive validation
        var sum = 0;
        for (int i = 0; i < 100; i++)
        {
            sum += value + i;
        }

        return sum > 0;
    }

    private static bool IsExpensiveValidationFailed(int value)
    {
        // Simulate expensive validation that fails
        var sum = 0;
        for (int i = 0; i < 100; i++)
        {
            sum += value + i;
        }

        return sum < 0; // Will always fail for positive values
    }

    public record Person(int Id, string Name, int Age, string Email);
}