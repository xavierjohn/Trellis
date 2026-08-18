namespace Trellis.EntityFrameworkCore;

using System.Buffers.Binary;

/// <summary>
/// Computes the wait before a failed <see cref="OutboxMessage"/> is retried: an exponential backoff from
/// <see cref="OutboxOptions.RetryBackoff"/>, capped at <see cref="OutboxOptions.MaxRetryBackoff"/>, with a
/// deterministic per-message jitter that only subtracts from the delay so messages that failed together do
/// not all retry the instant a failed dependency recovers.
/// </summary>
internal static class OutboxRetryBackoff
{
    /// <summary>
    /// Computes the retry delay for the <paramref name="attempt"/>th attempt (1-based) of the message
    /// identified by <paramref name="id"/>. The result lies in
    /// <c>[(1 - jitter) × computed, computed]</c> where <c>computed = min(cap, baseDelay × 2^(attempt-1))</c>,
    /// so it never exceeds <paramref name="cap"/>.
    /// </summary>
    public static TimeSpan Compute(Guid id, int attempt, TimeSpan baseDelay, TimeSpan cap, double jitter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempt);

        var computedTicks = ExponentialTicks(baseDelay.Ticks, cap.Ticks, attempt);

        if (jitter > 0)
        {
            // Subtract up to (jitter × computed) using a stable fraction of the id. Subtracting keeps the
            // delay at or below the cap; keying on the id spreads messages across the window deterministically
            // instead of bunching them onto one expiry.
            var reduction = (long)(computedTicks * jitter * StableFraction(id));
            computedTicks -= reduction;
        }

        return TimeSpan.FromTicks(computedTicks);
    }

    // base × 2^(attempt-1), saturating at the cap without overflowing for large attempt counts.
    private static long ExponentialTicks(long baseTicks, long capTicks, int attempt)
    {
        var exponent = attempt - 1;
        if (exponent >= 62 || baseTicks > capTicks >> exponent)
            return capTicks;
        return baseTicks << exponent;
    }

    // A stable, uniform fraction in [0, 1) derived from the id, so the same message always lands at the same
    // point in the jitter window and different messages spread out — no run-to-run randomness. Uses the top
    // 53 bits (a double's mantissa) over 2^53 so the result is exactly < 1 even at jitter = 1 (so a retry is
    // never scheduled for "now"); dividing a near-ulong.MaxValue value by 2^64 would round up to exactly 1.0.
    private static double StableFraction(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        var hi = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        var lo = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        return ((hi ^ lo) >> 11) / (double)(1UL << 53);
    }
}