namespace Trellis.Authorization;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Scoped accessor for the resource the framework loaded and authorized for the
/// current pipeline dispatch. Inject this into a handler to obtain the SAME instance
/// the loader returned, eliminating a duplicate load when the handler would otherwise
/// re-fetch the resource by id from its repository.
/// </summary>
/// <typeparam name="TMessage">The command or query type this accessor is bound to.</typeparam>
/// <typeparam name="TResource">
/// The resource type. For commands implementing <see cref="IAuthorizeResource{TResource}"/>
/// this is the same <c>TResource</c>. For commands implementing
/// <see cref="IAuthorizeResourceVia{TOwner}"/> this is the LEAF resource (the thing the
/// message identifies via <see cref="IIdentifyResource{TResource, TId}"/>), not the via
/// owner — the leaf is the typical mutation target.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>When the accessor is populated.</b> Population happens inside
/// <c>ResourceAuthorizationBehavior</c> (for direct
/// <see cref="IAuthorizeResource{TResource}"/> commands) and inside
/// <c>ResourceAuthorizationViaBehavior</c> (for via commands — pushes the leaf), and
/// ONLY after the message's <c>Authorize</c> call returns success. Denied authorizations,
/// failed loads, and missing actors do NOT populate the accessor.
/// </para>
/// <para>
/// <b>Identity guarantee.</b> The accessor returns the SAME instance the loader
/// returned. The framework makes no claim about that instance's shape. If your loader
/// returns a projection, a no-tracking EF entity, a stale read-replica POCO, or an HTTP
/// DTO that cannot be mutated and persisted by the handler, do NOT inject this accessor
/// for that command — reload via your repository instead. The framework cannot enforce
/// mutation-readiness; that is the loader author's contract with the handler author.
/// </para>
/// <para>
/// <b>Concurrency.</b> Implementations are safe to call from nested
/// <c>mediator.Send</c> and from concurrent <c>Task.WhenAll</c>-style
/// dispatch of the same closed pair within one DI scope. Implementations use
/// per-async-flow snapshot semantics so each dispatch sees only its own resource.
/// </para>
/// </remarks>
public interface IAuthorizedResource<TMessage, TResource>
    where TResource : class
{
    /// <summary>
    /// Returns the resource loaded and authorized for the current pipeline dispatch.
    /// </summary>
    /// <returns>The loaded resource.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed outside a pipeline dispatch that populated the accessor —
    /// typically because the message lacks resource-authorization registration,
    /// authentication failed, loading failed, authorization was denied, or the handler
    /// is being invoked directly (e.g., from a unit test) without going through the
    /// mediator pipeline. The exception message names the closed pair so misconfiguration
    /// is easy to diagnose.
    /// </exception>
    TResource GetRequiredResource();

    /// <summary>
    /// Attempts to read the loaded resource. Returns <c>true</c> with the resource when
    /// a pipeline dispatch populated it; returns <c>false</c> when no populated dispatch
    /// is active. Provided for genuinely optional reads (e.g., diagnostic logging that
    /// runs both inside and outside the pipeline). Production handlers should prefer
    /// <see cref="GetRequiredResource"/> so a misconfigured pipeline fails loudly instead of
    /// silently skipping work.
    /// </summary>
    /// <param name="resource">The loaded resource on success; <c>null</c> otherwise.</param>
    /// <returns><c>true</c> when populated; <c>false</c> otherwise.</returns>
    bool TryGetResource([MaybeNullWhen(false)] out TResource resource);
}