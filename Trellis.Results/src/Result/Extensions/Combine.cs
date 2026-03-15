namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Combines two or more <see cref="Result{TValue}"/> into one tuple containing all the Results.
/// </summary>
[DebuggerStepThrough]
public static partial class CombineExtensions
{
    /// <summary>
    /// Combine a <see cref="Result{TValue}"/> with <see cref="Result{Unit}"/> return <see cref="Result{TValue}"/>.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <param name="t1"></param>
    /// <param name="t2"></param>
    /// <returns>Tuple containing both the results.</returns>
    public static Result<T1> Combine<T1>(this Result<T1> t1, Result<Unit> t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Failure<T1>(error);
        return Result.Success(t1.Value);
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
        if (error is not null) return Result.Failure<(T1, T2)>(error);
        return Result.Success<(T1, T2)>((t1.Value, t2.Value));
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
        if (error is not null) return Result.Failure<(T1, T2)>(error);
        return Result.Success((t1.Value, t2.Value));
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
        if (error is not null) return Result.Failure<(T1, T2)>(error);
        return Result.Success((t1.Value, t2.Value));
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
        if (error is not null) return Result.Failure<(T1, T2)>(error);
        return Result.Success((t1.Value, t2.Value));
    }

    /// <summary>
    /// Combine a Task result with a Unit result.
    /// </summary>
    public static async Task<Result<T1>> CombineAsync<T1>(this Task<Result<T1>> tt1, Result<Unit> t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(CombineExtensions.Combine));
        Error? error = null;
        var t1 = await tt1.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Failure<T1>(error);
        return Result.Success(t1.Value);
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
        if (error is not null) return Result.Failure<(T1, T2)>(error);
        return Result.Success((t1.Value, t2.Value));
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
        if (error is not null) return Result.Failure<(T1, T2)>(error);
        return Result.Success((t1.Value, t2.Value));
    }

    /// <summary>
    /// Combine two results into a tuple. Both sides are async (ValueTask).
    /// </summary>
    public static async ValueTask<Result<(T1, T2)>> CombineAsync<T1, T2>(this ValueTask<Result<T1>> vt1, ValueTask<Result<T2>> vt2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity();
        Error? error = null;
        var t1 = await vt1.ConfigureAwait(false);
        var t2 = await vt2.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Failure<(T1, T2)>(error);
        return Result.Success((t1.Value, t2.Value));
    }

    /// <summary>
    /// Combine a ValueTask result with a Unit result.
    /// </summary>
    public static async ValueTask<Result<T1>> CombineAsync<T1>(this ValueTask<Result<T1>> vt1, Result<Unit> t2)
    {
        using var activity = RopTrace.ActivitySource.StartActivity();
        Error? error = null;
        var t1 = await vt1.ConfigureAwait(false);
        if (t1.IsFailure) error = error.Combine(t1.Error);
        if (t2.IsFailure) error = error.Combine(t2.Error);
        if (error is not null) return Result.Failure<T1>(error);
        return Result.Success(t1.Value);
    }

    #endregion
}