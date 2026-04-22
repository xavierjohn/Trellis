namespace Trellis;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

// =============================================================================
// Pipeline verb extensions for the non-generic Result.
//
// This file mirrors the seven pipeline verbs (Map, Bind, Tap, TapOnFailure, Ensure,
// Match, Recover) plus a small set of cross-shape helpers, but for the
// non-generic Result that replaces Result<Unit>. It is deliberately a single
// file rather than the per-verb / per-async-shape split that Result<T> uses,
// because:
//   1. The unit shape has no value to project, so Map/Bind/Tap/Ensure/Recover
//      collapse to a much smaller set of overloads (no Func<T, …> shape).
//   2. Co-locating the unit-Result surface keeps the migration auditable.
//
// Async overload model mirrors §3.3 of the v2 redesign plan: 6 overloads per
// verb covering Sync/Task/ValueTask × sync/async function. Mixing Task and
// ValueTask requires explicit conversion (deliberate constraint).
//
// Tracing parity: every overload that owns an Activity (i.e., starts one with
// RopTrace.ActivitySource.StartActivity) calls LogActivityStatus() before
// returning on every exit path — short-circuit, success and failure — to
// mirror the behavior of the generic Result<T> verbs. Pure passthrough
// overloads delegate to an inner overload that owns the activity; they only
// validate their arguments and forward.
// =============================================================================

#region Map (unit → value)

/// <summary>Map for the non-generic <see cref="Result"/>: projects a successful unit-result into a value-bearing result.</summary>
[DebuggerStepThrough]
public static class ResultMapExtensions
{
    /// <summary>Maps a successful <see cref="Result"/> to a value via <paramref name="func"/>; failure propagates unchanged.</summary>
    public static Result<TOut> Map<TOut>(this Result result, Func<TOut> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Map));
        return result.IsFailure ? Result.Fail<TOut>(result.Error) : Result.Ok(func());
    }
}

/// <summary>Async Map for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultMapExtensionsAsync
{
    /// <summary>Awaits <paramref name="resultTask"/> and projects success to a value via <paramref name="func"/>.</summary>
    public static async Task<Result<TOut>> MapAsync<TOut>(this Task<Result> resultTask, Func<TOut> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Map(func);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and projects success to a value via <paramref name="func"/>.</summary>
    public static async ValueTask<Result<TOut>> MapAsync<TOut>(this ValueTask<Result> resultTask, Func<TOut> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Map(func);
    }

    /// <summary>Projects success to a value via async <paramref name="func"/>; failure propagates unchanged.</summary>
    public static async Task<Result<TOut>> MapAsync<TOut>(this Result result, Func<Task<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultMapExtensions.Map));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return Result.Fail<TOut>(result.Error);
        }

        var output = Result.Ok(await func().ConfigureAwait(false));
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and projects success to a value via async <paramref name="func"/>.</summary>
    public static async Task<Result<TOut>> MapAsync<TOut>(this Task<Result> resultTask, Func<Task<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.MapAsync(func).ConfigureAwait(false);
    }

    /// <summary>Projects success to a value via async <paramref name="func"/> (ValueTask); failure propagates unchanged.</summary>
    public static async ValueTask<Result<TOut>> MapAsync<TOut>(this Result result, Func<ValueTask<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultMapExtensions.Map));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return Result.Fail<TOut>(result.Error);
        }

        var output = Result.Ok(await func().ConfigureAwait(false));
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and projects success via async <paramref name="func"/> (ValueTask).</summary>
    public static async ValueTask<Result<TOut>> MapAsync<TOut>(this ValueTask<Result> resultTask, Func<ValueTask<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.MapAsync(func).ConfigureAwait(false);
    }
}

#endregion

#region Bind (unit → unit, unit → value, value → unit)

