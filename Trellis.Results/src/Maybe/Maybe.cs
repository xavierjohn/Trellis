namespace Trellis;

/// <summary>
/// Contains static methods to create a <see cref="Maybe{T}"/> object.
/// </summary>
public static class Maybe
{
    /// <summary>
    /// Creates a new <see cref="Maybe{T}"/> from a value.
    /// If the value is null, creates an empty Maybe.
    /// </summary>
    /// <typeparam name="T">The type of the value. Must be a non-null type.</typeparam>
    /// <param name="value">The value to wrap. If null, returns <see cref="Maybe{T}.None"/>.</param>
    /// <returns>A <see cref="Maybe{T}"/> object with the value, or None if null.</returns>
    public static Maybe<T> From<T>(T? value) where T : notnull => new(value);

    /// <summary>
    /// Converts an optional nullable reference type to a strongly typed value object wrapped in <see cref="Maybe{TOut}"/>.
    /// </summary>
    /// <typeparam name="TIn">The nullable reference input type.</typeparam>
    /// <typeparam name="TOut">The validated output type.</typeparam>
    /// <param name="value">The nullable input. If null, returns <c>Result.Success(Maybe&lt;TOut&gt;.None)</c>.</param>
    /// <param name="function">A function that validates the input and returns a <see cref="Result{TOut}"/>.</param>
    /// <returns>
    ///     <list type="table">
    ///         <listheader>
    ///             <term>State</term>
    ///             <description>Return</description>
    ///         </listheader>
    ///         <item>
    ///             <term><paramref name="value"/> is null</term>
    ///             <description>Maybe&lt;<typeparamref name="TOut"/>&gt; without value.</description>
    ///         </item>
    ///         <item>
    ///             <term><paramref name="value"/> is not null and <paramref name="function"/> returned Success</term>
    ///             <description>Maybe&lt;<typeparamref name="TOut"/>&gt; with value from <paramref name="function"/>.</description>
    ///         </item>
    ///         <item>
    ///             <term><paramref name="value"/> is not null and <paramref name="function"/> returned Failure</term>
    ///             <description>The <see cref="Error" /> from the <paramref name="function"/>.</description>
    ///         </item>
    ///     </list>
    /// </returns>
    /// <example>
    /// <code>
    /// string? zipCode = "98052";
    /// var result = Maybe.Optional(zipCode, ZipCode.TryCreate);
    /// </code>
    /// </example>
    public static Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function)
        where TIn : class
        where TOut : notnull
    {
        if (value is null)
            return Maybe<TOut>.None;

        return function(value).Map(r => Maybe.From(r));
    }

    /// <summary>
    /// Converts an optional nullable value type to a strongly typed value object wrapped in <see cref="Maybe{TOut}"/>.
    /// </summary>
    /// <typeparam name="TIn">The nullable value input type.</typeparam>
    /// <typeparam name="TOut">The validated output type.</typeparam>
    /// <param name="value">The nullable input. If null, returns <c>Result.Success(Maybe&lt;TOut&gt;.None)</c>.</param>
    /// <param name="function">A function that validates the input and returns a <see cref="Result{TOut}"/>.</param>
    /// <returns>
    ///     <list type="table">
    ///         <listheader>
    ///             <term>State</term>
    ///             <description>Return</description>
    ///         </listheader>
    ///         <item>
    ///             <term><paramref name="value"/> is null</term>
    ///             <description>Maybe&lt;<typeparamref name="TOut"/>&gt; without value.</description>
    ///         </item>
    ///         <item>
    ///             <term><paramref name="value"/> has value and <paramref name="function"/> returned Success</term>
    ///             <description>Maybe&lt;<typeparamref name="TOut"/>&gt; with value from <paramref name="function"/>.</description>
    ///         </item>
    ///         <item>
    ///             <term><paramref name="value"/> has value and <paramref name="function"/> returned Failure</term>
    ///             <description>The <see cref="Error" /> from the <paramref name="function"/>.</description>
    ///         </item>
    ///     </list>
    /// </returns>
    /// <example>
    /// <code>
    /// int? quantity = 5;
    /// var result = Maybe.Optional(quantity, Quantity.TryCreate);
    /// </code>
    /// </example>
    public static Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function)
        where TIn : struct
        where TOut : notnull
    {
        if (!value.HasValue)
            return Maybe<TOut>.None;

        return function(value.Value).Map(r => Maybe.From(r));
    }
}