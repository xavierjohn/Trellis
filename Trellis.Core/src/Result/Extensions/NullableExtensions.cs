namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Extension methods for converting nullable values to Result types.
/// Provides a convenient way to handle null checking with Railway Oriented Programming.
/// </summary>
/// <remarks>
/// These extensions allow you to convert potentially null values (both nullable value types and reference types)
/// into Result types, making null handling explicit and composable with other Result operations.
/// </remarks>
/// <example>
/// <code>
/// // Convert nullable value type to Result
/// int? maybeAge = GetAge();
/// var result = maybeAge.ToResult(Error.UnprocessableContent.ForField("age", "invalid", "Age is required"));
/// 
/// // Convert nullable reference type to Result
/// User? maybeUser = FindUser(id);
/// var userResult = maybeUser.ToResult(Error.NotFound("User not found", id));
/// 
/// // Chain with other Result operations
/// var validatedResult = GetUser(id)
///     .ToResult(Error.NotFound("User not found"))
///     .Ensure(u => u.IsActive, Error.Domain("User is inactive"));
/// </code>
/// </example>
[DebuggerStepThrough]
public static class NullableExtensions
{
    /// <summary>
    /// Converts a nullable value type to a Result.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="nullable">The nullable value to convert.</param>
    /// <param name="error">The error to return if the value is null.</param>
    /// <returns>A success Result containing the value if not null; otherwise a failure Result with the specified error.</returns>
    public static Result<T> ToResult<T>(this T? nullable, Error error)
        where T : struct
    {
        using var activity = RopTrace.ActivitySource.StartActivity();
        if (!nullable.HasValue)
            return Result.Fail<T>(error);

        return Result.Ok<T>(nullable.Value);
    }

    /// <summary>
    /// Converts a nullable value type to a Result using a function to create the error.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="nullable">The nullable value to convert.</param>
    /// <param name="errorFactory">A function that produces the error if the value is null.</param>
    /// <returns>A success Result containing the value if not null; otherwise a failure Result with an error from the factory.</returns>
    public static Result<T> ToResult<T>(this T? nullable, Func<Error> errorFactory)
        where T : struct
    {
        using var activity = RopTrace.ActivitySource.StartActivity();
        if (!nullable.HasValue)
            return Result.Fail<T>(errorFactory());

        return Result.Ok<T>(nullable.Value);
    }

    /// <summary>
    /// Converts a nullable reference type to a Result.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="obj">The potentially null object to convert.</param>
    /// <param name="error">The error to return if the object is null.</param>
    /// <returns>A success Result containing the object if not null; otherwise a failure Result with the specified error.</returns>
    public static Result<T> ToResult<T>(this T? obj, Error error)
        where T : class
    {
        using var activity = RopTrace.ActivitySource.StartActivity();
        if (obj == null)
            return Result.Fail<T>(error);

        return Result.Ok<T>(obj);
    }

    /// <summary>
    /// Converts a nullable reference type to a Result using a function to create the error.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="obj">The potentially null object to convert.</param>
    /// <param name="errorFactory">A function that produces the error if the object is null.</param>
    /// <returns>A success Result containing the object if not null; otherwise a failure Result with an error from the factory.</returns>
    public static Result<T> ToResult<T>(this T? obj, Func<Error> errorFactory)
        where T : class
    {
        using var activity = RopTrace.ActivitySource.StartActivity();
        if (obj == null)
            return Result.Fail<T>(errorFactory());

        return Result.Ok<T>(obj);
    }
}

/// <summary>
/// Asynchronous extension methods for converting nullable values to Result types.
/// </summary>
/// <remarks>
/// These extensions allow you to convert tasks or value tasks returning potentially null values
/// into Result types in an async context.
/// </remarks>
[DebuggerStepThrough]
public static class NullableExtensionsAsync
{
    /// <summary>
    /// Converts a task returning a nullable value type to a Result asynchronously.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="nullableTask">The task returning a nullable value.</param>
    /// <param name="error">The error to return if the value is null.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is a success Result if the value is not null; otherwise a failure Result.</returns>
    public static async Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Error error)
        where T : struct
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(error);
    }

    /// <summary>
    /// Converts a task returning a nullable value type to a Result asynchronously using a function to create the error.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="nullableTask">The task returning a nullable value.</param>
    /// <param name="errorFactory">A function that produces the error if the value is null.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is a success Result if the value is not null; otherwise a failure Result with an error from the factory.</returns>
    public static async Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Func<Error> errorFactory)
        where T : struct
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(errorFactory);
    }

    /// <summary>
    /// Converts a task returning a nullable reference type to a Result asynchronously.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="nullableTask">The task returning a potentially null object.</param>
    /// <param name="error">The error to return if the object is null.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is a success Result if the object is not null; otherwise a failure Result.</returns>
    public static async Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Error error)
    where T : class
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(error);
    }

    /// <summary>
    /// Converts a task returning a nullable reference type to a Result asynchronously using a function to create the error.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="nullableTask">The task returning a potentially null object.</param>
    /// <param name="errorFactory">A function that produces the error if the object is null.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is a success Result if the object is not null; otherwise a failure Result with an error from the factory.</returns>
    public static async Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Func<Error> errorFactory)
        where T : class
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(errorFactory);
    }

    /// <summary>
    /// Converts a ValueTask returning a nullable value type to a Result asynchronously.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="nullableTask">The ValueTask returning a nullable value.</param>
    /// <param name="error">The error to return if the value is null.</param>
    /// <returns>A ValueTask that represents the asynchronous operation. The task result is a success Result if the value is not null; otherwise a failure Result.</returns>
    public static async ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Error error)
        where T : struct
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(error);
    }

    /// <summary>
    /// Converts a ValueTask returning a nullable value type to a Result asynchronously using a function to create the error.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="nullableTask">The ValueTask returning a nullable value.</param>
    /// <param name="errorFactory">A function that produces the error if the value is null.</param>
    /// <returns>A ValueTask that represents the asynchronous operation. The task result is a success Result if the value is not null; otherwise a failure Result with an error from the factory.</returns>
    public static async ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Func<Error> errorFactory)
        where T : struct
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(errorFactory);
    }

    /// <summary>
    /// Converts a ValueTask returning a nullable reference type to a Result asynchronously.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="nullableTask">The ValueTask returning a potentially null object.</param>
    /// <param name="error">The error to return if the object is null.</param>
    /// <returns>A ValueTask that represents the asynchronous operation. The task result is a success Result if the object is not null; otherwise a failure Result.</returns>
    public static async ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Error error)
        where T : class
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(error);
    }

    /// <summary>
    /// Converts a ValueTask returning a nullable reference type to a Result asynchronously using a function to create the error.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="nullableTask">The ValueTask returning a potentially null object.</param>
    /// <param name="errorFactory">A function that produces the error if the object is null.</param>
    /// <returns>A ValueTask that represents the asynchronous operation. The task result is a success Result if the object is not null; otherwise a failure Result with an error from the factory.</returns>
    public static async ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Func<Error> errorFactory)
        where T : class
    {
        var nullable = await nullableTask.ConfigureAwait(false);
        return nullable.ToResult(errorFactory);
    }
}