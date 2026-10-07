namespace Trellis;

using System.Runtime.CompilerServices;

public static partial class Result
{
    /// <summary>
    /// Guards a required reference, returning the same non-null value or the supplied error.
    /// </summary>
    /// <typeparam name="T">The reference type carried by success.</typeparam>
    /// <param name="value">The possibly missing value.</param>
    /// <param name="error">The failure error, used only when the value is null.</param>
    /// <returns>A result carrying the non-null value or the error.</returns>
    /// <exception cref="ArgumentNullException">The value and error are both null.</exception>
    [OverloadResolutionPriority(1)]
    public static Result<T> EnsureNotNull<T>(T? value, Error error) where T : class
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureNotNull));
        return value is not null ? Ok(value) : Fail<T>(error);
    }

    /// <summary>
    /// Guards a required reference, creating an error exactly once only when the value is null.
    /// </summary>
    /// <typeparam name="T">The reference type carried by success.</typeparam>
    /// <param name="value">The possibly missing value.</param>
    /// <param name="errorFactory">Creates the error only for a missing value.</param>
    /// <returns>A result carrying the same non-null value or the created error.</returns>
    /// <exception cref="ArgumentNullException">The factory is null, or an invoked factory returns null.</exception>
    /// <remarks>The factory is required even on success. Factory exceptions propagate.</remarks>
    public static Result<T> EnsureNotNull<T>(T? value, Func<Error> errorFactory) where T : class
    {
        ArgumentNullException.ThrowIfNull(errorFactory);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureNotNull));
        return value is not null ? Ok(value) : Fail<T>(errorFactory());
    }

    /// <summary>
    /// Guards a required reference, creating a standard required-field error only when it is null.
    /// </summary>
    /// <typeparam name="T">The reference type carried by success.</typeparam>
    /// <param name="value">The possibly missing value.</param>
    /// <param name="fieldName">Property name or full JSON Pointer; null or empty targets the root.</param>
    /// <param name="detail">Optional human-readable violation detail.</param>
    /// <returns>The same non-null value or an <see cref="Error.InvalidInput"/> containing one required-field violation.</returns>
    /// <exception cref="ArgumentException">The value is null and the field name is a malformed JSON Pointer.</exception>
    public static Result<T> EnsureNotNull<T>(T? value, string? fieldName, string? detail = null) where T : class
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureNotNull));
        return value is not null ? Ok(value) : Fail<T>(Error.InvalidInput.Required(fieldName, detail));
    }

    /// <summary>
    /// Guards a required nullable struct, unwrapping its value or returning the supplied error.
    /// </summary>
    /// <typeparam name="T">The value type carried by success.</typeparam>
    /// <param name="value">The possibly missing value.</param>
    /// <param name="error">The failure error, used only when the value is null.</param>
    /// <returns>A result carrying the unwrapped value or the error.</returns>
    /// <exception cref="ArgumentNullException">The value and error are both null.</exception>
    [OverloadResolutionPriority(1)]
    public static Result<T> EnsureNotNull<T>(T? value, Error error) where T : struct
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureNotNull));
        return value.HasValue ? Ok(value.Value) : Fail<T>(error);
    }

    /// <summary>
    /// Guards a required nullable struct, creating an error exactly once only when it is null.
    /// </summary>
    /// <typeparam name="T">The value type carried by success.</typeparam>
    /// <param name="value">The possibly missing value.</param>
    /// <param name="errorFactory">Creates the error only for a missing value.</param>
    /// <returns>A result carrying the unwrapped value or the created error.</returns>
    /// <exception cref="ArgumentNullException">The factory is null, or an invoked factory returns null.</exception>
    /// <remarks>The factory is required even on success. Factory exceptions propagate.</remarks>
    public static Result<T> EnsureNotNull<T>(T? value, Func<Error> errorFactory) where T : struct
    {
        ArgumentNullException.ThrowIfNull(errorFactory);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureNotNull));
        return value.HasValue ? Ok(value.Value) : Fail<T>(errorFactory());
    }

    /// <summary>
    /// Guards a required nullable struct, creating a standard required-field error only when it is null.
    /// </summary>
    /// <typeparam name="T">The value type carried by success.</typeparam>
    /// <param name="value">The possibly missing value.</param>
    /// <param name="fieldName">Property name or full JSON Pointer; null or empty targets the root.</param>
    /// <param name="detail">Optional human-readable violation detail.</param>
    /// <returns>The unwrapped value or an <see cref="Error.InvalidInput"/> containing one required-field violation.</returns>
    /// <exception cref="ArgumentException">The value is null and the field name is a malformed JSON Pointer.</exception>
    public static Result<T> EnsureNotNull<T>(T? value, string? fieldName, string? detail = null) where T : struct
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureNotNull));
        return value.HasValue ? Ok(value.Value) : Fail<T>(Error.InvalidInput.Required(fieldName, detail));
    }
}

