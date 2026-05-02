namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides adapters that wrap already-computed <see cref="Maybe{T}"/> values in completed task-like carriers.
/// Useful for disambiguating <see cref="Task{T}"/> versus <see cref="ValueTask{T}"/> overloads at call sites
/// (including LINQ query expressions over async Maybe carriers).
/// </summary>
[DebuggerStepThrough]
public static class MaybeTaskAdapterExtensions
{
    /// <summary>
    /// Wraps the maybe in a completed <see cref="Task{TResult}"/>.
    /// </summary>
    /// <typeparam name="T">The optional value type.</typeparam>
    /// <param name="maybe">The maybe to wrap.</param>
    /// <returns>A completed task containing <paramref name="maybe"/>.</returns>
    public static Task<Maybe<T>> AsTask<T>(this Maybe<T> maybe)
        where T : notnull =>
        Task.FromResult(maybe);

    /// <summary>
    /// Wraps the maybe in a completed <see cref="ValueTask{TResult}"/>.
    /// </summary>
    /// <typeparam name="T">The optional value type.</typeparam>
    /// <param name="maybe">The maybe to wrap.</param>
    /// <returns>A completed value task containing <paramref name="maybe"/>.</returns>
    public static ValueTask<Maybe<T>> AsValueTask<T>(this Maybe<T> maybe)
        where T : notnull =>
        new(maybe);
}