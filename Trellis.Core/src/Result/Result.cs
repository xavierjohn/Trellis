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
/// either succeed (no value) or fail (with an <see cref="Error"/>).
/// </para>
/// <para>
/// Per ADR-002 §3.5.1, <c>default(Result)</c> represents a <em>failure</em> carrying a sentinel
/// <see cref="Trellis.Error.Unexpected"/> with <c>ReasonCode = "default_initialized"</c>. This makes
/// uninitialized state a typed failure rather than a silent success. Always construct via
/// <see cref="Ok()"/> or <see cref="Fail(Error)"/>; analyzer <c>TRLS019</c> flags explicit
/// <c>default(Result)</c> at call sites.
/// </para>
/// <para>
/// This struct also hosts factory and helper methods to construct both <see cref="Result"/> and <see cref="Result{TValue}"/>
/// instances (e.g. <see cref="Ok()"/>, <see cref="Fail(Error)"/>, <see cref="Ok{TValue}(TValue)"/>, <see cref="Try{T}(Func{T}, Func{Exception, Error}?)"/>).
/// </para>
/// </remarks>
[DebuggerDisplay("{IsSuccess ? \"Success\" : \"Failure\"}, Error = {(IsSuccess ? \"<none>\" : EffectiveError().Code)}")]
public readonly partial struct Result : IResult, IEquatable<Result>, IFailureFactory<Result>
{
    private readonly bool _isOk;
    private readonly Error? _error;

    /// <summary>True when the result represents success.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isOk;

    /// <summary>True when the result represents failure.</summary>
    /// <remarks>
    /// <c>default(Result).IsFailure</c> is <see langword="true"/> per ADR-002 §3.5.1.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isOk;

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

        _isOk = !isFailure;
        _error = error;

        Activity.Current?.SetStatus(IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Ok);

        if (IsFailure && Activity.Current is { } act && error is not null)
        {
            act.SetTag("result.error.code", error.Code);
        }
    }

    internal void LogActivityStatus() => Activity.Current?.SetStatus(IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Ok);

    /// <summary>
    /// Returns the failure-side error, routing default-initialized failures through the shared
    /// <see cref="ResultDefaults.Sentinel"/>. Caller is responsible for checking <see cref="IsFailure"/> first.
    /// </summary>
    private Error EffectiveError() => _error ?? ResultDefaults.Sentinel;

    /// <summary>
    /// Gets the error when this result is a failure, or <see langword="null"/> when it is a success.
    /// </summary>
    /// <remarks>
    /// Reading this property never throws. The nullable return type is the discriminator: a non-null
    /// <see cref="Trellis.Error"/> means the result is a failure; <see langword="null"/> means success.
    /// For <c>default(Result)</c>, returns the shared <see cref="Trellis.Error.Unexpected"/> sentinel
    /// (per ADR-002 §3.5.1) so default-initialized failures are observationally equivalent to
    /// <c>Result.Fail(new Error.Unexpected("default_initialized"))</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// if (result.Error is { } error)
    ///     return error switch
    ///     {
    ///         Error.NotFound nf => HandleNotFound(nf),
    ///         _ => HandleGeneric(error),
    ///     };
    /// </code>
    /// </example>
    public Error? Error => _isOk ? null : EffectiveError();

    /// <summary>
    /// Attempts to get the error without throwing. Companion to <see cref="Error"/> for callers
    /// that prefer <c>TryParse</c>-style imperative usage where a non-null local binding is desired.
    /// </summary>
    /// <param name="error">When this method returns <see langword="true"/>, contains the error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Error? error)
    {
        if (_isOk)
        {
            error = null;
            return false;
        }

        error = EffectiveError();
        return true;
    }

    /// <summary>
    /// Deconstructs the result into its components for pattern matching.
    /// </summary>
    public void Deconstruct(out bool isSuccess, out Error? error)
    {
        isSuccess = IsSuccess;
        error = _isOk ? null : EffectiveError();
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
        if (_isOk != other._isOk) return false;
        if (_isOk) return true;
        return EffectiveError().Equals(other.EffectiveError());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _isOk
            ? HashCode.Combine(false)
            : HashCode.Combine(true, EffectiveError());

    /// <summary>Determines whether two results are equal.</summary>
    public static bool operator ==(Result left, Result right) => left.Equals(right);

    /// <summary>Determines whether two results are not equal.</summary>
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString()
    {
        if (_isOk) return "Success";
        var error = EffectiveError();
        return $"Failure({error.Code}: {error.Detail})";
    }

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
    /// matching the tracing behavior of <see cref="Ok{TValue}(TValue)"/>. Always prefer this factory over
    /// <c>default(Result)</c>: per ADR-002 §3.5.1, <c>default(Result)</c> represents <em>failure</em> with
    /// the <see cref="Trellis.Error.Unexpected"/> sentinel, not success.
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
    /// Default mapper converting an exception into an <see cref="Error.InternalServerError"/>.
    /// The exception message is attached as <c>Detail</c>; richer diagnostics belong in the
    /// logging/telemetry layer indexed by <c>FaultId</c>.
    /// </summary>
    private static Error.InternalServerError DefaultExceptionMapper(Exception ex) =>
        new(FaultId: Guid.NewGuid().ToString("N")) { Detail = ex.Message };
}