public static partial class EnsureExtensionsAsync
{
    /// <summary>Awaits a nullable reference once, returning its non-null value or the supplied error.</summary>
    /// <remarks>A null task throws. Source faults and cancellation propagate unchanged.</remarks>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<T>> EnsureNotNullAsync<T>(this Task<T?> task, Error error) where T : class
    {
        ArgumentNullException.ThrowIfNull(task);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), error);
    }

    /// <summary>Awaits a nullable reference once, creating an error only for a null result.</summary>
    /// <remarks>Task and factory are validated before awaiting. Source and factory exceptions propagate.</remarks>
    public static async Task<Result<T>> EnsureNotNullAsync<T>(this Task<T?> task, Func<Error> errorFactory) where T : class
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(errorFactory);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), errorFactory);
    }

    /// <summary>Awaits a nullable reference once, creating a required-field error only for a null result.</summary>
    /// <remarks>The field name and detail follow <see cref="Error.InvalidInput.Required(string, string)"/>. Source faults and cancellation propagate.</remarks>
    public static async Task<Result<T>> EnsureNotNullAsync<T>(this Task<T?> task, string? fieldName, string? detail = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(task);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), fieldName, detail);
    }

    /// <summary>Awaits a nullable struct once, returning its unwrapped value or the supplied error.</summary>
    /// <remarks>A null task throws. Source faults and cancellation propagate unchanged.</remarks>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<T>> EnsureNotNullAsync<T>(this Task<T?> task, Error error) where T : struct
    {
        ArgumentNullException.ThrowIfNull(task);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), error);
    }

    /// <summary>Awaits a nullable struct once, creating an error only for a null result.</summary>
    /// <remarks>Task and factory are validated before awaiting. Source and factory exceptions propagate.</remarks>
    public static async Task<Result<T>> EnsureNotNullAsync<T>(this Task<T?> task, Func<Error> errorFactory) where T : struct
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(errorFactory);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), errorFactory);
    }

    /// <summary>Awaits a nullable struct once, creating a required-field error only for a null result.</summary>
    /// <remarks>The field name and detail follow <see cref="Error.InvalidInput.Required(string, string)"/>. Source faults and cancellation propagate.</remarks>
    public static async Task<Result<T>> EnsureNotNullAsync<T>(this Task<T?> task, string? fieldName, string? detail = null) where T : struct
    {
        ArgumentNullException.ThrowIfNull(task);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), fieldName, detail);
    }

    /// <summary>Awaits a nullable reference once, returning its non-null value or the supplied error.</summary>
    /// <remarks>Source faults and cancellation propagate unchanged.</remarks>
    [OverloadResolutionPriority(1)]
    public static async ValueTask<Result<T>> EnsureNotNullAsync<T>(this ValueTask<T?> task, Error error) where T : class =>
        Result.EnsureNotNull(await task.ConfigureAwait(false), error);

    /// <summary>Awaits a nullable reference once, creating an error only for a null result.</summary>
    /// <remarks>The factory is validated before awaiting. Source and factory exceptions propagate.</remarks>
    public static async ValueTask<Result<T>> EnsureNotNullAsync<T>(this ValueTask<T?> task, Func<Error> errorFactory) where T : class
    {
        ArgumentNullException.ThrowIfNull(errorFactory);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), errorFactory);
    }

    /// <summary>Awaits a nullable reference once, creating a required-field error only for a null result.</summary>
    /// <remarks>The field name and detail follow <see cref="Error.InvalidInput.Required(string, string)"/>. Source faults and cancellation propagate.</remarks>
    public static async ValueTask<Result<T>> EnsureNotNullAsync<T>(this ValueTask<T?> task, string? fieldName, string? detail = null) where T : class =>
        Result.EnsureNotNull(await task.ConfigureAwait(false), fieldName, detail);

    /// <summary>Awaits a nullable struct once, returning its unwrapped value or the supplied error.</summary>
    /// <remarks>Source faults and cancellation propagate unchanged.</remarks>
    [OverloadResolutionPriority(1)]
    public static async ValueTask<Result<T>> EnsureNotNullAsync<T>(this ValueTask<T?> task, Error error) where T : struct =>
        Result.EnsureNotNull(await task.ConfigureAwait(false), error);

    /// <summary>Awaits a nullable struct once, creating an error only for a null result.</summary>
    /// <remarks>The factory is validated before awaiting. Source and factory exceptions propagate.</remarks>
    public static async ValueTask<Result<T>> EnsureNotNullAsync<T>(this ValueTask<T?> task, Func<Error> errorFactory) where T : struct
    {
        ArgumentNullException.ThrowIfNull(errorFactory);
        return Result.EnsureNotNull(await task.ConfigureAwait(false), errorFactory);
    }

    /// <summary>Awaits a nullable struct once, creating a required-field error only for a null result.</summary>
    /// <remarks>The field name and detail follow <see cref="Error.InvalidInput.Required(string, string)"/>. Source faults and cancellation propagate.</remarks>
    public static async ValueTask<Result<T>> EnsureNotNullAsync<T>(this ValueTask<T?> task, string? fieldName, string? detail = null) where T : struct =>
        Result.EnsureNotNull(await task.ConfigureAwait(false), fieldName, detail);
}
