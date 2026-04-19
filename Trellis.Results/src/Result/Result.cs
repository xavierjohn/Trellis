namespace Trellis;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Represents the outcome of an operation that has no success payload — either success or failure with an <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// Non-generic <see cref="Result"/> is the v2 replacement for <c>Result&lt;Unit&gt;</c>. Use it for operations that
/// either succeed (no value) or fail (with an <see cref="Error"/>). The default value (<c>default(Result)</c>)
/// represents success — matching the default-state invariant of <see cref="Result{TValue}"/>.
/// </para>
/// <para>
/// This struct also hosts factory and helper methods to construct both <see cref="Result"/> and <see cref="Result{TValue}"/>
/// instances (e.g. <see cref="Ok()"/>, <see cref="Fail(Error)"/>, <see cref="Ok{TValue}(TValue)"/>, <see cref="Try{T}(Func{T}, Func{Exception, Error}?)"/>).
/// </para>
/// </remarks>
[DebuggerDisplay("{IsSuccess ? \"Success\" : \"Failure\"}, Error = {(_error is null ? \"<none>\" : _error.Code)}")]
public readonly partial struct Result : IResult, IEquatable<Result>, IFailureFactory<Result>
{
    private readonly Error? _error;

    /// <summary>True when the result represents success.</summary>
    public bool IsSuccess => !IsFailure;

    /// <summary>True when the result represents failure.</summary>
    public bool IsFailure { get; }

    internal Result(bool isFailure, Error? error)
    {
        if (isFailure)
        {
            if (error is null)
                throw new ArgumentException("If 'isFailure' is true, 'error' must not be null.", nameof(error));
        }
        else
        {
            if (error is not null)
                throw new ArgumentException("If 'isFailure' is false, 'error' must be null.", nameof(error));
        }

        IsFailure = isFailure;
        _error = error;

        Activity.Current?.SetStatus(IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Ok);

        if (IsFailure && Activity.Current is { } act && error is not null)
        {
            act.SetTag("result.error.code", error.Code);
        }
    }

    internal void LogActivityStatus() => Activity.Current?.SetStatus(IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Ok);

    /// <summary>
    /// Internal accessor for in-assembly use, mirroring the <see cref="Result{TValue}"/> pattern.
    /// External consumers must use <see cref="TryGetError(out Error)"/> or <see cref="Deconstruct(out bool, out Error?)"/>.
    /// </summary>
    internal Error Error =>
        IsFailure
            ? _error!
            : throw new InvalidOperationException("Cannot access Error on a successful result.");

    /// <summary>
    /// Attempts to get the error without throwing.
    /// </summary>
    public bool TryGetError(out Error error)
    {
        if (IsFailure)
        {
            error = _error!;
            return true;
        }

        error = default!;
        return false;
    }

    /// <summary>
    /// Deconstructs the result into its components for pattern matching.
    /// </summary>
    public void Deconstruct(out bool isSuccess, out Error? error)
    {
        isSuccess = IsSuccess;
        error = _error;
    }

    /// <summary>
    /// Creates a failure result wrapping the given error. Used by generic pipeline behaviors that need to construct
    /// failure results polymorphically via <see cref="IFailureFactory{TSelf}"/>.
    /// </summary>
    public static Result CreateFailure(Error error) => Fail(error);

    // ------------- Equality & hashing -------------

    /// <inheritdoc />
    public bool Equals(Result other)
    {
        if (IsFailure != other.IsFailure) return false;
        if (IsFailure) return _error!.Equals(other._error);
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        IsFailure
            ? HashCode.Combine(true, _error)
            : HashCode.Combine(false);

    /// <summary>Determines whether two results are equal.</summary>
    public static bool operator ==(Result left, Result right) => left.Equals(right);

    /// <summary>Determines whether two results are not equal.</summary>
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        IsFailure
            ? $"Failure({Error.Code}: {Error.Detail})"
            : "Success";

    // ------------- Factories -------------

    /// <summary>
    /// Creates a successful result wrapping the provided <paramref name="value"/>.
    /// </summary>
    public static Result<TValue> Ok<TValue>(TValue value) =>
        new(false, value, default);

    /// <summary>
    /// Creates a failed result with the specified <paramref name="error"/>.
    /// </summary>
    public static Result<TValue> Fail<TValue>(Error error) =>
        new(true, default, error);

    /// <summary>
    /// Creates a successful non-generic <see cref="Result"/> (no payload).
    /// </summary>
    /// <remarks>
    /// Goes through the constructor so that <see cref="Activity.Current"/> receives the success status,
    /// matching the tracing behavior of <see cref="Ok{TValue}(TValue)"/>. Note that <c>default(Result)</c>
    /// is still a valid success value (per the §3.5.1 default-state invariant) but will not tag any active activity.
    /// </remarks>
    public static Result Ok() => new(false, null);

    /// <summary>
    /// Creates a failed non-generic <see cref="Result"/> with the specified <paramref name="error"/>.
    /// </summary>
    public static Result Fail(Error error) => new(true, error);

    /// <summary>
    /// Returns a successful <see cref="Result"/> if the flag is true; otherwise a failure with the specified error.
    /// </summary>
    public static Result Ensure(bool flag, Error error)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var result = flag ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Returns a successful <see cref="Result"/> if the predicate is true; otherwise a failure with the specified error.
    /// </summary>
    public static Result Ensure(Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var result = predicate() ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously evaluates the predicate and returns success if true; otherwise a failure with the specified error.
    /// </summary>
    public static async Task<Result> EnsureAsync(Func<Task<bool>> predicate, Error error)
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
    /// Executes the function and converts exceptions to a failed result using the optional mapper (default Unexpected).
    /// </summary>
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
    /// Executes the asynchronous function and converts exceptions to a failed result.
    /// </summary>
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
    /// Executes the action and converts exceptions to a failed non-generic <see cref="Result"/> using the optional mapper.
    /// </summary>
    public static Result Try(Action work, Func<Exception, Error>? map = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            work();
            return Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Executes the asynchronous action and converts exceptions to a failed non-generic <see cref="Result"/>.
    /// </summary>
    public static async Task<Result> TryAsync(Func<Task> work, Func<Exception, Error>? map = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            await work().ConfigureAwait(false);
            return Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Default mapper converting an exception into an <see cref="UnexpectedError"/>.
    /// </summary>
    private static UnexpectedError DefaultExceptionMapper(Exception ex) =>
        Error.Unexpected(ex.Message);
}