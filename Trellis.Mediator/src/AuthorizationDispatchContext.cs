namespace Trellis.Mediator;

using Trellis.Authorization;

internal static class AuthorizationDispatchContext
{
    private static readonly AsyncLocal<Frame?> s_current = new();

    internal static object? CurrentIdentity => s_current.Value;

    internal static IDisposable Begin<TMessage, TResponse>(TMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var frame = new Frame(
            s_current.Value, typeof(TMessage), typeof(TResponse),
            message is IAuthorize, message is IResourceAuthorizationMessage);
        s_current.Value = frame;
        return new Scope(frame);
    }

    internal static ValueTask<Actor?> ResolveActorAsync<TMessage, TResponse>(
        IActorProvider actorProvider, CancellationToken cancellationToken)
        => GetRequiredFrame<TMessage, TResponse>().ResolveActorAsync(actorProvider, cancellationToken);

    internal static void MarkStaticAuthorized<TMessage, TResponse>()
        => GetRequiredFrame<TMessage, TResponse>().MarkStaticAuthorized();

    internal static void MarkResourceAuthorized<TMessage, TResource, TResponse>()
        => GetRequiredFrame<TMessage, TResponse>().MarkResourceAuthorized(typeof(TResource));

    internal static Actor GetRequiredActor<TMessage, TResponse>()
        => GetRequiredFrame<TMessage, TResponse>().GetAuthorizedActor();

    internal static TResource GetRequiredResource<TMessage, TResource, TResponse>() where TResource : class =>
        AuthorizedResourceHolder<TMessage, TResource>.GetRequiredResourceForDispatch(
            GetRequiredResourceDispatch<TMessage, TResource, TResponse>());

    internal static object GetRequiredResourceDispatch<TMessage, TResource, TResponse>()
    {
        var frame = GetRequiredFrame<TMessage, TResponse>();
        frame.GetAuthorizedActor();
        var resourceTypes = frame.ResourceTypes;
        if (!resourceTypes.Contains(typeof(TResource)))
            throw frame.Fault(
                $"The handler requires {typeof(TResource)}, but resource authorization supplied {string.Join(", ", resourceTypes)}. " +
                "Align the handler resource/leaf with AddResourceAuthorization or AddRelatedResourceAuthorization.");
        return frame;
    }

    private static Frame GetRequiredFrame<TMessage, TResponse>()
    {
        var frame = s_current.Value;
        if (frame is null)
            throw new InvalidOperationException(
                $"No authorization dispatch is in scope for {typeof(TMessage)} / {typeof(TResponse)}. " +
                "Register AuthorizationContextBehavior before authorization, normally through AddTrellisBehaviors " +
                "or the resource-authorization helpers. Invoke the handler through Mediator, " +
                "or use its explicit-argument Handle overload only for isolated business tests.");

        if (!frame.IsActive)
            throw frame.Fault("The authorization dispatch has expired. Do not acquire snapshots from orphan tasks.");

        if (frame.MessageType != typeof(TMessage) || frame.ResponseType != typeof(TResponse))
            throw new InvalidOperationException(
                $"Authorization for {typeof(TMessage)} / {typeof(TResponse)} has a mismatched dispatch: " +
                $"current message/response is {frame.MessageType} / {frame.ResponseType}. " +
                "Each Mediator dispatch needs its own AuthorizationContextBehavior before authorization; " +
                "do not invoke another handler's normal Handle entry directly.");

        return frame;
    }

    private sealed class Frame(
        Frame? previous, Type messageType, Type responseType, bool requiresStatic, bool requiresResource)
    {
        private readonly object _stateLock = new();
        private Task<Actor?>? _resolution;
        private Actor? _actor;
        private volatile bool _staticAuthorized;
        private volatile Type[] _resourceTypes = [];
        private volatile bool _isActive = true;

        internal Frame? Previous { get; } = previous;
        internal Type MessageType { get; } = messageType;
        internal Type ResponseType { get; } = responseType;
        internal bool IsActive => _isActive;
        internal IReadOnlyList<Type> ResourceTypes => _resourceTypes;

        internal ValueTask<Actor?> ResolveActorAsync(IActorProvider actorProvider, CancellationToken cancellationToken)
        {
            lock (_stateLock)
                return new ValueTask<Actor?>(_resolution ??= ResolveCoreAsync(actorProvider, cancellationToken));
        }

        private async Task<Actor?> ResolveCoreAsync(IActorProvider actorProvider, CancellationToken cancellationToken)
        {
            var maybeActor = await actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
            _actor = maybeActor.TryGetValue(out var actor) ? actor : null;
            return _actor;
        }

        internal void MarkStaticAuthorized()
        {
            if (!requiresStatic || _actor is null)
                throw Fault("Static authorization cannot succeed without its declared gate and authenticated actor.");
            _staticAuthorized = true;
        }

        internal void MarkResourceAuthorized(Type resourceType)
        {
            if (!requiresResource || _actor is null)
                throw Fault("Resource authorization cannot succeed without its declared gate and authenticated actor.");
            lock (_stateLock)
            {
                if (!_resourceTypes.Contains(resourceType))
                    _resourceTypes = [.. _resourceTypes, resourceType];
            }
        }

        internal Actor GetAuthorizedActor()
        {
            if (requiresStatic && !_staticAuthorized)
                throw Fault("Required static authorization has not succeeded. Register AuthorizationBehavior before the handler.");
            if (requiresResource && _resourceTypes.Length == 0)
                throw Fault(
                    "Required resource authorization has not succeeded. Register AddResourceAuthorization " +
                    "or AddRelatedResourceAuthorization before the handler.");
            if (_actor is null || (!requiresStatic && !requiresResource))
                throw Fault("Authentication and a declared authorization gate must succeed before acquiring the actor.");
            return _actor;
        }

        internal InvalidOperationException Fault(string detail) =>
            new($"Authorization dispatch for {MessageType} / {ResponseType}: {detail} " +
                "Use the normal Mediator pipeline; explicit-argument Handle is only a business-test seam.");

        internal void Deactivate() => _isActive = false;
    }

    private sealed class Scope(Frame frame) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            if (!ReferenceEquals(s_current.Value, frame))
                throw frame.Fault("Authorization dispatch scopes must be disposed in reverse order.");
            _disposed = true;
            frame.Deactivate();
            s_current.Value = frame.Previous;
        }
    }
}
