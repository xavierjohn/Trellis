namespace Trellis;

using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Provides async extension methods for explicitly discarding a ValueTask-wrapped Result value.
/// </summary>
[DebuggerStepThrough]
public static class DiscardValueTaskExtensions
{
    /// <summary>
    /// Awaits the value task and explicitly discards the result, indicating the caller
    /// intentionally ignores the outcome.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">The value task producing the result to discard.</param>
    public static async ValueTask DiscardAsync<T>(this ValueTask<Result<T>> resultTask) =>
        await resultTask.ConfigureAwait(false);
}