/// <summary>Bind for the non-generic <see cref="Result"/>, including cross-shape forms.</summary>
[DebuggerStepThrough]
public static class ResultBindExtensions
{
    /// <summary>Chains another non-generic <see cref="Result"/>-returning step. Failure short-circuits.</summary>
    public static Result Bind(this Result result, Func<Result> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Bind));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = func();
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Chains a value-producing step from a unit-shaped success. Failure short-circuits.</summary>
    public static Result<TOut> Bind<TOut>(this Result result, Func<Result<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Bind));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return Result.Fail<TOut>(result.Error);
        }

        var output = func();
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Cross-shape: discards the value of a value-bearing result and chains a unit-shaped step.</summary>
    public static Result Bind<TIn>(this Result<TIn> result, Func<TIn, Result> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Bind));
        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return Result.Fail(result.Error);
        }

        var output = func(value);
        output.LogActivityStatus();
        return output;
    }
}

/// <summary>Async Bind for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultBindExtensionsAsync
{
    // unit → unit

    /// <summary>Awaits <paramref name="resultTask"/> and chains a unit-shaped step via <paramref name="func"/>.</summary>
    public static async Task<Result> BindAsync(this Task<Result> resultTask, Func<Result> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Bind(func);
    }

    /// <summary>Chains an async unit-shaped step via <paramref name="func"/>. Failure short-circuits.</summary>
    public static async Task<Result> BindAsync(this Result result, Func<Task<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultBindExtensions.Bind));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains an async unit-shaped step via <paramref name="func"/>.</summary>
    public static async Task<Result> BindAsync(this Task<Result> resultTask, Func<Task<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains a unit-shaped step via <paramref name="func"/>.</summary>
    public static async ValueTask<Result> BindAsync(this ValueTask<Result> resultTask, Func<Result> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Bind(func);
    }

    /// <summary>Chains an async unit-shaped step (ValueTask). Failure short-circuits.</summary>
    public static async ValueTask<Result> BindAsync(this Result result, Func<ValueTask<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultBindExtensions.Bind));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains an async unit-shaped step (ValueTask).</summary>
    public static async ValueTask<Result> BindAsync(this ValueTask<Result> resultTask, Func<ValueTask<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    // unit → value

    /// <summary>Awaits <paramref name="resultTask"/> and chains a value-producing step via <paramref name="func"/>.</summary>
    public static async Task<Result<TOut>> BindAsync<TOut>(this Task<Result> resultTask, Func<Result<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Bind(func);
    }

    /// <summary>Chains an async value-producing step via <paramref name="func"/>. Failure short-circuits.</summary>
    public static async Task<Result<TOut>> BindAsync<TOut>(this Result result, Func<Task<Result<TOut>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultBindExtensions.Bind));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return Result.Fail<TOut>(result.Error);
        }

        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains an async value-producing step via <paramref name="func"/>.</summary>
    public static async Task<Result<TOut>> BindAsync<TOut>(this Task<Result> resultTask, Func<Task<Result<TOut>>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains a value-producing step via <paramref name="func"/>.</summary>
    public static async ValueTask<Result<TOut>> BindAsync<TOut>(this ValueTask<Result> resultTask, Func<Result<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Bind(func);
    }

    /// <summary>Chains an async value-producing step (ValueTask). Failure short-circuits.</summary>
    public static async ValueTask<Result<TOut>> BindAsync<TOut>(this Result result, Func<ValueTask<Result<TOut>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultBindExtensions.Bind));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return Result.Fail<TOut>(result.Error);
        }

        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains an async value-producing step (ValueTask).</summary>
    public static async ValueTask<Result<TOut>> BindAsync<TOut>(this ValueTask<Result> resultTask, Func<ValueTask<Result<TOut>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    // value → unit (cross-shape)

    /// <summary>Awaits <paramref name="resultTask"/> and discards the value to chain a unit-shaped step.</summary>
    public static async Task<Result> BindAsync<TIn>(this Task<Result<TIn>> resultTask, Func<TIn, Result> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Bind(func);
    }

    /// <summary>Cross-shape async: chains an async unit-shaped step from a value-bearing success.</summary>
    public static async Task<Result> BindAsync<TIn>(this Result<TIn> result, Func<TIn, Task<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultBindExtensions.Bind));
        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return Result.Fail(result.Error);
        }

        var output = await func(value).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains an async unit-shaped step from a value-bearing success.</summary>
    public static async Task<Result> BindAsync<TIn>(this Task<Result<TIn>> resultTask, Func<TIn, Task<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and discards the value to chain a unit-shaped step.</summary>
    public static async ValueTask<Result> BindAsync<TIn>(this ValueTask<Result<TIn>> resultTask, Func<TIn, Result> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return (await resultTask.ConfigureAwait(false)).Bind(func);
    }

    /// <summary>Cross-shape async (ValueTask): chains an async unit-shaped step from a value-bearing success.</summary>
    public static async ValueTask<Result> BindAsync<TIn>(this Result<TIn> result, Func<TIn, ValueTask<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultBindExtensions.Bind));
        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return Result.Fail(result.Error);
        }

        var output = await func(value).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and chains an async unit-shaped step (ValueTask) from a value-bearing success.</summary>
    public static async ValueTask<Result> BindAsync<TIn>(this ValueTask<Result<TIn>> resultTask, Func<TIn, ValueTask<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }
}

