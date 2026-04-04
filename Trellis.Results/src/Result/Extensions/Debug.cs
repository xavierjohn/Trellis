namespace Trellis;

using System;
using System.Diagnostics;

/// <summary>
/// Debug extension methods for inspecting Result values during development.
/// These methods execute only in DEBUG builds and become no-ops in RELEASE builds (with zero overhead).
/// </summary>
/// <remarks>
/// The methods are always available to prevent compilation errors in RELEASE builds,
/// but their implementation is conditionally compiled and becomes empty in RELEASE mode.
/// The compiler will inline and optimize away these empty methods, resulting in zero runtime overhead.
/// 
/// These methods create dedicated Activities for debug operations, making them compatible with OpenTelemetry
/// and modern observability systems like .NET Aspire, Application Insights, and Jaeger.
/// Debug activities appear as child spans in distributed traces, making it easy to filter them out in production.
/// </remarks>
public static class ResultDebugExtensions
{
    /// <summary>
    /// Writes debug information about the Result to a new Activity and returns the same Result.
    /// This method only executes in DEBUG builds.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The Result to debug.</param>
    /// <param name="message">Optional message to prefix the debug output.</param>
    /// <returns>The same Result that was passed in.</returns>
    /// <remarks>
    /// Creates a new Activity span for the debug operation, making it visible as a child span in:
    /// <list type="bullet">
    /// <item>.NET Aspire dashboard</item>
    /// <item>Application Insights</item>
    /// <item>Jaeger, Zipkin, and other OpenTelemetry-compatible tools</item>
    /// </list>
    /// The debug span includes result status, value/error information as tags.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = GetUser(id)
    ///     .Debug("After GetUser")
    ///     .Ensure(u => u.IsActive, Error.Validation("Inactive"))
    ///     .Debug("After Ensure")
    ///     .Bind(ProcessUser)
    ///     .Debug("After ProcessUser");
    /// </code>
    /// </example>
    public static Result<TValue> Debug<TValue>(this Result<TValue> result, string message = "")
    {
#if DEBUG
        if (!ResultDebugSettings.EnableDebugTracing)
            return result;

        var activityName = string.IsNullOrEmpty(message) ? "Debug" : $"Debug: {message}";
        using var activity = RopTrace.ActivitySource.StartActivity(activityName, ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("debug.result.status", result.IsSuccess ? "Success" : "Failure");

            if (result.IsSuccess)
            {
                activity.SetTag("debug.result.value", result.Value?.ToString() ?? "<null>");
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity.SetTag("debug.error.code", result.Error.Code);
                activity.SetTag("debug.error.detail", result.Error.Detail);
                activity.SetStatus(ActivityStatusCode.Error, result.Error.Detail);
            }
        }
#endif

        return result;
    }

    /// <summary>
    /// Writes detailed debug information about the Result to a new Activity, including error properties.
    /// This method only executes in DEBUG builds.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The Result to debug.</param>
    /// <param name="message">Optional message to prefix the debug output.</param>
    /// <returns>The same Result that was passed in.</returns>
    /// <example>
    /// <code>
    /// var result = EmailAddress.TryCreate(email)
    ///     .Combine(FirstName.TryCreate(firstName))
    ///     .DebugDetailed("After validation");
    /// </code>
    /// </example>
    public static Result<TValue> DebugDetailed<TValue>(this Result<TValue> result, string message = "")
    {
#if DEBUG
        if (!ResultDebugSettings.EnableDebugTracing)
            return result;

        var activityName = string.IsNullOrEmpty(message) ? "Debug (Detailed)" : $"Debug: {message} (Detailed)";
        using var activity = RopTrace.ActivitySource.StartActivity(activityName, ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("debug.result.status", result.IsSuccess ? "Success" : "Failure");

            if (result.IsSuccess)
            {
                activity.SetTag("debug.result.type", typeof(TValue).Name);
                activity.SetTag("debug.result.value", result.Value?.ToString() ?? "<null>");
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                var error = result.Error;
                activity.SetTag("debug.error.type", error.GetType().Name);
                activity.SetTag("debug.error.code", error.Code);
                activity.SetTag("debug.error.detail", error.Detail);
                activity.SetTag("debug.error.instance", error.Instance ?? "(none)");

                if (error is ValidationError validationError)
                {
                    activity.SetTag("debug.error.validation.field_count", validationError.FieldErrors.Length);
                    for (int i = 0; i < Math.Min(validationError.FieldErrors.Length, 10); i++)
                    {
                        var fieldError = validationError.FieldErrors[i];
                        activity.SetTag($"debug.error.validation.field[{i}].name", fieldError.FieldName);
                        activity.SetTag($"debug.error.validation.field[{i}].details", string.Join(", ", fieldError.Details));
                    }
                }
                else if (error is AggregateError aggregated)
                {
                    activity.SetTag("debug.error.aggregate.count", aggregated.Errors.Count);
                    for (int i = 0; i < Math.Min(aggregated.Errors.Count, 10); i++)
                    {
                        var err = aggregated.Errors[i];
                        activity.SetTag($"debug.error.aggregate[{i}].code", err.Code);
                        activity.SetTag($"debug.error.aggregate[{i}].detail", err.Detail);
                    }
                }

                activity.SetStatus(ActivityStatusCode.Error, error.Detail);
            }
        }
#endif

        return result;
    }

