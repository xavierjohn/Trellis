namespace Trellis;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Static factory and helper methods for constructing <see cref="Result{TValue}"/> values.
/// </summary>
/// <remarks>
/// <para>
/// No-payload outcomes are represented as <see cref="Result{Unit}"/>. Call <see cref="Ok()"/> /
/// <see cref="Fail(Error)"/> / <see cref="Ensure(bool, Error)"/> / <see cref="Try(Action, Func{Exception, Error}?)"/>
/// for no-payload outcomes; call <see cref="Ok{TValue}(TValue)"/> / <see cref="Fail{TValue}(Error)"/> /
/// <see cref="Try{T}(Func{T}, Func{Exception, Error}?)"/> for value-bearing outcomes.
/// </para>
/// <para>
/// <c>default(Result&lt;Unit&gt;)</c> represents a <em>failure</em> carrying a sentinel
/// <see cref="Trellis.Error.Unexpected"/> with <c>ReasonCode = "default_initialized"</c> — uninitialized state
/// is a typed failure, never a silent success. Always construct via <see cref="Ok()"/>; analyzer <c>TRLS019</c>
/// flags explicit <c>default(Result&lt;Unit&gt;)</c> at call sites.
/// </para>
/// </remarks>
public static partial class Result
{
    // ----- Generic factories -----------------------------------------------------------------

    /// <summary>Creates a successful result wrapping the provided <paramref name="value"/>.</summary>
    public static Result<TValue> Ok<TValue>(TValue value) =>
        new(false, value, default);

    /// <summary>Creates a failed result with the specified <paramref name="error"/>.</summary>
    public static Result<TValue> Fail<TValue>(Error error) =>
        new(true, default, error);

    // ----- No-payload factories (Result<Unit>) -----------------------------------------------

    /// <summary>
    /// Creates a successful no-payload result (<c>Result&lt;Unit&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Goes through the constructor so that <see cref="Activity.Current"/> receives the success status,
    /// matching the tracing behavior of <see cref="Ok{TValue}(TValue)"/>. Always prefer this factory over
    /// <c>default(Result&lt;Unit&gt;)</c>: <c>default</c> represents <em>failure</em> with the
    /// <see cref="Trellis.Error.Unexpected"/> sentinel, not success.
    /// </remarks>
    public static Result<Unit> Ok() => new(false, Unit.Default, default);

    /// <summary>
    /// Creates a failed no-payload result (<c>Result&lt;Unit&gt;</c>) with the specified <paramref name="error"/>.
    /// </summary>
    public static Result<Unit> Fail(Error error) => new(true, default, error);

    // ----- Static Combine ---------------------------------------------------------------------

    /// <summary>
    /// Combines two independent <see cref="Result{TValue}"/> instances into a single tuple result.
    /// </summary>
    /// <typeparam name="T1">Type of the first result value.</typeparam>
    /// <typeparam name="T2">Type of the second result value.</typeparam>
    /// <param name="r1">First result.</param>
    /// <param name="r2">Second result.</param>
    /// <returns>
    /// A success result with a 2-element tuple if both succeed; otherwise a failure with combined errors.
    /// </returns>
    /// <example>
    /// <code>
    /// var emailResult = EmailAddress.TryCreate(dto.Email);
    /// var nameResult = FirstName.TryCreate(dto.Name);
    ///
    /// var result = Result.Combine(emailResult, nameResult)
    ///     .Bind((email, name) => User.Create(email, name));
    /// </code>
    /// </example>
    public static Result<(T1, T2)> Combine<T1, T2>(
        Result<T1> r1, Result<T2> r2)
        => r1.Combine(r2);

    // ----- Ensure -----------------------------------------------------------------------------