#endregion

#region Tap (success-side side effect)

/// <summary>Tap for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultTapExtensions
{
    /// <summary>Invokes <paramref name="action"/> if the result is a success; returns the result unchanged.</summary>
    public static Result Tap(this Result result, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Tap));
        if (result.IsSuccess) action();
        result.LogActivityStatus();
        return result;
    }
}

/// <summary>Async Tap for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultTapExtensionsAsync
{
    /// <summary>Awaits <paramref name="resultTask"/> and invokes <paramref name="action"/> on success.</summary>
    public static async Task<Result> TapAsync(this Task<Result> resultTask, Action action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return (await resultTask.ConfigureAwait(false)).Tap(action);
    }

    /// <summary>Invokes async <paramref name="func"/> on success; returns the result unchanged.</summary>
    public static async Task<Result> TapAsync(this Result result, Func<Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultTapExtensions.Tap));
        if (result.IsSuccess) await func().ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and invokes async <paramref name="func"/> on success.</summary>
    public static async Task<Result> TapAsync(this Task<Result> resultTask, Func<Task> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapAsync(func).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and invokes <paramref name="action"/> on success.</summary>
    public static async ValueTask<Result> TapAsync(this ValueTask<Result> resultTask, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (await resultTask.ConfigureAwait(false)).Tap(action);
    }

    /// <summary>Invokes async <paramref name="func"/> on success (ValueTask); returns the result unchanged.</summary>
    public static async ValueTask<Result> TapAsync(this Result result, Func<ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultTapExtensions.Tap));
        if (result.IsSuccess) await func().ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and invokes async <paramref name="func"/> on success (ValueTask).</summary>
    public static async ValueTask<Result> TapAsync(this ValueTask<Result> resultTask, Func<ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapAsync(func).ConfigureAwait(false);
    }
}

#endregion

#region TapOnFailure (failure-side side effect)

/// <summary>TapOnFailure for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultTapOnFailureExtensions
{
    /// <summary>Invokes <paramref name="action"/> with the error if the result is a failure; returns the result unchanged.</summary>
    public static Result TapOnFailure(this Result result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(TapOnFailure));
        if (result.IsFailure) action(result.Error);
        result.LogActivityStatus();
        return result;
    }
}

/// <summary>Async TapOnFailure for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultTapOnFailureExtensionsAsync
{
    /// <summary>Awaits <paramref name="resultTask"/> and invokes <paramref name="action"/> on failure.</summary>
    public static async Task<Result> TapOnFailureAsync(this Task<Result> resultTask, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return (await resultTask.ConfigureAwait(false)).TapOnFailure(action);
    }

    /// <summary>Invokes async <paramref name="func"/> with the error on failure; returns the result unchanged.</summary>
    public static async Task<Result> TapOnFailureAsync(this Result result, Func<Error, Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultTapOnFailureExtensions.TapOnFailure));
        if (result.IsFailure) await func(result.Error).ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and invokes async <paramref name="func"/> on failure.</summary>
    public static async Task<Result> TapOnFailureAsync(this Task<Result> resultTask, Func<Error, Task> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapOnFailureAsync(func).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and invokes <paramref name="action"/> on failure.</summary>
    public static async ValueTask<Result> TapOnFailureAsync(this ValueTask<Result> resultTask, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (await resultTask.ConfigureAwait(false)).TapOnFailure(action);
    }

    /// <summary>Invokes async <paramref name="func"/> with the error on failure (ValueTask); returns the result unchanged.</summary>
    public static async ValueTask<Result> TapOnFailureAsync(this Result result, Func<Error, ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultTapOnFailureExtensions.TapOnFailure));
        if (result.IsFailure) await func(result.Error).ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and invokes async <paramref name="func"/> on failure (ValueTask).</summary>
    public static async ValueTask<Result> TapOnFailureAsync(this ValueTask<Result> resultTask, Func<Error, ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapOnFailureAsync(func).ConfigureAwait(false);
    }
}

