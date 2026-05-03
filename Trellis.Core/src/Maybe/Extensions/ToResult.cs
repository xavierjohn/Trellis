namespace Trellis;

/// <summary>
/// Provides extension methods for converting <see cref="Maybe{TValue}"/> instances to <see cref="Result{TValue}"/>
/// objects and for wrapping values in a <see cref="Result{TValue}"/>.
/// </summary>
/// <remarks>
/// These extension methods enable seamless transitions between optional and result-based value
/// representations, supporting functional programming patterns. They help unify error handling and value presence
/// checks in codebases that use both <see cref="Maybe{TValue}"/> and <see cref="Result{TValue}"/> types.
/// </remarks>
public static partial class MaybeExtensions
{
    /// <summary>
    /// Converts a <see cref="Maybe{TValue}"/> to a <see cref="Result{TValue}"/>.
    /// If the Maybe has a value, returns success; otherwise, returns failure with the specified error.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in Maybe.</typeparam>
    /// <param name="maybe">The Maybe instance to convert.</param>
    /// <param name="error">The error to return if the Maybe has no value.</param>
    /// <returns>A Result containing the Maybe's value or the specified error.</returns>
    public static Result<TValue> ToResult<TValue>(in this Maybe<TValue> maybe, Error error)
        where TValue : notnull
    {
        if (maybe.HasNoValue)
            return Result.Fail<TValue>(error);

        return Result.Ok(maybe.GetValueOrThrow());
    }

    /// <summary>
    /// Converts a <see cref="Maybe{TValue}"/> to a <see cref="Result{TValue}"/> using a function to create the error.
    /// If the Maybe has a value, returns success; otherwise, returns failure with an error from the function.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in Maybe.</typeparam>
    /// <param name="maybe">The Maybe instance to convert.</param>
    /// <param name="ferror">A function that produces the error if the Maybe has no value.</param>
    /// <returns>A Result containing the Maybe's value or an error from the function.</returns>
    public static Result<TValue> ToResult<TValue>(in this Maybe<TValue> maybe, Func<Error> ferror)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(ferror);
        if (maybe.HasNoValue)
            return Result.Fail<TValue>(ferror());

        return Result.Ok(maybe.GetValueOrThrow());
    }

    /// <summary>
    /// Wraps a value in a <see cref="Result{TValue}"/> as a success.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A successful Result containing the value.</returns>
    public static Result<TValue> ToResult<TValue>(this TValue value) => Result.Ok(value);
}

/// <summary>
/// Provides asynchronous extension methods for converting <see cref="Maybe{TValue}"/> instances to <see cref="Result{TValue}"/>.
/// </summary>
public static partial class MaybeExtensionsAsync
{
    /// <summary>
    /// Asynchronously converts a <see cref="Maybe{TValue}"/> to a <see cref="Result{TValue}"/>.
    /// If the Maybe has a value, returns success; otherwise, returns failure with the specified error.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in Maybe.</typeparam>
    /// <param name="maybeTask">The task containing the Maybe instance to convert.</param>
    /// <param name="error">The error to return if the Maybe has no value.</param>
    /// <returns>A task containing a Result with the Maybe's value or the specified error.</returns>
    public static async Task<Result<TValue>> ToResultAsync<TValue>(this Task<Maybe<TValue>> maybeTask, Error error)
        where TValue : notnull
    {
        var maybe = await maybeTask.ConfigureAwait(false);
        return maybe.ToResult(error);
    }

    /// <summary>
    /// Asynchronously converts a <see cref="Maybe{TValue}"/> to a <see cref="Result{TValue}"/> using a ValueTask.
    /// If the Maybe has a value, returns success; otherwise, returns failure with the specified error.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in Maybe.</typeparam>
    /// <param name="maybeTask">The ValueTask containing the Maybe instance to convert.</param>
    /// <param name="error">The error to return if the Maybe has no value.</param>
    /// <returns>A ValueTask containing a Result with the Maybe's value or the specified error.</returns>
    public static async ValueTask<Result<TValue>> ToResultAsync<TValue>(this ValueTask<Maybe<TValue>> maybeTask, Error error)
        where TValue : notnull
    {
        Maybe<TValue> maybe = await maybeTask.ConfigureAwait(false);
        return maybe.ToResult(error);
    }

    /// <summary>
    /// Asynchronously converts a <see cref="Maybe{TValue}"/> to a <see cref="Result{TValue}"/> using a function to create the error.
    /// If the Maybe has a value, returns success; otherwise, returns failure with an error from the function.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in Maybe.</typeparam>
    /// <param name="maybeTask">The task containing the Maybe instance to convert.</param>
    /// <param name="ferror">A function that produces the error if the Maybe has no value.</param>
    /// <returns>A task containing a Result with the Maybe's value or an error from the function.</returns>
    public static async Task<Result<TValue>> ToResultAsync<TValue>(this Task<Maybe<TValue>> maybeTask, Func<Error> ferror)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(ferror);
        var maybe = await maybeTask.ConfigureAwait(false);
        return maybe.ToResult(ferror);
    }

    /// <summary>
    /// Asynchronously converts a <see cref="Maybe{TValue}"/> to a <see cref="Result{TValue}"/> using a ValueTask and a function to create the error.
    /// If the Maybe has a value, returns success; otherwise, returns failure with an error from the function.
    /// </summary>
    /// <typeparam name="TValue">Type of the value contained in Maybe.</typeparam>
    /// <param name="maybeTask">The ValueTask containing the Maybe instance to convert.</param>
    /// <param name="ferror">A function that produces the error if the Maybe has no value.</param>
    /// <returns>A ValueTask containing a Result with the Maybe's value or an error from the function.</returns>
    public static async ValueTask<Result<TValue>> ToResultAsync<TValue>(this ValueTask<Maybe<TValue>> maybeTask, Func<Error> ferror)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(ferror);
        Maybe<TValue> maybe = await maybeTask.ConfigureAwait(false);
        return maybe.ToResult(ferror);
    }
}
