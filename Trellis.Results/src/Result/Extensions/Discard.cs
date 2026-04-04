namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides extension methods for explicitly discarding a Result value.
/// Use when the outcome is intentionally ignored (e.g., best-effort operations).
/// This is the idiomatic alternative to <c>_ = result;</c> and suppresses TRLS001
/// without requiring pragma directives.
/// </summary>
[DebuggerStepThrough]
public static class DiscardExtensions
{
    /// <summary>
    /// Explicitly discards the result, indicating the caller intentionally ignores the outcome.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to discard.</param>
    public static void Discard<T>(this Result<T> result) { }
}
