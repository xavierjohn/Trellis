namespace Trellis;

/// <summary>
/// Asynchronous <c>MatchAsync</c> extension methods for <see cref="Task{TResult}"/> and
/// <see cref="ValueTask{TResult}"/> of <see cref="Maybe{TValue}"/>.
/// </summary>
/// <remarks>
/// Mirrors the synchronous <see cref="Maybe{T}.Match{TResult}(System.Func{T, TResult}, System.Func{TResult})"/>
/// pattern, awaiting the upstream maybe carrier and dispatching to either the <c>some</c> or <c>none</c>
/// branch. Both sync and async branch overloads are provided.
/// </remarks>
public static partial class MaybeExtensionsAsync
{
    /// <summary>
    /// Asynchronously pattern-matches on a <see cref="Maybe{TValue}"/> produced by a <see cref="Task{TResult}"/>,
    /// invoking the synchronous <paramref name="some"/> branch when a value is present or the synchronous
    /// <paramref name="none"/> branch otherwise.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in the Maybe.</typeparam>
    /// <typeparam name="TResult">The return type of the match.</typeparam>
    /// <param name="maybeTask">The task containing the Maybe instance to match on.</param>
    /// <param name="some">The function to invoke when the Maybe has a value.</param>
    /// <param name="none">The function to invoke when the Maybe has no value.</param>
    /// <returns>A task containing the result of the matched branch.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="some"/> or <paramref name="none"/> is null.</exception>
    public static async Task<TResult> MatchAsync<TValue, TResult>(
        this Task<Maybe<TValue>> maybeTask,
        Func<TValue, TResult> some,
        Func<TResult> none)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(maybeTask);
        ArgumentNullException.ThrowIfNull(some);
        ArgumentNullException.ThrowIfNull(none);

        var maybe = await maybeTask.ConfigureAwait(false);
        return maybe.Match(some, none);
    }

    /// <summary>
    /// Asynchronously pattern-matches on a <see cref="Maybe{TValue}"/> produced by a <see cref="ValueTask{TResult}"/>,
    /// invoking the synchronous <paramref name="some"/> branch when a value is present or the synchronous
    /// <paramref name="none"/> branch otherwise.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in the Maybe.</typeparam>
    /// <typeparam name="TResult">The return type of the match.</typeparam>
    /// <param name="maybeTask">The ValueTask containing the Maybe instance to match on.</param>
    /// <param name="some">The function to invoke when the Maybe has a value.</param>
    /// <param name="none">The function to invoke when the Maybe has no value.</param>
    /// <returns>A ValueTask containing the result of the matched branch.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="some"/> or <paramref name="none"/> is null.</exception>
    public static async ValueTask<TResult> MatchAsync<TValue, TResult>(
        this ValueTask<Maybe<TValue>> maybeTask,
        Func<TValue, TResult> some,
        Func<TResult> none)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(some);
        ArgumentNullException.ThrowIfNull(none);

        Maybe<TValue> maybe = await maybeTask.ConfigureAwait(false);
        return maybe.Match(some, none);
    }

    /// <summary>
    /// Asynchronously pattern-matches on a <see cref="Maybe{TValue}"/> produced by a <see cref="Task{TResult}"/>,
    /// awaiting the asynchronous <paramref name="some"/> branch when a value is present or the asynchronous
    /// <paramref name="none"/> branch otherwise.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in the Maybe.</typeparam>
    /// <typeparam name="TResult">The return type of the match.</typeparam>
    /// <param name="maybeTask">The task containing the Maybe instance to match on.</param>
    /// <param name="some">The asynchronous function to invoke when the Maybe has a value.</param>
    /// <param name="none">The asynchronous function to invoke when the Maybe has no value.</param>
    /// <returns>A task containing the result of the awaited branch.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="some"/> or <paramref name="none"/> is null.</exception>
    public static async Task<TResult> MatchAsync<TValue, TResult>(
        this Task<Maybe<TValue>> maybeTask,
        Func<TValue, Task<TResult>> some,
        Func<Task<TResult>> none)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(maybeTask);
        ArgumentNullException.ThrowIfNull(some);
        ArgumentNullException.ThrowIfNull(none);

        var maybe = await maybeTask.ConfigureAwait(false);
        if (maybe.HasValue)
            return await some(maybe.GetValueOrThrow()).ConfigureAwait(false);

        return await none().ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously pattern-matches on a <see cref="Maybe{TValue}"/> produced by a <see cref="ValueTask{TResult}"/>,
    /// awaiting the asynchronous <paramref name="some"/> branch when a value is present or the asynchronous
    /// <paramref name="none"/> branch otherwise.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in the Maybe.</typeparam>
    /// <typeparam name="TResult">The return type of the match.</typeparam>
    /// <param name="maybeTask">The ValueTask containing the Maybe instance to match on.</param>
    /// <param name="some">The asynchronous function to invoke when the Maybe has a value.</param>
    /// <param name="none">The asynchronous function to invoke when the Maybe has no value.</param>
    /// <returns>A ValueTask containing the result of the awaited branch.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="some"/> or <paramref name="none"/> is null.</exception>
    public static async ValueTask<TResult> MatchAsync<TValue, TResult>(
        this ValueTask<Maybe<TValue>> maybeTask,
        Func<TValue, ValueTask<TResult>> some,
        Func<ValueTask<TResult>> none)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(some);
        ArgumentNullException.ThrowIfNull(none);

        Maybe<TValue> maybe = await maybeTask.ConfigureAwait(false);
        if (maybe.HasValue)
            return await some(maybe.GetValueOrThrow()).ConfigureAwait(false);

        return await none().ConfigureAwait(false);
    }
}