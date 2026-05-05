namespace Trellis.Mediator;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Pipeline behavior that loads a resource and performs resource-based authorization
/// before the handler runs. Registered as scoped so the injected <see cref="IServiceProvider"/>
/// is the request-scoped provider, allowing correct resolution of scoped dependencies.
/// </summary>
/// <typeparam name="TMessage">
/// The message type, constrained to <see cref="IAuthorizeResource{TResource}"/>.
/// </typeparam>
/// <typeparam name="TResource">The resource type loaded for authorization.</typeparam>
/// <typeparam name="TResponse">
/// The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// This behavior cannot be registered as an open generic because it has 3 type parameters
/// while <see cref="IPipelineBehavior{TMessage, TResponse}"/> has 2. Register per-command via
/// <see cref="ServiceCollectionExtensions.AddResourceAuthorization{TMessage, TResource, TResponse}"/>.
/// </para>
/// <para>
/// The behavior is registered as scoped (not singleton) because it resolves
/// <see cref="IResourceLoader{TMessage, TResource}"/> from the injected <see cref="IServiceProvider"/>.
/// A singleton would receive the root provider, causing <c>InvalidOperationException</c>
/// when ASP.NET Core's scope validation is enabled (default in Development).
/// </para>
/// <para>
/// Pipeline execution order for a command implementing both <see cref="IAuthorize"/> and
/// <see cref="IAuthorizeResource{TResource}"/>:
/// <list type="number">
///   <item><description>AuthorizationBehavior — checks static permissions</description></item>
///   <item><description>ResourceAuthorizationBehavior — loads resource, checks ownership</description></item>
///   <item><description>ValidationBehavior — validates command properties</description></item>
///   <item><description>Handler — pure business logic</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ResourceAuthorizationBehavior<TMessage, TResource, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly IActorProvider _actorProvider;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/> class.
    /// </summary>
    /// <param name="actorProvider">The provider used to resolve the current actor.</param>
    /// <param name="serviceProvider">The request-scoped service provider used to resolve the per-message resource loader.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actorProvider"/> or <paramref name="serviceProvider"/> is null.</exception>
    public ResourceAuthorizationBehavior(IActorProvider actorProvider, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _actorProvider = actorProvider;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        // Resolve the scoped loader per-request (like middleware resolving scoped services)
        var loader = _serviceProvider.GetService<IResourceLoader<TMessage, TResource>>()
            ?? throw new InvalidOperationException(
                $"ResourceAuthorizationBehavior<{typeof(TMessage).Name}, {typeof(TResource).Name}, {typeof(TResponse).Name}> " +
                $"requires a registered {typeof(IResourceLoader<TMessage, TResource>).Name}. " +
                $"Register IResourceLoader<{typeof(TMessage).Name}, {typeof(TResource).Name}> in the current DI scope.");

        // 1. Check the caller is authenticated BEFORE doing any I/O. Avoids spending a database
        //    round-trip on the resource loader when the request would be rejected anyway, and
        //    closes a small enumeration / timing-side-channel surface (an attacker could
        //    otherwise probe resource existence without credentials). The IActorProvider
        //    contract requires implementations to throw when no authenticated actor exists;
        //    the explicit null-check is defense-in-depth so a misbehaving provider that returns
        //    null cannot silently bypass the actor-first ordering (ga-11).
        var actor = await _actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new InvalidOperationException(
                "IActorProvider.GetCurrentActorAsync returned null. The contract requires "
                + "implementations to throw when no authenticated actor exists; returning null is a "
                + "violation of the IActorProvider contract.");

        // 2. Load the resource. The combined TryGetValue(out value, out error) overload removes
        //    the dead defensive throw the two-call (TryGetError + TryGetValue) shape required.
        var loadResult = await loader.LoadAsync(message, cancellationToken).ConfigureAwait(false);
        if (!loadResult.TryGetValue(out var resource, out var loadError))
            return TResponse.CreateFailure(loadError);

        // 3. Authorize against the loaded resource
        var authResult = message.Authorize(actor, resource);
        if (authResult.TryGetError(out var authError))
            return TResponse.CreateFailure(authError);

        // 4. Proceed to handler
        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}