#endregion

#region Ensure (predicate gate, instance-shape on Result)

/// <summary>Ensure for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultEnsureExtensions
{
    /// <summary>Returns success if the predicate holds; otherwise a failure with <paramref name="error"/>. Failure short-circuits.</summary>
    public static Result Ensure(this Result result, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = predicate() ? Result.Ok() : Result.Fail(error);
        output.LogActivityStatus();
        return output;
    }
}

/// <summary>Async Ensure for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultEnsureExtensionsAsync
{
    /// <summary>Awaits <paramref name="resultTask"/> and applies <see cref="ResultEnsureExtensions.Ensure"/>.</summary>
    public static async Task<Result> EnsureAsync(this Task<Result> resultTask, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);
    }

    /// <summary>Returns success if the async predicate holds; otherwise a failure with <paramref name="error"/>. Failure short-circuits.</summary>
    public static async Task<Result> EnsureAsync(this Result result, Func<Task<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultEnsureExtensions.Ensure));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await predicate().ConfigureAwait(false) ? Result.Ok() : Result.Fail(error);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and applies the async predicate gate.</summary>
    public static async Task<Result> EnsureAsync(this Task<Result> resultTask, Func<Task<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await resultTask.ConfigureAwait(false);
        return await result.EnsureAsync(predicate, error).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and applies <see cref="ResultEnsureExtensions.Ensure"/>.</summary>
    public static async ValueTask<Result> EnsureAsync(this ValueTask<Result> resultTask, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);
    }

    /// <summary>Returns success if the async predicate (ValueTask) holds; otherwise a failure. Failure short-circuits.</summary>
    public static async ValueTask<Result> EnsureAsync(this Result result, Func<ValueTask<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultEnsureExtensions.Ensure));
        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await predicate().ConfigureAwait(false) ? Result.Ok() : Result.Fail(error);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and applies the async predicate gate (ValueTask).</summary>
    public static async ValueTask<Result> EnsureAsync(this ValueTask<Result> resultTask, Func<ValueTask<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        var result = await resultTask.ConfigureAwait(false);
        return await result.EnsureAsync(predicate, error).ConfigureAwait(false);
    }
}

#endregion

#region Match (collapse to a single value)

/// <summary>Match for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultMatchExtensions
{
    /// <summary>Collapses to a value: invokes <paramref name="onSuccess"/> on success, <paramref name="onFailure"/> on failure.</summary>
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsFailure ? onFailure(result.Error) : onSuccess();
    }

    /// <summary>Collapses to side effects: invokes <paramref name="onSuccess"/> on success, <paramref name="onFailure"/> on failure.</summary>
    public static void Match(this Result result, Action onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        if (result.IsFailure) onFailure(result.Error); else onSuccess();
    }
}

