namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Benchmarks for the Money value object — currency-aware arithmetic operations
/// with Result-returning methods for error handling.
/// </summary>
[MemoryDiagnoser]
public class MoneyBenchmarks
{
    private Money _usd100 = default!;
    private Money _usd50 = default!;
    private Money _eur100 = default!;
    private Money _jpy1000 = default!;

    [GlobalSetup]
    public void Setup()
    {
        _usd100 = Money.Create(100.00m, "USD");
        _usd50 = Money.Create(50.00m, "USD");
        _eur100 = Money.Create(100.00m, "EUR");
        _jpy1000 = Money.Create(1000m, "JPY");
    }

    #region Creation

    [Benchmark(Baseline = true)]
    public Money Create_Valid()
    {
        return Money.Create(99.99m, "USD");
    }

    [Benchmark]
    public Money Create_ZeroDecimalCurrency()
    {
        return Money.Create(1000m, "JPY");
    }

    #endregion

    #region Arithmetic — Same Currency

    [Benchmark]
    public Money Add_SameCurrency()
    {
        return _usd100.Add(_usd50).OrThrow();
    }

    [Benchmark]
    public Money Subtract_SameCurrency()
    {
        return _usd100.Subtract(_usd50).OrThrow();
    }

    [Benchmark]
    public Money Multiply_Decimal()
    {
        return _usd100.Multiply(2.5m).OrThrow();
    }

    [Benchmark]
    public Money Multiply_Integer()
    {
        return _usd100.Multiply(3).OrThrow();
    }

    [Benchmark]
    public Money Divide_Decimal()
    {
        return _usd100.Divide(3m).OrThrow();
    }

    [Benchmark]
    public Money Divide_Integer()
    {
        return _usd100.Divide(4).OrThrow();
    }

    #endregion

    #region Arithmetic — Currency Mismatch (Error Path)

    [Benchmark]
    public bool Add_DifferentCurrency_Fails()
    {
        return _usd100.Add(_eur100).IsFailure;
    }

    [Benchmark]
    public bool Subtract_DifferentCurrency_Fails()
    {
        return _usd100.Subtract(_eur100).IsFailure;
    }

    #endregion

    #region Comparison

    [Benchmark]
    public bool IsGreaterThan()
    {
        return _usd100.IsGreaterThan(_usd50);
    }

    [Benchmark]
    public bool IsLessThan()
    {
        return _usd50.IsLessThan(_usd100);
    }

    #endregion

    #region Allocation

    [Benchmark]
    public Money[] Allocate_ThreeWay()
    {
        return _usd100.Allocate(1, 2, 1).OrThrow();
    }

    [Benchmark]
    public Money[] Allocate_EvenSplit()
    {
        return _usd100.Allocate(1, 1, 1).OrThrow();
    }

    #endregion

    #region Chained Operations

    [Benchmark]
    public Money ArithmeticPipeline()
    {
        return _usd100
            .Add(_usd50)
            .Bind(m => m.Multiply(2))
            .Bind(m => m.Divide(3))
            .OrThrow();
    }

    #endregion
}

internal static class BenchmarkResultExtensions
{
    public static T OrThrow<T>(this Result<T> r) =>
        r.Match(onSuccess: v => v, onFailure: e => throw new InvalidOperationException(e.Detail));
}