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
    ///   aggregating <see cref="Error.UnprocessableContent"/> failures into a single response.
    ///   External validation sources (e.g., the optional <c>Trellis.FluentValidation</c> package
    ///   contributes <c>FluentValidationMessageValidatorAdapter&lt;TMessage&gt;</c> via
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
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisBehaviors(this IServiceCollection services)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ExceptionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

    /// <summary>
    /// Registers the <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/>
    /// for a specific command/resource pair. Call once per command that implements
    /// <see cref="IAuthorizeResource{TResource}"/>.
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
    /// Prefer <see cref="AddResourceAuthorization(IServiceCollection, Assembly[])"/> for automatic
    /// discovery. Use this explicit overload for AOT/trimming scenarios where assembly scanning
    /// is not available.
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
    public static IServiceCollection AddResourceAuthorization<TMessage, TResource, TResponse>(
        this IServiceCollection services)
        where TMessage : IAuthorizeResource<TResource>, global::Mediator.IMessage
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        InsertResourceAuthorizationBehavior(
            services,
            ServiceDescriptor.Scoped<
                IPipelineBehavior<TMessage, TResponse>,
                ResourceAuthorizationBehavior<TMessage, TResource, TResponse>>());

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
    /// For each concrete type that implements <see cref="IAuthorizeResource{TResource}"/>,
    /// the method extracts <c>TResource</c> and resolves <c>TResponse</c> from
    /// <c>ICommand&lt;TResponse&gt;</c>, <c>IQuery&lt;TResponse&gt;</c>, or
    /// <c>IRequest&lt;TResponse&gt;</c>. It then registers the closed-generic
    /// <c>ResourceAuthorizationBehavior&lt;TMessage, TResource, TResponse&gt;</c>
    /// as <c>IPipelineBehavior&lt;TMessage, TResponse&gt;</c>.
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
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));

        var authorizeResourceDef = typeof(IAuthorizeResource<>);
        var loaderDef = typeof(IResourceLoader<,>);
        var sharedLoaderDef = typeof(SharedResourceLoaderById<,>);
        var identifyResourceDef = typeof(IIdentifyResource<,>);
        var adapterDef = typeof(SharedResourceLoaderAdapter<,,>);
        var behaviorDef = typeof(ResourceAuthorizationBehavior<,,>);
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

        foreach (var assembly in assemblies)
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

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

                // Register ResourceAuthorizationBehavior for IAuthorizeResource<TResource> commands
                var authIface = type.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == authorizeResourceDef);
                if (authIface is null)
                    continue;

                var commandResource = authIface.GetGenericArguments()[0];

                // Find TResponse from ICommand<TResponse>, IQuery<TResponse>, or IRequest<TResponse>
                var tResponse = type.GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Select(i => (iface: i, def: i.GetGenericTypeDefinition()))
                    .Where(x => messageInterfaces.Contains(x.def))
                    .Select(x => x.iface.GetGenericArguments()[0])
                    .FirstOrDefault();

                if (tResponse is null)
                    continue;

                // TResponse must satisfy the behavior's constraints: IResult + IFailureFactory<TResponse>
                if (!typeof(IResult).IsAssignableFrom(tResponse)
                    || !typeof(IFailureFactory<>).MakeGenericType(tResponse).IsAssignableFrom(tResponse))
                    continue;

                // Register ResourceAuthorizationBehavior<TMessage, TResource, TResponse>
                // as IPipelineBehavior<TMessage, TResponse>
                var closedBehavior = behaviorDef.MakeGenericType(type, commandResource, tResponse);
                var closedPipeline = pipelineDef.MakeGenericType(type, tResponse);
                InsertResourceAuthorizationBehavior(
                    services,
                    ServiceDescriptor.Scoped(closedPipeline, closedBehavior));

                // Check for IIdentifyResource<TResource, TId> for shared loader bridging
                var identifyIface = type.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType
                        && i.GetGenericTypeDefinition() == identifyResourceDef
                        && i.GetGenericArguments()[0] == commandResource);

                if (identifyIface is not null)
                {
                    commandsNeedingBridging.Add((type, commandResource, tResponse, identifyIface));
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

        return services;
    }

    private static void InsertResourceAuthorizationBehavior(
        IServiceCollection services,
        ServiceDescriptor descriptor)
    {
        var validationIndex = FindValidationBehaviorIndex(services);
        if (validationIndex >= 0)
        {
            services.Insert(validationIndex, descriptor);
            return;
        }

        services.Add(descriptor);
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
    /// The command type implementing <see cref="IAuthorizeResource{TResource}"/>
    /// and <see cref="IIdentifyResource{TResource, TId}"/>.
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
        where TMessage : IAuthorizeResource<TResource>, IIdentifyResource<TResource, TId>
    {
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
            return ex.Types.Where(t => t is not null).ToArray()!;
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
}