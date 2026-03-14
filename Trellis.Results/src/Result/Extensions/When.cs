namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Conditionally executes an operation based on a predicate, maintaining the railway.
/// Useful for executing operations only when certain conditions are met without breaking the Result chain.
/// </summary>
/// <remarks>
/// Users should capture CancellationToken in their lambda closures when cancellation support is needed.
/// </remarks>
/// <example>
/// <code>
/// // Apply discount only for premium users
/// var result = order
///     .When(o => o.IsPremiumUser, o => ApplyDiscount(o))
///     .Bind(o => ProcessPayment(o));
/// 
/// // Validate only if amount exceeds threshold with cancellation support
/// var ct = cancellationToken;
/// await GetTransactionAsync(id)
///     .WhenAsync(
///         t => t.Amount > 10000,
///         t => PerformAdditionalValidationAsync(t, ct)
///     );
/// </code>
/// </example>
[DebuggerStepThrough]
public static class WhenExtensions
{
    /// <summary>
    /// Conditionally executes an operation if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Operation to execute if predicate is true.</param>
    /// <returns>Result from the operation if predicate is true and result is success; otherwise the original result.</returns>
    public static Result<T> When<T>(
        this Result<T> result,
        Func<T, bool> predicate,
        Func<T, Result<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(When));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Value))
        {
            var operationResult = operation(result.Value);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an operation if the condition is true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="condition">Boolean condition.</param>
    /// <param name="operation">Operation to execute if condition is true.</param>
    /// <returns>Result from the operation if condition is true and result is success; otherwise the original result.</returns>
    public static Result<T> When<T>(
        this Result<T> result,
        bool condition,
        Func<T, Result<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(When));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (condition)
        {
            var operationResult = operation(result.Value);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an operation if the predicate returns false.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Operation to execute if predicate is false.</param>
    /// <returns>Result from the operation if predicate is false and result is success; otherwise the original result.</returns>
    public static Result<T> Unless<T>(
        this Result<T> result,
        Func<T, bool> predicate,
        Func<T, Result<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Unless));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (!predicate(result.Value))
        {
            var operationResult = operation(result.Value);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an operation if the condition is false.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="condition">Boolean condition.</param>
    /// <param name="operation">Operation to execute if condition is false.</param>
    /// <returns>Result from the operation if condition is false and result is success; otherwise the original result.</returns>
    public static Result<T> Unless<T>(
        this Result<T> result,
        bool condition,
        Func<T, Result<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Unless));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (!condition)
        {
            var operationResult = operation(result.Value);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }
}

/// <summary>
/// Asynchronous conditional execution operations for Result.
/// </summary>
/// <remarks>
/// Users should capture CancellationToken in their lambda closures when cancellation support is needed.
/// </remarks>
[DebuggerStepThrough]
public static class WhenExtensionsAsync
{
    /// <summary>
    /// Conditionally executes an async operation if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Async operation to execute if predicate is true.</param>
    /// <returns>Result from the operation if predicate is true and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> WhenAsync<T>(
        this Result<T> result,
        Func<T, bool> predicate,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(WhenExtensions.When));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Value))
        {
            var operationResult = await operation(result.Value).ConfigureAwait(false);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an async operation if the condition is true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="condition">Boolean condition.</param>
    /// <param name="operation">Async operation to execute if condition is true.</param>
    /// <returns>Result from the operation if condition is true and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> WhenAsync<T>(
        this Result<T> result,
        bool condition,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(WhenExtensions.When));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (condition)
        {
            var operationResult = await operation(result.Value).ConfigureAwait(false);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an async operation if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Async operation to execute if predicate is true.</param>
    /// <returns>Result from the operation if predicate is true and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> WhenAsync<T>(
        this Task<Result<T>> resultTask,
        Func<T, bool> predicate,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return await result.WhenAsync(predicate, operation).ConfigureAwait(false);
    }

    /// <summary>
    /// Conditionally executes an async operation if the condition is true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to test.</param>
    /// <param name="condition">Boolean condition.</param>
    /// <param name="operation">Async operation to execute if condition is true.</param>
    /// <returns>Result from the operation if condition is true and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> WhenAsync<T>(
        this Task<Result<T>> resultTask,
        bool condition,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(operation);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return await result.WhenAsync(condition, operation).ConfigureAwait(false);
    }

    /// <summary>
    /// Conditionally executes an async operation if the predicate returns false.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Async operation to execute if predicate is false.</param>
    /// <returns>Result from the operation if predicate is false and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> UnlessAsync<T>(
        this Result<T> result,
        Func<T, bool> predicate,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(WhenExtensions.Unless));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (!predicate(result.Value))
        {
            var operationResult = await operation(result.Value).ConfigureAwait(false);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an async operation if the condition is false.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to test.</param>
    /// <param name="condition">Boolean condition.</param>
    /// <param name="operation">Async operation to execute if condition is false.</param>
    /// <returns>Result from the operation if condition is false and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> UnlessAsync<T>(
        this Result<T> result,
        bool condition,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(WhenExtensions.Unless));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (!condition)
        {
            var operationResult = await operation(result.Value).ConfigureAwait(false);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an async operation if the predicate returns false.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Async operation to execute if predicate is false.</param>
    /// <returns>Result from the operation if predicate is false and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> UnlessAsync<T>(
        this Task<Result<T>> resultTask,
        Func<T, bool> predicate,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return await result.UnlessAsync(predicate, operation).ConfigureAwait(false);
    }

    /// <summary>
    /// Conditionally executes an async operation if the condition is false.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">Task containing the result to test.</param>
    /// <param name="condition">Boolean condition.</param>
    /// <param name="operation">Async operation to execute if condition is false.</param>
    /// <returns>Result from the operation if condition is false and result is success; otherwise the original result.</returns>
    public static async Task<Result<T>> UnlessAsync<T>(
        this Task<Result<T>> resultTask,
        bool condition,
        Func<T, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(operation);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return await result.UnlessAsync(condition, operation).ConfigureAwait(false);
    }

    // ValueTask overloads for zero-allocation scenarios

    /// <summary>
    /// Conditionally executes an async operation if the predicate returns true.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Async operation to execute if predicate is true.</param>
    /// <returns>Result from the operation if predicate is true and result is success; otherwise the original result.</returns>
    public static async ValueTask<Result<T>> WhenAsync<T>(
        this ValueTask<Result<T>> resultTask,
        Func<T, bool> predicate,
        Func<T, ValueTask<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        Result<T> result = await resultTask.ConfigureAwait(false);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(WhenExtensions.When));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Value))
        {
            var operationResult = await operation(result.Value).ConfigureAwait(false);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Conditionally executes an async operation if the predicate returns false.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">ValueTask containing the result to test.</param>
    /// <param name="predicate">Predicate function to test the value.</param>
    /// <param name="operation">Async operation to execute if predicate is false.</param>
    /// <returns>Result from the operation if predicate is false and result is success; otherwise the original result.</returns>
    public static async ValueTask<Result<T>> UnlessAsync<T>(
        this ValueTask<Result<T>> resultTask,
        Func<T, bool> predicate,
        Func<T, ValueTask<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);

        Result<T> result = await resultTask.ConfigureAwait(false);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(WhenExtensions.Unless));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (!predicate(result.Value))
        {
            var operationResult = await operation(result.Value).ConfigureAwait(false);
            operationResult.LogActivityStatus();
            return operationResult;
        }

        result.LogActivityStatus();
        return result;
    }
}