namespace Trellis.Asp.Authorization;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// Extension methods for registering actor providers in ASP.NET Core DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ClaimsActorProvider"/> as the scoped <see cref="IActorProvider"/>
    /// with configurable claim mapping for any OIDC/JWT identity provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="ClaimsActorOptions"/>.
    /// Override <see cref="ClaimsActorOptions.ActorIdClaim"/> and
    /// <see cref="ClaimsActorOptions.PermissionsClaim"/> to match your token format.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <b>Replaces</b> any prior <see cref="IActorProvider"/> registration — actor-provider
    /// helpers do not stack. Pick one provider per environment (or wrap an inner provider
    /// with <see cref="AddCachingActorProvider{T}"/>); the last <c>AddXxxActorProvider</c> call wins.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Auth0
    /// builder.Services.AddClaimsActorProvider(opts =>
    /// {
    ///     opts.ActorIdClaim = "sub";
    ///     opts.PermissionsClaim = "permissions";
    /// });
    ///
    /// // Keycloak
    /// builder.Services.AddClaimsActorProvider(opts =>
    /// {
    ///     opts.ActorIdClaim = "sub";
    ///     opts.PermissionsClaim = "realm_access.roles";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddClaimsActorProvider(
        this IServiceCollection services,
        Action<ClaimsActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<ClaimsActorOptions>(_ => { });

        // Replace (not append): each AddXxxActorProvider helper claims the IActorProvider
        // slot. Without Replace, calling two helpers leaves two descriptors and
        // GetServices<IActorProvider>() returns both — order-dependent and surprising.
        services.Replace(ServiceDescriptor.Scoped<IActorProvider, ClaimsActorProvider>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="NestedJsonPathClaimsActorProvider"/> as the scoped
    /// <see cref="IActorProvider"/> with nested-JSON claim shape support (Auth0
    /// <c>app_metadata.roles</c>, Azure B2C <c>extension_*</c>, Okta nested claims).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="NestedJsonPathClaimsActorOptions"/>.
    /// Set <c>ContainerClaim</c> to the top-level claim that carries the JSON document,
    /// <c>ActorIdPath</c> / <c>PermissionsPath</c> to the dotted JSON paths inside it, and
    /// optionally the inherited flat <c>ActorIdClaim</c> / <c>PermissionsClaim</c> for the
    /// fallback when the container claim is absent or malformed.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <b>Replaces</b> any prior <see cref="IActorProvider"/> registration — actor-provider
    /// helpers do not stack. Pick one provider per environment (or wrap with
    /// <see cref="AddCachingActorProvider{T}"/>); the last <c>AddXxxActorProvider</c> call wins.
    /// <para>
    /// <b>Startup validation caveat.</b> The matching <c>IValidateOptions&lt;NestedJsonPathClaimsActorOptions&gt;</c>
    /// runs at host startup (via <c>ValidateOnStart</c>) regardless of whether a later
    /// <c>AddXxxActorProvider</c> call replaces <see cref="IActorProvider"/>. If a consumer
    /// configures invalid nested-path options (e.g. sets <c>ActorIdPath</c> or
    /// <c>PermissionsPath</c> without <c>ContainerClaim</c>) and then registers a different
    /// actor-provider, host startup still fails with
    /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>. Either fix the
    /// invalid options or remove the call entirely; the validator is permissive of default
    /// (unconfigured) options to avoid blocking the common "register then replace" pattern.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Auth0 — permissions under app_metadata.roles as a JSON array of strings.
    /// builder.Services.AddNestedJsonPathClaimsActorProvider(opts =>
    /// {
    ///     opts.ActorIdClaim = "sub";
    ///     opts.ContainerClaim = "app_metadata";
    ///     opts.PermissionsPath = "roles";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddNestedJsonPathClaimsActorProvider(
        this IServiceCollection services,
        Action<NestedJsonPathClaimsActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        services
            .AddOptions<NestedJsonPathClaimsActorOptions>()
            .Configure(configure ?? (_ => { }))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NestedJsonPathClaimsActorOptions>, NestedJsonPathClaimsActorOptionsValidator>());

        services.Replace(ServiceDescriptor.Scoped<IActorProvider, NestedJsonPathClaimsActorProvider>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="EntraActorProvider"/> as the scoped <see cref="IActorProvider"/>
    /// and configures <see cref="EntraActorOptions"/> with default Entra v2.0 claim mappings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="EntraActorOptions"/>.
    /// Override <see cref="EntraActorOptions.MapPermissions"/> to flatten roles into granular permissions,
    /// <see cref="EntraActorOptions.MapForbiddenPermissions"/> to populate deny lists, or
    /// <see cref="EntraActorOptions.MapAttributes"/> to add custom ABAC attributes.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <b>Replaces</b> any prior <see cref="IActorProvider"/> registration — actor-provider
    /// helpers do not stack. Pick one provider per environment (or wrap with
    /// <see cref="AddCachingActorProvider{T}"/> using <c>EntraActorProvider</c> as the inner type);
    /// the last <c>AddXxxActorProvider</c> call wins.
    /// </remarks>
    /// <example>
    /// <code>
    /// // RolePermissionProjection flattens role claims into permissions from an app-supplied
    /// // map, skipping any role the service does not recognize (no throw on an unmapped role).
    /// builder.Services.AddEntraActorProvider(options =>
    /// {
    ///     options.MapPermissions = RolePermissionProjection.ForRoleClaims(rolePermissionMap);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEntraActorProvider(
        this IServiceCollection services,
        Action<EntraActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<EntraActorOptions>(_ => { });

        services.Replace(ServiceDescriptor.Scoped<IActorProvider, EntraActorProvider>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="DevelopmentActorProvider"/> as the scoped <see cref="IActorProvider"/>
    /// for development and testing environments. Reads actor identity from the
    /// <c>X-Test-Actor</c> HTTP header and falls back to a configurable default actor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="DevelopmentActorOptions"/>.
    /// Set <see cref="DevelopmentActorOptions.DefaultPermissions"/> to grant permissions
    /// when no header is present, or set <see cref="DevelopmentActorOptions.ThrowOnMalformedHeader"/>
    /// to <see langword="false"/> to fall back to the default actor on a malformed header
    /// instead of rejecting it (rejection is the default).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Security:</b> The provider throws <see cref="InvalidOperationException"/> unconditionally
    /// when resolved outside of the Development environment, regardless of whether an
    /// <c>X-Test-Actor</c> header is present on the request. Use <see cref="AddEntraActorProvider"/>
    /// for production deployments.
    /// </para>
    /// <para>
    /// <b>Replaces</b> any prior <see cref="IActorProvider"/> registration — actor-provider
    /// helpers do not stack. Pick one provider per environment; the last
    /// <c>AddXxxActorProvider</c> call wins.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// if (builder.Environment.IsDevelopment())
    /// {
    ///     builder.Services.AddDevelopmentActorProvider(options =>
    ///     {
    ///         options.DefaultPermissions = new HashSet&lt;string&gt;
    ///         {
    ///             "orders:create", "orders:read"
    ///         };
    ///     });
    /// }
    /// else
    /// {
    ///     builder.Services.AddEntraActorProvider();
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddDevelopmentActorProvider(
        this IServiceCollection services,
        Action<DevelopmentActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();
        services.AddLogging();

        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<DevelopmentActorOptions>(_ => { });

        services.Replace(ServiceDescriptor.Scoped<IActorProvider, DevelopmentActorProvider>());

        return services;
    }

    /// <summary>
    /// Registers a caching decorator around the specified <see cref="IActorProvider"/> implementation.
    /// The inner provider is resolved per-scope and wrapped with <see cref="CachingActorProvider"/>
    /// so that multiple calls within the same request return the same actor instance.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="IActorProvider"/> implementation to cache.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Replaces</b> the <see cref="IActorProvider"/> slot with the caching wrapper. The inner
    /// type <typeparamref name="T"/> is registered via <c>TryAddScoped&lt;T&gt;()</c>, so repeated
    /// calls do not accumulate inner-provider descriptors.
    /// </para>
    /// <para>
    /// <b>Inner-provider dependencies are NOT auto-registered.</b> This helper only adds
    /// <typeparamref name="T"/> itself and <c>IHttpContextAccessor</c>. If <typeparamref name="T"/>
    /// is one of the built-in providers (<see cref="EntraActorProvider"/>,
    /// <see cref="ClaimsActorProvider"/>) or a subclass thereof, you must first call the matching
    /// <c>AddXxxActorProvider(...)</c> helper so its <c>IOptions&lt;TOptions&gt;</c> is configured.
    /// The matching helper's <c>Replace</c> on the <see cref="IActorProvider"/> slot is then
    /// overwritten by this caching wrap — that is the intended composition order.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Pattern B(i): Custom provider with no Trellis-managed options dependencies.
    /// services.AddCachingActorProvider&lt;DatabaseActorProvider&gt;();
    ///
    /// // Pattern B(ii): Built-in provider — register the inner provider's options
    /// // helper FIRST so IOptions&lt;EntraActorOptions&gt; is configured, then wrap.
    /// services.AddEntraActorProvider(o => o.MapPermissions = ...);
    /// services.AddCachingActorProvider&lt;EntraActorProvider&gt;();
    ///
    /// // Pattern B(iii): Subclass of ClaimsActorProvider (e.g. KeycloakActorProvider).
    /// // Inner subclass shares ClaimsActorOptions, so AddClaimsActorProvider(...)
    /// // configures IOptions&lt;ClaimsActorOptions&gt; for the subclass too.
    /// services.AddClaimsActorProvider(o => o.ActorIdClaim = "sub");
    /// services.AddCachingActorProvider&lt;KeycloakActorProvider&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddCachingActorProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IActorProvider
    {
        services.AddHttpContextAccessor();
        // TryAddScoped (not AddScoped): repeated AddCachingActorProvider<T>() calls
        // (e.g. library + application) must not accumulate duplicate scoped descriptors
        // for the inner provider type. The IActorProvider slot is already idempotent via
        // the Replace below.
        services.TryAddScoped<T>();
        services.Replace(ServiceDescriptor.Scoped<IActorProvider>(sp =>
            new CachingActorProvider(
                sp.GetRequiredService<T>(),
                sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>())));

        return services;
    }

    /// <summary>
    /// Wraps the currently-registered <see cref="IActorProvider"/> so it composes cleanly
    /// across HTTP requests and non-HTTP execution (typically a <see cref="BackgroundService"/>
    /// tick). When the wrapper resolves outside an HTTP request scope (no
    /// <c>HttpContext</c>), it returns <paramref name="systemActor"/>; inside an HTTP request
    /// scope it delegates to the inner HTTP-side provider, preserving the standard
    /// claims/bearer/header-derived actor resolution and the
    /// <c>Maybe.None</c>-on-anonymous → HTTP 401 contract.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="systemActor">
    /// The actor returned for non-HTTP (worker / hosted-service) execution. Construct with
    /// the permissions the worker needs to issue mediator commands and queries
    /// (e.g. <c>new Actor("system", new HashSet&lt;string&gt; { "reminders:dispatch" }, ...)</c>).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="systemActor"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no prior <see cref="IActorProvider"/> registration exists, when more than
    /// one prior <see cref="IActorProvider"/> descriptor is present (registration order is
    /// ambiguous), or when <c>AddTrellisWorkerActor</c> has already been called on this
    /// collection. A separate <c>IHostedLifecycleService</c> validator
    /// (<see cref="WorkerActorRegistrationValidator"/>) also runs at host start
    /// (in <c>StartingAsync</c>, before any <c>IHostedService.StartAsync</c>) and throws if
    /// a later registration overwrote the worker composition.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Order matters.</b> Call <c>AddTrellisWorkerActor</c> AFTER the HTTP-side actor
    /// provider helper (<see cref="AddClaimsActorProvider"/>, <see cref="AddEntraActorProvider"/>,
    /// <see cref="AddDevelopmentActorProvider"/>) and AFTER any
    /// <see cref="AddCachingActorProvider{T}"/> wrap. The helper captures the
    /// <see cref="IActorProvider"/> slot present at call time and re-registers a wrapper
    /// that delegates to it; calling
    /// <c>services.AddXxxActorProvider</c> or <c>services.AddScoped&lt;IActorProvider, ...&gt;()</c>
    /// after this helper will silently overwrite the wrapper — background-worker ticks would
    /// then resolve whatever the new provider returns instead of the configured system actor
    /// (typically <see cref="Maybe{T}.None"/> in a tick context, surfacing as
    /// <see cref="Error.AuthenticationRequired"/> on the first dispatched command). The
    /// validator catches that case at host start.
    /// </para>
    /// <para>
    /// <b>Single-registration contract.</b> Calling <c>AddTrellisWorkerActor</c> twice
    /// throws — wrapping a wrapper would mean a worker scope returns the inner wrapper's
    /// system actor instead of the outer's, which is never the intended composition.
    /// </para>
    /// <para>
    /// <b>Lifetime.</b> The wrapper is registered as scoped (matching the other built-in
    /// actor providers). Built-in providers (Claims / Entra / Development / Caching) are
    /// scoped, so the typical wiring preserves the inner's lifetime semantics exactly. The
    /// inner provider is materialized lazily on first access via a thread-safe
    /// <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>
    /// — worker ticks (HttpContext null) never construct it.
    /// </para>
    /// <para>
    /// <b>Inner-descriptor support matrix.</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Scoped</b> via implementation type or factory: supported. Wrapper owns the
    ///     materialized inner and disposes it (sync or async) when the wrapper scope ends.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Singleton via <see cref="ServiceDescriptor.ImplementationInstance"/></b>:
    ///     supported. Wrapper delegates without re-materializing and does not dispose the
    ///     instance (the consumer owns app-lifetime disposal).
    ///   </description></item>
    ///   <item><description>
    ///     <b>Singleton via implementation type or factory</b>: <b>rejected at registration
    ///     time</b> — the wrapper is scoped, so materializing per wrapper scope would
    ///     silently break the singleton invariant (N instances instead of 1). The helper
    ///     throws with guidance to either re-register the provider as scoped or supply a
    ///     pre-constructed instance via
    ///     <c>services.AddSingleton&lt;IActorProvider&gt;(instance)</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Transient via implementation type or factory</b>: <b>rejected at registration
    ///     time</b> — the wrapper is scoped, so a transient inner would silently become
    ///     scoped-per-wrapper (one instance reused per scope rather than a fresh instance per
    ///     resolution). The helper throws with guidance to re-register as scoped.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Keyed registrations</b> are ignored — the helper inspects and replaces only the
    ///     unkeyed <see cref="IActorProvider"/> slot.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <b>Decoration awareness.</b> The wrapper implements
    /// <see cref="IProvideActorVaryHeaders"/> (delegating to the inner provider's headers)
    /// and <see cref="IDecoratingActorProvider"/> (so
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> diagnostics name the
    /// underlying HTTP-side provider rather than the worker wrapper).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddClaimsActorProvider(opts => opts.ActorIdClaim = "sub");
    /// builder.Services.AddTrellisWorkerActor(new Actor(
    ///     id: "system",
    ///     permissions: new HashSet&lt;string&gt; { "reminders:dispatch", "reminders:read" },
    ///     forbiddenPermissions: new HashSet&lt;string&gt;(),
    ///     attributes: new Dictionary&lt;string, string&gt;()));
    ///
    /// builder.Services.AddHostedService&lt;ReminderDispatchWorker&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisWorkerActor(
        this IServiceCollection services,
        Actor systemActor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(systemActor);

        // Sentinel-based duplicate detection: avoids invoking the existing IActorProvider
        // factory at registration time (which can fail when its dependencies haven't been
        // registered yet, or cause unintended side effects).
        if (services.Any(d => d.ServiceType == typeof(WorkerActorRegistrationMarker)))
        {
            throw new InvalidOperationException(
                "AddTrellisWorkerActor has already been called on this service collection. " +
                "Wrapping the wrapper is never the intended composition; remove the duplicate call.");
        }

        var existing = services
            .Where(d => d.ServiceType == typeof(IActorProvider) && !d.IsKeyedService)
            .ToList();

        if (existing.Count == 0)
        {
            throw new InvalidOperationException(
                "AddTrellisWorkerActor requires a prior unkeyed IActorProvider registration. " +
                "Call AddClaimsActorProvider, AddEntraActorProvider, or AddDevelopmentActorProvider first, " +
                "then AddTrellisWorkerActor to compose worker-context (HttpContext-null) handling on top.");
        }

        if (existing.Count > 1)
        {
            throw new InvalidOperationException(
                $"AddTrellisWorkerActor requires exactly one prior unkeyed IActorProvider registration but found {existing.Count}. " +
                "Resolve the ambiguity by removing the duplicate IActorProvider descriptors before calling AddTrellisWorkerActor. " +
                "The built-in AddXxxActorProvider helpers use Replace, so chaining them yields a single descriptor; multiple " +
                "descriptors only arise when a consumer manually appends with services.AddScoped<IActorProvider>() / " +
                "services.AddTransient<IActorProvider>() / services.AddSingleton<IActorProvider>() (which append rather than Replace). " +
                "Keyed IActorProvider registrations are ignored and remain untouched.");
        }

        services.AddHttpContextAccessor();

        // Fail fast on lifetime shapes that would silently change semantics under wrapping.
        // The wrapper is scoped, so materializing the inner via factory/type per wrapper scope
        // would silently convert:
        //   - Singleton-via-type/factory → scoped-per-wrapper (N inner instances instead of 1,
        //     breaking app-wide singleton invariants like cached config or connection handles).
        //   - Transient-via-type/factory → scoped-per-wrapper (one inner instance reused for
        //     every call within a scope instead of a fresh instance per resolution).
        // Singleton-via-ImplementationInstance is safe (the instance round-trips verbatim and
        // the wrapper does not own its disposal). Scoped descriptors are the canonical shape
        // and match the built-in AddXxxActorProvider helpers.
        var innerDescriptor = existing[0];
        if (innerDescriptor.Lifetime == ServiceLifetime.Singleton && innerDescriptor.ImplementationInstance is null)
        {
            throw new InvalidOperationException(
                "AddTrellisWorkerActor cannot wrap a singleton-lifetime IActorProvider registered via " +
                "ImplementationType or ImplementationFactory because the worker-composed wrapper is scoped, " +
                "and materializing the inner per wrapper scope would silently break the singleton invariant " +
                "(N inner instances instead of 1). Either register the inner provider as scoped (matching the " +
                "built-in AddClaimsActorProvider / AddEntraActorProvider / AddDevelopmentActorProvider helpers), " +
                "or register a pre-constructed singleton instance via services.AddSingleton<IActorProvider>(instance) " +
                "so the wrapper can delegate to it without re-materializing.");
        }

        if (innerDescriptor.Lifetime == ServiceLifetime.Transient)
        {
            throw new InvalidOperationException(
                "AddTrellisWorkerActor cannot wrap a transient-lifetime IActorProvider because the worker-composed " +
                "wrapper is scoped and would silently convert transient semantics into scoped semantics (one inner " +
                "instance reused for every call within a wrapper scope instead of a fresh instance per resolution). " +
                "Re-register the inner provider as scoped (matching the built-in AddClaimsActorProvider / " +
                "AddEntraActorProvider / AddDevelopmentActorProvider helpers) before calling AddTrellisWorkerActor.");
        }

        // Materialize the inner descriptor lazily, per wrapper scope. Built-in providers are
        // scoped with standard DI-constructor dependencies, so ActivatorUtilities
        // materialization matches the original scoped semantics. Factory and instance shapes
        // round-trip verbatim. The wrapper owns disposal of inners it constructed (scoped
        // factory / scoped type descriptors); singleton instances supplied by the consumer
        // are not disposed.
        var innerFactory = BuildInnerFactory(innerDescriptor);
        var ownsInner = innerDescriptor.ImplementationInstance is null;

        // Remove the exact captured descriptor (preserves any keyed IActorProvider
        // registrations untouched) and append the wrapper.
        services.Remove(innerDescriptor);
        services.Add(ServiceDescriptor.Scoped<IActorProvider>(sp =>
        {
            var http = sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var logger = sp.GetService<ILogger<WorkerComposedActorProvider>>();
            return new WorkerComposedActorProvider(() => innerFactory(sp), ownsInner, http, systemActor, logger);
        }));

        services.AddSingleton<WorkerActorRegistrationMarker>();

        // Hosted-service validator catches "AddXxxActorProvider called AFTER AddTrellisWorkerActor"
        // at host start, before any HTTP request or worker tick observes the broken composition.
        // TryAdd (not Add): repeated AddTrellisWorkerActor calls would already throw above, so
        // this guards the unrelated case where the type was added by another wiring path.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkerActorRegistrationValidator>());

        return services;
    }

    private static Func<IServiceProvider, IActorProvider> BuildInnerFactory(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IActorProvider singletonInstance)
            return _ => singletonInstance;

        if (descriptor.ImplementationFactory is { } factory)
            return sp => (IActorProvider)factory(sp);

        if (descriptor.ImplementationType is { } implType)
            return sp => (IActorProvider)ActivatorUtilities.CreateInstance(sp, implType);

        throw new InvalidOperationException(
            "AddTrellisWorkerActor cannot materialize the existing IActorProvider descriptor — " +
            "it has neither ImplementationType, ImplementationFactory, nor ImplementationInstance.");
    }
}