namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Extension methods for safely querying collections, returning <see cref="Maybe{T}"/> instead of throwing.
/// </summary>
[DebuggerStepThrough]
public static class MaybeTryFirstExtensions
{
    /// <summary>
    /// Returns the first element of a sequence wrapped in a <see cref="Maybe{T}"/>,
    /// or <see cref="Maybe{T}.None"/> if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to return the first element of.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the first element, or <see cref="Maybe{T}.None"/> if the sequence is empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public static Maybe<T> TryFirst<T>(this IEnumerable<T> source) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach (var item in source)
            return Maybe.From(item);

        return Maybe<T>.None;
    }

    /// <summary>
    /// Returns the first element of a sequence that satisfies a condition wrapped in a <see cref="Maybe{T}"/>,
    /// or <see cref="Maybe{T}.None"/> if no such element is found.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to search.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the first matching element, or <see cref="Maybe{T}.None"/> if no match is found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Maybe<T> TryFirst<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (var item in source)
        {
            if (predicate(item))
                return Maybe.From(item);
        }

        return Maybe<T>.None;
    }

    /// <summary>
    /// Returns the last element of a sequence wrapped in a <see cref="Maybe{T}"/>,
    /// or <see cref="Maybe{T}.None"/> if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to return the last element of.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the last element, or <see cref="Maybe{T}.None"/> if the sequence is empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public static Maybe<T> TryLast<T>(this IEnumerable<T> source) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = false;
        T last = default!;

        foreach (var item in source)
        {
            found = true;
            last = item;
        }

        return found ? Maybe.From(last) : Maybe<T>.None;
    }

    /// <summary>
    /// Returns the last element of a sequence that satisfies a condition wrapped in a <see cref="Maybe{T}"/>,
    /// or <see cref="Maybe{T}.None"/> if no such element is found.
    /// </summary>
    /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to search.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the last matching element, or <see cref="Maybe{T}.None"/> if no match is found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="predicate"/> is null.</exception>
    public static Maybe<T> TryLast<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        var found = false;
        T last = default!;

        foreach (var item in source)
        {
            if (predicate(item))
            {
                found = true;
                last = item;
            }
        }

        return found ? Maybe.From(last) : Maybe<T>.None;
    }
}