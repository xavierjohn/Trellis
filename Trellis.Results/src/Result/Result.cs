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
    /// <typeparam name="TValue">Type of the success value.</typeparam>
    /// <param name="value">Value to wrap in a successful result (may be null for reference types).</param>
    /// <returns>A successful <see cref="Result{TValue}"/> containing <paramref name="value"/>.</returns>
    public static Result<TValue> Success<TValue>(TValue value) =>
        new(false, value, default);

    /// <summary>
    /// Creates a successful result by invoking the supplied factory function.
    /// </summary>
    /// <typeparam name="TValue">Type of the success value.</typeparam>
    /// <param name="funcOk">Factory function producing the value. Must not be null.</param>
    /// <returns>A successful <see cref="Result{TValue}"/> containing the produced value.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="funcOk"/> is null.</exception>
    public static Result<TValue> Success<TValue>(Func<TValue> funcOk)
    {
        ArgumentNullException.ThrowIfNull(funcOk);
        TValue value = funcOk();
        return new(false, value, default);
    }

    /// <summary>
    /// Creates a failed result with the specified <paramref name="error"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the (missing) success value.</typeparam>
    /// <param name="error">Error describing the failure.</param>
    /// <returns>A failed <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> Failure<TValue>(Error error) =>
        new(true, default, error);

    /// <summary>
    /// Creates a failed result using a deferred error factory.
    /// </summary>
    /// <typeparam name="TValue">Type of the (missing) success value.</typeparam>
    /// <param name="error">Factory function producing an <see cref="Error"/>.</param>
    /// <returns>A failed <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> Failure<TValue>(Func<Error> error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error err = error();
        return new(true, default, err);
    }

    /// <summary>
    /// Returns a success or failure result based on <paramref name="isSuccess"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the success value.</typeparam>
    /// <param name="isSuccess">If true returns success; otherwise failure.</param>
    /// <param name="value">Value for the success case.</param>
    /// <param name="error">Error for the failure case.</param>
    /// <returns>A success or failure <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> SuccessIf<TValue>(bool isSuccess, in TValue value, Error error)
        => isSuccess ? Success(value) : Failure<TValue>(error);

    /// <summary>
    /// Returns a success (tuple) or failure result based on <paramref name="isSuccess"/>.
    /// </summary>
    /// <typeparam name="T1">Type of first value.</typeparam>
    /// <typeparam name="T2">Type of second value.</typeparam>
    /// <param name="isSuccess">If true returns success; otherwise failure.</param>
    /// <param name="t1">First value for the success case.</param>
    /// <param name="t2">Second value for the success case.</param>
    /// <param name="error">Error for the failure case.</param>
    /// <returns>A success or failure <see cref="Result{TValue}"/> with a tuple payload.</returns>
    public static Result<(T1, T2)> SuccessIf<T1, T2>(bool isSuccess, in T1 t1, in T2 t2, Error error)
        => isSuccess ? Success((t1, t2)) : Failure<(T1, T2)>(error);

    /// <summary>
    /// Returns failure if <paramref name="isFailure"/> is true; otherwise success with <paramref name="value"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the value.</typeparam>
    /// <param name="isFailure">If true produce a failure result.</param>
    /// <param name="value">Success value when not failing.</param>
    /// <param name="error">Error when failing.</param>
    /// <returns>A success or failure <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> FailureIf<TValue>(bool isFailure, TValue value, Error error)
        => SuccessIf(!isFailure, value, error);

    /// <summary>
    /// Returns failure if the provided predicate returns true; otherwise success with <paramref name="value"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the value.</typeparam>
    /// <param name="failurePredicate">Predicate indicating a failure condition.</param>
    /// <param name="value">Success value when predicate is false.</param>
    /// <param name="error">Error when predicate is true.</param>
    /// <returns>A success or failure <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> FailureIf<TValue>(Func<bool> failurePredicate, in TValue value, Error error)
        => SuccessIf(!failurePredicate(), value, error);

    /// <summary>
    /// Asynchronously determines success/failure using <paramref name="predicate"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the success value.</typeparam>
    /// <param name="predicate">Async predicate producing true for success.</param>
    /// <param name="value">Success value if predicate is true.</param>
    /// <param name="error">Error if predicate is false.</param>
    /// <returns>A task producing a success or failure <see cref="Result{TValue}"/>.</returns>
    public static async Task<Result<TValue>> SuccessIfAsync<TValue>(Func<Task<bool>> predicate, TValue value, Error error)
    {
        bool isSuccess = await predicate().ConfigureAwait(false);
        return SuccessIf(isSuccess, value, error);
    }

    /// <summary>
    /// Asynchronously determines failure/success using <paramref name="failurePredicate"/> (inverse semantics of <see cref="SuccessIfAsync{TValue}(Func{Task{bool}}, TValue, Error)"/>).
    /// </summary>
    /// <typeparam name="TValue">Type of the value.</typeparam>
    /// <param name="failurePredicate">Async predicate producing true for failure.</param>
    /// <param name="value">Success value if predicate is false.</param>
    /// <param name="error">Error if predicate is true.</param>
    /// <returns>A task producing a success or failure <see cref="Result{TValue}"/>.</returns>
    public static async Task<Result<TValue>> FailureIfAsync<TValue>(Func<Task<bool>> failurePredicate, TValue value, Error error)
    {
        bool isFailure = await failurePredicate().ConfigureAwait(false);
        return SuccessIf(!isFailure, value, error);
    }

    /// <summary>
    /// Creates a successful unit result (no payload).
    /// </summary>
    /// <returns>A successful <see cref="Result{TValue}"/> of <see cref="Unit"/>.</returns>
    public static Result<Unit> Success() =>
        new(false, default, default);

    /// <summary>
    /// Creates a failed unit result with the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">Error describing the failure.</param>
    /// <returns>A failed <see cref="Result{TValue}"/> of <see cref="Unit"/>.</returns>
    public static Result<Unit> Failure(Error error) =>
        new(true, default, error);

    // --- Exception capture helpers --------------------------------------------------

    /// <summary>
    /// Executes the function and converts exceptions to a failed result using the optional mapper (default maps to <see cref="Error.Unexpected(string, string?, string?)"/>).
    /// </summary>
    /// <typeparam name="T">Type of the produced value.</typeparam>
    /// <param name="func">Function to execute.</param>
    /// <param name="map">Optional exception-to-error mapper. If null, a default Unexpected error is used.</param>
    /// <returns>A success result with the value or a failure result if an exception was thrown.</returns>
    public static Result<T> Try<T>(Func<T> func, Func<Exception, Error>? map = null)
    {
        try
        {
            return Success(func());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure<T>((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Executes the asynchronous function and converts exceptions to a failed result using the optional mapper (default maps to Unexpected).
    /// </summary>
    /// <typeparam name="T">Type of the produced value.</typeparam>
    /// <param name="func">Asynchronous function to execute.</param>
    /// <param name="map">Optional exception-to-error mapper. If null, a default Unexpected error is used.</param>
    /// <returns>A task producing either a success or failure result.</returns>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error>? map = null)
    {
        try
        {
            return Success(await func().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure<T>((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Converts an exception to a failed unit result using the optional mapper (default Unexpected).
    /// </summary>
    /// <param name="ex">Exception to convert.</param>
    /// <param name="map">Optional exception-to-error mapper.</param>
    /// <returns>A failed unit result.</returns>
    public static Result<Unit> FromException(Exception ex, Func<Exception, Error>? map = null) =>
        Failure((map ?? DefaultExceptionMapper)(ex));

    /// <summary>
    /// Converts an exception to a failed result of type <typeparamref name="T"/> using the optional mapper (default Unexpected).
    /// </summary>
    /// <typeparam name="T">Type parameter of the target result.</typeparam>
    /// <param name="ex">Exception to convert.</param>
    /// <param name="map">Optional exception-to-error mapper.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> FromException<T>(Exception ex, Func<Exception, Error>? map = null) =>
        Failure<T>((map ?? DefaultExceptionMapper)(ex));

    /// <summary>
    /// Default mapper converting an exception into an <see cref="UnexpectedError"/>.
    /// </summary>
    /// <param name="ex">Exception that occurred.</param>
    /// <returns>An <see cref="UnexpectedError"/> containing the exception message.</returns>
    private static UnexpectedError DefaultExceptionMapper(Exception ex) =>
        Error.Unexpected(ex.Message);
}