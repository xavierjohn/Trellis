namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using Trellis.Authorization;

/// <summary>
/// Internal implementation of <see cref="IAuthorizedResource{TMessage, TResource}"/>.
/// Registered as a scoped DI service per closed <c>(TMessage, TResource)</c> pair; the
/// instance is just a façade — all state lives in the per-closed-pair static
/// <see cref="AsyncLocal{T}"/> so the "current resource" travels with the async flow
/// rather than the DI-scoped instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency model — linked frame list with an <c>IsActive</c> flag.</b>
/// <see cref="AsyncLocal{T}"/> flows the stored <i>reference</i> into child
/// <see cref="System.Threading.ExecutionContext"/>s when an async fork happens
/// (<c>Task.Run</c>, <c>Task.WhenAll</c>, etc.). Each <see cref="Push"/> creates a new
/// <c>Frame</c> whose <c>Previous</c> is the snapshot of the caller's current head and
/// assigns the new frame to the AsyncLocal slot. Dispose flips the frame's
/// <c>IsActive</c> flag to <c>false</c> AND restores the caller's AsyncLocal slot to
/// <c>Previous</c>.
/// </para>
/// <para>
/// <b>Why the <c>IsActive</c> flag matters.</b> The dispose only restores
/// <c>s_frame.Value</c> in the disposing async flow. Any orphan child task that
/// captured the frame reference at fork time (and outlives the parent dispatch) still
/// holds the frame and would otherwise continue to read the resource via
/// <see cref="GetRequiredResource"/> / <see cref="TryGetResource"/> — violating the documented
/// "populated only during an active dispatch" contract. The <c>IsActive</c> flip is
/// visible to those orphans on subsequent reads, so they correctly report
/// "no resource in scope." (Caught by GPT-5.5 code review on PR #578.)
/// </para>
/// <para>
/// <b>Sibling-fork correctness.</b> Each <see cref="Push"/> allocates a fresh frame, so
/// concurrent sibling <c>Task.WhenAll</c> branches that each call <see cref="Push"/>
/// end up with independent frames. There is no mutation of shared state between
/// siblings, so the cross-contamination scenario from the GPT-5.5 round-2 finding
/// (mutating a shared <see cref="Stack{T}"/>) cannot occur.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The closed-pair message type.</typeparam>
/// <typeparam name="TResource">The closed-pair resource type.</typeparam>
internal sealed class AuthorizedResourceHolder<TMessage, TResource>
    : IAuthorizedResource<TMessage, TResource>
    where TResource : class
{
    private static readonly AsyncLocal<Frame?> s_frame = new();

    /// <inheritdoc />
    public TResource GetRequiredResource()
    {
        var frame = s_frame.Value;
        if (frame is null || !frame.IsActive)
            throw new InvalidOperationException(
                $"No authorized {typeof(TResource).Name} is in scope for {typeof(TMessage).Name}. " +
                $"IAuthorizedResource<{typeof(TMessage).Name}, {typeof(TResource).Name}> was injected " +
                "but the resource-authorization pipeline did not populate it for the current dispatch. " +
                "Verify: (1) resource authorization is registered for this command via AddResourceAuthorization, " +
                "(2) the actor is authenticated, " +
                "(3) the loader returned a successful Result, " +
                "(4) the message's Authorize check returned success, and " +
                "(5) the handler is invoked through the mediator pipeline (not constructed and called directly, " +
                "and not from an orphan task that outlived the parent dispatch).");
        return frame.Resource;
    }

    /// <inheritdoc />
    public bool TryGetResource([MaybeNullWhen(false)] out TResource resource)
    {
        var frame = s_frame.Value;
        if (frame is null || !frame.IsActive)
        {
            resource = null;
            return false;
        }

        resource = frame.Resource;
        return true;
    }

    /// <summary>
    /// Pushes <paramref name="resource"/> onto the per-async-flow linked frame list and
    /// returns a scope token. Dispose the token (typically via <c>using</c> on the
    /// pipeline-behavior path) to flip the frame's <c>IsActive</c> flag and restore the
    /// previous frame in the disposing async flow. Static because the underlying storage
    /// is a per-closed-pair static <see cref="AsyncLocal{T}"/> — no instance state is
    /// involved.
    /// </summary>
    /// <param name="resource">The resource to push. Must not be null.</param>
    /// <returns>A disposable scope token that ends this frame on dispose.</returns>
    internal static IDisposable Push(TResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var previous = s_frame.Value;
        var frame = new Frame(previous, resource);
        s_frame.Value = frame;
        return new ScopeToken(frame);
    }

    /// <summary>
    /// Test-only inspector. Counts <i>active</i> frames in the current async-flow's
    /// linked list; <c>0</c> when no push has occurred or all pushes have been disposed.
    /// </summary>
    internal static int CurrentDepth
    {
        get
        {
            var depth = 0;
            for (var f = s_frame.Value; f is { IsActive: true }; f = f.Previous)
                depth++;
            return depth;
        }
    }

    /// <summary>
    /// A single push frame in the per-async-flow linked list. Immutable except for
    /// <see cref="IsActive"/>, which flips from <c>true</c> to <c>false</c> exactly
    /// once at dispose. The <c>volatile</c> qualifier ensures the false-write is
    /// visible to readers on other cores (orphan child tasks captured this frame and
    /// may read on a different core after dispose has flipped it).
    /// </summary>
    private sealed class Frame
    {
        public Frame(Frame? previous, TResource resource)
        {
            Previous = previous;
            Resource = resource;
        }

        public Frame? Previous { get; }

        public TResource Resource { get; }

        public volatile bool IsActive = true;
    }

    /// <summary>
    /// Ends the frame's lifetime and restores the previous frame in the disposing
    /// async flow. Idempotent — second dispose is a no-op.
    /// </summary>
    private sealed class ScopeToken : IDisposable
    {
        private readonly Frame _frame;
        private bool _disposed;

        public ScopeToken(Frame frame) => _frame = frame;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _frame.IsActive = false;
            s_frame.Value = _frame.Previous;
        }
    }
}