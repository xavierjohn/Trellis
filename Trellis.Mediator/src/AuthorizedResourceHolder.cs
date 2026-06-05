namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using Trellis.Authorization;

/// <summary>
/// Internal implementation of <see cref="IAuthorizedResource{TMessage, TResource}"/>.
/// Registered as a scoped DI service per closed <c>(TMessage, TResource)</c> pair so
/// the same instance is resolved for both the pipeline behavior that pushes and the
/// handler that reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency model.</b> Uses a static <see cref="AsyncLocal{T}"/> stack so the
/// "current resource" travels with the async flow rather than the DI-scoped instance.
/// Static-AsyncLocal-per-closed-pair is the standard .NET pattern (mirrors
/// <c>HttpContextAccessor</c>, <see cref="System.Diagnostics.Activity.Current"/>, etc.)
/// and is exactly the right scope: different requests run in different async flows, so
/// the AsyncLocal value naturally isolates them.
/// </para>
/// <para>
/// <b>Why copy-on-push, not mutate-in-place.</b> <see cref="AsyncLocal{T}"/> flows the
/// stored value <i>reference</i> into child <see cref="System.Threading.ExecutionContext"/>s
/// when an async fork happens (e.g., <c>Task.WhenAll</c>). Mutating a shared
/// <see cref="Stack{T}"/> would cross-contaminate sibling forks AND race on the
/// non-thread-safe <see cref="Stack{T}"/> internals. <see cref="Push"/> instead snapshots
/// the previous stack into a freshly-allocated <see cref="Stack{T}"/>, pushes the new
/// resource, assigns the new reference, and on dispose restores the previous reference.
/// Each fork captures the previous reference at fork time and operates on its own copy.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The closed-pair message type.</typeparam>
/// <typeparam name="TResource">The closed-pair resource type.</typeparam>
internal sealed class AuthorizedResourceHolder<TMessage, TResource>
    : IAuthorizedResource<TMessage, TResource>
    where TResource : class
{
    private static readonly AsyncLocal<Stack<TResource>?> s_stack = new();

    /// <inheritdoc />
    public TResource GetRequired()
    {
        var stack = s_stack.Value;
        if (stack is null || stack.Count == 0)
            throw new InvalidOperationException(
                $"No authorized {typeof(TResource).Name} is in scope for {typeof(TMessage).Name}. " +
                $"IAuthorizedResource<{typeof(TMessage).Name}, {typeof(TResource).Name}> was injected " +
                "but the resource-authorization pipeline did not populate it for the current dispatch. " +
                "Verify: (1) resource authorization is registered for this command via AddResourceAuthorization, " +
                "(2) the actor is authenticated, " +
                "(3) the loader returned a successful Result, " +
                "(4) the message's Authorize check returned success, and " +
                "(5) the handler is invoked through the mediator pipeline (not constructed and called directly).");
        return stack.Peek();
    }

    /// <inheritdoc />
    public bool TryGet([MaybeNullWhen(false)] out TResource resource)
    {
        var stack = s_stack.Value;
        if (stack is null || stack.Count == 0)
        {
            resource = null;
            return false;
        }

        resource = stack.Peek();
        return true;
    }

    /// <summary>
    /// Pushes <paramref name="resource"/> onto the per-async-flow stack and returns a
    /// scope token. Dispose the token (typically via <c>using</c> on the pipeline-behavior
    /// path) to restore the previous stack reference. Static because the underlying
    /// storage is a per-closed-pair static <see cref="AsyncLocal{T}"/> — no instance
    /// state is involved; any holder instance for the same closed pair observes the same
    /// stack via <see cref="GetRequired"/> / <see cref="TryGet"/>.
    /// </summary>
    /// <param name="resource">The resource to push. Must not be null.</param>
    /// <returns>A disposable scope token that restores the previous stack on dispose.</returns>
    internal static IDisposable Push(TResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var previous = s_stack.Value;

        // Build a copy of the previous stack with the new resource on top. We must NOT
        // mutate the previous instance: AsyncLocal flows the *reference*, so concurrent
        // sibling forks share it and would race / cross-contaminate.
        //
        // Stack<T>(IEnumerable<T>) pushes items in iteration order. Stack<T>'s own
        // iteration yields top-to-bottom; reversing it yields bottom-to-top, which when
        // pushed onto a fresh stack reproduces the original top-to-bottom layout.
        var next = previous is null
            ? new Stack<TResource>(1)
            : new Stack<TResource>(previous.Reverse());
        next.Push(resource);
        s_stack.Value = next;

        return new ScopeToken(previous);
    }

    /// <summary>
    /// Test-only inspector. Returns the depth of the current async-flow stack; <c>0</c>
    /// when no push has occurred (or all pushes have been disposed) in the current flow.
    /// </summary>
    internal static int CurrentDepth => s_stack.Value?.Count ?? 0;

    /// <summary>
    /// Restores the previous AsyncLocal value when disposed. Idempotent — second dispose
    /// is a no-op rather than corrupting the AsyncLocal (defense against
    /// double-dispose patterns in test fixtures or error paths).
    /// </summary>
    private sealed class ScopeToken : IDisposable
    {
        private readonly Stack<TResource>? _previous;
        private bool _disposed;

        public ScopeToken(Stack<TResource>? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_stack.Value = _previous;
        }
    }
}
