namespace Trellis.Mediator;

using System.Threading;

/// <summary>
/// Non-generic holder for the re-entrancy flag consumed by
/// <see cref="TrackedAggregateDomainEventDispatchBehavior{TMessage, TResponse}"/>.
/// </summary>
/// <remarks>
/// The flag MUST be stored on a non-generic type because static fields on a generic class are
/// per-closed-generic. A re-entrant nested command typically resolves a different
/// <c>Behavior&lt;TMessage, TResponse&gt;</c> closed type (different command, different response),
/// and a per-closed-generic <see cref="AsyncLocal{T}"/> would default to <see langword="false"/>
/// in that nested closed generic — bypassing the guard. Routing every closed behavior through this
/// shared cell ensures the guard observes re-entrancy regardless of nested message shapes.
/// </remarks>
internal static class TrackedAggregateDispatchReentrancyGuard
{
    private static readonly AsyncLocal<bool> s_inDispatch = new();

    internal static bool IsInDispatch
    {
        get => s_inDispatch.Value;
        set => s_inDispatch.Value = value;
    }
}