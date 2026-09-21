namespace Trellis;

using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// Provides extension methods for ensuring multiple conditions on Result values simultaneously.
/// Unlike <see cref="EnsureExtensions.Ensure{TValue}(Result{TValue}, Func{TValue, bool}, Error)"/> which short-circuits on the first failure,
/// EnsureAll runs ALL checks and accumulates errors into a single combined error.
/// </summary>
/// <remarks>
/// This is the "applicative" validation style — validate everything, merge errors.
/// Useful for form validation where users need to see all errors at once.
/// </remarks>
[DebuggerStepThrough]
public static class EnsureAllExtensions
{
    /// <summary>
    /// Runs all validation checks on the result value and accumulates any failures into a single error.
    /// If the result is already a failure, it is returned unchanged.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to validate.</param>
    /// <param name="checks">The validation checks to run, each consisting of a predicate and an error to use if the predicate fails.</param>
    /// <returns>The original result if all checks pass; otherwise a failure with all accumulated errors.</returns>
    [OverloadResolutionPriority(1)]
    public static Result<TValue> EnsureAll<TValue>(
        this Result<TValue> result,
        params (Func<TValue, bool> predicate, Error error)[] checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureAll));

        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        result.TryGetValue(out var value);
        Error? accumulated = null;
        for (var i = 0; i < checks.Length; i++)
        {
            var (predicate, error) = checks[i];

            if (predicate is null)
                throw new ArgumentNullException(nameof(checks), $"checks[{i}].predicate is null.");

            if (error is null)
                throw new ArgumentNullException(nameof(checks), $"checks[{i}].error is null.");

            if (!predicate(value!))
                accumulated = accumulated.Combine(error);
        }

        if (accumulated is not null)
        {
            var output = Result.Fail<TValue>(accumulated);
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Runs every predicate and accumulates errors created lazily for failed checks.
    /// An upstream failure is returned unchanged without invoking predicates or factories.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to validate.</param>
    /// <param name="checks">Predicates and factories that receive the successful value.
    /// A factory is invoked exactly once when its predicate fails.</param>
    /// <returns>The original result if all checks pass; otherwise all accumulated errors.</returns>
    /// <exception cref="ArgumentNullException">The checks array or a checked delegate is null.</exception>
    /// <exception cref="InvalidOperationException">An invoked factory returns null.</exception>
    /// <remarks>
    /// The constant-error overload has higher overload-resolution priority so existing
    /// empty-check and null-literal calls retain their original meaning.
    /// </remarks>
    public static Result<TValue> EnsureAll<TValue>(
        this Result<TValue> result,
        params (Func<TValue, bool> predicate, Func<TValue, Error> errorFactory)[] checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureAll));

        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        result.TryGetValue(out var value);
        Error? accumulated = null;
        for (var i = 0; i < checks.Length; i++)
        {
            var (predicate, errorFactory) = checks[i];
            if (predicate is null)
                throw new ArgumentNullException(nameof(checks), $"checks[{i}].predicate is null.");
            if (errorFactory is null)
                throw new ArgumentNullException(nameof(checks), $"checks[{i}].errorFactory is null.");

            if (!predicate(value!))
            {
                var error = errorFactory(value!)
                    ?? throw new InvalidOperationException($"checks[{i}].errorFactory returned null.");
                accumulated = accumulated.Combine(error);
            }
        }

        if (accumulated is not null)
        {
            var output = Result.Fail<TValue>(accumulated);
            output.LogActivityStatus();
            return output;
        }

        result.LogActivityStatus();
        return result;
    }
}

/// <summary>
/// Provides asynchronous extension methods for EnsureAll validation accumulation on Result values.
/// </summary>
[DebuggerStepThrough]
public static class EnsureAllExtensionsAsync
{
    /// <summary>
    /// Asynchronously runs all validation checks on the result value and accumulates any failures.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task producing the result to validate.</param>
    /// <param name="checks">The validation checks to run, each consisting of a predicate and an error to use if the predicate fails.</param>
    /// <returns>The original result if all checks pass; otherwise a failure with all accumulated errors.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<Result<TValue>> EnsureAllAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        params (Func<TValue, bool> predicate, Error error)[] checks)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(checks);

        var result = await resultTask.ConfigureAwait(false);
        return result.EnsureAll(checks);
    }

    /// <summary>
    /// Asynchronously runs all validation checks on the result value and accumulates any failures.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The value task producing the result to validate.</param>
    /// <param name="checks">The validation checks to run, each consisting of a predicate and an error to use if the predicate fails.</param>
    /// <returns>The original result if all checks pass; otherwise a failure with all accumulated errors.</returns>
    [OverloadResolutionPriority(1)]
    public static async ValueTask<Result<TValue>> EnsureAllAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        params (Func<TValue, bool> predicate, Error error)[] checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var result = await resultTask.ConfigureAwait(false);
        return result.EnsureAll(checks);
    }

    /// <summary>
    /// Awaits the result, then runs all predicates with lazy, value-dependent error factories.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task producing the result to validate.</param>
    /// <param name="checks">Validation predicates and lazy error factories.</param>
    /// <returns>The original result or all accumulated validation errors.</returns>
    public static async Task<Result<TValue>> EnsureAllAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        params (Func<TValue, bool> predicate, Func<TValue, Error> errorFactory)[] checks)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(checks);

        var result = await resultTask.ConfigureAwait(false);
        return result.EnsureAll(checks);
    }

    /// <summary>
    /// Awaits the result, then runs all predicates with lazy, value-dependent error factories.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The value task producing the result to validate.</param>
    /// <param name="checks">Validation predicates and lazy error factories.</param>
    /// <returns>The original result or all accumulated validation errors.</returns>
    public static async ValueTask<Result<TValue>> EnsureAllAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        params (Func<TValue, bool> predicate, Func<TValue, Error> errorFactory)[] checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var result = await resultTask.ConfigureAwait(false);
        return result.EnsureAll(checks);
    }
}