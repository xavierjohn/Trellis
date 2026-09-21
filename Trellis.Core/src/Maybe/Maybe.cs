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
    /// <param name="value">The nullable input. If null, returns <c>Result.Ok(Maybe&lt;TOut&gt;.None)</c>.</param>
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
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    public static Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function)
        where TIn : class
        where TOut : notnull
    {
        ArgumentNullException.ThrowIfNull(function);

        if (value is null)
            return Result.Ok<Maybe<TOut>>(Maybe<TOut>.None);

        return function(value).Map(r => Maybe.From(r));
    }

    /// <summary>
    /// Validates an optional string, treating null, empty, or whitespace-only input as absence.
    /// </summary>
    /// <typeparam name="TOut">The validated output type.</typeparam>
    /// <param name="value">The optional string. Nonblank input is passed unchanged to the function.</param>
    /// <param name="function">A validation function invoked exactly once for nonblank input, and never for blank input.</param>
    /// <returns>Success with <see cref="Maybe{TOut}.None"/> for blank input; otherwise the function's
    /// successful value wrapped in <see cref="Maybe{TOut}"/>, or its failure.</returns>
    /// <remarks>
    /// Blankness follows <see cref="string.IsNullOrWhiteSpace(string?)"/>. This method does not trim
    /// nonblank input. Factory failures retain their persist-on-failure intent, and exceptions propagate.
    /// Unlike this opt-in helper, <c>Maybe.Optional</c> treats only null as absence.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null, even when the input is blank.</exception>
    public static Result<Maybe<TOut>> OptionalNonBlank<TOut>(string? value, Func<string, Result<TOut>> function)
        where TOut : notnull =>
        Optional(string.IsNullOrWhiteSpace(value) ? null : value, function);

    /// <summary>
    /// Converts an optional nullable value type to a strongly typed value object wrapped in <see cref="Maybe{TOut}"/>.
    /// </summary>
    /// <typeparam name="TIn">The nullable value input type.</typeparam>
    /// <typeparam name="TOut">The validated output type.</typeparam>
    /// <param name="value">The nullable input. If null, returns <c>Result.Ok(Maybe&lt;TOut&gt;.None)</c>.</param>
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
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    public static Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function)
        where TIn : struct
        where TOut : notnull
    {
        ArgumentNullException.ThrowIfNull(function);

        if (!value.HasValue)
            return Result.Ok<Maybe<TOut>>(Maybe<TOut>.None);

        return function(value.Value).Map(r => Maybe.From(r));
    }
}