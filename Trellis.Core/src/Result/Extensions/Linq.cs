namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Provides LINQ query expression support for Result types, enabling C# query syntax for functional operations.
/// </summary>
/// <remarks>
/// <para>
/// These extension methods allow you to use LINQ query syntax (from, select, where) with Result types,
/// making functional composition more readable and familiar to C# developers.
/// </para>
/// <para>
/// The mapping is:
/// - Select maps to <see cref="MapExtensions.Map{TIn, TOut}(Result{TIn}, Func{TIn, TOut})"/>
/// - SelectMany maps to <see cref="BindExtensions.Bind{TIn, TOut}(Result{TIn}, Func{TIn, Result{TOut}})"/>
/// - Where maps to <see cref="EnsureExtensions.Ensure{T}(Result{T}, Func{T, bool}, Error)"/>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Using LINQ query syntax with Result
/// var result = from firstName in FirstName.TryCreate(firstNameInput)
///              from lastName in LastName.TryCreate(lastNameInput)
///              from email in EmailAddress.TryCreate(emailInput)
///              where email.Value.EndsWith("@company.com")
///              select new User(firstName, lastName, email);
///              
/// // This is equivalent to:
/// var result2 = FirstName.TryCreate(firstNameInput)
///     .Bind(firstName => LastName.TryCreate(lastNameInput)
///         .Bind(lastName => EmailAddress.TryCreate(emailInput)
///             .Ensure(email => email.Value.EndsWith("@company.com"), 
///                     Error.UnprocessableContent.ForField("email", "invalid", "Must be company email"))
///             .Map(email => new User(firstName, lastName, email))));
/// </code>
/// </example>
[DebuggerStepThrough]
public static class ResultLinqExtensions
{
    /// <summary>
    /// Projects the value of a successful Result using a selector function (LINQ Select operation).
    /// Maps to <see cref="MapExtensions.Map{TIn, TOut}(Result{TIn}, Func{TIn, TOut})"/>.
    /// </summary>
    /// <typeparam name="TIn">The type of the input value.</typeparam>
    /// <typeparam name="TOut">The type of the output value.</typeparam>
    /// <param name="result">The result to project.</param>
    /// <param name="selector">The projection function to apply to the value.</param>
    /// <returns>A new Result with the projected value, or the original failure.</returns>
    public static Result<TOut> Select<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> selector) =>
        result.Map(selector);

    /// <summary>
    /// Projects each value of a Result to a new Result and flattens the result (LINQ SelectMany operation).
    /// Enables composition of multiple Result-returning operations in LINQ query syntax.
    /// </summary>
    /// <typeparam name="TSource">The type of the source value.</typeparam>
    /// <typeparam name="TCollection">The type of the intermediate collection value.</typeparam>
    /// <typeparam name="TResult">The type of the final result value.</typeparam>
    /// <param name="source">The source result.</param>
    /// <param name="collectionSelector">A function that returns a Result based on the source value.</param>
    /// <param name="resultSelector">A function to create the final result from the source and collection values.</param>
    /// <returns>A new Result with the final projected value, or the first failure encountered.</returns>
    /// <remarks>
    /// This is the key method that enables LINQ query syntax with multiple 'from' clauses.
    /// It performs monadic bind followed by a projection.
    /// </remarks>
    public static Result<TResult> SelectMany<TSource, TCollection, TResult>(
        this Result<TSource> source,
        Func<TSource, Result<TCollection>> collectionSelector,
        Func<TSource, TCollection, TResult> resultSelector)
        => source.Bind(s => collectionSelector(s).Map(c => resultSelector(s, c)));

    /// <summary>
    /// Filters a Result based on a predicate (LINQ Where operation).
    /// If the predicate returns false, converts the success Result to a failure.
    /// </summary>
    /// <typeparam name="TSource">The type of the source value.</typeparam>
    /// <param name="source">The result to filter.</param>
    /// <param name="predicate">The predicate to test the value against.</param>
    /// <returns>The original success Result if the predicate is true; otherwise a failure Result with a generic error.</returns>
    /// <remarks>
    /// <para>
    /// This method returns a generic "filtered out" error message.
    /// For more meaningful error messages, use <see cref="EnsureExtensions.Ensure{T}(Result{T}, Func{T, bool}, Error)"/> directly instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Using Where in query syntax (generic error)
    /// var result = from user in GetUser(id)
    ///              where user.IsActive
    ///              select user;
    ///              
    /// // Better: Use Ensure for custom error
    /// var betterResult = GetUser(id)
    ///     .Ensure(u => u.IsActive, Error.UnprocessableContent.ForRule("inactive", "User is not active"));
    /// </code>
    /// </example>
    public static Result<TSource> Where<TSource>(this Result<TSource> source, Func<TSource, bool> predicate)
        => source.Ensure(predicate, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Result filtered out by predicate." });
}