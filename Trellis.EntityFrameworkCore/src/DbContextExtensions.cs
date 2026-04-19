namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods on <see cref="DbContext"/> that wrap <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
/// and convert expected database exceptions to <see cref="Result{T}"/> failures.
/// </summary>
public static class DbContextExtensions
{
    /// <summary>
    /// Convenience overload: delegates to
    /// <see cref="SaveChangesResultAsync(DbContext, bool, CancellationToken)"/> with
    /// <c>acceptAllChangesOnSuccess</c> set to <see langword="true"/> (EF Core's default behavior).
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to save changes on.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{T}"/> containing the number of state entries written on success,
    /// or an error on failure.</returns>
    public static Task<Result<int>> SaveChangesResultAsync(
        this DbContext context,
        CancellationToken cancellationToken = default) =>
        context.SaveChangesResultAsync(true, cancellationToken);

    /// <summary>
    /// Calls <see cref="DbContext.SaveChangesAsync(bool, CancellationToken)"/> and converts expected database exceptions
    /// to <see cref="Result{T}"/> failures. The <paramref name="acceptAllChangesOnSuccess"/> parameter controls whether
    /// <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker.AcceptAllChanges"/>
    /// is called after saving successfully.
    /// <para>
    /// Expected exceptions converted:
    /// <list type="bullet">
    ///   <item><see cref="DbUpdateConcurrencyException"/> → <see cref="Error.Conflict(string, string?)"/> with detail</item>
    ///   <item><see cref="DbUpdateException"/> (duplicate key) → <see cref="Error.Conflict(string, string?)"/> with detail</item>
    ///   <item><see cref="DbUpdateException"/> (foreign key violation) → <see cref="Error.Domain(string, string?)"/> with detail</item>
    /// </list>
    /// </para>
    /// <para>
    /// Unexpected exceptions (connection failures, timeouts, etc.) are NOT caught.
    /// They propagate as exceptions for global exception handlers and retry policies.
    /// </para>
    /// <para>
    /// <see cref="OperationCanceledException"/> is NOT caught (re-throws).
    /// </para>
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to save changes on.</param>
    /// <param name="acceptAllChangesOnSuccess">
    /// <see langword="true"/> to accept all changes after saving (default EF Core behavior);
    /// <see langword="false"/> to leave the change tracker state unchanged.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{T}"/> containing the number of state entries written on success,
    /// or an error on failure.</returns>
    public static async Task<Result<int>> SaveChangesResultAsync(
        this DbContext context,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await context.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            return Result.Ok(count);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Error.Conflict(
                $"One or more entities were modified by another process. {ex.Entries.Count} entities affected.");
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsDuplicateKey(ex))
        {
            // Use a safe generic message for Error.Detail — it flows to API responses.
            // For logging, use DbExceptionClassifier.ExtractConstraintDetail(ex) which may contain schema details.
            _ = ex; // available for caller logging via ExceptionBehavior or repository catch blocks
            return Error.Conflict("A record with the same unique value already exists.");
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsForeignKeyViolation(ex))
        {
            // Use a safe generic message for Error.Detail — it flows to API responses.
            // For logging, use DbExceptionClassifier.ExtractConstraintDetail(ex) which may contain schema details.
            _ = ex;
            return Error.Domain("Operation violates a referential integrity constraint.");
        }
    }

    /// <summary>
    /// Convenience overload: calls <see cref="SaveChangesResultAsync(DbContext, CancellationToken)"/> and maps success to <see cref="Result{Unit}"/>.
    /// Use when callers don't need the affected row count.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to save changes on.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{Unit}"/> representing success or failure.</returns>
    public static async Task<Result<Unit>> SaveChangesResultUnitAsync(
        this DbContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await context.SaveChangesResultAsync(cancellationToken).ConfigureAwait(false);
        return result.Map(_ => default(Unit));
    }

    /// <summary>
    /// Convenience overload: calls <see cref="SaveChangesResultAsync(DbContext, bool, CancellationToken)"/>
    /// and maps success to <see cref="Result{Unit}"/>.
    /// Use when callers don't need the affected row count.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> to save changes on.</param>
    /// <param name="acceptAllChangesOnSuccess">
    /// <see langword="true"/> to accept all changes after saving (default EF Core behavior);
    /// <see langword="false"/> to leave the change tracker state unchanged.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{Unit}"/> representing success or failure.</returns>
    public static async Task<Result<Unit>> SaveChangesResultUnitAsync(
        this DbContext context,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var result = await context.SaveChangesResultAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        return result.Map(_ => default(Unit));
    }
}