namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Combines two or more <see cref="Result{TValue}"/> into one tuple containing all the Results.
/// </summary>
[DebuggerStepThrough]
public static partial class CombineExtensions
{
    /// <summary>
    /// Combine a <see cref="Result{TValue}"/> with a non-generic <see cref="Result"/> (no-payload), returning <see cref="Result{TValue}"/>.
    /// </summary>
    public static Result<T1> Combine<T1>(this Result<T1> t1, Result t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<T1>(error);
        t1.TryGetValue(out var value);
        return Result.Ok(value!);
    }

    /// <summary>
    /// Combine two non-generic <see cref="Result"/> values into one.
    /// </summary>
    public static Result Combine(this Result r1, Result r2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        if (r1.IsFailure) error = error.Combine(r1.Error);
        if (r2.IsFailure) error = error.Combine(r2.Error);
        if (error is not null) return Result.Fail(error);
        return Result.Ok();
    }

    /// <summary>
    /// Combine two <see cref="Result{TValue}"/> into one <see cref="Tuple"/> containing all the Results.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <param name="t1"></param>
    /// <param name="t2"></param>
    /// <returns>Tuple containing both the results.</returns>
    public static Result<(T1, T2)> Combine<T1, T2>(this Result<T1> t1, Result<T2> t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<(T1, T2)>(error);
        t1.TryGetValue(out var v1);
        t2.TryGetValue(out var v2);
        return Result.Ok<(T1, T2)>((v1!, v2!));
    }
}

/// <summary>
/// Combines two or more <see cref="Result{TValue}"/> into one tuple containing all the Results.
/// </summary>
[DebuggerStepThrough]
public static partial class CombineExtensionsAsync
{
    #region Task-based overloads

    /// <summary>
    /// Combine two results into a tuple. Left is async (Task), right is sync.
    /// </summary>
    public static async Task<Result<(T1, T2)>> CombineAsync<T1, T2>(this Task<Result<T1>> tt1, Result<T2> t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t1 = await tt1.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<(T1, T2)>(error);
        t1.TryGetValue(out var v1);
        t2.TryGetValue(out var v2);
        return Result.Ok((v1!, v2!));
    }

    /// <summary>
    /// Combine two results into a tuple. Left is sync, right is async (Task).
    /// </summary>
    public static async Task<Result<(T1, T2)>> CombineAsync<T1, T2>(this Result<T1> t1, Task<Result<T2>> tt2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t2 = await tt2.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<(T1, T2)>(error);
        t1.TryGetValue(out var v1);
        t2.TryGetValue(out var v2);
        return Result.Ok((v1!, v2!));
    }

    /// <summary>
    /// Combine two results into a tuple. Both sides are async (Task).
    /// </summary>
    public static async Task<Result<(T1, T2)>> CombineAsync<T1, T2>(this Task<Result<T1>> tt1, Task<Result<T2>> tt2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t1 = await tt1.ConfigureAwait(false);
        var t2 = await tt2.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<(T1, T2)>(error);
        t1.TryGetValue(out var v1);
        t2.TryGetValue(out var v2);
        return Result.Ok((v1!, v2!));
    }

    /// <summary>
    /// Combine a Task result with a non-generic <see cref="Result"/>.
    /// </summary>
    public static async Task<Result<T1>> CombineAsync<T1>(this Task<Result<T1>> tt1, Result t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t1 = await tt1.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<T1>(error);
        t1.TryGetValue(out var value);
        return Result.Ok(value!);
    }

    #endregion

    #region ValueTask-based overloads

    /// <summary>
    /// Combine two results into a tuple. Left is async (ValueTask), right is sync.
    /// </summary>
    public static async ValueTask<Result<(T1, T2)>> CombineAsync<T1, T2>(this ValueTask<Result<T1>> vt1, Result<T2> t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t1 = await vt1.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<(T1, T2)>(error);
        t1.TryGetValue(out var v1);
        t2.TryGetValue(out var v2);
        return Result.Ok((v1!, v2!));
    }

    /// <summary>
    /// Combine two results into a tuple. Left is sync, right is async (ValueTask).
    /// </summary>
    public static async ValueTask<Result<(T1, T2)>> CombineAsync<T1, T2>(this Result<T1> t1, ValueTask<Result<T2>> vt2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t2 = await vt2.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<(T1, T2)>(error);
        t1.TryGetValue(out var v1);
        t2.TryGetValue(out var v2);
        return Result.Ok((v1!, v2!));
    }

    /// <summary>
    /// Combine two results into a tuple. Both sides are async (ValueTask).
    /// </summary>
    public static async ValueTask<Result<(T1, T2)>> CombineAsync<T1, T2>(this ValueTask<Result<T1>> vt1, ValueTask<Result<T2>> vt2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t1 = await vt1.ConfigureAwait(false);
        var t2 = await vt2.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<(T1, T2)>(error);
        t1.TryGetValue(out var v1);
        t2.TryGetValue(out var v2);
        return Result.Ok((v1!, v2!));
    }

    /// <summary>
    /// Combine a ValueTask result with a non-generic <see cref="Result"/>.
    /// </summary>
    public static async ValueTask<Result<T1>> CombineAsync<T1>(this ValueTask<Result<T1>> vt1, Result t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t1 = await vt1.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Fail<T1>(error);
        t1.TryGetValue(out var value);
        return Result.Ok(value!);
    }

    #endregion
}
