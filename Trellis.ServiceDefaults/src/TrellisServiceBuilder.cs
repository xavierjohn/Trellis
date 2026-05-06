namespace Trellis.ServiceDefaults;

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.FluentValidation;
using Trellis.Mediator;

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
    private Action<TrellisAspOptions>? _configureAsp;
    private Action<TrellisMediatorTelemetryOptions>? _configureMediatorTelemetry;
    private Action<IServiceCollection>? _actorProviderRegistration;
    private Action<IServiceCollection>? _cachingActorProviderWrap;
    private Action<IServiceCollection>? _unitOfWorkRegistration;
    private bool _useAsp;
    private bool _useMediator;
    private bool _useFluentValidation;
    private bool _useResourceAuthorization;
    private bool _useDomainEvents;
    private ActorProviderKind _actorProviderKind;

    internal TrellisServiceBuilder(IServiceCollection services) =>
        _services = services;

    /// <summary>
    /// Registers Trellis ASP.NET Core integration.
    /// </summary>
    /// <remarks>
    /// Calling this method more than once is allowed; the configure delegates are composed in
    /// call order rather than overwriting. Each delegate runs against the same
    /// <see cref="TrellisAspOptions"/> instance, so layered configuration (e.g. a library
    /// applies defaults; the host overrides specific properties) is supported.
    /// </remarks>
    public TrellisServiceBuilder UseAsp(Action<TrellisAspOptions>? configure = null)
    {
        _useAsp = true;
        if (configure is not null)
            _configureAsp = Combine(_configureAsp, configure);

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
    /// Registers resource authorization behaviors and resource loaders discovered in assemblies.
    /// Implies <see cref="UseMediator"/>.
    /// </summary>
    /// <remarks>
    /// Passing no assemblies keeps the resource-authorization pipeline available for explicit
    /// registrations without performing assembly scanning.
    /// </remarks>
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
    /// Registers EF Core Unit of Work and the transactional command behavior.
    /// Implies <see cref="UseMediator"/> and is always applied after all other behavior registrations.
    /// </summary>
    /// <remarks>
    /// Calling this method more than once — with the same <typeparamref name="TContext"/> or
    /// a different one — throws <see cref="InvalidOperationException"/>. The Trellis pipeline
    /// supports exactly one transactional <see cref="IUnitOfWork"/> per composition; chaining
    /// two calls (e.g. for a read/write context split) is always misconfiguration.
    /// </remarks>
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
    /// Passing no assemblies registers the behavior + default <see cref="IDomainEventPublisher"/>
    /// without scanning; pair with explicit
    /// <c>services.AddDomainEventHandler&lt;TEvent, THandler&gt;()</c> calls (AOT-friendly).
    /// The dispatch behavior runs after <c>ValidationBehavior</c> and before
    /// <c>TransactionalCommandBehavior</c> in the pipeline, so events fire after the
    /// transaction commits.
    /// </remarks>
    public TrellisServiceBuilder UseDomainEvents(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length > 0)
            AddAssemblies(_domainEventAssemblies, assemblies, nameof(assemblies));

        _useDomainEvents = true;
        _useMediator = true;
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

    internal void Apply()
    {
        if (_useAsp)
        {
            if (_configureAsp is null)
                _services.AddTrellisAsp();
            else
                _services.AddTrellisAsp(_configureAsp);
        }

        _actorProviderRegistration?.Invoke(_services);
        _cachingActorProviderWrap?.Invoke(_services);

        if (_useMediator)
        {
            if (_configureMediatorTelemetry is null)
                _services.AddTrellisBehaviors();
            else
                _services.AddTrellisBehaviors(_configureMediatorTelemetry);
        }

        if (_useResourceAuthorization && _resourceAuthorizationAssemblies.Count > 0)
            _services.AddResourceAuthorization([.. _resourceAuthorizationAssemblies]);

        if (_useFluentValidation && _fluentValidationAssemblies.Count == 0)
            _services.AddTrellisFluentValidation();
        else if (_useFluentValidation)
            _services.AddTrellisFluentValidation([.. _fluentValidationAssemblies]);

        if (_useDomainEvents && _domainEventAssemblies.Count == 0)
            _services.AddDomainEventDispatch();
        else if (_useDomainEvents)
            _services.AddDomainEventDispatch([.. _domainEventAssemblies]);

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
    }
}