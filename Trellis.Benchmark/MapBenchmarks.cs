namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for Map operation testing pure transformations without introducing failure.
/// Map is lighter than Bind as it transforms values without Result wrapping.
/// </summary>
[MemoryDiagnoser]
public class MapBenchmarks
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
    public Result<int> Map_SingleTransformation_Success()
    {
        return _successResult.Map(x => x * 2);
    }

    [Benchmark]
    public Result<int> Map_SingleTransformation_Failure()
    {
        return _failureResult.Map(x => x * 2);
    }

    [Benchmark]
    public Result<int> Map_ThreeTransformations_Success()
    {
        return _successResult
            .Map(x => x * 2)
            .Map(x => x + 10)
            .Map(x => x - 5);
    }

    [Benchmark]
    public Result<int> Map_ThreeTransformations_Failure()
    {
        return _failureResult
            .Map(x => x * 2)
            .Map(x => x + 10)
            .Map(x => x - 5);
    }

    [Benchmark]
    public Result<string> Map_TypeConversion_IntToString()
    {
        return _successResult.Map(x => x.ToString());
    }

    [Benchmark]
    public Result<string> Map_ComplexTransformation()
    {
        return _successResult
            .Map(x => x * x)
            .Map(x => x.ToString())
            .Map(s => $"Result: {s}");
    }

    [Benchmark]
    public Result<double> Map_MathematicalOperations()
    {
        return _successResult
            .Map(x => (double)x)
            .Map(x => Math.Sqrt(x))
            .Map(x => Math.Round(x, 2));
    }

    [Benchmark]
    public Result<int> Map_FiveTransformations_Success()
    {
        return _successResult
            .Map(x => x + 1)
            .Map(x => x * 2)
            .Map(x => x + 10)
            .Map(x => x - 5)
            .Map(x => x / 2);
    }

    [Benchmark]
    public Result<string> Map_StringManipulation()
    {
        var strResult = Result.Ok("hello");
        return strResult
            .Map(s => s.ToUpperInvariant())
            .Map(s => s + " WORLD")
            .Map(s => s.Trim());
    }

    [Benchmark]
    public Result<int> Map_WithComplexCalculation()
    {
        return _successResult
            .Map(x => PerformComplexCalculation(x))
            .Map(x => x % 1000)
            .Map(x => Math.Abs(x));
    }

    [Benchmark]
    public Result<Person> Map_ToComplexObject()
    {
        return _successResult
            .Map(id => new Person(id, $"User{id}", $"user{id}@example.com"));
    }

    private static int PerformComplexCalculation(int value)
    {
        var result = value;
        for (int i = 0; i < 10; i++)
        {
            result = result * 2 + i - 5;
        }

        return result;
    }

    public record Person(int Id, string Name, string Email);
}