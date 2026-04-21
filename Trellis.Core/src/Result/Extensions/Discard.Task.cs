namespace Trellis;

using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Provides async extension methods for explicitly discarding a Task-wrapped Result value.
/// </summary>
[DebuggerStepThrough]
public static class DiscardTaskExtensions
{
    /// <summary>
    /// Awaits the task and explicitly discards the result, indicating the caller
    /// intentionally ignores the outcome.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">The task producing the result to discard.</param>
    public static async Task DiscardAsync<T>(this Task<Result<T>> resultTask)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        await resultTask.ConfigureAwait(false);
    }
}