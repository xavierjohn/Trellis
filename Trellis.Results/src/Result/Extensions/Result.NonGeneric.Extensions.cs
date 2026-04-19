namespace Trellis;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

#pragma warning disable CS1591 // Missing XML comment — overload-heavy mirror file; one summary per region documents intent.

// =============================================================================
// Pipeline verb extensions for the non-generic Result.
//
// This file mirrors the seven pipeline verbs (Map, Bind, Tap, TapError, Ensure,
// Match, Recover) plus Try and a small set of cross-shape helpers, but for the
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
    public static async Task<Result<TOut>> MapAsync<TOut>(this Task<Result> resultTask, Func<TOut> func)
        => (await resultTask.ConfigureAwait(false)).Map(func);

    public static async ValueTask<Result<TOut>> MapAsync<TOut>(this ValueTask<Result> resultTask, Func<TOut> func)
        => (await resultTask.ConfigureAwait(false)).Map(func);

    public static async Task<Result<TOut>> MapAsync<TOut>(this Result result, Func<Task<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return Result.Fail<TOut>(result.Error);
        return Result.Ok(await func().ConfigureAwait(false));
    }

    public static async Task<Result<TOut>> MapAsync<TOut>(this Task<Result> resultTask, Func<Task<TOut>> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.MapAsync(func).ConfigureAwait(false);
    }

    public static async ValueTask<Result<TOut>> MapAsync<TOut>(this Result result, Func<ValueTask<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return Result.Fail<TOut>(result.Error);
        return Result.Ok(await func().ConfigureAwait(false));
    }

    public static async ValueTask<Result<TOut>> MapAsync<TOut>(this ValueTask<Result> resultTask, Func<ValueTask<TOut>> func)
    {
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
        if (result.IsFailure) return result;
        var output = func();
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Chains a value-producing step from a unit-shaped success.</summary>
    public static Result<TOut> Bind<TOut>(this Result result, Func<Result<TOut>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Bind));
        if (result.IsFailure) return Result.Fail<TOut>(result.Error);
        var output = func();
        output.LogActivityStatus();
        return output;
    }

    /// <summary>Cross-shape: discards the value of a value-bearing result and chains a unit-shaped step.</summary>
    public static Result Bind<TIn>(this Result<TIn> result, Func<TIn, Result> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Bind));
        if (result.IsFailure) return Result.Fail(result.Error);
        var output = func(result.Value);
        output.LogActivityStatus();
        return output;
    }
}

