namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for Maybe type testing optional value handling and comparisons.
/// Maybe is used to make optionality explicit and avoid null reference exceptions.
/// </summary>
[MemoryDiagnoser]
public class MaybeBenchmarks
{
    private Maybe<int> _maybeWithValue = default!;
    private Maybe<int> _maybeEmpty = default!;
    private Maybe<string> _maybeStringWithValue = default!;
    private Maybe<string> _maybeStringEmpty = default!;

    [GlobalSetup]
    public void Setup()
    {
        _maybeWithValue = Maybe.From(42);
        _maybeEmpty = Maybe<int>.None;
        _maybeStringWithValue = Maybe.From("Hello World");
        _maybeStringEmpty = Maybe<string>.None;
    }

    [Benchmark(Baseline = true)]
    public bool HasValue_WithValue()
    {
        return _maybeWithValue.HasValue;
    }

    [Benchmark]
    public bool HasValue_Empty()
    {
        return _maybeEmpty.HasValue;
    }

    [Benchmark]
    public bool HasNoValue_WithValue()
    {
        return _maybeWithValue.HasNoValue;
    }

    [Benchmark]
    public bool HasNoValue_Empty()
    {
        return _maybeEmpty.HasNoValue;
    }

    [Benchmark]
    public int GetValueOrDefault_WithValue()
    {
        return _maybeWithValue.GetValueOrDefault(0);
    }

    [Benchmark]
    public int GetValueOrDefault_Empty()
    {
        return _maybeEmpty.GetValueOrDefault(0);
    }

    [Benchmark]
    public bool TryGetValue_WithValue()
    {
        return _maybeWithValue.TryGetValue(out var value);
    }

    [Benchmark]
    public bool TryGetValue_Empty()
    {
        return _maybeEmpty.TryGetValue(out var value);
    }

    [Benchmark]
    public Maybe<int> From_Value()
    {
        return Maybe.From(42);
    }

    [Benchmark]
    public Maybe<int> None_Creation()
    {
        return Maybe<int>.None;
    }

    [Benchmark]
    public Maybe<int> ImplicitConversion_FromValue()
    {
        Maybe<int> maybe = 42;
        return maybe;
    }

    [Benchmark]
    public Maybe<string> ImplicitConversion_FromNull()
    {
        Maybe<string> maybe = (string)null!;
        return maybe;
    }

    [Benchmark]
    public bool Equality_BothWithSameValue()
    {
        var maybe1 = Maybe.From(42);
        var maybe2 = Maybe.From(42);
        return maybe1 == maybe2;
    }

    [Benchmark]
    public bool Equality_BothEmpty()
    {
        var maybe1 = Maybe<int>.None;
        var maybe2 = Maybe<int>.None;
        return maybe1 == maybe2;
    }

    [Benchmark]
    public bool Equality_OneEmptyOneWithValue()
    {
        return _maybeWithValue == _maybeEmpty;
    }

    [Benchmark]
    public int GetHashCode_WithValue()
    {
        return _maybeWithValue.GetHashCode();
    }

    [Benchmark]
    public int GetHashCode_Empty()
    {
        return _maybeEmpty.GetHashCode();
    }

    [Benchmark]
    public string ToString_WithValue()
    {
        return _maybeStringWithValue.ToString();
    }

    [Benchmark]
    public string ToString_Empty()
    {
        return _maybeStringEmpty.ToString();
    }

    [Benchmark]
    public Result<int> ToResult_WithValue()
    {
        return _maybeWithValue.ToResult(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Value not found" });
    }

    [Benchmark]
    public Result<int> ToResult_Empty()
    {
        return _maybeEmpty.ToResult(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Value not found" });
    }

    [Benchmark]
    public Maybe<string> Optional_WithNullValue()
    {
        string? value = null;
        return value != null ? Maybe.From(value) : Maybe<string>.None;
    }

    [Benchmark]
    public Maybe<string> Optional_WithValue()
    {
        string? value = "test";
        return value != null ? Maybe.From(value) : Maybe<string>.None;
    }

    [Benchmark]
    public bool Equals_WithObject()
    {
        object obj = 42;
        return _maybeWithValue.Equals(obj);
    }

    [Benchmark]
    public bool Equals_WithSameValue()
    {
        return _maybeWithValue.Equals(42);
    }

    [Benchmark]
    public Maybe<ComplexObject> CreateComplexMaybe()
    {
        var obj = new ComplexObject(42, "Test", DateTime.UtcNow);
        return Maybe.From(obj);
    }

    public record ComplexObject(int Id, string Name, DateTime Created);
}