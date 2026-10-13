namespace Trellis.Authorization;

/// <summary>
/// Resolves the current principal's canonical identity without loading permissions or
/// authorization attributes. Implement alongside <see cref="IActorProvider"/> when supported.
/// </summary>
/// <remarks>
/// A resolved identity does not establish actor eligibility or authorization. Whenever
/// full actor resolution succeeds, its <see cref="Actor.Id"/> must equal this identity.
/// Identity resolution may require I/O, but must not fall back to full actor resolution.
/// Register as scoped. Missing identity is ordinary absence; configuration and infrastructure
/// failures and cancellation propagate rather than becoming absence.
/// </remarks>
public interface IActorIdentityProvider
{
    /// <summary>
    /// Returns the canonical identity, or <see cref="Maybe{T}.None"/> when no usable
    /// authenticated identity is available, without resolving permissions or attributes.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel identity resolution.</param>
    /// <returns>The optional canonical identity for the current operation.</returns>
    ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default);
}