/// <summary>Async Bind for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultBindExtensionsAsync
{
    // unit → unit
    public static async Task<Result> BindAsync(this Task<Result> resultTask, Func<Result> func)
        => (await resultTask.ConfigureAwait(false)).Bind(func);

    public static async Task<Result> BindAsync(this Result result, Func<Task<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return result;
        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async Task<Result> BindAsync(this Task<Result> resultTask, Func<Task<Result>> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    public static async ValueTask<Result> BindAsync(this ValueTask<Result> resultTask, Func<Result> func)
        => (await resultTask.ConfigureAwait(false)).Bind(func);

    public static async ValueTask<Result> BindAsync(this Result result, Func<ValueTask<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return result;
        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async ValueTask<Result> BindAsync(this ValueTask<Result> resultTask, Func<ValueTask<Result>> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    // unit → value
    public static async Task<Result<TOut>> BindAsync<TOut>(this Task<Result> resultTask, Func<Result<TOut>> func)
        => (await resultTask.ConfigureAwait(false)).Bind(func);

    public static async Task<Result<TOut>> BindAsync<TOut>(this Result result, Func<Task<Result<TOut>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return Result.Fail<TOut>(result.Error);
        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async Task<Result<TOut>> BindAsync<TOut>(this Task<Result> resultTask, Func<Task<Result<TOut>>> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    public static async ValueTask<Result<TOut>> BindAsync<TOut>(this ValueTask<Result> resultTask, Func<Result<TOut>> func)
        => (await resultTask.ConfigureAwait(false)).Bind(func);

    public static async ValueTask<Result<TOut>> BindAsync<TOut>(this Result result, Func<ValueTask<Result<TOut>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return Result.Fail<TOut>(result.Error);
        var output = await func().ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async ValueTask<Result<TOut>> BindAsync<TOut>(this ValueTask<Result> resultTask, Func<ValueTask<Result<TOut>>> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    // value → unit (cross-shape)
    public static async Task<Result> BindAsync<TIn>(this Task<Result<TIn>> resultTask, Func<TIn, Result> func)
        => (await resultTask.ConfigureAwait(false)).Bind(func);

    public static async Task<Result> BindAsync<TIn>(this Result<TIn> result, Func<TIn, Task<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return Result.Fail(result.Error);
        var output = await func(result.Value).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async Task<Result> BindAsync<TIn>(this Task<Result<TIn>> resultTask, Func<TIn, Task<Result>> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.BindAsync(func).ConfigureAwait(false);
    }

    public static async ValueTask<Result> BindAsync<TIn>(this ValueTask<Result<TIn>> resultTask, Func<TIn, Result> func)
        => (await resultTask.ConfigureAwait(false)).Bind(func);

    public static async ValueTask<Result> BindAsync<TIn>(this Result<TIn> result, Func<TIn, ValueTask<Result>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) return Result.Fail(result.Error);
        var output = await func(result.Value).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async ValueTask<Result> BindAsync<TIn>(this ValueTask<Result<TIn>> resultTask, Func<TIn, ValueTask<Result>> func)
    {
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
    public static async Task<Result> TapAsync(this Task<Result> resultTask, Action action)
        => (await resultTask.ConfigureAwait(false)).Tap(action);

    public static async Task<Result> TapAsync(this Result result, Func<Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsSuccess) await func().ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    public static async Task<Result> TapAsync(this Task<Result> resultTask, Func<Task> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapAsync(func).ConfigureAwait(false);
    }

    public static async ValueTask<Result> TapAsync(this ValueTask<Result> resultTask, Action action)
        => (await resultTask.ConfigureAwait(false)).Tap(action);

    public static async ValueTask<Result> TapAsync(this Result result, Func<ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsSuccess) await func().ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    public static async ValueTask<Result> TapAsync(this ValueTask<Result> resultTask, Func<ValueTask> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapAsync(func).ConfigureAwait(false);
    }
}

#endregion

#region TapError (failure-side side effect)

/// <summary>TapError for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultTapErrorExtensions
{
    public static Result TapError(this Result result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(TapError));
        if (result.IsFailure) action(result.Error);
        result.LogActivityStatus();
        return result;
    }
}

/// <summary>Async TapError for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultTapErrorExtensionsAsync
{
    public static async Task<Result> TapErrorAsync(this Task<Result> resultTask, Action<Error> action)
        => (await resultTask.ConfigureAwait(false)).TapError(action);

    public static async Task<Result> TapErrorAsync(this Result result, Func<Error, Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) await func(result.Error).ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    public static async Task<Result> TapErrorAsync(this Task<Result> resultTask, Func<Error, Task> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapErrorAsync(func).ConfigureAwait(false);
    }

    public static async ValueTask<Result> TapErrorAsync(this ValueTask<Result> resultTask, Action<Error> action)
        => (await resultTask.ConfigureAwait(false)).TapError(action);

    public static async ValueTask<Result> TapErrorAsync(this Result result, Func<Error, ValueTask> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (result.IsFailure) await func(result.Error).ConfigureAwait(false);
        result.LogActivityStatus();
        return result;
    }

    public static async ValueTask<Result> TapErrorAsync(this ValueTask<Result> resultTask, Func<Error, ValueTask> func)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.TapErrorAsync(func).ConfigureAwait(false);
    }
}

#endregion

#region Ensure (predicate gate, instance-shape on Result)

/// <summary>Ensure for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultEnsureExtensions
{
    public static Result Ensure(this Result result, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        if (result.IsFailure) return result;
        return predicate() ? Result.Ok() : Result.Fail(error);
    }
}

/// <summary>Async Ensure for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultEnsureExtensionsAsync
{
    public static async Task<Result> EnsureAsync(this Task<Result> resultTask, Func<bool> predicate, Error error)
        => (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);

    public static async Task<Result> EnsureAsync(this Result result, Func<Task<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        if (result.IsFailure) return result;
        return await predicate().ConfigureAwait(false) ? Result.Ok() : Result.Fail(error);
    }

    public static async Task<Result> EnsureAsync(this Task<Result> resultTask, Func<Task<bool>> predicate, Error error)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.EnsureAsync(predicate, error).ConfigureAwait(false);
    }

    public static async ValueTask<Result> EnsureAsync(this ValueTask<Result> resultTask, Func<bool> predicate, Error error)
        => (await resultTask.ConfigureAwait(false)).Ensure(predicate, error);

    public static async ValueTask<Result> EnsureAsync(this Result result, Func<ValueTask<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        if (result.IsFailure) return result;
        return await predicate().ConfigureAwait(false) ? Result.Ok() : Result.Fail(error);
    }

    public static async ValueTask<Result> EnsureAsync(this ValueTask<Result> resultTask, Func<ValueTask<bool>> predicate, Error error)
    {
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
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsFailure ? onFailure(result.Error) : onSuccess();
    }

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
    public static async Task<TOut> MatchAsync<TOut>(this Task<Result> resultTask, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
        => (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);

    public static async Task<TOut> MatchAsync<TOut>(this Result result, Func<Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsFailure
            ? await onFailure(result.Error).ConfigureAwait(false)
            : await onSuccess().ConfigureAwait(false);
    }

    public static async Task<TOut> MatchAsync<TOut>(this Task<Result> resultTask, Func<Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.MatchAsync(onSuccess, onFailure).ConfigureAwait(false);
    }

    public static async ValueTask<TOut> MatchAsync<TOut>(this ValueTask<Result> resultTask, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
        => (await resultTask.ConfigureAwait(false)).Match(onSuccess, onFailure);

    public static async ValueTask<TOut> MatchAsync<TOut>(this Result result, Func<ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsFailure
            ? await onFailure(result.Error).ConfigureAwait(false)
            : await onSuccess().ConfigureAwait(false);
    }

    public static async ValueTask<TOut> MatchAsync<TOut>(this ValueTask<Result> resultTask, Func<ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
    {
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
    public static Result Recover(this Result result, Func<Error, Result> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Recover));
        if (result.IsSuccess) return result;
        var output = recovery(result.Error);
        output.LogActivityStatus();
        return output;
    }
}

/// <summary>Async Recover for the non-generic <see cref="Result"/>.</summary>
[DebuggerStepThrough]
public static class ResultRecoverExtensionsAsync
{
    public static async Task<Result> RecoverAsync(this Task<Result> resultTask, Func<Error, Result> recovery)
        => (await resultTask.ConfigureAwait(false)).Recover(recovery);

    public static async Task<Result> RecoverAsync(this Result result, Func<Error, Task<Result>> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        if (result.IsSuccess) return result;
        var output = await recovery(result.Error).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async Task<Result> RecoverAsync(this Task<Result> resultTask, Func<Error, Task<Result>> recovery)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.RecoverAsync(recovery).ConfigureAwait(false);
    }

    public static async ValueTask<Result> RecoverAsync(this ValueTask<Result> resultTask, Func<Error, Result> recovery)
        => (await resultTask.ConfigureAwait(false)).Recover(recovery);

    public static async ValueTask<Result> RecoverAsync(this Result result, Func<Error, ValueTask<Result>> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        if (result.IsSuccess) return result;
        var output = await recovery(result.Error).ConfigureAwait(false);
        output.LogActivityStatus();
        return output;
    }

    public static async ValueTask<Result> RecoverAsync(this ValueTask<Result> resultTask, Func<Error, ValueTask<Result>> recovery)
    {
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
    public static async Task<Result> AsUnitAsync<T>(this Task<Result<T>> resultTask)
        => (await resultTask.ConfigureAwait(false)).AsUnit();

    public static async ValueTask<Result> AsUnitAsync<T>(this ValueTask<Result<T>> resultTask)
        => (await resultTask.ConfigureAwait(false)).AsUnit();
}

#endregion
