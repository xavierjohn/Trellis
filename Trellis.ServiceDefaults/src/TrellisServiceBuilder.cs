namespace Trellis.ServiceDefaults;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::FluentValidation;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Idempotency;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

/// <summary>
/// Collects requested Trellis integration modules and applies them in canonical order.
/// </summary>
/// <remarks>
/// Callers do not invoke this builder directly. Use
/// <see cref="TrellisServiceCollectionExtensions.AddTrellis(IServiceCollection, Action{TrellisServiceBuilder})"/>;
/// it constructs a <see cref="TrellisServiceBuilder"/>, hands it to the configure callback, and
/// invokes <c>Apply()</c> after the callback returns to register the selected modules in the
/// canonical pipeline order.
/// </remarks>
public sealed class TrellisServiceBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Assembly> _fluentValidationAssemblies = [];
    private readonly List<Assembly> _resourceAuthorizationAssemblies = [];
    private readonly List<Assembly> _domainEventAssemblies = [];
    private readonly List<Action<IServiceCollection>> _typedFluentValidatorRegistrations = [];
    private readonly List<Action<IServiceCollection>> _typedResourceAuthorizationRegistrations = [];
    private readonly List<Action<IServiceCollection>> _typedDomainEventHandlerRegistrations = [];
    private Action<TrellisAspOptions>? _configureAsp;
    private Action<TrellisMediatorTelemetryOptions>? _configureMediatorTelemetry;
    private Action<IServiceCollection>? _actorProviderRegistration;
    private Action<IServiceCollection>? _cachingActorProviderWrap;
    private Action<IServiceCollection>? _workerActorWrap;
    private Action<IServiceCollection>? _unitOfWorkRegistration;
    private bool _useAsp;
    private bool _useScalarValueValidation;
    private bool _useProblemDetails;
    private bool _useIdempotency;
    private Action<Trellis.Asp.Idempotency.IdempotencyOptions>? _configureIdempotency;
    private bool _useMediator;
    private bool _useFluentValidation;
    private bool _useResourceAuthorization;
    private bool _useDomainEvents;
    private bool _useTrackedAggregateDomainEvents;
    private ActorProviderKind _actorProviderKind;

    internal TrellisServiceBuilder(IServiceCollection services) =>
        _services = services;

    /// <summary>
    /// Registers Trellis ASP.NET Core integration (error-to-status-code mapping and the
    /// <see cref="ResourceCollectionNameRegistry"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calling this method more than once is allowed; the configure delegates are composed in
    /// call order rather than overwriting. Each delegate runs against the same
    /// <see cref="TrellisAspOptions"/> instance, so layered configuration (e.g. a library
    /// applies defaults; the host overrides specific properties) is supported.
    /// </para>
    /// <para>
    /// <b>Does NOT register scalar-value validation.</b> Scalar-value validation mutates
    /// global <c>MvcOptions</c> / <c>JsonOptions</c> (model binders, JSON converters). If
    /// your endpoints bind request bodies / route / query containing Trellis value objects
    /// (<see cref="IScalarValue{TSelf, TPrimitive}"/>, <see cref="Maybe{T}"/>), additionally
    /// call <see cref="UseScalarValueValidation"/>. The two slots are independent so the
    /// global-options mutation is opt-in instead of silent.
    /// </para>
    /// </remarks>
    public TrellisServiceBuilder UseAsp(Action<TrellisAspOptions>? configure = null)
    {
        _useAsp = true;
        if (configure is not null)
            _configureAsp = Combine(_configureAsp, configure);

        return this;
    }

    /// <summary>
    /// Registers Trellis scalar-value validation: configures both MVC and Minimal API JSON
    /// pipelines (model binders, JSON converters, <c>SuppressModelStateInvalidFilter</c>
    /// toggle) so <see cref="IScalarValue{TSelf, TPrimitive}"/> / <see cref="Maybe{T}"/>
    /// validation surfaces as RFC 9457 ProblemDetails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mutates global <c>MvcOptions</c> and <c>JsonOptions</c>. Independent of
    /// <see cref="UseAsp(Action{TrellisAspOptions}?)"/>: hosts that only need
    /// error-to-status-code mapping (e.g. an MVC site that does not bind value-object DTOs)
    /// can call <c>UseAsp()</c> without paying for the binder / converter mutation.
    /// </para>
    /// <para>
    /// <strong>Minimal API endpoint filter and middleware are NOT registered by this slot.</strong>
    /// Minimal API hosts must additionally call <c>app.UseScalarValueValidation()</c>
    /// (middleware) and chain <c>.WithScalarValueValidation()</c> on each endpoint that
    /// should surface validation as RFC 9457 ProblemDetails.
    /// </para>
    /// <para>
    /// Idempotent: multiple calls register the underlying services exactly once. Equivalent
    /// to calling <c>services.AddScalarValueValidation()</c> directly.
    /// </para>
    /// </remarks>
    public TrellisServiceBuilder UseScalarValueValidation()
    {
        _useScalarValueValidation = true;
        return this;
    }

    /// <summary>
    /// Registers Trellis ProblemDetails customization (traceId on every error, 405 Allow
    /// header projected as <c>extensions.allow</c>, 500 detail rewrite).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Independent of <see cref="UseAsp(Action{TrellisAspOptions}?)"/>: ProblemDetails
    /// customization layers on top of ASP.NET Core's <c>IProblemDetailsService</c> and does
    /// not pull in Trellis MVC/result-mapping infrastructure.
    /// </para>
    /// <para>
    /// Idempotent across direct + builder composition: calling
    /// <c>services.AddTrellisProblemDetails()</c> directly and also opting in via
    /// <c>options.UseProblemDetails()</c> registers exactly one Trellis post-configure
    /// layer, so <c>traceId</c> / <c>allow</c> extensions are not duplicated.
    /// </para>
    /// </remarks>
    public TrellisServiceBuilder UseProblemDetails()
    {
        _useProblemDetails = true;
        return this;
    }

    /// <summary>
    /// Registers the Trellis Idempotency-Key middleware and its supporting services.
    /// </summary>
    /// <param name="configure">Optional callback to mutate <c>IdempotencyOptions</c>.</param>
    /// <remarks>
    /// <para>
    /// Endpoints opt in to idempotency by carrying <c>IdempotentAttribute</c> metadata; the
    /// middleware is a no-op on endpoints that do not opt in. Application code is still
    /// responsible for mounting the middleware in the request pipeline via
    /// <c>app.UseTrellisIdempotency()</c> and for registering a store (for example
    /// <c>services.AddInMemoryIdempotencyStore()</c>).
    /// </para>
    /// <para>
    /// Calling this method more than once is allowed; the configure delegates are composed in
    /// call order rather than overwriting, so a library that applies defaults and a host that
    /// tweaks individual properties can both call <c>UseIdempotency(...)</c> without either
    /// erasing the other's configuration.
    /// </para>
    /// </remarks>
    public TrellisServiceBuilder UseIdempotency(Action<Trellis.Asp.Idempotency.IdempotencyOptions>? configure = null)
    {
        _useIdempotency = true;
        if (configure is not null)
            _configureIdempotency = Combine(_configureIdempotency, configure);

        return this;
    }

    /// <summary>
    /// Registers the Trellis Mediator pipeline behaviors.
    /// </summary>
    /// <remarks>
    /// Calling this method more than once is allowed; the configure delegates are composed in
    /// call order rather than overwriting.
    /// </remarks>
    public TrellisServiceBuilder UseMediator(Action<TrellisMediatorTelemetryOptions>? configureTelemetry = null)
    {
        _useMediator = true;
        if (configureTelemetry is not null)
            _configureMediatorTelemetry = Combine(_configureMediatorTelemetry, configureTelemetry);

        return this;
    }

    /// <summary>
    /// Registers the FluentValidation adapter and optionally scans validator assemblies.
    /// Implies <see cref="UseMediator"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calling without arguments registers only the adapter (AOT-friendly); pair with
    /// <see cref="UseFluentValidation{TValidator, TMessage}()"/> or per-validator
    /// <c>services.AddScoped&lt;IValidator&lt;TMessage&gt;, TValidator&gt;()</c> calls.
    /// </para>
    /// <para>
    /// Calling with one or more assemblies scans them for concrete <see cref="IValidator{T}"/>
    /// implementations at startup; the scan uses reflection and is therefore not AOT- or
    /// trim-safe. The <see cref="RequiresUnreferencedCodeAttribute"/> and
    /// <see cref="RequiresDynamicCodeAttribute"/> annotations on this overload surface the
    /// limitation at the consumer's call site when the AOT analyzer is enabled.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Scans assemblies for IValidator<T> implementations. Use UseFluentValidation() with UseFluentValidation<TValidator, TMessage>() for AOT/trim scenarios.")]
    [RequiresDynamicCode("Constructs closed generic IValidator<T> service types at runtime. Use UseFluentValidation() with UseFluentValidation<TValidator, TMessage>() for AOT scenarios.")]
    public TrellisServiceBuilder UseFluentValidation(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length > 0)
            AddAssemblies(_fluentValidationAssemblies, assemblies, nameof(assemblies));

        _useFluentValidation = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// Registers only the FluentValidation adapter (no assembly scanning). AOT- and trim-safe.
    /// Pair with <see cref="UseFluentValidation{TValidator, TMessage}()"/> per validator, or with
    /// explicit <c>services.AddScoped&lt;IValidator&lt;TMessage&gt;, TValidator&gt;()</c> calls
    /// outside the builder. Implies <see cref="UseMediator"/>.
    /// </summary>
    public TrellisServiceBuilder UseFluentValidation()
    {
        _useFluentValidation = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// AOT-safe per-validator registration. Registers <typeparamref name="TValidator"/> as the
    /// <see cref="IValidator{T}"/> for <typeparamref name="TMessage"/> and ensures the
    /// FluentValidation adapter and Mediator behaviors are wired. Implies <see cref="UseMediator"/>.
    /// </summary>
    /// <typeparam name="TValidator">The concrete validator type implementing <see cref="IValidator{TMessage}"/>.</typeparam>
    /// <typeparam name="TMessage">The message type the validator validates.</typeparam>
    public TrellisServiceBuilder UseFluentValidation<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator,
        TMessage>()
        where TValidator : class, IValidator<TMessage>
    {
        _useFluentValidation = true;
        _useMediator = true;
        // Dedup via TryAddEnumerable so repeated calls (e.g. composed from multiple modules)
        // do not register the same validator twice. FluentValidation resolves every
        // IValidator<TMessage> registration during ValidationBehavior, so duplicates would
        // execute the same rules N times per request.
        _typedFluentValidatorRegistrations.Add(static services =>
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<TMessage>, TValidator>()));
        return this;
    }

    /// <summary>
    /// Registers resource authorization behaviors and resource loaders discovered in assemblies.
    /// Implies <see cref="UseMediator"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passing no assemblies keeps the resource-authorization pipeline available for explicit
    /// registrations without performing assembly scanning. Use this overload + the
    /// <see cref="UseResourceAuthorization{TMessage, TResource, TResponse}()"/> per-command
    /// typed overload for AOT/trim scenarios.
    /// </para>
    /// <para>
    /// Calling with one or more assemblies scans them at startup; the scan uses reflection and
    /// is not AOT- or trim-safe. The annotation surfaces the limitation at the call site.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Scans assemblies for IAuthorizeResource<TResource> command types. Use UseResourceAuthorization() with UseResourceAuthorization<TMessage, TResource, TResponse>() for AOT/trim scenarios.")]
    [RequiresDynamicCode("Constructs closed generic ResourceAuthorizationBehavior<,,> service types at runtime. Use UseResourceAuthorization() with UseResourceAuthorization<TMessage, TResource, TResponse>() for AOT scenarios.")]
    public TrellisServiceBuilder UseResourceAuthorization(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length > 0)
            AddAssemblies(_resourceAuthorizationAssemblies, assemblies, nameof(assemblies));

        _useResourceAuthorization = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// Enables resource authorization without assembly scanning. AOT- and trim-safe. Pair with
    /// <see cref="UseResourceAuthorization{TMessage, TResource, TResponse}()"/> per command, or
    /// with explicit <c>services.AddResourceAuthorization&lt;TMessage, TResource, TResponse&gt;()</c>
    /// calls outside the builder. Implies <see cref="UseMediator"/>.
    /// </summary>
    public TrellisServiceBuilder UseResourceAuthorization()
    {
        _useResourceAuthorization = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// AOT-safe per-command resource-authorization registration. Registers the closed-generic
    /// <c>ResourceAuthorizationBehavior&lt;TMessage, TResource, TResponse&gt;</c> for the named
    /// command and ensures the resource-authorization pipeline is enabled. Implies
    /// <see cref="UseMediator"/>.
    /// </summary>
    /// <typeparam name="TMessage">The command type implementing <see cref="IAuthorizeResource{TResource}"/>.</typeparam>
    /// <typeparam name="TResource">The resource type the command authorizes against.</typeparam>
    /// <typeparam name="TResponse">The command's response type.</typeparam>
    public TrellisServiceBuilder UseResourceAuthorization<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TMessage,
        TResource,
        TResponse>()
        where TMessage : IAuthorizeResource<TResource>, IMessage
        where TResource : class
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        _useResourceAuthorization = true;
        _useMediator = true;
        // AddResourceAuthorization<,,>() is fully idempotent (InsertResourceAuthorizationBehavior
        // dedupes by ServiceType+ImplementationType; the IAuthorizedResource accessor is
        // registered via TryAddScoped which is also idempotent). Call unconditionally so a
        // pre-existing behavior descriptor (registered by another module) doesn't cause this
        // helper to silently skip the v4 accessor registration that handlers depend on.
        _typedResourceAuthorizationRegistrations.Add(static services =>
            services.AddResourceAuthorization<TMessage, TResource, TResponse>());
        return this;
    }

    /// <summary>
    /// Registers <see cref="ClaimsActorProvider"/> as the scoped actor provider.
    /// </summary>
    public TrellisServiceBuilder UseClaimsActorProvider(Action<ClaimsActorOptions>? configure = null)
    {
        SetActorProvider(ActorProviderKind.Claims, services => services.AddClaimsActorProvider(configure));
        return this;
    }

    /// <summary>
    /// Registers <see cref="EntraActorProvider"/> as the scoped actor provider.
    /// </summary>
    public TrellisServiceBuilder UseEntraActorProvider(Action<EntraActorOptions>? configure = null)
    {
        SetActorProvider(ActorProviderKind.Entra, services => services.AddEntraActorProvider(configure));
        return this;
    }

    /// <summary>
    /// Registers <see cref="DevelopmentActorProvider"/> as the scoped actor provider.
    /// </summary>
    public TrellisServiceBuilder UseDevelopmentActorProvider(Action<DevelopmentActorOptions>? configure = null)
    {
        SetActorProvider(ActorProviderKind.Development, services => services.AddDevelopmentActorProvider(configure));
        return this;
    }

    /// <summary>
    /// Registers <see cref="NestedJsonPathClaimsActorProvider"/> as the scoped
    /// <see cref="IActorProvider"/> with nested-JSON claim shape support (Auth0
    /// <c>app_metadata.roles</c>, Azure B2C <c>extension_*</c>, Okta nested claims).
    /// </summary>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="NestedJsonPathClaimsActorOptions"/>.
    /// Set <c>ContainerClaim</c> to the top-level claim that carries the JSON document,
    /// <c>ActorIdPath</c> / <c>PermissionsPath</c> to the dotted JSON paths inside it,
    /// and the inherited flat <c>ActorIdClaim</c> / <c>PermissionsClaim</c> for the fallback
    /// when the container claim is absent or malformed.
    /// </param>
    /// <remarks>
    /// Mutually exclusive with the other actor-provider selectors.
    /// </remarks>
    public TrellisServiceBuilder UseNestedJsonPathClaimsActorProvider(Action<NestedJsonPathClaimsActorOptions>? configure = null)
    {
        SetActorProvider(ActorProviderKind.NestedJsonPathClaims, services => services.AddNestedJsonPathClaimsActorProvider(configure));
        return this;
    }

    /// <summary>
    /// Registers EF Core Unit of Work and the transactional command behavior.
    /// Implies <see cref="UseMediator"/> and is always applied after all other behavior registrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calling this method more than once — with the same <typeparamref name="TContext"/> or
    /// a different one — throws <see cref="InvalidOperationException"/>. The Trellis pipeline
    /// supports exactly one transactional <see cref="IUnitOfWork"/> per composition; chaining
    /// two calls (e.g. for a read/write context split) is always misconfiguration.
    /// </para>
    /// <para>
    /// This method is annotated <see cref="RequiresUnreferencedCodeAttribute"/> /
    /// <see cref="RequiresDynamicCodeAttribute"/> because the underlying
    /// <c>Trellis.EntityFrameworkCore</c> package is intentionally opted out of AOT and trim
    /// (EF Core's own runtime requires reflection over entity types and query expression trees).
    /// AOT consumers can still register Trellis ASP / Mediator / FluentValidation through this
    /// builder; the unit-of-work slot is the seam where the AOT contract ends.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Trellis.EntityFrameworkCore is not AOT- or trim-compatible because EF Core requires reflection over entity types and query expression trees. AOT consumers should compose their data access layer outside of this builder.")]
    [RequiresDynamicCode("Trellis.EntityFrameworkCore is not AOT-compatible because EF Core requires runtime code generation for query compilation. AOT consumers should compose their data access layer outside of this builder.")]
    public TrellisServiceBuilder UseEntityFrameworkUnitOfWork<TContext>()
        where TContext : DbContext
    {
        if (_unitOfWorkRegistration is not null)
            throw new InvalidOperationException(
                "Only one unit of work can be configured per Trellis composition.");

        _useMediator = true;
        _unitOfWorkRegistration = services => services.AddTrellisUnitOfWork<TContext>();
        return this;
    }

    /// <summary>
    /// Registers the domain-event dispatch behavior and (optionally) scans assemblies for
    /// <see cref="IDomainEventHandler{TEvent}"/> implementations. Implies <see cref="UseMediator"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passing no assemblies registers the behavior + default <see cref="IDomainEventPublisher"/>
    /// without scanning; pair with explicit
    /// <c>services.AddDomainEventHandler&lt;TEvent, THandler&gt;()</c> calls or the typed builder
    /// overload <see cref="UseDomainEvents{TEvent, THandler}()"/> (AOT-friendly). The dispatch
    /// behavior runs after <c>ValidationBehavior</c> and before <c>TransactionalCommandBehavior</c>
    /// in the pipeline, so events fire after the transaction commits.
    /// </para>
    /// <para>
    /// Calling with one or more assemblies scans them at startup; the scan uses reflection and
    /// is not AOT- or trim-safe.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Scans assemblies for IDomainEventHandler<TEvent> implementations. Use UseDomainEvents() with UseDomainEvents<TEvent, THandler>() for AOT/trim scenarios.")]
    [RequiresDynamicCode("Constructs closed generic IDomainEventHandler<TEvent> service types at runtime. Use UseDomainEvents() with UseDomainEvents<TEvent, THandler>() for AOT scenarios.")]
    public TrellisServiceBuilder UseDomainEvents(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (_useTrackedAggregateDomainEvents)
            throw new InvalidOperationException(
                "UseDomainEvents and UseTrackedAggregateDomainEvents are mutually exclusive. " +
                "Pick the response-shape dispatch (UseDomainEvents) or the tracked-aggregate " +
                "auto-dispatch (UseTrackedAggregateDomainEvents), not both.");

        if (assemblies.Length > 0)
            AddAssemblies(_domainEventAssemblies, assemblies, nameof(assemblies));

        _useDomainEvents = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// Registers the domain-event dispatch behavior without assembly scanning. AOT- and
    /// trim-safe. Pair with <see cref="UseDomainEvents{TEvent, THandler}()"/> per handler, or
    /// with explicit <c>services.AddDomainEventHandler&lt;TEvent, THandler&gt;()</c> calls
    /// outside the builder. Implies <see cref="UseMediator"/>. Mutually exclusive with
    /// <see cref="UseTrackedAggregateDomainEvents()"/>.
    /// </summary>
    public TrellisServiceBuilder UseDomainEvents()
    {
        if (_useTrackedAggregateDomainEvents)
            throw new InvalidOperationException(
                "UseDomainEvents and UseTrackedAggregateDomainEvents are mutually exclusive. " +
                "Pick the response-shape dispatch (UseDomainEvents) or the tracked-aggregate " +
                "auto-dispatch (UseTrackedAggregateDomainEvents), not both.");

        _useDomainEvents = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// AOT-safe per-handler domain-event registration. Registers <typeparamref name="THandler"/>
    /// as an <see cref="IDomainEventHandler{TEvent}"/> for <typeparamref name="TEvent"/> and
    /// ensures the dispatch behavior is wired. Implies <see cref="UseMediator"/>. Mutually
    /// exclusive with <see cref="UseTrackedAggregateDomainEvents()"/>.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    public TrellisServiceBuilder UseDomainEvents<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        if (_useTrackedAggregateDomainEvents)
            throw new InvalidOperationException(
                "UseDomainEvents and UseTrackedAggregateDomainEvents are mutually exclusive. " +
                "Pick the response-shape dispatch (UseDomainEvents) or the tracked-aggregate " +
                "auto-dispatch (UseTrackedAggregateDomainEvents), not both.");

        _useDomainEvents = true;
        _useMediator = true;
        _typedDomainEventHandlerRegistrations.Add(static services =>
            services.AddDomainEventHandler<TEvent, THandler>());
        return this;
    }

    /// <summary>
    /// Registers the tracked-aggregate domain-event dispatch behavior and (optionally) scans
    /// assemblies for <see cref="IDomainEventHandler{TEvent}"/> implementations. Implies
    /// <see cref="UseMediator"/>. Mutually exclusive with <see cref="UseDomainEvents(Assembly[])"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="UseTrackedAggregateDomainEvents()"/> for the AOT-friendly parameterless
    /// overload. Calling with one or more assemblies scans them at startup; the scan uses
    /// reflection and is not AOT- or trim-safe.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Scans assemblies for IDomainEventHandler<TEvent> implementations. Use UseTrackedAggregateDomainEvents() with UseTrackedAggregateDomainEvents<TEvent, THandler>() for AOT/trim scenarios.")]
    [RequiresDynamicCode("Constructs closed generic IDomainEventHandler<TEvent> service types at runtime. Use UseTrackedAggregateDomainEvents() with UseTrackedAggregateDomainEvents<TEvent, THandler>() for AOT scenarios.")]
    public TrellisServiceBuilder UseTrackedAggregateDomainEvents(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (_useDomainEvents)
            throw new InvalidOperationException(
                "UseTrackedAggregateDomainEvents and UseDomainEvents are mutually exclusive. " +
                "Pick the tracked-aggregate auto-dispatch (UseTrackedAggregateDomainEvents) or the " +
                "response-shape dispatch (UseDomainEvents), not both.");

        if (assemblies.Length > 0)
            AddAssemblies(_domainEventAssemblies, assemblies, nameof(assemblies));

        _useTrackedAggregateDomainEvents = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// Registers the tracked-aggregate domain-event dispatch behavior without assembly
    /// scanning. AOT- and trim-safe. Pair with <see cref="UseTrackedAggregateDomainEvents{TEvent, THandler}()"/>
    /// per handler, or with explicit <c>services.AddDomainEventHandler&lt;TEvent, THandler&gt;()</c>
    /// calls outside the builder. Implies <see cref="UseMediator"/>. Mutually exclusive with
    /// <see cref="UseDomainEvents()"/>.
    /// </summary>
    public TrellisServiceBuilder UseTrackedAggregateDomainEvents()
    {
        if (_useDomainEvents)
            throw new InvalidOperationException(
                "UseTrackedAggregateDomainEvents and UseDomainEvents are mutually exclusive. " +
                "Pick the tracked-aggregate auto-dispatch (UseTrackedAggregateDomainEvents) or the " +
                "response-shape dispatch (UseDomainEvents), not both.");

        _useTrackedAggregateDomainEvents = true;
        _useMediator = true;
        return this;
    }

    /// <summary>
    /// AOT-safe per-handler tracked-aggregate domain-event registration. Registers
    /// <typeparamref name="THandler"/> as an <see cref="IDomainEventHandler{TEvent}"/> for
    /// <typeparamref name="TEvent"/> and ensures the tracked dispatch behavior is wired.
    /// Implies <see cref="UseMediator"/>. Mutually exclusive with <see cref="UseDomainEvents()"/>.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    public TrellisServiceBuilder UseTrackedAggregateDomainEvents<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        if (_useDomainEvents)
            throw new InvalidOperationException(
                "UseTrackedAggregateDomainEvents and UseDomainEvents are mutually exclusive. " +
                "Pick the tracked-aggregate auto-dispatch (UseTrackedAggregateDomainEvents) or the " +
                "response-shape dispatch (UseDomainEvents), not both.");

        _useTrackedAggregateDomainEvents = true;
        _useMediator = true;
        _typedDomainEventHandlerRegistrations.Add(static services =>
            services.AddDomainEventHandler<TEvent, THandler>());
        return this;
    }

    /// <summary>
    /// Registers a caching decorator around the inner <see cref="IActorProvider"/>
    /// (typically registered by a previous <c>UseXxxActorProvider</c> call).
    /// Per-request caching ensures multiple actor lookups within one request return the same instance.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="IActorProvider"/> implementation to wrap.</typeparam>
    /// <remarks>
    /// For built-in providers (<see cref="EntraActorProvider"/>, <see cref="ClaimsActorProvider"/>),
    /// chain after the matching <c>UseXxxActorProvider(...)</c> call so its
    /// <c>IOptions&lt;TOptions&gt;</c> is configured before the caching wrap replaces the
    /// <see cref="IActorProvider"/> slot. Custom providers without Trellis-managed options can
    /// be cached without a prior <c>UseXxxActorProvider</c>.
    /// </remarks>
    public TrellisServiceBuilder UseCachingActorProvider<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IActorProvider
    {
        if (_cachingActorProviderWrap is not null)
            throw new InvalidOperationException(
                "Only one caching actor provider can be configured. Caching actor provider already configured.");

        _cachingActorProviderWrap = services => services.AddCachingActorProvider<T>();
        return this;
    }

    /// <summary>
    /// Composes the HTTP-side <see cref="IActorProvider"/> with a system-actor identity used by
    /// non-HTTP execution contexts (typically a <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
    /// tick that runs outside any ambient request).
    /// </summary>
    /// <param name="systemActor">
    /// The actor returned when <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor.HttpContext"/>
    /// is <see langword="null"/>. Permissions on this actor become the authorization envelope for
    /// every mediator command the worker dispatches; scope them as narrowly as the worker requires.
    /// </param>
    /// <remarks>
    /// <para>
    /// Applied last, AFTER both the HTTP-side actor provider (<c>UseClaimsActorProvider</c>,
    /// <c>UseEntraActorProvider</c>, or <c>UseDevelopmentActorProvider</c>) and any
    /// <see cref="UseCachingActorProvider{T}"/> wrap. The worker wrapper sits on the OUTSIDE
    /// so worker-tick lookups never traverse the caching layer (which has no scope to cache
    /// against), while HTTP-side lookups flow through caching → inner provider unchanged.
    /// </para>
    /// <para>
    /// Calling this method requires that an actor provider slot is selected somewhere in the
    /// composition — typically via <c>UseClaimsActorProvider</c>, <c>UseEntraActorProvider</c>,
    /// <c>UseDevelopmentActorProvider</c>, or <see cref="UseCachingActorProvider{T}"/> with a
    /// custom provider. Because <c>Apply()</c> records selections and runs them in canonical
    /// order, the chain position of <c>UseWorkerActor(...)</c> relative to those selectors does
    /// not matter; only that one of them is present. Calling <c>UseWorkerActor</c> twice throws.
    /// A hosted-service validator runs at host start and fails fast if any subsequent
    /// <c>services.AddScoped&lt;IActorProvider, ...&gt;()</c> registration overwrites the
    /// wrapper — background-worker ticks would otherwise stop resolving the configured system
    /// actor and would surface the first dispatched command as
    /// <see cref="Trellis.Error.AuthenticationRequired"/>.
    /// </para>
    /// </remarks>
    public TrellisServiceBuilder UseWorkerActor(Actor systemActor)
    {
        ArgumentNullException.ThrowIfNull(systemActor);

        if (_workerActorWrap is not null)
            throw new InvalidOperationException(
                "Only one worker actor composition can be configured. Worker actor already configured.");

        _workerActorWrap = services => services.AddTrellisWorkerActor(systemActor);
        return this;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Apply() only invokes the assembly-scanning AddTrellisFluentValidation / AddResourceAuthorization / AddDomainEventDispatch overloads when the consumer explicitly passed assemblies to the matching builder method, which has already surfaced the IL2026/IL3050 warning at the consumer's call site.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Apply() only invokes the assembly-scanning AddTrellisFluentValidation / AddResourceAuthorization / AddDomainEventDispatch overloads when the consumer explicitly passed assemblies to the matching builder method, which has already surfaced the IL2026/IL3050 warning at the consumer's call site.")]
    internal void Apply()
    {
        if (_useAsp)
        {
            if (_configureAsp is null)
                _services.AddTrellisAsp();
            else
                _services.AddTrellisAsp(_configureAsp);
        }

        if (_useScalarValueValidation)
            _services.AddScalarValueValidation();

        if (_useProblemDetails)
            _services.AddTrellisProblemDetails();

        if (_useIdempotency)
            _services.AddTrellisIdempotency(_configureIdempotency);

        _actorProviderRegistration?.Invoke(_services);
        _cachingActorProviderWrap?.Invoke(_services);
        _workerActorWrap?.Invoke(_services);

        if (_useMediator)
        {
            if (_configureMediatorTelemetry is null)
                _services.AddTrellisBehaviors();
            else
                _services.AddTrellisBehaviors(_configureMediatorTelemetry);
        }

        if (_useResourceAuthorization && _resourceAuthorizationAssemblies.Count > 0)
            _services.AddResourceAuthorization([.. _resourceAuthorizationAssemblies]);

        foreach (var register in _typedResourceAuthorizationRegistrations)
            register(_services);

        if (_useFluentValidation && _fluentValidationAssemblies.Count == 0)
            _services.AddTrellisFluentValidation();
        else if (_useFluentValidation)
            _services.AddTrellisFluentValidation([.. _fluentValidationAssemblies]);

        foreach (var register in _typedFluentValidatorRegistrations)
            register(_services);

        if (_useDomainEvents && _domainEventAssemblies.Count == 0)
            _services.AddDomainEventDispatch();
        else if (_useDomainEvents)
            _services.AddDomainEventDispatch([.. _domainEventAssemblies]);

        if (_useTrackedAggregateDomainEvents)
        {
            _services.AddTrackedAggregateDomainEventDispatch();

            // The tracked opt-in registers the publisher + behavior; assemblies passed to
            // UseTrackedAggregateDomainEvents are scanned for IDomainEventHandler<TEvent>
            // implementations via the same AddDomainEventDispatch(params Assembly[]) entry
            // point. That call is safe here: AddDomainEventDispatch detects the tracked
            // behavior and skips the response-shape append.
            if (_domainEventAssemblies.Count > 0)
                _services.AddDomainEventDispatch([.. _domainEventAssemblies]);
        }

        foreach (var register in _typedDomainEventHandlerRegistrations)
            register(_services);

        _unitOfWorkRegistration?.Invoke(_services);
    }

    private void SetActorProvider(ActorProviderKind kind, Action<IServiceCollection> registration)
    {
        if (_actorProviderKind != ActorProviderKind.None)
            throw new InvalidOperationException(
                $"Only one actor provider can be selected. '{_actorProviderKind}' is already configured.");

        _actorProviderKind = kind;
        _actorProviderRegistration = registration;
    }

    private static void AddAssemblies(List<Assembly> target, Assembly[] assemblies, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", parameterName);

        for (var i = 0; i < assemblies.Length; i++)
        {
            if (assemblies[i] is null)
                throw new ArgumentException($"Assembly at index [{i}] is null.", parameterName);

            if (!target.Contains(assemblies[i]))
                target.Add(assemblies[i]);
        }
    }

    private static Action<T> Combine<T>(Action<T>? existing, Action<T> next) =>
        existing is null ? next : value =>
        {
            existing(value);
            next(value);
        };

    private enum ActorProviderKind
    {
        None,
        Claims,
        Entra,
        Development,
        NestedJsonPathClaims,
    }
}