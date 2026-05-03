namespace Trellis;

using System;
using System.Diagnostics;

/// <summary>
/// Represents either a successful computation (with a value) or a failure (with an <see cref="Error"/>).
/// </summary>
/// <typeparam name="TValue">Success value type.</typeparam>
/// <remarks>
/// <para>
/// Result is the core type for Railway Oriented Programming. It forces explicit handling of both
/// success and failure cases, making error handling visible in the type system. Use Result when
/// an operation can fail in a predictable way that should be handled by the caller.
/// </para>
/// <para>
/// <c>default(Result&lt;T&gt;)</c> represents a <em>failure</em> carrying a sentinel
/// <see cref="Trellis.Error.Unexpected"/> with <c>ReasonCode = "default_initialized"</c>. This makes
/// uninitialized state a typed failure rather than a silent success that would hide a programming error.
/// Always construct via <see cref="Result.Ok{T}(T)"/> or <see cref="Result.Fail{T}(Error)"/>; analyzer
/// <c>TRLS019</c> flags explicit <c>default(Result&lt;T&gt;)</c> at call sites.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Creating results
/// Result&lt;User&gt; success = Result.Ok(user);
/// Result&lt;User&gt; failure = new Error.NotFound(new ResourceRef("User", "42")) { Detail = "User not found" };
/// 
/// // Pattern matching with Deconstruct
/// var (isSuccess, value, error) = result;
/// var message = isSuccess
///     ? $"Found user: {value!.Name}"
///     : $"Error: {error!.Detail}";
/// 
/// // Or use the safe accessors
/// if (result.TryGetValue(out var user)) Console.WriteLine(user.Name);
/// else if (result.Error is { } err) Console.WriteLine(err.Detail);
/// 
/// // Chaining operations
/// var finalResult = GetUser(id)
///     .Bind(user => ValidateUser(user))
///     .Map(user => user.Name);
/// </code>
/// </example>
[DebuggerDisplay("{IsSuccess ? \"Success\" : \"Failure\"}, Value = {(_value is null ? \"<null>\" : _value)}, Error = {(IsSuccess ? \"<none>\" : EffectiveError().Code)}")]
[DebuggerTypeProxy(typeof(ResultDebugView<>))]
public readonly struct Result<TValue> : IResult<TValue>, IEquatable<Result<TValue>>, IFailureFactory<Result<TValue>>
{
    /// <summary>
    /// True when the result represents success.
    /// </summary>
    /// <value>True if successful; otherwise false.</value>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isOk;

    /// <summary>
    /// True when the result represents failure.
    /// </summary>
    /// <value>True if failed; otherwise false.</value>
    /// <remarks>
    /// <c>default(Result&lt;T&gt;).IsFailure</c> is <see langword="true"/>.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isOk;

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
                throw new ArgumentNullException(nameof(error), "If 'isFailure' is true, 'error' must not be null.");
        }
        else
        {
            if (error is not null)
                throw new ArgumentException("If 'isFailure' is false, 'error' must be null.", nameof(error));
        }

        _isOk = !isFailure;
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

    private readonly bool _isOk;
    private readonly TValue? _value;
    private readonly Error? _error;

    /// <summary>
    /// Returns the failure-side error, routing default-initialized failures through the shared
    /// <see cref="ResultDefaults.Sentinel"/>. Caller is responsible for checking <see cref="IsFailure"/> first.
    /// </summary>
    private Error EffectiveError() => _error ?? ResultDefaults.Sentinel;

    // ------------- Public accessors -------------
    //
    // Note: in v1 there was a `public TValue Value { get; }` property that threw
    // InvalidOperationException on failure — the primary cause of TRLS003. It was
    // removed from the current API. Use TryGetValue, Match, or Deconstruct
    // to extract the success value safely. The non-throwing nullable `Error` property
    // is intentionally retained because it powers clean pattern-match idioms
    // (`if (r.Error is { } e) ...`, `r.Error switch { Error.NotFound => ..., ... }`).

    /// <summary>
    /// Gets the error when this result is a failure, or <see langword="null"/> when it is a success.
    /// </summary>
    /// <remarks>
    /// Reading this property never throws. The nullable return type is the discriminator: a non-null
    /// <see cref="Trellis.Error"/> means the result is a failure; <see langword="null"/> means success.
    /// For <c>default(Result&lt;T&gt;)</c>, returns the shared <see cref="Trellis.Error.Unexpected"/>
    /// sentinel so default-initialized failures are observationally equivalent to
    /// <c>Result.Fail&lt;T&gt;(new Error.Unexpected("default_initialized"))</c>.
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

    // ------------- Convenience / ergonomic APIs ------------

    /// <summary>
    /// Attempts to get the success value without throwing.
    /// </summary>
    /// <param name="value">When this method returns true, contains the success value; otherwise, the default value.</param>
    /// <returns>True if the result is successful; otherwise false.</returns>
    /// <remarks>
    /// Equivalent to <c>!IsFailure</c>; provided as a TryParse-style convenience that binds the value in a single call.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(Error))]
    public bool TryGetValue([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TValue value)
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
    /// Attempts to get the success value and the error in a single call, eliminating the need
    /// for <c>result.Error!</c> null-suppression after a failed <see cref="TryGetValue(out TValue)"/>.
    /// </summary>
    /// <param name="value">When this method returns <see langword="true"/>, contains the success value; otherwise, the default value.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains the error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is successful; otherwise <see langword="false"/>.</returns>
    /// <example>
    /// <code>
    /// if (!result.TryGetValue(out var v, out var err))
    ///     return Result.Fail&lt;T&gt;(err); // err is non-null here (flow-analysis verified)
    /// // use v on success
    /// </code>
    /// </example>
    public bool TryGetValue(
        [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TValue value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out Error? error)
    {
        if (IsSuccess)
        {
            value = _value!;
            error = null;
            return true;
        }

        value = default!;
        error = EffectiveError();
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
    ///     Console.WriteLine($"User: {value!.Name}");
    /// else
    ///     Console.WriteLine($"Error: {error!.Detail}");
    /// </code>
    /// </example>
    public void Deconstruct(out bool isSuccess, out TValue? value, out Error? error)
    {
        isSuccess = IsSuccess;
        value = _value;
        error = _isOk ? null : EffectiveError();
    }

    // ------------- Equality & hashing -------------

    /// <summary>
    /// Determines whether the specified result is equal to the current result.
    /// </summary>
    /// <param name="other">The result to compare with the current result.</param>
    /// <returns>True if the specified result is equal to the current result; otherwise false.</returns>
    /// <remarks>
    /// Two results are equal if they have the same success/failure state and equal values/errors.
    /// Default-initialized failures use the shared sentinel <see cref="Trellis.Error.Unexpected"/>
    /// so two <c>default(Result&lt;T&gt;)</c> values are equal, and a default
    /// equals an explicit <c>Result.Fail&lt;T&gt;(...)</c> with the same sentinel.
    /// </remarks>
    public bool Equals(Result<TValue> other)
    {
        if (_isOk != other._isOk) return false;
        if (_isOk) return EqualityComparer<TValue>.Default.Equals(_value, other._value);
        return EffectiveError().Equals(other.EffectiveError());
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
        _isOk
            ? HashCode.Combine(false, _value)
            : HashCode.Combine(true, EffectiveError());

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
    /// Converts this <see cref="Result{TValue}"/> to a no-payload <see cref="Result{Unit}"/>, discarding the success value.
    /// Failures preserve their <see cref="Error"/>.
    /// </summary>
    /// <remarks>
    /// Use <see cref="AsUnit"/> when a pipeline returns a value but the next step only cares about success/failure
    /// (e.g., bridging a value-producing operation into a no-payload consumer). This returns the canonical
    /// no-payload result shape <c>Result&lt;Unit&gt;</c>. For default-initialized failures
    /// the returned result is constructed via <see cref="Result.Fail(Error)"/> with
    /// the shared sentinel — never returns a default-initialized <c>Result&lt;Unit&gt;</c>.
    /// </remarks>
    /// <returns>A <see cref="Result{Unit}"/> mirroring this result's success/failure state.</returns>
    public Result<Unit> AsUnit() => _isOk ? Result.Ok() : Result.Fail(EffectiveError());

    /// <summary>
    /// Returns a string representation of the result.
    /// </summary>
    /// <returns>A string in the format "Success(value)" or "Failure(ErrorCode: detail)".</returns>
    public override string ToString()
    {
        if (_isOk) return $"Success({(_value is null ? "<null>" : _value)})";
        var error = EffectiveError();
        return $"Failure({error.Code}: {error.Detail})";
    }
}