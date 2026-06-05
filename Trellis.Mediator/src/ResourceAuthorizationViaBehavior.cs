namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// Pipeline behavior that performs indirect (multi-hop) resource-based authorization.
/// The command identifies a leaf resource via the existing
/// <see cref="IResourceLoader{TMessage, TResource}"/> infrastructure and declares its final
/// authorization target via <see cref="IAuthorizeResourceVia{TOwner}"/>; this behavior walks
/// the pre-resolved <see cref="ResolvedAuthorizationPath"/> from leaf to owner and invokes
/// the command's <see cref="IAuthorizeResourceVia{TOwner}.Authorize"/> method against the
/// final list of owners.
/// </summary>
/// <typeparam name="TMessage">The message type, constrained to <see cref="IAuthorizeResourceVia{TOwner}"/>.</typeparam>
/// <typeparam name="TLeaf">The leaf resource type identified by the message.</typeparam>
/// <typeparam name="TOwner">The owner resource type at the end of the navigation chain.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.</typeparam>
/// <remarks>
/// <para>
/// Failure semantics:
/// <list type="bullet">
///   <item><description><b>Leaf load failure</b> — the loader's error bubbles verbatim (matches the existing <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/> semantics for the resource the command identifies).</description></item>
///   <item><description><b>Intermediate / owner load failure</b> — collapsed to <see cref="Error.Forbidden"/> to avoid leaking existence of related resources whose presence/absence the actor may not be authorized to learn.</description></item>
///   <item><description><b>Empty result at any hop</b> (singular extract returning 0 IDs or plural extract returning 0 IDs) — short-circuits to <see cref="Error.Forbidden"/> without calling <see cref="IAuthorizeResourceVia{TOwner}.Authorize"/>.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class ResourceAuthorizationViaBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TMessage,
    TLeaf,
    TOwner,
    TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorizeResourceVia<TOwner>, global::Mediator.IMessage
    where TLeaf : class
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly IActorProvider _actorProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ResolvedAuthorizationPath _path;
    private readonly ResourceAuthorizationOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance using a <see cref="ResolvedAuthorizationPathHolder{TMessage, TLeaf, TOwner, TResponse}"/>
    /// for DI-friendly typed registration. The holder is registered as a closed-generic singleton
    /// per via-authorized command; DI naturally disambiguates it per command, eliminating the
    /// need for factory-style descriptor registration.
    /// </summary>
    /// <param name="actorProvider">Provider used to resolve the current actor.</param>
    /// <param name="serviceProvider">The request-scoped service provider used to resolve the leaf loader and per-hop loaders.</param>
    /// <param name="pathHolder">The closed-generic carrier for the resolved path.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public ResourceAuthorizationViaBehavior(
        IActorProvider actorProvider,
        IServiceProvider serviceProvider,
        ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse> pathHolder)
        : this(actorProvider, serviceProvider, pathHolder, options: null, logger: null)
    {
    }

    /// <summary>
    /// Initializes a new instance using a <see cref="ResolvedAuthorizationPathHolder{TMessage, TLeaf, TOwner, TResponse}"/>
    /// for DI-friendly typed registration. Includes exposure-policy options and logger for
    /// the v2 hide-as-NotFound translation.
    /// </summary>
    /// <param name="actorProvider">Provider used to resolve the current actor.</param>
    /// <param name="serviceProvider">The request-scoped service provider used to resolve the leaf loader and per-hop loaders.</param>
    /// <param name="pathHolder">The closed-generic carrier for the resolved path.</param>
    /// <param name="options">Per-resource exposure-policy options resolved from DI. Null defaults to the always-propagate behavior.</param>
    /// <param name="logger">Logger used to emit the <c>ExistenceHidden</c> event when a Forbidden or AuthenticationRequired failure is translated to NotFound. Null defaults to <see cref="NullLogger.Instance"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required argument is null.</exception>
    public ResourceAuthorizationViaBehavior(
        IActorProvider actorProvider,
        IServiceProvider serviceProvider,
        ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse> pathHolder,
        IOptions<ResourceAuthorizationOptions>? options,
        ILogger<ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>>? logger = null)
        : this(
            actorProvider,
            serviceProvider,
            (pathHolder ?? throw new ArgumentNullException(nameof(pathHolder))).Path,
            options,
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/> class.
    /// </summary>
    /// <param name="actorProvider">Provider used to resolve the current actor.</param>
    /// <param name="serviceProvider">The request-scoped service provider used to resolve the leaf loader and per-hop loaders.</param>
    /// <param name="path">The pre-resolved authorization path from leaf to owner.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> does not agree with the behavior's generic
    /// arguments — specifically when <see cref="ResolvedAuthorizationPath.MessageType"/>
    /// is not <typeparamref name="TMessage"/>, <see cref="ResolvedAuthorizationPath.LeafType"/>
    /// is not <typeparamref name="TLeaf"/>, or <see cref="ResolvedAuthorizationPath.OwnerType"/>
    /// is not <typeparamref name="TOwner"/>. This guards against a single
    /// <see cref="ResolvedAuthorizationPath"/> being shared across multiple via-authorized
    /// commands via DI; the typed-registration path uses
    /// <see cref="ResolvedAuthorizationPathHolder{TMessage, TLeaf, TOwner, TResponse}"/> to
    /// prevent that misuse statically, and this constructor's defense applies if a consumer
    /// constructs the behavior manually.
    /// </exception>
    public ResourceAuthorizationViaBehavior(
        IActorProvider actorProvider,
        IServiceProvider serviceProvider,
        ResolvedAuthorizationPath path)
        : this(actorProvider, serviceProvider, path, options: null, logger: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/>
    /// class with exposure-policy options and logger for the v2 hide-as-NotFound translation.
    /// </summary>
    /// <param name="actorProvider">Provider used to resolve the current actor.</param>
    /// <param name="serviceProvider">The request-scoped service provider used to resolve the leaf loader and per-hop loaders.</param>
    /// <param name="path">The pre-resolved authorization path from leaf to owner.</param>
    /// <param name="options">Per-resource exposure-policy options resolved from DI.</param>
    /// <param name="logger">Logger used to emit the <c>ExistenceHidden</c> event when a translation occurs.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required argument is null.</exception>
    /// <exception cref="ArgumentException">Same shape as the parameterless overload — see remarks on path agreement.</exception>
    public ResourceAuthorizationViaBehavior(
        IActorProvider actorProvider,
        IServiceProvider serviceProvider,
        ResolvedAuthorizationPath path,
        IOptions<ResourceAuthorizationOptions>? options,
        ILogger<ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(path);

        if (path.MessageType != typeof(TMessage))
            throw new ArgumentException(
                $"Resolved authorization path is for {path.MessageType.Name} but behavior is closed over " +
                $"TMessage = {typeof(TMessage).Name}. Each via-authorized command must receive its own " +
                $"ResolvedAuthorizationPath; register the path via " +
                $"ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse> (the typed-registration " +
                $"pattern used by AddResourceAuthorization assembly scanning and AddRelatedResourceAuthorization) " +
                $"so DI naturally disambiguates per command.",
                nameof(path));

        if (path.LeafType != typeof(TLeaf))
            throw new ArgumentException(
                $"Resolved authorization path leaf type is {path.LeafType.Name} but behavior is closed over " +
                $"TLeaf = {typeof(TLeaf).Name}. The path and behavior generic arguments must agree on the leaf type.",
                nameof(path));

        if (path.OwnerType != typeof(TOwner))
            throw new ArgumentException(
                $"Resolved authorization path owner type is {path.OwnerType.Name} but behavior is closed over " +
                $"TOwner = {typeof(TOwner).Name}. The path and behavior generic arguments must agree on the owner type.",
                nameof(path));

        _actorProvider = actorProvider;
        _serviceProvider = serviceProvider;
        _path = path;
        _options = options?.Value ?? new ResourceAuthorizationOptions();
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        // "No authenticated actor" is client-error state per RFC 9110 §15.5.2; route to 401.
        // Genuine provider failures (missing HttpContext, mapping delegate threw, etc.) still
        // throw plain InvalidOperationException and surface as 500 via ExceptionBehavior.
        var actor = await ActorResolution.TryResolveAsync(_actorProvider, cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return TResponse.CreateFailure(MaybeTranslateExposure(ActorResolution.AuthenticationRequired(), message));

        var leafLoader = _serviceProvider.GetService<IResourceLoader<TMessage, TLeaf>>()
            ?? throw new InvalidOperationException(
                $"ResourceAuthorizationViaBehavior<{typeof(TMessage).Name}, {typeof(TLeaf).Name}, " +
                $"{typeof(TOwner).Name}, {typeof(TResponse).Name}> requires a registered " +
                $"IResourceLoader<{typeof(TMessage).Name}, {typeof(TLeaf).Name}>. " +
                $"Register one explicitly or implement IIdentifyResource<{typeof(TLeaf).Name}, ...> on the command " +
                $"and register the matching SharedResourceLoaderById and adapter.");

        var leafResult = await leafLoader.LoadAsync(message, cancellationToken).ConfigureAwait(false);
        if (!leafResult.TryGetValue(out var leaf, out var leafError))
            return TResponse.CreateFailure(MaybeTranslateExposure(leafError, message));

        // Defense-in-depth: a leaf loader that violates its Result<T> contract by returning
        // a successful Result carrying a null payload must NOT crash the pipeline with
        // NullReferenceException from a downstream ExtractIds cast — fail closed with
        // Forbidden so the documented "load failure collapses to a fail-closed result"
        // posture also covers this corner. Leaf-load *errors* (TryGetValue=false) bubble
        // verbatim per the documented zero-hop semantics; only the null-success corner is
        // collapsed here.
        if (leaf is null)
            return TResponse.CreateFailure(MaybeTranslateExposure(
                new Error.Forbidden("resource.authorization-via.null-payload")
                {
                    Detail = "The leaf resource loader returned a successful result with a null value.",
                },
                message));

        List<object> current = [leaf];
        for (var hopIndex = 0; hopIndex < _path.Hops.Count; hopIndex++)
        {
            var hop = _path.Hops[hopIndex];

            var idSet = new HashSet<object>();
            var idList = new List<object>();
            foreach (var src in current)
            {
                var ids = hop.ExtractIds(src);
                if (ids is null)
                    continue;
                foreach (var id in ids)
                {
                    if (id is null)
                        continue;
                    if (idSet.Add(id))
                        idList.Add(id);
                }
            }

            if (idList.Count == 0)
                return TResponse.CreateFailure(MaybeTranslateExposure(
                    new Error.Forbidden("resource.authorization-via.empty")
                    {
                        Detail = "No related resources were available at the authorization hop.",
                    },
                    message));

            var loaded = new List<object>(idList.Count);
            foreach (var id in idList)
            {
                var hopOutcome = await hop.LoadAsync(_serviceProvider, id, cancellationToken).ConfigureAwait(false);
                if (!hopOutcome.IsSuccess)
                {
                    return TResponse.CreateFailure(MaybeTranslateExposure(
                        new Error.Forbidden("resource.authorization-via.load-failed")
                        {
                            Detail = "A related resource could not be loaded during authorization.",
                        },
                        message));
                }

                loaded.Add(hopOutcome.Value!);
            }

            current = loaded;
        }

        var owners = new List<TOwner>(current.Count);
        foreach (var o in current)
            owners.Add((TOwner)o);

        var authResult = message.Authorize(actor, owners);
        if (authResult.TryGetError(out var authError))
            return TResponse.CreateFailure(MaybeTranslateExposure(authError, message));

        // Publish the LEAF resource via the per-async-flow accessor so handlers injecting
        // IAuthorizedResource<TMessage, TLeaf> can read the same instance the leaf loader
        // returned. Via authorization runs against the OWNER list, but the handler's
        // mutation target is almost always the leaf (e.g., UploadScorecardCommand
        // identifies a Match and authorizes via Team; the handler mutates the Match).
        // The owner accessor is intentionally NOT exposed; handlers needing owner state
        // reload via their repository. Backed by a linked-frame design with a volatile
        // IsActive flag: dispose flips IsActive (visible to orphan tasks that captured the
        // frame at fork time but outlived the parent dispatch) and restores the parent frame.
        using var _ = AuthorizedResourceHolder<TMessage, TLeaf>.Push(leaf);

        return await next(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates <c>Error.Forbidden</c> / <c>Error.AuthenticationRequired</c> to
    /// <c>Error.NotFound(ResourceRef)</c> when the LEAF resource is opted into
    /// <see cref="AuthFailureExposurePolicy.HideAsNotFound"/>. Lookup key is
    /// <typeparamref name="TLeaf"/> (the resource the command identifies), not
    /// <typeparamref name="TOwner"/> (an authorization implementation detail).
    /// </summary>
    private Error MaybeTranslateExposure(Error original, TMessage message)
    {
        if (original is not (Error.Forbidden or Error.AuthenticationRequired))
            return original;

        var entry = _options.Resolve(typeof(TLeaf));
        if (entry.Policy != AuthFailureExposurePolicy.HideAsNotFound)
            return original;

        var resourceId = ResourceIdExtractor.Extract(message, entry.IdResourceType)
            ?? ResourceIdExtractor.Extract(message, typeof(TLeaf));

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
        Message = "Resource-authorization (via) failure hidden as NotFound for {MessageName}: original Kind={OriginalKind} Code={OriginalCode} → public resource {PublicResourceType}")]
    private static partial void LogExistenceHidden(
        ILogger logger,
        string messageName,
        string originalKind,
        string originalCode,
        string publicResourceType);

    /// <summary>
    /// Reflection-based <c>IIdentifyResource&lt;TIdResource, TId&gt;.GetResourceId()</c>
    /// extractor, cached per <c>(TMessage, TIdResource)</c> closed pair. Same shape as the
    /// direct-path behavior's extractor; via commands always implement
    /// <c>IIdentifyResource&lt;TLeaf, TLeafId&gt;</c> per the registration invariant.
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
