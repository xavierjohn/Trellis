namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Production-safe throwing terminal extraction for <see cref="Result{T}"/>, intended for the
/// narrow case where a failure represents a broken invariant the caller cannot act on —
/// typically the persistence DTO → entity rehydration seam, where write-path validation
/// guarantees the row is valid and any <c>TryCreate</c> failure on read indicates database
/// corruption or migration drift.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="Maybe{T}.GetValueOrThrow(string?)"/> for symmetric ergonomics across
/// the two ROP types: an explicit, named "this throws" contract gated by the verb in the
/// method name, not an ambient property accessor (<c>Result&lt;T&gt;.Value</c> was removed in
/// v3 because the ambient accessor was the primary cause of unsafe value access).
/// </para>
/// <para>
/// Prefer <c>TryGetValue</c>, <c>Match</c>, <c>Bind</c>, <c>GetValueOrDefault</c>, or
/// destructuring for normal Result-track flow. <see cref="GetValueOrThrow{TValue}(Result{TValue}, string?)"/>
/// is the escape hatch for trust-boundary crossings where failure means
/// "the world is not as expected" — not for request validation where the caller should
/// surface a typed <c>Error.InvalidInput</c> on the Result track.
/// </para>
/// <para>
/// Cookbook Recipe 30 covers the persistence-rehydration use case end to end, including the
/// decision criteria for picking this throwing pattern versus a <c>Result&lt;TEntity&gt;</c>
/// end-to-end shape for legacy / corruptible data paths.
/// </para>
/// </remarks>
[DebuggerStepThrough]
public static class GetValueOrThrowExtensions
{
    /// <summary>
    /// Returns the success value, or throws <see cref="InvalidOperationException"/> if the
    /// result is a failure. This is a terminal operator that exits the Result railway.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The result to extract a value from.</param>
    /// <param name="errorMessage">
    /// Optional custom exception message used when the result is a failure. When
    /// <see langword="null"/>, a default message is synthesized from
    /// <c>typeof(<typeparamref name="TValue"/>).Name</c>, <see cref="Error.Code"/>, and
    /// <see cref="Error.GetDisplayMessage"/>.
    /// </param>
    /// <returns>The success value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="result"/> is a failure.</exception>
    public static TValue GetValueOrThrow<TValue>(this Result<TValue> result, string? errorMessage = null) =>
        result.TryGetValue(out var value)
            ? value
            : throw new InvalidOperationException(errorMessage ?? BuildDefaultMessage<TValue>(result.Error!));

    /// <summary>
    /// Awaits the task and returns the success value, or throws
    /// <see cref="InvalidOperationException"/> if the result is a failure. This is a terminal
    /// operator that exits the Result railway.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="resultTask">The task producing the result to extract a value from.</param>
    /// <param name="errorMessage">
    /// Optional custom exception message used when the result is a failure. When
    /// <see langword="null"/>, a default message is synthesized from
    /// <c>typeof(<typeparamref name="TValue"/>).Name</c>, <see cref="Error.Code"/>, and
    /// <see cref="Error.GetDisplayMessage"/>.
    /// </param>
    /// <returns>The success value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resultTask"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the awaited result is a failure.</exception>
    public static async Task<TValue> GetValueOrThrowAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return result.GetValueOrThrow(errorMessage);
    }

    /// <summary>
    /// Awaits the value-task and returns the success value, or throws
    /// <see cref="InvalidOperationException"/> if the result is a failure. This is a terminal
    /// operator that exits the Result railway.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="resultTask">The value-task producing the result to extract a value from.</param>
    /// <param name="errorMessage">
    /// Optional custom exception message used when the result is a failure. When
    /// <see langword="null"/>, a default message is synthesized from
    /// <c>typeof(<typeparamref name="TValue"/>).Name</c>, <see cref="Error.Code"/>, and
    /// <see cref="Error.GetDisplayMessage"/>.
    /// </param>
    /// <returns>The success value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the awaited result is a failure.</exception>
    public static async ValueTask<TValue> GetValueOrThrowAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string? errorMessage = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.GetValueOrThrow(errorMessage);
    }

    private static string BuildDefaultMessage<TValue>(Error error) =>
        $"Result<{typeof(TValue).Name}> was a failure. Error: [{error.Kind}] {error.GetDisplayMessage()}";
}