namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides LINQ query expression support for Maybe types, enabling C# query syntax for optional value composition.
/// </summary>
/// <remarks>
/// <para>
/// These extension methods allow you to use LINQ query syntax (from, select) with Maybe types,
/// making functional composition of optional values more readable.
/// </para>
/// <para>
/// The mapping is:
/// - Select maps to <see cref="Maybe{T}.Map{TResult}(Func{T, TResult})"/>
/// - SelectMany enables monadic composition (flatMap) for chaining optional lookups
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Composing multiple optional values with LINQ query syntax
/// Maybe&lt;FullName&gt; fullName =
///     from first in firstName
///     from last in lastName
///     select new FullName(first, last);
///
/// // Chaining optional lookups
/// Maybe&lt;Email&gt; managerEmail =
///     from user in users.FindById(userId)
///     from manager in users.FindById(user.ManagerId)
///     from email in manager.Email
///     select email;
/// </code>
/// </example>
[DebuggerStepThrough]
public static class MaybeLinqExtensions
{
    /// <summary>
    /// Projects the value of a Maybe using a selector function (LINQ Select operation).
    /// Maps to <see cref="Maybe{T}.Map{TResult}(Func{T, TResult})"/>.
    /// </summary>
    /// <typeparam name="TIn">The type of the input value.</typeparam>
    /// <typeparam name="TOut">The type of the output value.</typeparam>
    /// <param name="maybe">The Maybe to project.</param>
    /// <param name="selector">The projection function to apply to the value.</param>
    /// <returns>A new Maybe with the projected value, or None if the input has no value.</returns>
    public static Maybe<TOut> Select<TIn, TOut>(this Maybe<TIn> maybe, Func<TIn, TOut> selector)
        where TIn : notnull
        where TOut : notnull
        => maybe.Map(selector);

    /// <summary>
    /// Projects each value of a Maybe to a new Maybe and flattens the result (LINQ SelectMany operation).
    /// Enables composition of multiple Maybe-returning operations in LINQ query syntax.
    /// </summary>
    /// <typeparam name="TSource">The type of the source value.</typeparam>
    /// <typeparam name="TCollection">The type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">The type of the final result value.</typeparam>
    /// <param name="source">The source Maybe.</param>
    /// <param name="collectionSelector">A function that returns a Maybe based on the source value.</param>
    /// <param name="resultSelector">A function to create the final result from the source and collection values.</param>
    /// <returns>A new Maybe with the final projected value, or None if any step produces None.</returns>
    /// <remarks>
    /// This is the key method that enables LINQ query syntax with multiple 'from' clauses.
    /// It performs monadic bind (flatMap) followed by a projection.
    /// </remarks>
    public static Maybe<TResult> SelectMany<TSource, TCollection, TResult>(
        this Maybe<TSource> source,
        Func<TSource, Maybe<TCollection>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
        where TSource : notnull
        where TCollection : notnull
        where TResult : notnull
    {
        if (source.HasNoValue)
            return default;

        var collection = collectionSelector(source.Value);
        if (collection.HasNoValue)
            return default;

        return Maybe.From(resultSelector(source.Value, collection.Value));
    }
}