    /// <summary>
    /// Writes debug information with a stack trace for detailed debugging.
    /// Useful for understanding the call chain when debugging complex ROP operations.
    /// This method only executes in DEBUG builds.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The Result to debug.</param>
    /// <param name="message">Optional message to prefix the debug output.</param>
    /// <param name="includeStackTrace">Whether to include the stack trace. Default is true.</param>
    /// <returns>The same Result that was passed in.</returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Debug-only code not included in Release builds")]
    public static Result<TValue> DebugWithStack<TValue>(this Result<TValue> result, string message = "", bool includeStackTrace = true)
    {
#if DEBUG
        if (!ResultDebugSettings.EnableDebugTracing)
            return result;

        var activityName = string.IsNullOrEmpty(message) ? "Debug (with stack)" : $"Debug: {message} (with stack)";
        using var activity = RopTrace.ActivitySource.StartActivity(activityName, ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("debug.result.status", result.IsSuccess ? "Success" : "Failure");

            if (result.IsSuccess)
            {
                activity.SetTag("debug.result.value", result.Value?.ToString() ?? "<null>");
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity.SetTag("debug.error.code", result.Error.Code);
                activity.SetTag("debug.error.detail", result.Error.Detail);
                activity.SetStatus(ActivityStatusCode.Error, result.Error.Detail);
            }

            if (includeStackTrace)
            {
                var stackTrace = new StackTrace(true);
                var frames = stackTrace.GetFrames();

                if (frames is null)
                    return result;

                // Capture up to 10 stack frames
                for (int i = 0; i < Math.Min(frames.Length, 10); i++)
                {
                    var frame = frames[i];
                    var method = frame.GetMethod();
                    if (method != null)
                    {
                        activity.SetTag($"debug.stack[{i}].method", $"{method.DeclaringType?.Name}.{method.Name}");
                        var fileName = frame.GetFileName();
                        if (fileName != null)
                        {
                            activity.SetTag($"debug.stack[{i}].file", fileName);
                            activity.SetTag($"debug.stack[{i}].line", frame.GetFileLineNumber());
                        }
                    }
                }
            }
        }
#endif

        return result;
    }

    /// <summary>
    /// Executes a custom debug action if the result is a success.
    /// This method only executes in DEBUG builds.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The Result to debug.</param>
    /// <param name="action">The action to execute with the success value.</param>
    /// <returns>The same Result that was passed in.</returns>
    /// <example>
    /// <code>
    /// var result = GetUser(id)
    ///     .DebugOnSuccess(user => 
    ///     {
    ///         var activity = Activity.Current;
    ///         activity?.SetTag("user.id", user.Id);
    ///         activity?.SetTag("user.email", user.Email);
    ///         activity?.SetTag("user.is_active", user.IsActive);
    ///     });
    /// </code>
    /// </example>
    public static Result<TValue> DebugOnSuccess<TValue>(this Result<TValue> result, Action<TValue> action)
    {
        ArgumentNullException.ThrowIfNull(action);

#if DEBUG
        if (ResultDebugSettings.EnableDebugTracing && result.IsSuccess)
        {
            using var activity = RopTrace.ActivitySource.StartActivity("Debug: OnSuccess", ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            action(result.Value);
        }
#endif

        return result;
    }

    /// <summary>
    /// Executes a custom debug action if the result is a failure.
    /// This method only executes in DEBUG builds.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The Result to debug.</param>
    /// <param name="action">The action to execute with the error.</param>
    /// <returns>The same Result that was passed in.</returns>
    /// <example>
    /// <code>
    /// var result = GetUser(id)
    ///     .DebugOnFailure(error => 
    ///     {
    ///         var activity = Activity.Current;
    ///         activity?.SetTag("error.type", error.GetType().Name);
    ///         activity?.SetTag("error.code", error.Code);
    ///         activity?.SetTag("error.detail", error.Detail);
    ///     });
    /// </code>
    /// </example>
    public static Result<TValue> DebugOnFailure<TValue>(this Result<TValue> result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);

#if DEBUG
        if (ResultDebugSettings.EnableDebugTracing && result.IsFailure)
        {
            using var activity = RopTrace.ActivitySource.StartActivity("Debug: OnFailure", ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag("debug.error.code", result.Error.Code);
                activity.SetStatus(ActivityStatusCode.Error, result.Error.Detail);
            }

            action(result.Error);
        }
#endif

        return result;
    }
}

