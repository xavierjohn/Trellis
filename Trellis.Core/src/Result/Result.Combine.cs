namespace Trellis;

/// <summary>
/// Static Combine methods for combining multiple independent Result values without chaining.
/// </summary>
/// <remarks>
/// Use <c>Result.Combine(r1, r2, r3)</c> when you already have individual Result variables
/// and want to combine them before error checking. This is syntactic sugar over chaining:
/// <code>
/// // These are equivalent:
/// Result.Combine(r1, r2, r3)
/// r1.Combine(r2).Combine(r3)
/// </code>
/// </remarks>
public readonly partial struct Result
{
    /// <summary>
    /// Combines two independent <see cref="Result{TValue}"/> instances into a single tuple result.
    /// </summary>
    /// <typeparam name="T1">Type of the first result value.</typeparam>
    /// <typeparam name="T2">Type of the second result value.</typeparam>
    /// <param name="r1">First result.</param>
    /// <param name="r2">Second result.</param>
    /// <returns>
    /// A success result with a 2-element tuple if both succeed; otherwise a failure with combined errors.
    /// </returns>
    /// <example>
    /// <code>
    /// var emailResult = EmailAddress.TryCreate(dto.Email);
    /// var nameResult = FirstName.TryCreate(dto.Name);
    ///
    /// var result = Result.Combine(emailResult, nameResult)
    ///     .Bind((email, name) => User.Create(email, name));
    /// </code>
    /// </example>
    public static Result<(T1, T2)> Combine<T1, T2>(
        Result<T1> r1, Result<T2> r2)
        => r1.Combine(r2);
}