    /// <summary>
    /// Returns a successful no-payload result if <paramref name="flag"/> is <see langword="true"/>;
    /// otherwise a failure with the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="flag">The condition to assert. <see langword="true"/> yields success; <see langword="false"/> yields failure.</param>
    /// <param name="error">The error to wrap when <paramref name="flag"/> is <see langword="false"/>. Always evaluated by the caller.</param>
    /// <returns><see cref="Ok()"/> when <paramref name="flag"/> is <see langword="true"/>; otherwise <see cref="Fail(Error)"/>.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Canonical guard primitive.</strong> Prefer <c>Result.Ensure(condition, error)</c> over
    /// hand-written <c>if (!condition) return Result.Fail(error);</c> blocks or ad-hoc ternaries — Ensure
    /// reads as a single declarative line, participates in <see cref="Activity"/> tracing, and is the
    /// preferred form for guard clauses in current guidance.
    /// </para>
    /// <para>
    /// For an asynchronous predicate use <see cref="EnsureAsync(Func{Task{bool}}, Error)"/>. For a value-threaded
    /// guard that preserves an existing <see cref="Result{TValue}"/> on success, use the
    /// <c>Result&lt;TValue&gt;.Ensure(predicate, error)</c> extension overloads on <see cref="EnsureExtensions"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// Authorization guard inside <c>IAuthorizeResource&lt;TResource&gt;.Authorize</c>:
    /// <code>
    /// public IResult Authorize(Actor actor, Order resource) =&gt;
    ///     Result.Ensure(
    ///         resource.OwnerId == actor.UserId,
    ///         new Error.Forbidden("order_not_owned"));
    /// </code>
    /// </example>
    /// <example>
    /// Domain invariant guard inside an aggregate method (replaces a hand-written <c>if</c>/<c>return</c>):
    /// <code>
    /// public Result&lt;Unit&gt; Cancel(DateTimeOffset now) =&gt;
    ///     Result.Ensure(Status == OrderStatus.Pending,
    ///         new Error.Conflict(null, "order_not_cancellable") { Detail = "Only pending orders can be cancelled." })
    ///     .Tap(() =&gt; { Status = OrderStatus.Cancelled; CancelledAt = now; });
    /// </code>
    /// </example>
    public static Result<Unit> Ensure(bool flag, Error error)
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var result = flag ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Returns a successful no-payload result if the predicate is true; otherwise a failure with the specified error.
    /// </summary>
    public static Result<Unit> Ensure(Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var result = predicate() ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Asynchronously evaluates the predicate and returns success if true; otherwise a failure with the specified error.
    /// </summary>
    public static async Task<Result<Unit>> EnsureAsync(Func<Task<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(Ensure));
        var isSuccess = await predicate().ConfigureAwait(false);
        var result = isSuccess ? Ok() : Fail(error);
        result.LogActivityStatus();
        return result;
    }

    // ----- Try / TryAsync ---------------------------------------------------------------------

    /// <summary>
    /// Executes the function and converts exceptions to a failed result using the optional mapper (default Unexpected).
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is null.</exception>
    public static Result<T> Try<T>(Func<T> func, Func<Exception, Error>? map = null)
    {
        ArgumentNullException.ThrowIfNull(func);
        try
        {
            return Ok(func());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail<T>((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Executes the asynchronous function and converts exceptions to a failed result.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is null.</exception>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error>? map = null)
    {
        ArgumentNullException.ThrowIfNull(func);
        try
        {
            return Ok(await func().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail<T>((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Executes the action and converts exceptions to a failed no-payload result using the optional mapper.
    /// </summary>
    public static Result<Unit> Try(Action work, Func<Exception, Error>? map = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            work();
            return Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Executes the asynchronous action and converts exceptions to a failed no-payload result.
    /// </summary>
    public static async Task<Result<Unit>> TryAsync(Func<Task> work, Func<Exception, Error>? map = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            await work().ConfigureAwait(false);
            return Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail((map ?? DefaultExceptionMapper)(ex));
        }
    }

    /// <summary>
    /// Default mapper converting an exception into an <see cref="Error.InternalServerError"/>.
    /// The exception message is attached as <c>Detail</c>; richer diagnostics belong in the
    /// logging/telemetry layer indexed by <c>FaultId</c>.
    /// </summary>
    private static Error.InternalServerError DefaultExceptionMapper(Exception ex) =>
        new(FaultId: Guid.NewGuid().ToString("N")) { Detail = ex.Message };
}
