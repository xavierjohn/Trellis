namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;

/// <summary>
/// Benchmarks for ValueObject — comparison, sorting, and equality operations.
/// </summary>
[MemoryDiagnoser]
public class ValueObjectBenchmarks
{
    private AddressVO _address1 = default!;
    private AddressVO _address2Equal = default!;
    private AddressVO _address3Different = default!;
    private AddressVO[] _unsortedArray = default!;

    [GlobalSetup]
    public void Setup()
    {
        _address1 = new AddressVO("123 Main St", "Springfield", "IL", "62701");
        _address2Equal = new AddressVO("123 Main St", "Springfield", "IL", "62701");
        _address3Different = new AddressVO("456 Oak Ave", "Chicago", "IL", "60601");

        // Create an array of 100 addresses to sort
        _unsortedArray = new AddressVO[100];
        for (var i = 0; i < 100; i++)
            _unsortedArray[i] = new AddressVO($"{i} Street", $"City{i % 10}", "ST", $"{10000 + i}");
    }

    [Benchmark(Baseline = true)]
    public int CompareTo_Equal()
    {
        return _address1.CompareTo(_address2Equal);
    }

    [Benchmark]
    public int CompareTo_Different()
    {
        return _address1.CompareTo(_address3Different);
    }

    [Benchmark]
    public void Sort_100_ValueObjects()
    {
        var copy = (AddressVO[])_unsortedArray.Clone();
        Array.Sort(copy);
    }

    private sealed class AddressVO : ValueObject
    {
        public string Street { get; }
        public string City { get; }
        public string State { get; }
        public string Zip { get; }

        public AddressVO(string street, string city, string state, string zip)
        {
            Street = street;
            City = city;
            State = state;
            Zip = zip;
        }

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Street;
            yield return City;
            yield return State;
            yield return Zip;
        }

    }
}