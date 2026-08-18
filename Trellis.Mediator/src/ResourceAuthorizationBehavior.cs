namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
public sealed partial class ResourceAuthorizationBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TMessage,
    TResource,
    TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage
    where TResource : class
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly IActorProvider _actorProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ResourceAuthorizationOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/> class.
    /// </summary>
    /// <param name="actorProvider">The provider used to resolve the current actor.</param>
    /// <param name="serviceProvider">The request-scoped service provider used to resolve the per-message resource loader.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actorProvider"/> or <paramref name="serviceProvider"/> is null.</exception>
    public ResourceAuthorizationBehavior(IActorProvider actorProvider, IServiceProvider serviceProvider)
        : this(actorProvider, serviceProvider, options: null, logger: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/> class.
    /// </summary>
    /// <param name="actorProvider">The provider used to resolve the current actor.</param>
    /// <param name="serviceProvider">The request-scoped service provider used to resolve the per-message resource loader.</param>
    /// <param name="options">
    /// Per-resource exposure-policy options resolved from DI. Null defaults to the
    /// always-propagate behavior for back-compat with consumers that have not opted in.
    /// </param>
    /// <param name="logger">
    /// Logger used to emit the <c>ExistenceHidden</c> structured-log event when a Forbidden or
    /// AuthenticationRequired failure is translated to NotFound. Null defaults to
    /// <see cref="NullLogger.Instance"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actorProvider"/> or <paramref name="serviceProvider"/> is null.</exception>
    public ResourceAuthorizationBehavior(
        IActorProvider actorProvider,
        IServiceProvider serviceProvider,
        IOptions<ResourceAuthorizationOptions>? options,
        ILogger<ResourceAuthorizationBehavior<TMessage, TResource, TResponse>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _actorProvider = actorProvider;
        _serviceProvider = serviceProvider;
        _options = options?.Value ?? new ResourceAuthorizationOptions();
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        // 1. Check the caller is authenticated BEFORE doing any I/O — including resolving
        //    the resource loader from DI. The DI factory or constructor for a custom
        //    IResourceLoader<TMessage, TResource> is arbitrary user code (e.g. it may open a
        //    DbContext or pre-fetch state during construction), so loader *resolution* itself
        //    counts as I/O for the ga-11 guarantee. "No authenticated actor" is client-error
        //    state per RFC 9110 §15.5.2; route it to 401 via Error.AuthenticationRequired rather than
        //    letting it fall through to the resource-load path. Reported by GPT-5.5 review:
        //    the previous order (resolve loader → resolve actor) let an unauthenticated
        //    caller trigger loader-side effects via the DI factory before the actor check.
        var actor = await ActorResolution.TryResolveAsync(_actorProvider, cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return TResponse.CreateFailure(MaybeTranslateExposure(ActorResolution.AuthenticationRequired(), message));

        // 2. Resolve the scoped loader per-request (like middleware resolving scoped services).
        var loader = _serviceProvider.GetService<IResourceLoader<TMessage, TResource>>()
            ?? throw new InvalidOperationException(
                $"ResourceAuthorizationBehavior<{typeof(TMessage).Name}, {typeof(TResource).Name}, {typeof(TResponse).Name}> " +
                $"requires a registered {typeof(IResourceLoader<TMessage, TResource>).Name}. " +
                $"Register IResourceLoader<{typeof(TMessage).Name}, {typeof(TResource).Name}> in the current DI scope.");

        // 3. Load the resource. The combined TryGetValue(out value, out error) overload removes
        //    the dead defensive throw the two-call (TryGetError + TryGetValue) shape required.
        var loadResult = await loader.LoadAsync(message, cancellationToken).ConfigureAwait(false);
        if (!loadResult.TryGetValue(out var resource, out var loadError))
            return TResponse.CreateFailure(MaybeTranslateExposure(loadError, message));

        // Defense-in-depth: an IResourceLoader that violates its Result<T> contract by
        // returning Result.Ok carrying a null payload must NOT pass null through to
        // message.Authorize where a downstream member access would NRE and bubble as 500.
        // Mirrors the leaf-loader / hop-loader null-payload defense in the via-authorization
        // path so all resource-authorization entry points fail closed (Forbidden) when the
        // loaded resource is unexpectedly null.
        if (resource is null)
            return TResponse.CreateFailure(MaybeTranslateExposure(
                new Error.Forbidden("resource.authorization.null-payload")
                {
                    Detail = "The resource loader returned a successful result with a null value.",
                },
                message));

        // 4. Authorize against the loaded resource
        var authResult = message.Authorize(actor, resource);
        if (authResult.TryGetError(out var authError))
            return TResponse.CreateFailure(MaybeTranslateExposure(authError, message));

        // 5. Publish the authorized resource via the per-async-flow accessor so handlers
        //    injecting IAuthorizedResource<TMessage, TResource> can read the same instance
        //    the loader returned — eliminating a duplicate load for CosmosDB (doubled RU
        //    charge), Dapper (doubled roundtrip), and HTTP-backed loaders. Publication
        //    happens only AFTER a successful Authorize call so denied authorizations
        //    cannot expose the loaded resource to any out-of-band observer. The accessor
        //    is backed by a linked-frame design with a volatile IsActive flag: dispose
        //    flips IsActive (visible across cores to orphan tasks that captured the frame
        //    at fork time but outlived the parent dispatch) and restores the parent frame,
        //    so nested mediator.Send of the same closed pair sees the outer resource again.
        using var _ = AuthorizedResourceHolder<TMessage, TResource>.Push(resource);

        // 6. Proceed to handler
        return await next(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates <c>Error.Forbidden</c> / <c>Error.AuthenticationRequired</c> to
    /// <c>new Error.NotFound(ResourceRef)</c> when the resource is opted into
    /// <see cref="AuthFailureExposurePolicy.HideAsNotFound"/>; otherwise returns the original
    /// error unchanged. Other error kinds are never translated — operational signal must not
    /// be hidden behind a 404.
    /// </summary>
    private Error MaybeTranslateExposure(Error original, TMessage message)
    {
        if (original is not (Error.Forbidden or Error.AuthenticationRequired))
            return original;

        var entry = _options.Resolve(typeof(TResource));
        if (entry.Policy != AuthFailureExposurePolicy.HideAsNotFound)
            return original;

        var resourceId = ResourceIdExtractor.Extract(message, entry.IdResourceType)
            ?? ResourceIdExtractor.Extract(message, typeof(TResource));

        var publicTypeName = ResourceRef.FormatTypeName(entry.PublicResourceType);
        var resourceRef = resourceId is null
            ? ResourceRef.For(publicTypeName)
            : ResourceRef.For(publicTypeName, resourceId);

        LogExistenceHidden(_logger, typeof(TMessage).Name, original.Kind, original.Code, publicTypeName);

        return new Error.NotFound(resourceRef);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "ExistenceHidden",
        Level = LogLevel.Information,
        Message = "Resource-authorization failure hidden as NotFound for {MessageName}: original Kind={OriginalKind} Code={OriginalCode} → public resource {PublicResourceType}")]
    private static partial void LogExistenceHidden(
        ILogger logger,
        string messageName,
        string originalKind,
        string originalCode,
        string publicResourceType);

    /// <summary>
    /// Reflection-based <c>IIdentifyResource&lt;TIdResource, TId&gt;.GetResourceId()</c>
    /// extractor, cached per <c>(TMessage, TIdResource)</c> closed pair. Reflection is needed
    /// because the covariance on <c>IIdentifyResource&lt;TResource, out TId&gt;</c> only kicks
    /// in for reference-type <c>TId</c> — value-typed IDs (record struct, raw <c>Guid</c> /
    /// <c>int</c>) can't be reached through covariance to <c>object</c>.
    /// </summary>
    private static class ResourceIdExtractor
    {
        private static readonly Dictionary<Type, Func<TMessage, object?>?> s_cache = [];

        public static object? Extract(TMessage message, Type idResourceType)
        {
            Func<TMessage, object?>? extractor;
            lock (s_cache)
            {
                if (!s_cache.TryGetValue(idResourceType, out extractor))
                {
                    extractor = Build(idResourceType);
                    s_cache[idResourceType] = extractor;
                }
            }

            return extractor?.Invoke(message);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2070:'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method.",
            Justification = "TMessage is annotated [DynamicallyAccessedMembers(Interfaces)] on the closed-generic behavior, preserving interface metadata under trimming. Failure to find the interface returns a null extractor, so the missing-interface branch is non-fatal.")]
        [UnconditionalSuppressMessage("Trimming", "IL2075:'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method.",
            Justification = "The IIdentifyResource<,> interface metadata is preserved by the [DynamicallyAccessedMembers(Interfaces)] annotation on TMessage; the closed interface type retrieved by reflection therefore carries its single declared method.")]
        private static Func<TMessage, object?>? Build(Type idResourceType)
        {
            var identifyIface = typeof(TMessage).GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(IIdentifyResource<,>)
                    && i.GetGenericArguments()[0] == idResourceType);
            if (identifyIface is null)
                return null;

            var method = identifyIface.GetMethod(nameof(IIdentifyResource<object, object>.GetResourceId));
            return method is null ? null : msg => method.Invoke(msg, null);
        }
    }
}