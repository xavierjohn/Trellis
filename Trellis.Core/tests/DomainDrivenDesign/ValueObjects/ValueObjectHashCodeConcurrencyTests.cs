namespace Trellis.Core.Tests.DomainDrivenDesign.ValueObjects;

using System.Collections.Concurrent;

/// <summary>
/// Pins down whether <see cref="ValueObject"/>'s cached <c>GetHashCode</c> can return a torn
/// read under concurrent access (CORE-DDD-005 candidate).
/// </summary>
public class ValueObjectHashCodeConcurrencyTests
{
    [Fact]
    public void GetHashCode_UnderHeavyConcurrentAccess_AlwaysReturnsCanonicalHash()
    {
        // Arrange — repeated runs to maximize chance of catching a torn read.
        // Sized to stay under ~1s while still exercising the cache-init race window meaningfully.
        const int iterations = 50;
        const int threadsPerIteration = 16;
        const int callsPerThread = 2_000;

        for (var iter = 0; iter < iterations; iter++)
        {
            var subject = new HashStressVo(100m, "abc");
            var canonical = ComputeCanonicalHash(subject);

            var observed = new ConcurrentBag<int>();
            using var startGate = new ManualResetEventSlim(false);

            var threads = new Thread[threadsPerIteration];
            for (var t = 0; t < threadsPerIteration; t++)
            {
                threads[t] = new Thread(() =>
                {
                    startGate.Wait();
                    for (var i = 0; i < callsPerThread; i++)
                        observed.Add(subject.GetHashCode());
                });
                threads[t].Start();
            }

            startGate.Set();
            foreach (var th in threads) th.Join();

            // Assert — every observation must match the canonical hash.
            // A torn read would expose either 0 (default int) or another non-canonical value.
            observed.Should().OnlyContain(h => h == canonical,
                $"iteration {iter}: at least one thread observed a torn cached hash code");
        }
    }

    private static int ComputeCanonicalHash(HashStressVo vo) => vo.GetHashCode();
}

#region Test Value Object

internal sealed class HashStressVo(decimal amount, string tag) : ValueObject
{
    public decimal Amount { get; } = amount;
    public string Tag { get; } = tag;

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Tag;
    }
}

#endregion
