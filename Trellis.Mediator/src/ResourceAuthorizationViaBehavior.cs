namespace Trellis.Mediator;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IAuthorizeResourceVia<TOwner>, global::Mediator.IMessage
    where TLeaf : class
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly IActorProvider _actorProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ResolvedAuthorizationPath _path;

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
        : this(
            actorProvider,
            serviceProvider,
            (pathHolder ?? throw new ArgumentNullException(nameof(pathHolder))).Path)
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
            return TResponse.CreateFailure(ActorResolution.AuthenticationRequired());

        var leafLoader = _serviceProvider.GetService<IResourceLoader<TMessage, TLeaf>>()
            ?? throw new InvalidOperationException(
                $"ResourceAuthorizationViaBehavior<{typeof(TMessage).Name}, {typeof(TLeaf).Name}, " +
                $"{typeof(TOwner).Name}, {typeof(TResponse).Name}> requires a registered " +
                $"IResourceLoader<{typeof(TMessage).Name}, {typeof(TLeaf).Name}>. " +
                $"Register one explicitly or implement IIdentifyResource<{typeof(TLeaf).Name}, ...> on the command " +
                $"and register the matching SharedResourceLoaderById and adapter.");

        var leafResult = await leafLoader.LoadAsync(message, cancellationToken).ConfigureAwait(false);
        if (!leafResult.TryGetValue(out var leaf, out var leafError))
            return TResponse.CreateFailure(leafError);

        // Defense-in-depth: a leaf loader that violates its Result<T> contract by returning
        // a successful Result carrying a null payload must NOT crash the pipeline with
        // NullReferenceException from a downstream ExtractIds cast — fail closed with
        // Forbidden so the documented "load failure collapses to a fail-closed result"
        // posture also covers this corner. Leaf-load *errors* (TryGetValue=false) bubble
        // verbatim per the documented zero-hop semantics; only the null-success corner is
        // collapsed here.
        if (leaf is null)
            return TResponse.CreateFailure(new Error.Forbidden("resource.authorization-via.null-payload")
            {
                Detail = "The leaf resource loader returned a successful result with a null value.",
            });

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
                return TResponse.CreateFailure(new Error.Forbidden("resource.authorization-via.empty")
                {
                    Detail = "No related resources were available at the authorization hop.",
                });

            var loaded = new List<object>(idList.Count);
            foreach (var id in idList)
            {
                var hopOutcome = await hop.LoadAsync(_serviceProvider, id, cancellationToken).ConfigureAwait(false);
                if (!hopOutcome.IsSuccess)
                {
                    return TResponse.CreateFailure(new Error.Forbidden("resource.authorization-via.load-failed")
                    {
                        Detail = "A related resource could not be loaded during authorization.",
                    });
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
            return TResponse.CreateFailure(authError);

        // Push the LEAF resource onto the per-async-flow stack so handlers injecting
        // IAuthorizedResource<TMessage, TLeaf> can read the same instance the leaf loader
        // returned. Via authorization runs against the OWNER list, but the handler's
        // mutation target is almost always the leaf (e.g., UploadScorecardCommand
        // identifies a Match and authorizes via Team; the handler mutates the Match).
        // The owner accessor is intentionally NOT exposed; handlers needing owner
        // state reload via their repository.
        using var _ = AuthorizedResourceHolder<TMessage, TLeaf>.Push(leaf);

        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}
