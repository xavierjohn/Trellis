namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis.Authorization;

/// <summary>
/// Extension methods for registering Trellis.Mediator pipeline behaviors.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Gets the ordered array of Trellis Result-aware pipeline behavior types contributed by this
    /// package. Assign this to <c>MediatorOptions.PipelineBehaviors</c> in your <c>AddMediator</c>
    /// call when wiring the AOT-friendly source generator path.
    /// <para>The canonical Trellis pipeline (outermost to innermost) is:</para>
    /// <list type="number">
    ///   <item><description><see cref="ExceptionBehavior{TMessage, TResponse}"/> — catches unhandled exceptions and converts to typed failures.</description></item>
    ///   <item><description><see cref="TracingBehavior{TMessage, TResponse}"/> — emits an OpenTelemetry activity span around the message.</description></item>
    ///   <item><description><see cref="LoggingBehavior{TMessage, TResponse}"/> — structured logging with duration and outcome.</description></item>
    ///   <item><description><see cref="AuthorizationBehavior{TMessage, TResponse}"/> — checks static permissions declared by <see cref="IAuthorize"/>.</description></item>
    ///   <item><description><see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/> — checks resource-bound authorization for <see cref="IAuthorizeResource{TResource}"/> commands. Inserted by <see cref="AddResourceAuthorization{TMessage, TResource, TResponse}"/> or <see cref="AddResourceAuthorization(IServiceCollection, Assembly[])"/> immediately before the validation behavior so the loaded resource is checked once per request.</description></item>
    ///   <item><description><see cref="ValidationBehavior{TMessage, TResponse}"/> — unified
    ///   validation stage. Runs <see cref="IValidate.Validate"/> when the message implements it
    ///   AND every <see cref="IMessageValidator{TMessage}"/> registered in DI for the message,
    ///   aggregating <see cref="Error.InvalidInput"/> failures into a single response.
    ///   External validation sources (e.g., the optional <c>Trellis.Mediator.FluentValidation</c>
    ///   package contributes <c>FluentValidationMessageValidatorAdapter&lt;TMessage&gt;</c> via
    ///   <c>AddTrellisFluentValidation()</c>) plug in here without an extra pipeline behavior.</description></item>
    ///   <item><description><c>TransactionalCommandBehavior&lt;TMessage, TResponse&gt;</c>
    ///   (in the optional <c>Trellis.EntityFrameworkCore</c> package) — runs the handler then
    ///   calls <c>IUnitOfWork.CommitAsync</c> on success. Opt in via
    ///   <c>AddTrellisUnitOfWork&lt;TContext&gt;()</c> after all other behavior registrations
    ///   so it lands innermost (closest to the handler).</description></item>
    /// </list>
    /// <para>
    /// This array contains the always-on behaviors (<see cref="ExceptionBehavior{TMessage, TResponse}"/>,
    /// <see cref="TracingBehavior{TMessage, TResponse}"/>, <see cref="LoggingBehavior{TMessage, TResponse}"/>,
    /// <see cref="AuthorizationBehavior{TMessage, TResponse}"/>, and
    /// <see cref="ValidationBehavior{TMessage, TResponse}"/>). The resource-authorization and
    /// transactional behaviors are opt-in and supplied by separate registration helpers.
    /// FluentValidation (and any other external validation source) participates inside
    /// the existing <see cref="ValidationBehavior{TMessage, TResponse}"/> via the
    /// <see cref="IMessageValidator{TMessage}"/> abstraction, so it does not occupy its own
    /// pipeline slot.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddMediator(options =>
    /// {
    ///     options.Assemblies = [typeof(MyCommand).Assembly];
    ///     options.PipelineBehaviors = ServiceCollectionExtensions.PipelineBehaviors.ToArray();
    /// });
    /// </code>
    /// </example>
    private static readonly IReadOnlyList<Type> s_pipelineBehaviors =
    [
        typeof(ExceptionBehavior<,>),
        typeof(TracingBehavior<,>),
        typeof(LoggingBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(ValidationBehavior<,>),
    ];

    /// <inheritdoc cref="s_pipelineBehaviors" />
    public static IReadOnlyList<Type> PipelineBehaviors => s_pipelineBehaviors;

    /// <summary>
    /// Registers Trellis Result-aware pipeline behaviors as open generic
    /// <see cref="IPipelineBehavior{TMessage, TResponse}"/> implementations.
    /// Use this when NOT using <c>MediatorOptions.PipelineBehaviors</c> (non-AOT scenario).
    /// </summary>
    /// <remarks>
    /// Idempotent: calling this method more than once registers each behavior exactly once,
    /// so plug-in extension methods (e.g. <c>AddTrellisFluentValidation</c>, <c>AddTrellisAsp</c>)
    /// that defensively call it as a precondition will not produce duplicate pipeline entries
    /// when the consumer also calls it explicitly.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Safe-by-default telemetry options (Detail redacted). Registered as TryAddSingleton so
        // a prior call to AddTrellisBehaviors(configure) wins — idempotency is preserved.
        services.TryAddSingleton<TrellisMediatorTelemetryOptions>();

        // TryAddEnumerable deduplicates by (ServiceType, ImplementationType), preserving the
        // canonical insertion order documented on PipelineBehaviors.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(ExceptionBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));

        // Order-independence for explicit AddResourceAuthorization<TMessage,TResource,TResponse>()
        // calls made BEFORE AddTrellisBehaviors(): such calls inserted closed-generic
        // ResourceAuthorizationBehavior<,,> descriptors at the END of the collection (because
        // ValidationBehavior wasn't registered yet to anchor the insert). Now that the standard
        // pipeline is in place, relocate them to sit immediately before ValidationBehavior so
        // they end up in the canonical envelope. Mirrors the AddTrellisUnitOfWork ↔
        // AddDomainEventDispatch symmetry: pipeline-position-aware registrations are
        // order-independent regardless of which one runs first.
        RelocateResourceAuthorizationBehaviorsBeforeValidation(services);

        return services;
    }

    /// <summary>
    /// Walks the descriptor list, finds any closed-generic
    /// <c>IPipelineBehavior&lt;TMessage, TResponse&gt;</c> registrations whose
    /// implementation type is <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/>,
    /// and re-inserts each one immediately before <see cref="ValidationBehavior{TMessage, TResponse}"/>
    /// (preserving relative order among the resource-auth behaviors themselves).
    /// </summary>
    private static void RelocateResourceAuthorizationBehaviorsBeforeValidation(IServiceCollection services)
    {
        var validationIndex = FindValidationBehaviorIndex(services);
        if (validationIndex < 0)
            return;

        // Collect every closed-generic resource-auth descriptor in source-order so relative
        // ordering among them is preserved when we re-insert. We don't filter out descriptors
        // that already happen to sit directly before ValidationBehavior — moving such a
        // descriptor from position N to position N is a no-op effectively (the same instance
        // is removed and re-inserted at the same slot), and skipping the optimization keeps
        // the relocation logic uniform.
        var relocations = new List<ServiceDescriptor>();
        for (int i = 0; i < services.Count; i++)
        {
            var d = services[i];
            if (IsClosedResourceAuthorizationBehavior(d))
                relocations.Add(d);
        }

        if (relocations.Count == 0)
            return;

        // Remove first (descending index to avoid shifting), then re-insert before validation.
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (IsClosedResourceAuthorizationBehavior(services[i]))
                services.RemoveAt(i);
        }

        // ValidationBehavior may have moved if it was after any of the relocated entries; relocate
        // it freshly post-removal.
        var newValidationIndex = FindValidationBehaviorIndex(services);
        if (newValidationIndex < 0)
        {
            // Defensive: ValidationBehavior should always be present since we just registered it.
            // Fall back to appending to preserve the resource-auth registrations rather than
            // dropping them.
            foreach (var d in relocations)
                services.Add(d);
            return;
        }

        for (int i = 0; i < relocations.Count; i++)
            services.Insert(newValidationIndex + i, relocations[i]);
    }

    private static bool IsClosedResourceAuthorizationBehavior(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType.IsGenericTypeDefinition)
            return false;
        if (!descriptor.ServiceType.IsGenericType)
            return false;
        if (descriptor.ServiceType.GetGenericTypeDefinition() != typeof(IPipelineBehavior<,>))
            return false;

        var impl = descriptor.ImplementationType;
        if (impl is null || !impl.IsGenericType || impl.IsGenericTypeDefinition)
            return false;

        var def = impl.GetGenericTypeDefinition();
        return def == typeof(ResourceAuthorizationBehavior<,,>)
            || def == typeof(ResourceAuthorizationViaBehavior<,,,>);
    }

    /// <summary>
    /// Registers Trellis pipeline behaviors and configures
    /// <see cref="TrellisMediatorTelemetryOptions"/> for logging/tracing redaction. Equivalent
    /// to calling <see cref="AddTrellisBehaviors(IServiceCollection)"/> followed by mutating
    /// the resolved singleton via <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Callback invoked with the singleton telemetry options instance. Use this to opt in to
    /// including <see cref="Error.Detail"/> in log messages and activity descriptions.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisBehaviors(
        this IServiceCollection services,
        Action<TrellisMediatorTelemetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var instance = new TrellisMediatorTelemetryOptions();
        configure(instance);

        // Replace any prior TryAddSingleton<TrellisMediatorTelemetryOptions>() registration
        // with the configured instance so this call wins regardless of ordering.
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TrellisMediatorTelemetryOptions))
            {
                services.RemoveAt(i);
            }
        }

        services.AddSingleton(instance);
        AddTrellisBehaviors(services);

        return services;
    }

    /// <summary>
    /// Registers the <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/>
    /// for a specific command/resource pair. Duplicate registrations of the same closed
    /// behavior are ignored.
    /// </summary>
    /// <typeparam name="TMessage">
    /// The command or query type that implements <see cref="IAuthorizeResource{TResource}"/>.
    /// </typeparam>
    /// <typeparam name="TResource">The resource type loaded for authorization.</typeparam>
    /// <typeparam name="TResponse">
    /// The response type (e.g., <c>Result&lt;Order&gt;</c>).
    /// Must implement <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Despite the feature name (<i>resource authorization</i>), this extension lives in the
    /// <c>Trellis.Mediator</c> namespace — not <c>Trellis.Authorization</c> — because the
    /// resource is loaded by a Mediator pipeline behavior. Add
    /// <c>using Trellis.Mediator;</c> in your composition root (e.g.,
    /// <c>DependencyInjection.cs</c>) to bring this method into scope.
    /// </para>
    /// <para>
    /// Prefer <see cref="AddResourceAuthorization(IServiceCollection, Assembly[])"/> for automatic
    /// discovery. Use this explicit overload for AOT/trimming scenarios where assembly scanning
    /// is not available.
    /// </para>
    /// <para>
    /// Repeated explicit or scanned registration of the same closed
    /// <c>IPipelineBehavior&lt;TMessage, TResponse&gt;</c> implementation is idempotent.
    /// Distinct closed generic behaviors, including different response types, remain distinct.
    /// </para>
    /// <para>
    /// Also register the corresponding <see cref="IResourceLoader{TMessage, TResource}"/> as scoped,
    /// either explicitly or via <see cref="AddResourceLoaders"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddResourceAuthorization&lt;CancelOrderCommand, Order, Result&lt;Order&gt;&gt;();
    /// services.AddScoped&lt;IResourceLoader&lt;CancelOrderCommand, Order&gt;, CancelOrderResourceLoader&gt;();
    /// </code>
    /// </example>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2090:'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method.",
        Justification = "TMessage is annotated [DynamicallyAccessedMembers(Interfaces)] on the public surface; the suppression covers the call-site inside this method to GetInterfaces() reflectively.")]
    public static IServiceCollection AddResourceAuthorization<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)] TMessage,
        TResource,
        TResponse>(
        this IServiceCollection services)
        where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage
        where TResource : class
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        // Reject dual-mode commands here too — assembly scanning is not the only path that
        // can register resource authorization, so the security invariant must guard every entry.
        var ifaces = typeof(TMessage).GetInterfaces();
        var authIface = ifaces.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuthorizeResource<>));
        var viaIfaceCheck = ifaces.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuthorizeResourceVia<>));
        EnsureNotDualSecurityMode(typeof(TMessage), authIface, viaIfaceCheck);

        InsertResourceAuthorizationBehavior(
            services,
            ServiceDescriptor.Scoped<
                IPipelineBehavior<TMessage, TResponse>,
                ResourceAuthorizationBehavior<TMessage, TResource, TResponse>>());

        // v4 typed accessor — register IAuthorizedResource<TMessage, TResource> so handlers
        // can inject the loaded resource and avoid a duplicate load. The holder is stateless
        // (per-closed-pair static AsyncLocal); Scoped lifetime matches surrounding pipeline
        // components. TryAddScoped is idempotent on repeat registration.
        services.TryAddScoped<
            IAuthorizedResource<TMessage, TResource>,
            AuthorizedResourceHolder<TMessage, TResource>>();

        return services;
    }

    /// <summary>
    /// Scans the specified assembly for types implementing
    /// <see cref="IAuthorizeResource{TResource}"/> and automatically registers the
    /// <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/> for each.
    /// Also scans and registers all <see cref="IResourceLoader{TMessage, TResource}"/> implementations
    /// and <see cref="SharedResourceLoaderById{TResource, TId}"/> implementations as scoped services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan. Pass both the Application assembly
    /// (containing <see cref="IAuthorizeResource{TResource}"/> commands) and the Acl assembly
    /// (containing <see cref="IResourceLoader{TMessage, TResource}"/> implementations).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Despite the feature name (<i>resource authorization</i>), this extension lives in the
    /// <c>Trellis.Mediator</c> namespace — not <c>Trellis.Authorization</c> — because the
    /// resource is loaded by a Mediator pipeline behavior. Add
    /// <c>using Trellis.Mediator;</c> in your composition root (e.g.,
    /// <c>DependencyInjection.cs</c>) to bring this method into scope.
    /// </para>
    /// <para>
    /// For each concrete type that implements <see cref="IAuthorizeResource{TResource}"/>,
    /// the method extracts <c>TResource</c> and resolves <c>TResponse</c> from
    /// <c>ICommand&lt;TResponse&gt;</c>, <c>IQuery&lt;TResponse&gt;</c>, or
    /// <c>IRequest&lt;TResponse&gt;</c>. It then registers the closed-generic
    /// <c>ResourceAuthorizationBehavior&lt;TMessage, TResource, TResponse&gt;</c>
    /// as <c>IPipelineBehavior&lt;TMessage, TResponse&gt;</c>.
    /// </para>
    /// <para>
    /// Closed resource-authorization behavior registrations are idempotent across repeated
    /// scans and explicit-plus-scanned overlap when both service type and implementation type match.
    /// </para>
    /// <para>
    /// This method also scans for <see cref="IResourceLoader{TMessage, TResource}"/>
    /// implementations and registers them as scoped services, so you don't need to call
    /// <see cref="AddResourceLoaders"/> separately.
    /// </para>
    /// <para>
    /// When a command implements <see cref="IIdentifyResource{TResource, TId}"/> and no explicit
    /// <see cref="IResourceLoader{TMessage, TResource}"/> is found, a
    /// <see cref="SharedResourceLoaderAdapter{TMessage, TResource, TId}"/> is automatically
    /// registered, bridging to the <see cref="SharedResourceLoaderById{TResource, TId}"/>.
    /// Explicit loaders always take priority.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Scans both Application (commands) and Acl (loaders) assemblies
    /// services.AddResourceAuthorization(
    ///     typeof(CancelOrderCommand).Assembly,
    ///     typeof(CancelOrderResourceLoader).Assembly);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")]
    [RequiresDynamicCode("Constructs closed generic types at runtime. Use explicit registration for AOT scenarios.")]
    public static IServiceCollection AddResourceAuthorization(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));
        for (var i = 0; i < assemblies.Length; i++)
        {
            if (assemblies[i] is null)
                throw new ArgumentException($"Assembly at index [{i}] is null.", nameof(assemblies));
        }

        // Deduplicate the assemblies parameter so a consumer passing the same assembly
        // twice (e.g. via `typeof(X).Assembly, typeof(Y).Assembly` where X and Y live in the
        // same assembly, or via overlapping library + application-level scan calls) does not
        // cause closed-generic IPipelineBehavior<TMessage,TResponse> descriptors to be inserted
        // twice. The IResourceLoader and SharedResourceLoaderById registrations are already
        // idempotent via TryAddScoped, but the pipeline-behavior Insert is not — duplicate
        // entries would cause the authorization behavior to run multiple times per request.
        // Preserve first-seen order so the precedence of TryAdd-style loader registrations
        // remains deterministic and matches the consumer's input order (HashSet alone would
        // not guarantee enumeration order).
        var seenAssemblies = new HashSet<Assembly>(assemblies.Length);
        var distinctAssemblies = new List<Assembly>(assemblies.Length);
        for (var i = 0; i < assemblies.Length; i++)
        {
            if (seenAssemblies.Add(assemblies[i]))
                distinctAssemblies.Add(assemblies[i]);
        }

        var authorizeResourceDef = typeof(IAuthorizeResource<>);
        var authorizeViaDef = typeof(IAuthorizeResourceVia<>);
        var loaderDef = typeof(IResourceLoader<,>);
        var sharedLoaderDef = typeof(SharedResourceLoaderById<,>);
        var identifyResourceDef = typeof(IIdentifyResource<,>);
        var adapterDef = typeof(SharedResourceLoaderAdapter<,,>);
        var behaviorDef = typeof(ResourceAuthorizationBehavior<,,>);
        var viaBehaviorDef = typeof(ResourceAuthorizationViaBehavior<,,,>);
        var pipelineDef = typeof(IPipelineBehavior<,>);

        Type[] messageInterfaces =
        [
            typeof(global::Mediator.ICommand<>),
            typeof(global::Mediator.IQuery<>),
            typeof(global::Mediator.IRequest<>),
        ];

        // Track shared loader availability and commands needing bridging
        var sharedLoaderTypes = new HashSet<Type>(); // closed SharedResourceLoaderById<TResource, TId> base types
        var commandsNeedingBridging = new List<(Type commandType, Type tResource, Type tResponse, Type identifyIface)>();
        // Collected via-authorized commands (Pass 6). Path resolution runs after first sweep
        // because the resolver needs every candidate entity type discovered across all assemblies.
        var viaCommands = new List<(Type commandType, Type tLeaf, Type tOwner, Type tResponse, Type identifyIface)>();
        var allCandidateEntities = new List<Type>();

        foreach (var assembly in distinctAssemblies)
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                allCandidateEntities.Add(type);

                // Register IResourceLoader<,> implementations as scoped
                // TryAdd ensures pre-registered loaders are not overridden
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == loaderDef)
                        services.TryAddScoped(iface, type);
                }

                // Discover SharedResourceLoaderById<TResource, TId> implementations
                var baseType = type.BaseType;
                while (baseType is not null)
                {
                    if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == sharedLoaderDef)
                    {
                        services.TryAddScoped(baseType, type);
                        sharedLoaderTypes.Add(baseType);
                        break;
                    }

                    baseType = baseType.BaseType;
                }

                // Check both authorization markers up-front so we can reject dual-mode commands
                // before doing any registration work for either marker. Enumerate ALL closed forms
                // of each marker (not FirstOrDefault) so a message that incorrectly closes the
                // same marker over multiple resource types is rejected rather than silently having
                // authorization registered for only one of the closed forms. The closed-form
                // ambiguity check runs AFTER the mediator-message guard below so non-message
                // fixtures that happen to declare multiple closed markers for testing purposes
                // don't trigger spurious failures.
                var authIfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == authorizeResourceDef)
                    .ToList();
                var viaIfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == authorizeViaDef)
                    .ToList();

                if (authIfaces.Count == 0 && viaIfaces.Count == 0)
                    continue;

                // Find TResponse from ICommand<TResponse>, IQuery<TResponse>, or IRequest<TResponse>.
                // Types that don't implement a mediator message interface are skipped — they may
                // be test fixtures or DTOs that incidentally declare an auth marker.
                var tResponse = type.GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Select(i => (iface: i, def: i.GetGenericTypeDefinition()))
                    .Where(x => messageInterfaces.Contains(x.def))
                    .Select(x => x.iface.GetGenericArguments()[0])
                    .FirstOrDefault();

                if (tResponse is null)
                    continue;

                // Closed-marker ambiguity rejection runs only for types confirmed to be mediator
                // messages. Mirrors the dual-mode rejection's "message-only" scope (line ~456) so
                // non-message fixtures that happen to declare multiple closed markers for testing
                // purposes don't trigger spurious failures.
                EnsureAtMostOneClosedAuthorizationMarker(type, authIfaces, "IAuthorizeResource");
                EnsureAtMostOneClosedAuthorizationMarker(type, viaIfaces, "IAuthorizeResourceVia");

                var authIface = authIfaces.Count == 1 ? authIfaces[0] : null;
                var viaIface = viaIfaces.Count == 1 ? viaIfaces[0] : null;

                // Dual-mode rejection runs only for types confirmed to be mediator messages. This
                // avoids spurious failures from non-message fixtures that happen to declare both
                // markers for testing purposes.
                EnsureNotDualSecurityMode(type, authIface, viaIface);

                if (authIface is not null)
                {
                    var commandResource = authIface.GetGenericArguments()[0];

                    // TResponse must satisfy the behavior's constraints: IResult + IFailureFactory<TResponse>.
                    // Fail fast on misconfigured security-marked commands rather than silently
                    // skipping them — IAuthorizeResource<TResource> is a security marker and a
                    // resource-authorized command that is silently never wired up at startup is a
                    // dangerous failure mode (the command runs without resource authorization).
                    ValidateResourceAuthorizationResponseType(type, commandResource, tResponse,
                        markerInterfaceName: "IAuthorizeResource",
                        behaviorTypeName: "ResourceAuthorizationBehavior<TMessage, TResource, TResponse>");

                    var closedBehavior = behaviorDef.MakeGenericType(type, commandResource, tResponse);
                    var closedPipeline = pipelineDef.MakeGenericType(type, tResponse);
                    InsertResourceAuthorizationBehavior(
                        services,
                        ServiceDescriptor.Scoped(closedPipeline, closedBehavior));

                    // v4 typed accessor — register IAuthorizedResource<type, commandResource> so
                    // handlers can inject the loaded resource and avoid a duplicate load. Mirrors
                    // the explicit AddResourceAuthorization<,,>() helper above.
                    RegisterAuthorizedResourceAccessor(services, type, commandResource);

                    var identifyIfaceForAuth = type.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType
                            && i.GetGenericTypeDefinition() == identifyResourceDef
                            && i.GetGenericArguments()[0] == commandResource);

                    if (identifyIfaceForAuth is not null)
                        commandsNeedingBridging.Add((type, commandResource, tResponse, identifyIfaceForAuth));
                }
                else
                {
                    var tOwner = viaIface!.GetGenericArguments()[0];

                    // Via-commands declare their leaf via IIdentifyResource<TLeaf, TLeafId> so the
                    // pipeline can load it before walking the navigation chain. Missing
                    // IIdentifyResource on a via-command is a registration error: the security
                    // marker is present but the framework cannot infer the leaf type for the
                    // pipeline-behavior closed generic. Multiple IIdentifyResource<,>
                    // declarations on one via-command are also rejected — leaf selection would
                    // be ambiguous and could authorize the wrong resource chain. Throw at
                    // startup rather than silently skipping or picking the first. Consumers
                    // needing a custom leaf source register via the explicit AOT helper
                    // (AddRelatedResourceAuthorization), which bypasses the scanner.
                    var identifyIfacesForVia = type.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == identifyResourceDef)
                        .ToList();

                    EnsureExactlyOneIIdentifyResourceForVia(type, viaIface!, identifyIfacesForVia);

                    var identifyIfaceForVia = identifyIfacesForVia[0];
                    var tLeaf = identifyIfaceForVia.GetGenericArguments()[0];

                    // Same TResponse constraint as IAuthorizeResource<T>: IResult + IFailureFactory<TResponse>.
                    ValidateResourceAuthorizationResponseType(type, tOwner, tResponse,
                        markerInterfaceName: "IAuthorizeResourceVia",
                        behaviorTypeName: "ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>");

                    viaCommands.Add((type, tLeaf, tOwner, tResponse, identifyIfaceForVia));
                    // Leaf-loader bridging shares the same machinery as IAuthorizeResource<T>.
                    commandsNeedingBridging.Add((type, tLeaf, tResponse, identifyIfaceForVia));

                    // v4 typed accessor — register IAuthorizedResource<type, tLeaf>. For via
                    // commands the accessor exposes the LEAF (the typical mutation target),
                    // not the owner. The owner accessor is intentionally not in scope per ADR-002 §5.3.
                    RegisterAuthorizedResourceAccessor(services, type, tLeaf);
                }
            }

        // Register SharedResourceLoaderAdapter for commands that need bridging
        // (TryAdd ensures pre-registered or scanned loaders take priority)
        foreach (var (commandType, tResource, _, identifyIface) in commandsNeedingBridging)
        {
            var closedLoader = loaderDef.MakeGenericType(commandType, tResource);

            // Only bridge if a SharedResourceLoaderById<TResource, TId> with matching TId exists
            // (either discovered via scanning or pre-registered in DI)
            var tId = identifyIface.GetGenericArguments()[1];
            var closedSharedLoader = sharedLoaderDef.MakeGenericType(tResource, tId);
            if (!sharedLoaderTypes.Contains(closedSharedLoader)
                && !services.Any(d => d.ServiceType == closedSharedLoader))
                continue;

            var closedAdapter = adapterDef.MakeGenericType(commandType, tResource, tId);
            services.TryAdd(ServiceDescriptor.Scoped(closedLoader, closedAdapter));
        }

        // Resolve paths and register ResourceAuthorizationViaBehavior for via-commands.
        // Path resolution is deferred until after the full entity sweep so the resolver
        // sees every IIdentifyRelatedResource[s] declaration across all input assemblies.
        var holderDef = typeof(ResolvedAuthorizationPathHolder<,,,>);
        foreach (var (commandType, tLeaf, tOwner, tResponse, _) in viaCommands)
        {
            var path = ResourceAuthorizationPathResolver.Resolve(
                commandType, tLeaf, tOwner, allCandidateEntities);

            var closedHolder = holderDef.MakeGenericType(commandType, tLeaf, tOwner, tResponse);
            var holderInstance = Activator.CreateInstance(closedHolder, path)
                ?? throw new InvalidOperationException(
                    $"Failed to construct {closedHolder.Name} for via-authorized command {commandType.Name}.");

            // Per-command closed-generic holder — DI naturally disambiguates by closed type,
            // so two via-commands cannot accidentally share a path.
            services.TryAdd(ServiceDescriptor.Singleton(closedHolder, holderInstance));

            var closedViaBehavior = viaBehaviorDef.MakeGenericType(commandType, tLeaf, tOwner, tResponse);
            var closedPipeline = pipelineDef.MakeGenericType(commandType, tResponse);

            // TYPED descriptor (not factory): the via-behavior's holder-taking constructor
            // is selected by DI because the holder is registered and the alternate
            // ResolvedAuthorizationPath ctor's parameter is not. Typed registration lets the
            // relocator match descriptors by ImplementationType — no marker hack needed and
            // no risk of misclassifying unrelated consumer factory-registered behaviors.
            InsertResourceAuthorizationBehavior(
                services,
                ServiceDescriptor.Scoped(closedPipeline, closedViaBehavior));
        }

        return services;
    }

    /// <summary>
    /// Explicitly registers indirect (multi-hop) resource authorization for a single
    /// via-authorized command without assembly scanning. Use this in AOT / trimming scenarios
    /// where <see cref="AddResourceAuthorization(IServiceCollection, Assembly[])"/> cannot
    /// run. The single-hop arity covers the 80% case; consumers needing multi-hop chains
    /// can either provide a fully-built <see cref="ResolvedAuthorizationPath"/> via
    /// <see cref="AddRelatedResourceAuthorization{TMessage, TLeaf, TOwner, TResponse}(IServiceCollection, ResolvedAuthorizationPath)"/>
    /// or drop to <see cref="IResourceLoader{TMessage, TResource}"/> with a projection type.
    /// </summary>
    /// <typeparam name="TMessage">The command type implementing <see cref="IAuthorizeResourceVia{TOwner}"/>.</typeparam>
    /// <typeparam name="TLeaf">The leaf resource type.</typeparam>
    /// <typeparam name="TLeafId">The leaf resource identifier type.</typeparam>
    /// <typeparam name="TOwner">The owner resource type authorization is evaluated against.</typeparam>
    /// <typeparam name="TOwnerId">The owner resource identifier type.</typeparam>
    /// <typeparam name="TResponse">The response type returned by the message handler.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="extractOwnerId">
    /// Delegate that, given a loaded <typeparamref name="TLeaf"/>, returns the
    /// <typeparamref name="TOwnerId"/> identifying the owner to authorize against. Returning
    /// <c>null</c> short-circuits authorization to <see cref="Error.Forbidden"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This overload assumes the consumer has already registered:
    /// <list type="bullet">
    ///   <item><description><see cref="SharedResourceLoaderById{TLeaf, TLeafId}"/> for the leaf.</description></item>
    ///   <item><description><see cref="SharedResourceLoaderById{TOwner, TOwnerId}"/> for the owner.</description></item>
    ///   <item><description>An <see cref="IResourceLoader{TMessage, TLeaf}"/> for the command (typically via <see cref="AddSharedResourceLoader{TMessage, TResource, TId}"/>).</description></item>
    /// </list>
    /// The behavior fails fast at request time if any of these are missing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRelatedResourceAuthorization<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)] TMessage,
        TLeaf,
        TLeafId,
        TOwner,
        TOwnerId,
        TResponse>(
        this IServiceCollection services,
        Func<TLeaf, TOwnerId?> extractOwnerId)
        where TMessage : IAuthorizeResourceVia<TOwner>, IIdentifyResource<TLeaf, TLeafId>, global::Mediator.IMessage
        where TLeaf : class
        where TOwner : class
        where TOwnerId : notnull
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extractOwnerId);

        var hop = new ResolvedAuthorizationHop(
            fromType: typeof(TLeaf),
            toType: typeof(TOwner),
            toIdType: typeof(TOwnerId),
            extractIds: src =>
            {
                var id = extractOwnerId((TLeaf)src);
                return id is null ? Array.Empty<object>() : new object[] { id };
            },
            loadAsync: async (sp, id, ct) =>
            {
                // Missing loader is a DEPLOYMENT bug — throw to fail loud rather than masking
                // it as a 403 Forbidden. Persistent 403s caused by missing loader registrations
                // are very hard to distinguish from real authorization denials in production.
                var loader = sp.GetService<SharedResourceLoaderById<TOwner, TOwnerId>>()
                    ?? throw new InvalidOperationException(
                        $"ResourceAuthorizationViaBehavior<{typeof(TMessage).Name}, ...> requires a registered " +
                        $"SharedResourceLoaderById<{typeof(TOwner).Name}, {typeof(TOwnerId).Name}> for the owner hop. " +
                        $"Register one in the composition root.");

                var result = await loader.GetByIdAsync((TOwnerId)id, ct).ConfigureAwait(false);
                if (!result.TryGetValue(out var v, out var err))
                    return HopLoadResult.Failure(err);

                // Defense-in-depth: a SharedResourceLoaderById that violates its Result<T>
                // contract by returning a successful result carrying a null payload must NOT
                // crash the pipeline with ArgumentNullException from HopLoadResult.Success —
                // mirrors the same defense in ResourceAuthorizationPathResolver.LoaderImpl
                // so the explicit AOT helper preserves the documented "intermediate / owner
                // load failure collapses to Forbidden" invariant.
                if (v is null)
                    return HopLoadResult.Failure(new Error.Forbidden("resource.authorization-via.null-payload")
                    {
                        Detail = "A related resource loader returned a successful result with a null value.",
                    });

                return HopLoadResult.Success(v);
            },
            isPlural: false);

        var path = new ResolvedAuthorizationPath(
            messageType: typeof(TMessage),
            leafType: typeof(TLeaf),
            ownerType: typeof(TOwner),
            hops: [hop]);

        return AddRelatedResourceAuthorization<TMessage, TLeaf, TOwner, TResponse>(services, path);
    }

    /// <summary>
    /// Explicitly registers indirect resource authorization with a fully-built
    /// <see cref="ResolvedAuthorizationPath"/>. Use this when the single-hop overload
    /// is insufficient (chains, plural-terminal fan-out, or composite shapes built by hand).
    /// </summary>
    /// <typeparam name="TMessage">The command type.</typeparam>
    /// <typeparam name="TLeaf">The leaf resource type.</typeparam>
    /// <typeparam name="TOwner">The owner resource type.</typeparam>
    /// <typeparam name="TResponse">The response type returned by the message handler.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="path">The resolved path. Must match the generic arguments on <see cref="ResolvedAuthorizationPath.MessageType"/>, <see cref="ResolvedAuthorizationPath.LeafType"/>, and <see cref="ResolvedAuthorizationPath.OwnerType"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2090:'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method.",
        Justification = "TMessage is annotated [DynamicallyAccessedMembers(Interfaces)] on the public surface; the suppression covers the call-site inside this method to GetInterfaces() reflectively.")]
    public static IServiceCollection AddRelatedResourceAuthorization<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)] TMessage,
        TLeaf,
        TOwner,
        TResponse>(
        this IServiceCollection services,
        ResolvedAuthorizationPath path)
        where TMessage : IAuthorizeResourceVia<TOwner>, global::Mediator.IMessage
        where TLeaf : class
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(path);

        // Reject dual-mode commands at every entry point. The scanner has its own check; this
        // guard fires for AOT/explicit registration so the invariant cannot be bypassed.
        // TMessage is annotated with [DynamicallyAccessedMembers(Interfaces)] so trimming
        // preserves the interface metadata needed to detect a violating IAuthorizeResource<T>.
        var ifaces = typeof(TMessage).GetInterfaces();
        var authIface = ifaces.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuthorizeResource<>));
        var viaIfaceCheck = ifaces.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuthorizeResourceVia<>));
        EnsureNotDualSecurityMode(typeof(TMessage), authIface, viaIfaceCheck);

        // Closed-generic holder so DI naturally disambiguates per via-authorized command.
        services.TryAddSingleton(new ResolvedAuthorizationPathHolder<TMessage, TLeaf, TOwner, TResponse>(path));

        // TYPED descriptor — the holder-taking constructor of ResourceAuthorizationViaBehavior
        // is selected by DI because the holder is registered and the alternate path-taking
        // ctor's parameter is not. Letting the relocator match by ImplementationType eliminates
        // the previous factory-descriptor misclassification hazard.
        InsertResourceAuthorizationBehavior(
            services,
            ServiceDescriptor.Scoped<
                IPipelineBehavior<TMessage, TResponse>,
                ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>>());

        // v4 typed accessor — register IAuthorizedResource<TMessage, TLeaf> so handlers
        // can inject the LEAF (the mutation target). Mirrors the scan-path registration
        // for via commands; ensures AOT/explicit registration users also get the accessor.
        services.TryAddScoped<
            IAuthorizedResource<TMessage, TLeaf>,
            AuthorizedResourceHolder<TMessage, TLeaf>>();

        return services;
    }

    private static void InsertResourceAuthorizationBehavior(
        IServiceCollection services,
        ServiceDescriptor descriptor)
    {
        for (int i = 0; i < services.Count; i++)
        {
            var existing = services[i];
            if (existing.ServiceType == descriptor.ServiceType
                && existing.ImplementationType == descriptor.ImplementationType
                && existing.ImplementationType is not null)
                return;
        }

        var validationIndex = FindValidationBehaviorIndex(services);
        if (validationIndex >= 0)
        {
            services.Insert(validationIndex, descriptor);
            return;
        }

        services.Add(descriptor);
    }

    /// <summary>
    /// Registers <c>IAuthorizedResource&lt;TMessage, TResource&gt;</c> implemented by
    /// <c>AuthorizedResourceHolder&lt;TMessage, TResource&gt;</c> as scoped for the closed pair,
    /// so handlers can inject the v4 accessor and avoid a duplicate load of the
    /// pipeline-loaded resource. Called from the scan path for each discovered
    /// <see cref="IAuthorizeResource{TResource}"/> command (with the resource type) and for each
    /// discovered <see cref="IAuthorizeResourceVia{TOwner}"/> command (with the LEAF type).
    /// The explicit AOT registration helpers register the accessor inline via the typed overload
    /// of <c>TryAddScoped&lt;TService, TImplementation&gt;</c>; this helper covers the scan path
    /// where the types are only known at runtime.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
        Justification = "Caller is itself annotated [RequiresDynamicCode] — the entire scan path is AOT-incompatible by design; AOT consumers use the typed AddResourceAuthorization<,,>() / AddRelatedResourceAuthorization<,,,>() overloads which register the accessor without MakeGenericType.")]
    private static void RegisterAuthorizedResourceAccessor(
        IServiceCollection services,
        Type messageType,
        Type resourceType)
    {
        var holderType = typeof(AuthorizedResourceHolder<,>).MakeGenericType(messageType, resourceType);
        var accessorType = typeof(IAuthorizedResource<,>).MakeGenericType(messageType, resourceType);
        services.TryAddScoped(accessorType, holderType);
    }

    /// <summary>
    /// Scans the specified assembly for types implementing
    /// <see cref="IResourceLoader{TMessage, TResource}"/> and registers them as scoped services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan for resource loader implementations.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddResourceLoaders(typeof(CancelOrderResourceLoader).Assembly);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use explicit registration for AOT/trimming scenarios.")]
    public static IServiceCollection AddResourceLoaders(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        var loaderInterface = typeof(IResourceLoader<,>);

        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == loaderInterface)
                    services.TryAddScoped(iface, type);
            }
        }

        return services;
    }

    /// <summary>
    /// Registers a <see cref="SharedResourceLoaderAdapter{TMessage, TResource, TId}"/> for a specific
    /// command, bridging to a <see cref="SharedResourceLoaderById{TResource, TId}"/>.
    /// Use this for AOT/trimming scenarios where assembly scanning is not available.
    /// </summary>
    /// <typeparam name="TMessage">
    /// The command type implementing <see cref="IIdentifyResource{TResource, TId}"/>. The
    /// constraint is intentionally limited to <see cref="IIdentifyResource{TResource, TId}"/>
    /// only — both <see cref="IAuthorizeResource{TResource}"/> commands (where
    /// <typeparamref name="TResource"/> is the resource the command authorizes against) and
    /// <see cref="IAuthorizeResourceVia{TOwner}"/> via-commands (where
    /// <typeparamref name="TResource"/> is the via-command's leaf resource type) reuse this
    /// bridge.
    /// </typeparam>
    /// <typeparam name="TResource">The resource type.</typeparam>
    /// <typeparam name="TId">The identifier type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The <see cref="SharedResourceLoaderById{TResource, TId}"/> implementation must be registered
    /// separately (e.g., <c>services.AddScoped&lt;SharedResourceLoaderById&lt;Order, OrderId&gt;, OrderResourceLoader&gt;()</c>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddScoped&lt;SharedResourceLoaderById&lt;Order, OrderId&gt;, OrderResourceLoader&gt;();
    /// services.AddSharedResourceLoader&lt;CancelOrderCommand, Order, OrderId&gt;();
    /// services.AddSharedResourceLoader&lt;ReturnOrderCommand, Order, OrderId&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddSharedResourceLoader<TMessage, TResource, TId>(
        this IServiceCollection services)
        where TMessage : IIdentifyResource<TResource, TId>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IResourceLoader<TMessage, TResource>,
            SharedResourceLoaderAdapter<TMessage, TResource, TId>>();
        return services;
    }

    /// <summary>
    /// Returns all types from the assembly that can be loaded, gracefully handling
    /// <see cref="ReflectionTypeLoadException"/> when some types have missing dependencies.
    /// </summary>
    [RequiresUnreferencedCode("Calls Assembly.GetTypes().")]
    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }

    private static int FindValidationBehaviorIndex(IServiceCollection services)
    {
        for (int i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(IPipelineBehavior<,>)
                && descriptor.ImplementationType == typeof(ValidationBehavior<,>))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a scanned message closes the same
    /// authorization marker (<see cref="IAuthorizeResource{T}"/> or
    /// <see cref="IAuthorizeResourceVia{T}"/>) over more than one resource type. The assembly
    /// scanner registers a closed-generic pipeline behavior per closed marker, so a multi-closed
    /// message would silently have authorization registered for only the first discovered
    /// closed form and the remaining marker(s) would be effectively ignored at runtime — a
    /// dangerous failure mode for a security marker. Internal so the rejection invariant can be
    /// unit-tested directly without round-tripping through assembly scanning.
    /// </summary>
    /// <param name="messageType">The concrete message type discovered by the scanner.</param>
    /// <param name="markerIfaces">All closed forms of the marker found on <paramref name="messageType"/>.</param>
    /// <param name="markerInterfaceName">
    /// Display name of the security-marker interface — either <c>"IAuthorizeResource"</c> or
    /// <c>"IAuthorizeResourceVia"</c>. Used so the diagnostic names the actual marker the
    /// consumer declared.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="markerIfaces"/> contains more than one entry.
    /// </exception>
    internal static void EnsureAtMostOneClosedAuthorizationMarker(
        Type messageType,
        IReadOnlyList<Type> markerIfaces,
        string markerInterfaceName)
    {
        if (markerIfaces.Count <= 1)
            return;

        throw new InvalidOperationException(
            $"{messageType.FullName ?? messageType.Name} implements {markerIfaces.Count} closed forms of " +
            $"{markerInterfaceName}<> — assembly scanning would register the closed-generic authorization " +
            $"behavior for only one of them and silently leave the remaining marker(s) unenforced at runtime. " +
            $"Closed markers: " +
            string.Join(", ", markerIfaces.Select(i =>
                $"{markerInterfaceName}<{i.GetGenericArguments()[0].Name}>")) +
            $". Declare exactly one closed {markerInterfaceName}<> per command, or register the additional " +
            $"closed forms via the explicit AddResourceAuthorization<TMessage, TResource, TResponse>() / " +
            $"AddRelatedResourceAuthorization<...>() helpers (which bypass assembly scanning and require " +
            $"the consumer to opt in explicitly per closed form).");
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a command type implements both
    /// <see cref="IAuthorizeResource{TResource}"/> and <see cref="IAuthorizeResourceVia{TOwner}"/>.
    /// Security primitives are not silently composed; a command declares exactly one
    /// resource-authorization mode. Internal so the rejection invariant can be unit-tested
    /// directly without round-tripping through assembly scanning.
    /// </summary>
    /// <param name="messageType">The concrete message type discovered by the scanner.</param>
    /// <param name="authIface">The closed <see cref="IAuthorizeResource{TResource}"/> interface on the message, or null.</param>
    /// <param name="viaIface">The closed <see cref="IAuthorizeResourceVia{TOwner}"/> interface on the message, or null.</param>
    /// <exception cref="InvalidOperationException">Thrown when both interfaces are present.</exception>
    internal static void EnsureNotDualSecurityMode(Type messageType, Type? authIface, Type? viaIface)
    {
        if (authIface is null || viaIface is null)
            return;

        throw new InvalidOperationException(
            $"{messageType.FullName ?? messageType.Name} implements both IAuthorizeResource<{authIface.GetGenericArguments()[0].Name}> " +
            $"and IAuthorizeResourceVia<{viaIface.GetGenericArguments()[0].Name}>. " +
            $"A command must declare exactly one resource-authorization mode; security primitives are not " +
            $"silently composed. Pick one: use IAuthorizeResource<T> when authorization runs against the " +
            $"resource the command identifies, or IAuthorizeResourceVia<T> when it runs against a related resource.");
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a via-authorized command does not
    /// declare exactly one <see cref="IIdentifyResource{TResource, TId}"/>. Zero identify
    /// interfaces leaves the marker unprotected at runtime; two or more makes leaf selection
    /// ambiguous and could authorize the wrong resource chain. Internal so the rejection
    /// invariant can be unit-tested directly without round-tripping through assembly scanning.
    /// </summary>
    /// <param name="messageType">The concrete via-command type.</param>
    /// <param name="viaIface">The closed <see cref="IAuthorizeResourceVia{TOwner}"/> interface on the message.</param>
    /// <param name="identifyIfaces">All closed <see cref="IIdentifyResource{TResource, TId}"/> interfaces the message declares.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="identifyIfaces"/> is empty (no leaf) or contains more than
    /// one entry (ambiguous leaf).
    /// </exception>
    internal static void EnsureExactlyOneIIdentifyResourceForVia(
        Type messageType,
        Type viaIface,
        IReadOnlyList<Type> identifyIfaces)
    {
        var tOwner = viaIface.GetGenericArguments()[0];

        if (identifyIfaces.Count == 0)
            throw new InvalidOperationException(
                $"{messageType.FullName ?? messageType.Name} implements IAuthorizeResourceVia<{tOwner.Name}> " +
                $"but does not implement IIdentifyResource<TLeaf, TLeafId>. Via-commands must declare " +
                $"their leaf resource so the pipeline can load it before walking the navigation chain. " +
                $"For non-IIdentifyResource leaf sources, register the via-behavior via the explicit " +
                $"AddRelatedResourceAuthorization helper (which bypasses assembly scanning).");

        if (identifyIfaces.Count > 1)
            throw new InvalidOperationException(
                $"{messageType.FullName ?? messageType.Name} implements IAuthorizeResourceVia<{tOwner.Name}> " +
                $"and {identifyIfaces.Count} IIdentifyResource<,> interfaces — leaf selection " +
                $"would be ambiguous and could authorize the wrong resource chain. Candidates: " +
                string.Join(", ", identifyIfaces.Select(i =>
                    $"IIdentifyResource<{i.GetGenericArguments()[0].Name}, {i.GetGenericArguments()[1].Name}>")) +
                $". Declare exactly one IIdentifyResource<TLeaf, TLeafId> matching the via-authorization " +
                $"chain, or register the via-behavior via the explicit AddRelatedResourceAuthorization helper.");
    }

    /// <summary>
    /// Validates that a message-implemented <c>TResponse</c> can satisfy the
    /// resource-authorization behaviors' constraints (<see cref="IResult"/> +
    /// <see cref="IFailureFactory{TSelf}"/>). Throws <see cref="InvalidOperationException"/>
    /// with a diagnostic message naming the message type, the relevant authorization-marker
    /// interface (so the error points at the actual culprit for via-authorized commands),
    /// and the response type when the constraints are not met. Internal so the assembly
    /// scanner's fail-fast contract can be unit-tested without round-tripping through a
    /// synthetic assembly.
    /// </summary>
    /// <param name="messageType">The concrete message type discovered by the scanner.</param>
    /// <param name="resourceType">For <see cref="IAuthorizeResource{TResource}"/>: the closed <c>TResource</c>. For <see cref="IAuthorizeResourceVia{TOwner}"/>: the closed <c>TOwner</c>.</param>
    /// <param name="responseType">The closed response type from the message's
    /// <c>ICommand&lt;TResponse&gt;</c> / <c>IQuery&lt;TResponse&gt;</c> /
    /// <c>IRequest&lt;TResponse&gt;</c> interface.</param>
    /// <param name="markerInterfaceName">
    /// Display name of the security-marker interface the message implements — either
    /// <c>"IAuthorizeResource"</c> or <c>"IAuthorizeResourceVia"</c>. Used so the
    /// diagnostic points at the actual interface the consumer declared rather than
    /// always claiming <c>IAuthorizeResource</c>.
    /// </param>
    /// <param name="behaviorTypeName">
    /// Display name of the pipeline behavior that consumes this message — either
    /// <c>"ResourceAuthorizationBehavior"</c> or <c>"ResourceAuthorizationViaBehavior"</c>.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="responseType"/>
    /// does not implement <see cref="IResult"/> or <see cref="IFailureFactory{TSelf}"/>
    /// closed over itself.</exception>
    internal static void ValidateResourceAuthorizationResponseType(
        Type messageType,
        Type resourceType,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        Type responseType,
        string markerInterfaceName = "IAuthorizeResource",
        string behaviorTypeName = "ResourceAuthorizationBehavior<TMessage, TResource, TResponse>")
    {
        if (!typeof(IResult).IsAssignableFrom(responseType))
            throw new InvalidOperationException(
                $"{messageType.FullName ?? messageType.Name} implements {markerInterfaceName}<{resourceType.Name}> " +
                $"and {responseType.FullName ?? responseType.Name} via the message-marker interface, but " +
                $"{responseType.FullName ?? responseType.Name} does not implement IResult. " +
                $"{behaviorTypeName} requires TResponse : IResult, IFailureFactory<TResponse>. " +
                $"Use a result type that satisfies both constraints — e.g. Result<{resourceType.Name}>, Result<string>, Result<Unit>, " +
                $"or any other Result<T> the message handler can return; alternatively, remove {markerInterfaceName}<{resourceType.Name}> " +
                $"from the message.");

        // IFailureFactory<TSelf> is F-bounded (where TSelf : IFailureFactory<TSelf>), so we
        // can't use MakeGenericType(responseType).IsAssignableFrom(responseType) — that would
        // throw ArgumentException at MakeGenericType time when responseType doesn't satisfy
        // the constraint. Walk the implemented interfaces directly looking for a closed
        // IFailureFactory<X> where X == responseType.
        var implementsFailureFactory = false;
        foreach (var iface in responseType.GetInterfaces())
        {
            if (iface.IsGenericType
                && iface.GetGenericTypeDefinition() == typeof(IFailureFactory<>)
                && iface.GetGenericArguments()[0] == responseType)
            {
                implementsFailureFactory = true;
                break;
            }
        }

        if (!implementsFailureFactory)
            throw new InvalidOperationException(
                $"{messageType.FullName ?? messageType.Name} implements {markerInterfaceName}<{resourceType.Name}> " +
                $"and {responseType.FullName ?? responseType.Name} via the message-marker interface, but " +
                $"{responseType.FullName ?? responseType.Name} does not implement IFailureFactory<{responseType.Name}>. " +
                $"{behaviorTypeName} requires TResponse : IResult, IFailureFactory<TResponse>. " +
                $"Use a result type that satisfies both constraints — e.g. Result<{resourceType.Name}>, Result<string>, Result<Unit>, " +
                $"or any other Result<T> the message handler can return; alternatively, remove {markerInterfaceName}<{resourceType.Name}> " +
                $"from the message.");
    }
}