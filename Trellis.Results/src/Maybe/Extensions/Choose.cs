namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Extension methods for filtering and unwrapping <see cref="Maybe{T}"/> collections.
/// </summary>
[DebuggerStepThrough]
public static class MaybeChooseExtensions
{
    /// <summary>
    /// Filters a sequence of <see cref="Maybe{T}"/> values and returns only the underlying values
    /// where <see cref="Maybe{T}.HasValue"/> is true.
    /// </summary>
    /// <typeparam name="T">The type of the value contained in the <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The sequence of <see cref="Maybe{T}"/> values to filter.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> containing only the unwrapped values from items that have a value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public static IEnumerable<T> Choose<T>(this IEnumerable<Maybe<T>> source) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        return ChooseIterator(source);
    }

    /// <summary>
    /// Filters a sequence of <see cref="Maybe{T}"/> values, unwraps them, and transforms the values
    /// using the specified selector function.
    /// </summary>
    /// <typeparam name="T">The type of the value contained in the <see cref="Maybe{T}"/>.</typeparam>
    /// <typeparam name="TResult">The type of the transformed result.</typeparam>
    /// <param name="source">The sequence of <see cref="Maybe{T}"/> values to filter and transform.</param>
    /// <param name="selector">A transform function to apply to each unwrapped value.</param>
    /// <returns>An <see cref="IEnumerable{TResult}"/> containing the transformed values from items that have a value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="selector"/> is null.</exception>
    public static IEnumerable<TResult> Choose<T, TResult>(this IEnumerable<Maybe<T>> source, Func<T, TResult> selector)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);
        return ChooseIterator(source, selector);
    }

    private static IEnumerable<T> ChooseIterator<T>(IEnumerable<Maybe<T>> source) where T : notnull
    {
        foreach (var item in source)
        {
            if (item.HasValue)
                yield return item.Value;
        }
    }

    private static IEnumerable<TResult> ChooseIterator<T, TResult>(IEnumerable<Maybe<T>> source, Func<T, TResult> selector)
        where T : notnull
    {
        foreach (var item in source)
        {
            if (item.HasValue)
                yield return selector(item.Value);
        }
    }
}
