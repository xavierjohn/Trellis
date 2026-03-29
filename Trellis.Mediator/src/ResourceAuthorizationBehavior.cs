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
public sealed class ResourceAuthorizationBehavior<TMessage, TResource, TResponse>(
    IActorProvider actorProvider,
    IServiceProvider serviceProvider)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        // Resolve the scoped loader per-request (like middleware resolving scoped services)
        var loader = serviceProvider.GetService<IResourceLoader<TMessage, TResource>>()
            ?? throw new InvalidOperationException(
                $"ResourceAuthorizationBehavior<{typeof(TMessage).Name}, {typeof(TResource).Name}, {typeof(TResponse).Name}> " +
                $"requires a registered {typeof(IResourceLoader<TMessage, TResource>).Name}. " +
                $"Register IResourceLoader<{typeof(TMessage).Name}, {typeof(TResource).Name}> in the current DI scope.");

        // 1. Load the resource
        var loadResult = await loader.LoadAsync(message, cancellationToken).ConfigureAwait(false);
        if (loadResult.IsFailure)
            return TResponse.CreateFailure(loadResult.Error);

        // 2. Authorize against the loaded resource
        var actor = await actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);

        if (actor is null)
            throw new InvalidOperationException("No authenticated actor available. Ensure an IActorProvider is configured and the user is authenticated.");

        var authResult = message.Authorize(actor, loadResult.Value);
        if (authResult.IsFailure)
            return TResponse.CreateFailure(authResult.Error);

        // 3. Proceed to handler
        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}