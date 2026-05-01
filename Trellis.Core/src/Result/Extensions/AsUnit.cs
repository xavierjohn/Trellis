namespace Trellis;

using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Asynchronous bridges that discard a result's success value and project to a no-payload <c>Result&lt;Unit&gt;</c>.
/// Pairs with <see cref="Result{TValue}.AsUnit"/> for sync receivers.
/// </summary>
/// <remarks>
/// Use these when an async pipeline produces a value-bearing result but the next step only needs success/failure.
/// Failures preserve their <see cref="Error"/>; default-initialized failures route through the shared sentinel
/// (matching <see cref="Result{TValue}.AsUnit"/>).
/// </remarks>
[DebuggerStepThrough]
public static class AsUnitExtensions
{
    /// <summary>Awaits <paramref name="resultTask"/> and discards its value, producing a <c>Result&lt;Unit&gt;</c>.</summary>
    public static async Task<Result<Unit>> AsUnitAsync<T>(this Task<Result<T>> resultTask)
        => (await resultTask.ConfigureAwait(false)).AsUnit();

    /// <summary>Awaits <paramref name="resultTask"/> and discards its value, producing a <c>Result&lt;Unit&gt;</c>.</summary>
    public static async ValueTask<Result<Unit>> AsUnitAsync<T>(this ValueTask<Result<T>> resultTask)
        => (await resultTask.ConfigureAwait(false)).AsUnit();
}
