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
/// <see cref="Trellis.Error.Unexpected"/> with <c>ReasonCode = "default-initialized"</c> — uninitialized state
/// is a typed failure, never a silent success. Always construct via <see cref="Ok()"/>; analyzer <c>TRLS019</c>
/// flags explicit <c>default(Result&lt;Unit&gt;)</c> at call sites.
/// </para>
/// </remarks>
public static partial class Result
{
    private const string UnexpectedExceptionDetail = "An unexpected error occurred while processing the request.";

    // ----- Generic factories -----------------------------------------------------------------

    /// <summary>Creates a successful result wrapping the provided <paramref name="value"/>.</summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <param name="value">The value to wrap in the successful result.</param>
    /// <returns>A successful <see cref="Result{TValue}"/> containing <paramref name="value"/>.</returns>
    public static Result<TValue> Ok<TValue>(TValue value) =>
        new(false, value, default);

    /// <summary>Creates a failed result with the specified <paramref name="error"/>.</summary>
    /// <typeparam name="TValue">The success value type for the failed result envelope.</typeparam>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{TValue}"/> containing <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
    public static Result<TValue> Fail<TValue>(Error error) =>
        new(true, default, error);

    /// <summary>
    /// Creates a failed result that <em>opts in</em> to post-handler persistence: pipeline
    /// behaviors implementing the <see cref="IPersistOnFailure"/> contract (notably
    /// <c>TransactionalCommandBehavior</c>) will still commit staged changes alongside this
    /// failure, so the failure state is durably recorded.
    /// </summary>
    /// <typeparam name="TValue">Success value type for the result envelope.</typeparam>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>
    /// A failed <see cref="Result{TValue}"/> whose <see cref="IPersistOnFailure.PersistOnFailure"/>
    /// flag is <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Worker-handler pattern.</strong> The canonical use case is a background-worker
    /// handler that converts a transient external-service rejection into a persisted
    /// <c>permanently_failed</c> state on the corresponding aggregate. The handler updates the
    /// aggregate via the change tracker, then returns <c>Result.FailAfterCommit&lt;TResponse&gt;(error)</c>.
    /// <c>TransactionalCommandBehavior</c> observes the per-instance flag and commits the staged
    /// row even though the result is a failure. The caller still receives the original
    /// <see cref="Error"/> — the result is <em>still a failure</em>.
    /// </para>
    /// <para>
    /// <strong>Commit-failure semantics.</strong> If the commit itself fails, the commit error
    /// is returned (overwriting the original handler error). This is intentional — the caller
    /// must learn that the persist-on-failure guarantee was not honored.
    /// </para>
    /// <para>
    /// <strong>Domain-event dispatch.</strong>
    /// <c>DomainEventDispatchBehavior</c> does <em>not</em> fan out events on a failed result,
    /// including persist-on-failure ones. Any events the handler raised on aggregates remain on
    /// those in-memory aggregate instances and are discarded with them when the request scope ends —
    /// they are <em>not</em> a durable retry buffer. If your scenario requires post-failure side
    /// effects (notifications, downstream commands), model them explicitly via an outbox row or a
    /// dedicated follow-up command instead of relying on event re-dispatch.
    /// </para>
    /// <para>
    /// <strong>Operator propagation.</strong> The persist-on-failure flag is preserved by every
    /// railway operator that projects a failure (<c>Map</c>, <c>Bind</c>, <c>MapOnFailure</c>,
    /// <c>Check</c>, <c>BindZip</c>, <c>Traverse</c>, <c>Ensure</c> upstream-propagation). Aggregating
    /// operators (<c>Combine</c>, <c>TraverseAll</c>, <c>SequenceAll</c>, <c>WhenAllAsync</c>)
    /// OR-accumulate the flag: if any failing source opted in, the aggregated failure carries the
    /// flag. The flag is sticky once introduced — a later validation failure combined with an
    /// upstream <c>FailAfterCommit</c> cannot suppress the persist intent. Fresh failures created
    /// without an upstream source (e.g., <c>Ensure</c> evaluating <c>false</c> on a previously
    /// successful value, <c>EnsureNotNull</c>'s value-was-null branch) do <em>not</em> set the flag.
    /// </para>
    /// <para>
    /// <strong>Do not compose <c>FailAfterCommit</c> with aggregating operators.</strong>
    /// <c>FailAfterCommit</c> is a <em>leaf</em> worker-handler operation: it converts a single
    /// aggregate's transient external rejection into a persisted <c>permanently_failed</c> state
    /// and returns. Threading that result through <c>Combine</c>, <c>TraverseAll</c>,
    /// <c>SequenceAll</c>, or <c>WhenAllAsync</c> — composing it with the outcomes of other,
    /// independent operations — is outside the supported pattern. The OR-accumulated flag will
    /// then commit the staged permanent-failure mutation alongside whatever the other legs
    /// produced, which is unlikely to match the handler author's intent. Restructure such
    /// handlers so the aggregating step runs to its terminal outcome first and
    /// <c>FailAfterCommit</c> is invoked at the end (or in a follow-up command), never as a
    /// leg inside a multi-aggregate composition.
    /// </para>
    /// </remarks>
    public static Result<TValue> FailAfterCommit<TValue>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(isFailure: true, ok: default, error: error, persistOnFailure: true);
    }

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
    /// <returns>A successful <see cref="Result{Unit}"/> containing <see cref="Unit.Default"/>.</returns>
    public static Result<Unit> Ok() => new(false, Unit.Default, default);

    /// <summary>
    /// Creates a failed no-payload result (<c>Result&lt;Unit&gt;</c>) with the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="Result{Unit}"/> containing <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
    public static Result<Unit> Fail(Error error) => new(true, default, error);

    /// <summary>
    /// Creates a failed no-payload result that opts in to post-handler persistence.
    /// See <see cref="FailAfterCommit{TValue}(Error)"/> for the full semantics.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>
    /// A failed <see cref="Result{Unit}"/> whose <see cref="IPersistOnFailure.PersistOnFailure"/>
    /// flag is <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
    public static Result<Unit> FailAfterCommit(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<Unit>(isFailure: true, ok: default, error: error, persistOnFailure: true);
    }

    /// <summary>
    /// Internal failure projector. Selects between <see cref="Fail{TValue}(Error)"/> and
    /// <see cref="FailAfterCommit{TValue}(Error)"/> based on <paramref name="persistOnFailure"/>.
    /// Used by railway operators (<c>Bind</c>, <c>Map</c>, <c>Combine</c>, <c>Check</c>, <c>BindZip</c>,
    /// <c>Traverse</c>, <c>MapOnFailure</c>, etc.) so that an upstream <see cref="FailAfterCommit{TValue}(Error)"/>
    /// result keeps its persist-on-failure intent when the operator changes the success value type or
    /// rewrites the error.
    /// </summary>
    /// <typeparam name="TValue">Success value type for the projected failure envelope.</typeparam>
    /// <param name="error">The error describing the failure. Must not be null.</param>
    /// <param name="persistOnFailure">When <see langword="true"/> the projected failure carries the
    /// <see cref="IPersistOnFailure.PersistOnFailure"/> flag; otherwise it is an ordinary failure.</param>
    /// <remarks>
    /// Aggregating operators (<c>Combine</c>, <c>TraverseAll</c>, <c>SequenceAll</c>) OR-accumulate
    /// the flag across every failing source: if any failing source opted into persist-on-failure, the
    /// aggregated result carries the flag. This is intentional and irreversible — a later validation
    /// or veto failure combined with an upstream <c>FailAfterCommit</c> cannot un-stage the persist
    /// intent. Model "abort persistence" as an explicit reset (e.g., wrap and rethrow as a plain
    /// <c>Result.Fail</c>) rather than relying on aggregation to suppress it.
    /// </remarks>
    internal static Result<TValue> ProjectFailure<TValue>(Error error, bool persistOnFailure)
    {
        Debug.Assert(error is not null, "ProjectFailure requires a non-null error.");
        return persistOnFailure
            ? new Result<TValue>(isFailure: true, ok: default, error: error, persistOnFailure: true)
            : new Result<TValue>(isFailure: true, ok: default, error: error);
    }

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
    ///         new Error.Forbidden("order.not-owned"));
    /// </code>
    /// </example>
    /// <example>
    /// Domain invariant guard inside an aggregate method (replaces a hand-written <c>if</c>/<c>return</c>):
    /// <code>
    /// public Result&lt;Unit&gt; Cancel(DateTimeOffset now) =&gt;
    ///     Result.Ensure(Status == OrderStatus.Pending,
    ///         new Error.Conflict(null, "order.not-cancellable") { Detail = "Only pending orders can be cancelled." })
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
    /// Executes the function and converts exceptions to a failed result using the optional mapper
    /// (default <see cref="Error.Unexpected"/> with a per-incident fault ID).
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
    /// Default mapper converting an exception into an <see cref="Error.Unexpected"/>.
    /// Public detail stays generic; exception diagnostics belong in call-site logging/telemetry
    /// indexed by <c>FaultId</c>.
    /// </summary>
    private static Error.Unexpected DefaultExceptionMapper(Exception _) =>
        new(FaultCodes.UnhandledException, Guid.NewGuid().ToString("N")) { Detail = UnexpectedExceptionDetail };
}