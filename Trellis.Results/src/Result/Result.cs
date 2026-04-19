namespace Trellis;

using System;
using System.Threading.Tasks;

/// <summary>
/// Non-generic Result utility host containing factory and helper methods to construct <see cref="Result{TValue}"/> instances.
/// NOTE: This struct is not intended to be instantiated; all members are static.
/// </summary>
public readonly partial struct Result
{
    /// <summary>
    /// Creates a successful result wrapping the provided <paramref name="value"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the Ok value.</typeparam>
    /// <param name="value">Value to wrap in a successful result (may be null for reference types).</param>
    /// <returns>A successful <see cref="Result{TValue}"/> containing <paramref name="value"/>.</returns>
    public static Result<TValue> Ok<TValue>(TValue value) =>
        new(false, value, default);

    /// <summary>
    /// Creates a failed result with the specified <paramref name="error"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the (missing) Ok value.</typeparam>
    /// <param name="error">Error describing the Fail.</param>
    /// <returns>A failed <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> Fail<TValue>(Error error) =>
        new(true, default, error);

    /// <summary>
    /// Creates a successful unit result (no payload).
    /// </summary>
    /// <returns>A successful <see cref="Result{TValue}"/> of <see cref="Unit"/>.</returns>
    public static Result<Unit> Ok() =>
        new(false, default, default);

    /// <summary>
    /// Creates a failed unit result with the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">Error describing the Fail.</param>
    /// <returns>A failed <see cref="Result{TValue}"/> of <see cref="Unit"/>.</returns>
    public static Result<Unit> Fail(Error error) =>
        new(true, default, error);

    /// <summary>
    /// Returns a Ok result if the flag is true; otherwise returns a Fail with the specified error.
    /// </summary>
    /// <param name="flag">The boolean condition to test.</param>
    /// <param name="error">The error to return if the condition is false.</param>
    /// <returns>A Ok result if flag is true; otherwise a Fail with the specified error.</returns>
    public static Result<Unit> Ensure(bool flag, Error error)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var result = flag ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Returns a Ok result if the predicate is true; otherwise returns a Fail with the specified error.
    /// </summary>
    /// <param name="predicate">The predicate to evaluate.</param>
    /// <param name="error">The error to return if the predicate is false.</param>
    /// <returns>A Ok result if predicate is true; otherwise a Fail with the specified error.</returns>
    public static Result<Unit> Ensure(Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var result = predicate() ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously evaluates the predicate and returns Ok if true; otherwise returns a Fail with the specified error.
    /// </summary>
    /// <param name="predicate">Async predicate producing true for Ok.</param>
    /// <param name="error">The error to return if the predicate is false.</param>
    /// <returns>A task producing a Ok or Fail Result of Unit.</returns>
    public static async Task<Result<Unit>> EnsureAsync(Func<Task<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var isSuccess = await predicate().ConfigureAwait(false);
        var result = isSuccess ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    // --- Exception capture helpers --------------------------------------------------

    /// <summary>
    /// Executes the function and converts exceptions to a failed result using the optional mapper (default maps to <see cref="Error.Unexpected(string, string?, string?)"/>).
    /// </summary>
    /// <typeparam name="T">Type of the produced value.</typeparam>
    /// <param name="func">Function to execute.</param>
    /// <param name="map">Optional exception-to-error mapper. If null, a default Unexpected error is used.</param>
    /// <returns>A Ok result with the value or a Fail result if an exception was thrown.</returns>
    public static Result<T> Try<T>(Func<T> func, Func<Exception, Error>? map = null)
    {
        try
        {
            return Ok(func());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail<T>((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Executes the asynchronous function and converts exceptions to a failed result using the optional mapper (default maps to Unexpected).
    /// </summary>
    /// <typeparam name="T">Type of the produced value.</typeparam>
    /// <param name="func">Asynchronous function to execute.</param>
    /// <param name="map">Optional exception-to-error mapper. If null, a default Unexpected error is used.</param>
    /// <returns>A task producing either a Ok or Fail result.</returns>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error>? map = null)
    {
        try
        {
            return Ok(await func().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail<T>((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Default mapper converting an exception into an <see cref="UnexpectedError"/>.
    /// </summary>
    /// <param name="ex">Exception that occurred.</param>
    /// <returns>An <see cref="UnexpectedError"/> containing the exception message.</returns>
    private static UnexpectedError DefaultExceptionMapper(Exception ex) =>
        Error.Unexpected(ex.Message);
}