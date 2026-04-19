namespace Trellis;

using System;
using System.Diagnostics;

/// <summary>
/// Represents either a successful computation (with a value) or a failure (with an <see cref="Error"/>).
/// </summary>
/// <typeparam name="TValue">Success value type.</typeparam>
/// <remarks>
/// Result is the core type for Railway Oriented Programming. It forces explicit handling of both
/// success and failure cases, making error handling visible in the type system. Use Result when
/// an operation can fail in a predictable way that should be handled by the caller.
/// </remarks>
/// <example>
/// <code>
/// // Creating results
/// Result&lt;User&gt; success = Result.Ok(user);
/// Result&lt;User&gt; failure = Error.NotFound("User not found");
/// 
/// // Pattern matching with Deconstruct
/// var (isSuccess, value, error) = result;
/// var message = isSuccess
///     ? $"Found user: {value!.Name}"
///     : $"Error: {error!.Detail}";
/// 
/// // Or use the safe accessors
/// if (result.TryGetValue(out var user)) Console.WriteLine(user.Name);
/// else if (result.TryGetError(out var err)) Console.WriteLine(err.Detail);
/// 
/// // Chaining operations
/// var finalResult = GetUser(id)
///     .Bind(user => ValidateUser(user))
///     .Map(user => user.Name);
/// </code>
/// </example>
[DebuggerDisplay("{IsSuccess ? \"Success\" : \"Failure\"}, Value = {(_value is null ? \"<null>\" : _value)}, Error = {(_error is null ? \"<none>\" : _error.Code)}")]
[DebuggerTypeProxy(typeof(ResultDebugView<>))]
public readonly struct Result<TValue> : IResult<TValue>, IEquatable<Result<TValue>>, IFailureFactory<Result<TValue>>
{
    /// <summary>
    /// True when the result represents success.
    /// </summary>
    /// <value>True if successful; otherwise false.</value>
    public bool IsSuccess => !IsFailure;

    /// <summary>
    /// True when the result represents failure.
    /// </summary>
    /// <value>True if failed; otherwise false.</value>
    public bool IsFailure { get; }

    /// <summary>
    /// Implicitly converts a value to a successful result.
    /// </summary>
    /// <param name="value">The value to wrap in a success result.</param>
    /// <returns>A successful result containing the value.</returns>
    public static implicit operator Result<TValue>(TValue value) => Result.Ok(value);

    /// <summary>
    /// Implicitly converts an error to a failed result.
    /// </summary>
    /// <param name="error">The error to wrap in a failed result.</param>
    /// <returns>A failed result containing the error.</returns>
    public static implicit operator Result<TValue>(Error error) => Result.Fail<TValue>(error);

    /// <summary>
    /// Creates a failure result wrapping the given error.
    /// Used by generic pipeline behaviors that need to construct failure results
    /// without knowing the inner type parameter.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{TValue}"/>.</returns>
#pragma warning disable CA1000 // Do not declare static members on generic types — required by IFailureFactory<TSelf>
    public static Result<TValue> CreateFailure(Error error) => Result.Fail<TValue>(error);
#pragma warning restore CA1000

    internal Result(bool isFailure, TValue? ok, Error? error)
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
        _value = ok;

        Activity.Current?.SetStatus(IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Ok);

        // Optional enrichment (safe no-op if no activity)
        if (IsFailure && Activity.Current is { } act && error is not null)
        {
            act.SetTag("result.error.code", error.Code);
        }
    }

    internal void LogActivityStatus() => Activity.Current?.SetStatus(IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Ok);

    private readonly TValue? _value;
    private readonly Error? _error;

    // ------------- Internal accessors for in-assembly use -------------
    // These intentionally mirror the v1 public Value/Error properties so that
    // ROP combinator extensions in this assembly stay readable and avoid the
    // (out param) cost of TryGet*. They are not part of the public API; external
    // consumers must use TryGetValue/TryGetError/Deconstruct (see plan §3.1).

    internal TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access Value on a failed result.");

    internal Error Error =>
        IsFailure
            ? _error!
            : throw new InvalidOperationException("Cannot access Error on a successful result.");

    // ------------- Convenience / ergonomic APIs ------------

    /// <summary>
    /// Attempts to get the success value without throwing.
    /// </summary>
    /// <param name="value">When this method returns true, contains the success value; otherwise, the default value.</param>
    /// <returns>True if the result is successful; otherwise false.</returns>
    /// <remarks>
    /// This is the recommended safe way to access the value without exception handling.
    /// Similar to the TryParse pattern in .NET.
    /// </remarks>
    public bool TryGetValue(out TValue value)
    {
        if (IsSuccess)
        {
            value = _value!;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Attempts to get the error without throwing.
    /// </summary>
    /// <param name="error">When this method returns true, contains the error; otherwise, null.</param>
    /// <returns>True if the result is a failure; otherwise false.</returns>
    /// <remarks>
    /// This is the recommended safe way to access the error without exception handling.
    /// </remarks>
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
    /// <param name="isSuccess">True if the result is successful; otherwise false.</param>
    /// <param name="value">The success value if successful; otherwise default.</param>
    /// <param name="error">The error if failed; otherwise null.</param>
    /// <example>
    /// <code>
    /// var (success, value, error) = GetUser(id);
    /// if (success)
    ///     Console.WriteLine($"User: {value.Name}");
    /// else
    ///     Console.WriteLine($"Error: {error.Detail}");
    /// </code>
    /// </example>
    public void Deconstruct(out bool isSuccess, out TValue? value, out Error? error)
    {
        isSuccess = IsSuccess;
        value = _value;
        error = _error;
    }

    // ------------- Equality & hashing -------------

    /// <summary>
    /// Determines whether the specified result is equal to the current result.
    /// </summary>
    /// <param name="other">The result to compare with the current result.</param>
    /// <returns>True if the specified result is equal to the current result; otherwise false.</returns>
    /// <remarks>
    /// Two results are equal if they have the same success/failure state and equal values/errors.
    /// </remarks>
    public bool Equals(Result<TValue> other)
    {
        if (IsFailure != other.IsFailure) return false;
        if (IsFailure) return _error!.Equals(other._error);
        return EqualityComparer<TValue>.Default.Equals(_value, other._value);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current result.
    /// </summary>
    /// <param name="obj">The object to compare with the current result.</param>
    /// <returns>True if the specified object is a Result and is equal to the current result; otherwise false.</returns>
    public override bool Equals(object? obj) => obj is Result<TValue> other && Equals(other);

    /// <summary>
    /// Returns a hash code for the current result.
    /// </summary>
    /// <returns>A hash code for the current result.</returns>
    public override int GetHashCode() =>
        IsFailure
            ? HashCode.Combine(true, _error)
            : HashCode.Combine(false, _value);

    /// <summary>
    /// Determines whether two results are equal.
    /// </summary>
    /// <param name="left">The first result to compare.</param>
    /// <param name="right">The second result to compare.</param>
    /// <returns>True if the results are equal; otherwise false.</returns>
    public static bool operator ==(Result<TValue> left, Result<TValue> right) => left.Equals(right);

    /// <summary>
    /// Determines whether two results are not equal.
    /// </summary>
    /// <param name="left">The first result to compare.</param>
    /// <param name="right">The second result to compare.</param>
    /// <returns>True if the results are not equal; otherwise false.</returns>
    public static bool operator !=(Result<TValue> left, Result<TValue> right) => !left.Equals(right);

    /// <summary>
    /// Returns a string representation of the result.
    /// </summary>
    /// <returns>A string in the format "Success(value)" or "Failure(ErrorCode: detail)".</returns>
    public override string ToString() =>
        IsFailure
            ? $"Failure({Error.Code}: {Error.Detail})"
            : $"Success({(_value is null ? "<null>" : _value)})";
}