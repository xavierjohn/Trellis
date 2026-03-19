namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Extension methods for registering Trellis.Mediator pipeline behaviors.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Gets the ordered array of Trellis Result-aware pipeline behavior types.
    /// Assign this to <c>MediatorOptions.PipelineBehaviors</c> in your <c>AddMediator</c> call.
    /// <para>Behaviors execute in this order (outermost to innermost):</para>
    /// <list type="number">
    ///   <item><description><see cref="ExceptionBehavior{TMessage, TResponse}"/> — catches unhandled exceptions</description></item>
    ///   <item><description><see cref="TracingBehavior{TMessage, TResponse}"/> — OpenTelemetry activity span</description></item>
    ///   <item><description><see cref="LoggingBehavior{TMessage, TResponse}"/> — structured logging with duration</description></item>
    ///   <item><description><see cref="AuthorizationBehavior{TMessage, TResponse}"/> — checks static permissions (<see cref="IAuthorize"/>)</description></item>
    ///   <item><description><see cref="ValidationBehavior{TMessage, TResponse}"/> — short-circuits on validation failure</description></item>
    /// </list>
    /// <para>
    /// To add resource-based authorization with a loaded resource, call
    /// <see cref="AddResourceAuthorization(IServiceCollection, Assembly[])"/> to auto-discover commands
    /// implementing <see cref="IAuthorizeResource{TResource}"/>, or register per-command via
    /// <see cref="AddResourceAuthorization{TMessage, TResource, TResponse}"/>.
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
    /// as scoped services.
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
        var authorizeResourceDef = typeof(IAuthorizeResource<>);
        var loaderDef = typeof(IResourceLoader<,>);
        var behaviorDef = typeof(ResourceAuthorizationBehavior<,,>);
        var pipelineDef = typeof(IPipelineBehavior<,>);

        Type[] messageInterfaces =
        [
            typeof(global::Mediator.ICommand<>),
            typeof(global::Mediator.IQuery<>),
            typeof(global::Mediator.IRequest<>),
        ];

        foreach (var assembly in assemblies)
        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                continue;

            // Register IResourceLoader<,> implementations as scoped
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == loaderDef)
                    services.AddScoped(iface, type);
            }

            // Register ResourceAuthorizationBehavior for IAuthorizeResource<TResource> commands
            var authIface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == authorizeResourceDef);
            if (authIface is null)
                continue;

            var tResource = authIface.GetGenericArguments()[0];

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
            var closedBehavior = behaviorDef.MakeGenericType(type, tResource, tResponse);
            var closedPipeline = pipelineDef.MakeGenericType(type, tResponse);
            InsertResourceAuthorizationBehavior(
                services,
                ServiceDescriptor.Scoped(closedPipeline, closedBehavior));
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
                    services.AddScoped(iface, type);
            }
        }

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