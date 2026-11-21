namespace Trellis.Primitives.Tests.Helpers;

internal static class AllocationMeasurement
{
    // Allow small measurement noise without hiding a per-call object or string allocation.
    internal const int TolerancePerOperation = 8;

    internal static long BytesPerOperation<T>(Func<T> operation)
    {
        const int iterations = 512;
        var result = operation();
        for (var i = 1; i < iterations; i++)
            result = operation();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
            result = operation();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(result);
        return allocated / iterations;
    }
}