/// <summary>Async Match for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultMatchExtensionsAsync
{
    /// <summary>Awaits <paramref name="resultTask"/> and collapses to a value via the appropriate handler.</summary>
    public static async Task<TOut> MatchAsync<TOut>(this Task<Result> resultTask, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);
    }

    /// <summary>Collapses to a value via async handlers.</summary>
    public static async Task<TOut> MatchAsync<TOut>(this Result result, Func<Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsFailure
            ? await onFailure(result.Error).ConfigureAwait(false)
            : await onSuccess().ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and collapses to a value via async handlers.</summary>
    public static async Task<TOut> MatchAsync<TOut>(this Task<Result> resultTask, Func<Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await resultTask.ConfigureAwait(false);
        return await result.MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and collapses to a value via the appropriate handler.</summary>
    public static async ValueTask<TOut> MatchAsync<TOut>(this ValueTask<Result> resultTask, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);
    }

    /// <summary>Collapses to a value via async handlers (ValueTask).</summary>
    public static async ValueTask<TOut> MatchAsync<TOut>(this Result result, Func<ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsFailure
            ? await onFailure(result.Error).ConfigureAwait(false)
            : await onSuccess().ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and collapses to a value via async handlers (ValueTask).</summary>
    public static async ValueTask<TOut> MatchAsync<TOut>(this ValueTask<Result> resultTask, Func<ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        var result = await resultTask.ConfigureAwait(false);
        return await result.MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
    }
}

#endregion

#region Recover (failure → success)

/// <summary>Recover for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultRecoverExtensions
{
    /// <summary>Replaces a failure with the result of <paramref name="recovery"/>; success passes through unchanged.</summary>
    public static Result Recover(this Result result, Func<Error, Result> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Recover));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = recovery(result.Error);
        output.LogActivityStatus();
        return output;
    }
}

/// <summary>Async Recover for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultRecoverExtensionsAsync
{
    /// <summary>Awaits <paramref name="resultTask"/> and applies <see cref="ResultRecoverExtensions.Recover"/>.</summary>
    public static async Task<Result> RecoverAsync(this Task<Result> resultTask, Func<Error, Result> recovery)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(recovery);
        return (await resultTask.ConfigureAwait(false)).Recover(recovery);
    }

    /// <summary>Replaces a failure with the result of an async <paramref name="recovery"/>; success passes through unchanged.</summary>
    public static async Task<Result> RecoverAsync(this Result result, Func<Error, Task<Result>> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultRecoverExtensions.Recover));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await recovery(result.Error).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and applies an async recovery.</summary>
    public static async Task<Result> RecoverAsync(this Task<Result> resultTask, Func<Error, Task<Result>> recovery)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(recovery);
        var result = await resultTask.ConfigureAwait(false);
        return await result.RecoverAsync(recovery).ConfigureAwait(false);
    }

    /// <summary>Awaits <paramref name="resultTask"/> and applies <see cref="ResultRecoverExtensions.Recover"/>.</summary>
    public static async ValueTask<Result> RecoverAsync(this ValueTask<Result> resultTask, Func<Error, Result> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        return (await resultTask.ConfigureAwait(false)).Recover(recovery);
    }

    /// <summary>Replaces a failure with the result of an async <paramref name="recovery"/> (ValueTask); success passes through unchanged.</summary>
    public static async ValueTask<Result> RecoverAsync(this Result result, Func<Error, ValueTask<Result>> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ResultRecoverExtensions.Recover));
        if (result.IsSuccess)
        {
            result.LogActivityStatus();
            return result;
        }

        var output = await recovery(result.Error).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Awaits <paramref name="resultTask"/> and applies an async recovery (ValueTask).</summary>
    public static async ValueTask<Result> RecoverAsync(this ValueTask<Result> resultTask, Func<Error, ValueTask<Result>> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        var result = await resultTask.ConfigureAwait(false);
        return await result.RecoverAsync(recovery).ConfigureAwait(false);
    }
}

#endregion

#region AsUnit (cross-shape async wrappers)

/// <summary>Async <see cref="Result{TValue}.AsUnit"/> wrappers.</summary>
[DebuggerStepThrough]
public static class ResultAsUnitExtensionsAsync
{
    /// <summary>Awaits <paramref name="resultTask"/> and discards its value, producing a non-generic <see cref="Result"/>.</summary>
    public static async Task<Result> AsUnitAsync<T>(this Task<Result<T>> resultTask)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return (await resultTask.ConfigureAwait(false)).AsUnit();
    }

    /// <summary>Awaits <paramref name="resultTask"/> and discards its value, producing a non-generic <see cref="Result"/>.</summary>
    public static async ValueTask<Result> AsUnitAsync<T>(this ValueTask<Result<T>> resultTask)
        => (await resultTask.ConfigureAwait(false)).AsUnit();
}

#endregion