/// <summary>
/// Debug-only async extension methods for inspecting Result values during development.
/// These methods are only available in DEBUG builds and are automatically excluded from RELEASE builds.
/// Debug information is written to dedicated Activity spans for OpenTelemetry compatibility.
/// </summary>
public static class ResultDebugExtensionsAsync
{
    /// <summary>
    /// Writes debug information about the Result to a new Activity and returns the same Result.
    /// This method only executes in DEBUG builds.
    /// </summary>
    public static async Task<Result<TValue>> DebugAsync<TValue>(this Task<Result<TValue>> resultTask, string message = "")
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask.ConfigureAwait(false);
#if DEBUG
        return result.Debug(message);
#else
        return result;
#endif
    }

    /// <summary>
    /// Writes detailed debug information about the Result to a new Activity.
    /// This method only executes in DEBUG builds.
    /// </summary>
    public static async Task<Result<TValue>> DebugDetailedAsync<TValue>(this Task<Result<TValue>> resultTask, string message = "")
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask.ConfigureAwait(false);
#if DEBUG
        return result.DebugDetailed(message);
#else
        return result;
#endif
    }

    /// <summary>
    /// Writes debug information with a stack trace for detailed debugging.
    /// This method only executes in DEBUG builds.
    /// </summary>
    public static async Task<Result<TValue>> DebugWithStackAsync<TValue>(this Task<Result<TValue>> resultTask, string message = "", bool includeStackTrace = true)
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask.ConfigureAwait(false);
#if DEBUG
        return result.DebugWithStack(message, includeStackTrace);
#else
        return result;
#endif
    }

    /// <summary>
    /// Executes a custom debug action if the result is a success.
    /// This method only executes in DEBUG builds.
    /// </summary>
    public static async Task<Result<TValue>> DebugOnSuccessAsync<TValue>(this Task<Result<TValue>> resultTask, Action<TValue> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        var result = await resultTask.ConfigureAwait(false);
#if DEBUG
        return result.DebugOnSuccess(action);
#else
        return result;
#endif
    }

    /// <summary>
    /// Executes a custom debug action if the result is a failure.
    /// This method only executes in DEBUG builds.
    /// </summary>
    public static async Task<Result<TValue>> DebugOnFailureAsync<TValue>(this Task<Result<TValue>> resultTask, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        var result = await resultTask.ConfigureAwait(false);
#if DEBUG
        return result.DebugOnFailure(action);
#else
        return result;
#endif
    }

    /// <summary>
    /// Executes an async custom debug action if the result is a success.
    /// This method only executes in DEBUG builds.
    /// </summary>
    public static async Task<Result<TValue>> DebugOnSuccessAsync<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        var result = await resultTask.ConfigureAwait(false);
#if DEBUG
        if (ResultDebugSettings.EnableDebugTracing && result.IsSuccess)
        {
            using var activity = RopTrace.ActivitySource.StartActivity("Debug: OnSuccess", ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            await action(result.Value).ConfigureAwait(false);
        }
#endif

        return result;
    }

    /// <summary>
    /// Executes an async custom debug action if the result is a failure.
    /// This method only executes in DEBUG builds.
    /// </summary>
    public static async Task<Result<TValue>> DebugOnFailureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<Error, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);

        var result = await resultTask.ConfigureAwait(false);
#if DEBUG
        if (ResultDebugSettings.EnableDebugTracing && result.IsFailure)
        {
            using var activity = RopTrace.ActivitySource.StartActivity("Debug: OnFailure", ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag("debug.error.code", result.Error.Code);
                activity.SetStatus(ActivityStatusCode.Error, result.Error.Detail);
            }

            await action(result.Error).ConfigureAwait(false);
        }
#endif

        return result;
    }
}