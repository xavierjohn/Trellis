namespace Trellis;

/// <summary>
/// Opt-in interface implemented by result types whose <em>failure</em> outcome should still
/// trigger any post-handler persistence step (e.g. <c>TransactionalCommandBehavior</c>'s
/// commit) so the failure state is durably recorded alongside the rest of the staged work.
/// </summary>
/// <remarks>
/// <para>
/// The canonical use case is a worker handler that converts a transient external-service
/// failure into a persisted "rejected" or "permanently_failed" row on the corresponding
/// aggregate. The handler stages the state-change-to-failed via the change tracker, then
/// returns <see cref="Result.FailAfterCommit{TValue}(Error)"/>. <c>TransactionalCommandBehavior</c>
/// observes <see cref="PersistOnFailure"/> = <see langword="true"/> and commits the staged
/// row even though the result is a failure. The caller still receives the original
/// <see cref="Error"/> — the result is <em>still a failure</em>.
/// </para>
/// <para>
/// <see cref="PersistOnFailure"/> is a <em>per-instance</em> property, not a type-level marker.
/// Implementations like <see cref="Result{TValue}"/> implement this interface unconditionally
/// (the same struct represents both ordinary and persist-on-failure outcomes) and return
/// the per-instance flag. A type-only <c>is IPersistOnFailure</c> check is therefore insufficient
/// and would incorrectly include ordinary <see cref="Result.Fail{TValue}(Error)"/> values;
/// consumers must use a property-bound pattern such as
/// <c>result is IPersistOnFailure { PersistOnFailure: true }</c>.
/// </para>
/// <para>
/// The flag is meaningless on success — successful results commit unconditionally — and
/// implementations should return <see langword="false"/> on success to keep the
/// <c>IsSuccess || PersistOnFailure</c> idiom unambiguous.
/// </para>
/// </remarks>
public interface IPersistOnFailure
{
    /// <summary>
    /// Gets a value indicating whether a failed result should still trigger the
    /// post-handler persistence step.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when this instance was created by a persist-on-failure
    /// factory (such as <see cref="Result.FailAfterCommit{TValue}(Error)"/>) or projected /
    /// aggregated from such an instance by a railway operator; otherwise <see langword="false"/>.
    /// </value>
    bool PersistOnFailure { get; }
}