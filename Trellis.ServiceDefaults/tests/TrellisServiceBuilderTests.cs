namespace Trellis.ServiceDefaults.Tests;

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using DefaultHttpContext = Microsoft.AspNetCore.Http.DefaultHttpContext;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;
using ProblemDetailsContext = Microsoft.AspNetCore.Http.ProblemDetailsContext;
using ProblemDetailsOptions = Microsoft.AspNetCore.Http.ProblemDetailsOptions;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

/// <summary>
/// Tests for <see cref="TrellisServiceBuilder"/>.
/// </summary>
public class TrellisServiceBuilderTests
{
    [Fact]
    public void UseEntityFrameworkUnitOfWork_AppliesTransactionalBehaviorLast()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseMediator()
            .UseEntityFrameworkUnitOfWork<TestDbContext>());

        var behaviorTypes = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        behaviorTypes.Should().EndWith(typeof(TransactionalCommandBehavior<,>));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IUnitOfWork) &&
            d.ImplementationType == typeof(EfUnitOfWork<TestDbContext>));
    }

    [Fact]
    public void UseFluentValidation_ImpliedMediatorAndRegistersAdapter()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseFluentValidation(typeof(TrellisServiceBuilderTests).Assembly));

        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(ValidationBehavior<,>));
        services.Count(d =>
            d.ServiceType == typeof(IMessageValidator<>) &&
            d.ImplementationType?.Name == "FluentValidationMessageValidatorAdapter`1").Should().Be(1);
    }

    [Fact]
    public void UseFluentValidation_WithoutAssemblies_RegistersAdapterOnly()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseFluentValidation());

        services.Count(d =>
            d.ServiceType == typeof(IMessageValidator<>) &&
            d.ImplementationType?.Name == "FluentValidationMessageValidatorAdapter`1").Should().Be(1);
    }

    [Fact]
    public void UseResourceAuthorization_WithoutAssemblies_RegistersMediatorOnly()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseResourceAuthorization());

        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(AuthorizationBehavior<,>));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IResourceLoader<UpdateProtectedOrderCommand, ProtectedOrder>));
    }

    [Fact]
    public void UseResourceAuthorization_WithAssembly_RegistersResourceAuthorizationForDiscoveredMessages()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseResourceAuthorization(typeof(UpdateProtectedOrderCommand).Assembly));

        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(AuthorizationBehavior<,>));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IResourceLoader<UpdateProtectedOrderCommand, ProtectedOrder>) &&
            d.ImplementationType == typeof(UpdateProtectedOrderLoader));
    }

    [Fact]
    public void UseResourceAuthorization_NullAssemblyArray_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        // Disambiguate the null literal across the new (Action<ResourceAuthorizationOptions>)
        // overload — without the cast, the call binds to the Action<> overload and the
        // ArgumentNullException is thrown with ParameterName "configure", not "assemblies".
        var act = () => services.AddTrellis(options => options.UseResourceAuthorization((Assembly[])null!));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("assemblies");
    }

    [Fact]
    public void UseResourceAuthorization_NullConfigureDelegate_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options.UseResourceAuthorization((Action<ResourceAuthorizationOptions>)null!));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configure");
    }

    [Fact]
    public void UseResourceAuthorization_NullAssemblyElement_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options.UseResourceAuthorization(
            typeof(UpdateProtectedOrderCommand).Assembly,
            null!));

        act.Should().Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "assemblies")
            .And.Message.Should().Contain("[1]");
    }

    [Fact]
    public void UseResourceAuthorization_ConfigureDelegate_RegistersResourceAuthorizationOptionsAndAppliesDelegate()
    {
        // The configure delegate must mutate the resolved options snapshot. Use a publicly
        // observable mutation (DefaultExposurePolicy) so a regression that silently drops the
        // delegate fails the assertion. `HideExistence<TResource>` keys on the RESOURCE type
        // (the type loaded by the pipeline), not the command type — so the canonical example
        // uses ProtectedOrder, not UpdateProtectedOrderCommand.
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseResourceAuthorization(o =>
            {
                o.DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound;
                o.HideExistence<ProtectedOrder>();
            }));

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IOptions<ResourceAuthorizationOptions>>().Value;

        // Visible delegate effect: DefaultExposurePolicy would be Propagate (the type default)
        // if the configure delegate had not run.
        resolved.DefaultExposurePolicy.Should().Be(AuthFailureExposurePolicy.HideAsNotFound,
            "the configure delegate must mutate the options snapshot — a default-Propagate value here would indicate the delegate never ran");
    }

    [Fact]
    public void UseResourceAuthorization_ConfigureDelegate_CalledTwice_ComposesBothConfigurations()
    {
        // Both configure delegates must be invoked against the SAME options instance — i.e.
        // the second delegate observes the first delegate's mutations, and a third-party
        // probe (resolving IOptions) sees the cumulative effect of both. The previous version
        // of this test asserted only the first delegate's effect, which would pass even if
        // the second delegate were silently dropped. This version captures observable state
        // inside each delegate AND in the resolved options snapshot.
        var invocationCount = 0;
        AuthFailureExposurePolicy defaultPolicySeenBySecondDelegate = default;

        var services = new ServiceCollection();
        services.AddTrellis(options => options
            .UseResourceAuthorization(o =>
            {
                o.DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound;
                o.HideExistence<ProtectedOrder>();
                invocationCount++;
            })
            .UseResourceAuthorization(o =>
            {
                // Inside the second delegate, the first delegate's mutations must already be
                // visible — that's what "compose" means in the IOptions.Configure model.
                defaultPolicySeenBySecondDelegate = o.DefaultExposurePolicy;
                o.Propagate<ProtectedOrder>();
                invocationCount++;
            }));

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IOptions<ResourceAuthorizationOptions>>().Value;

        invocationCount.Should().Be(2, "both configure delegates must be invoked when options is resolved");
        defaultPolicySeenBySecondDelegate.Should().Be(AuthFailureExposurePolicy.HideAsNotFound,
            "the second delegate must observe the first delegate's DefaultExposurePolicy mutation");
        resolved.DefaultExposurePolicy.Should().Be(AuthFailureExposurePolicy.HideAsNotFound,
            "the first delegate's DefaultExposurePolicy mutation must persist into the final snapshot — the second delegate did not overwrite it");
    }

    [Fact]
    public void UseResourceAuthorization_ConfigureDelegate_EnablesPipelineAndMediator()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseResourceAuthorization(_ => { }));

        // UseResourceAuthorization(configure) implies UseMediator so AddTrellisBehaviors fires.
        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(AuthorizationBehavior<,>));
    }

    [Fact]
    public void UseAsp_RegistersTrellisAspOptions()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseAsp());

        services.Should().ContainSingle(d => d.ServiceType == typeof(TrellisAspOptions));
    }

    [Fact]
    public void UseAsp_alone_DoesNotRegisterScalarValidation()
    {
        // The scalar-value validation slot is independent of UseAsp(). Hosts that only
        // need error-to-status-code mapping (e.g. an MVC site that does not bind
        // value-object DTOs from JSON/route/query) must NOT silently inherit the global
        // MvcOptions / JsonOptions mutation that AddScalarValueValidation performs.
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddTrellis(options => options.UseAsp());

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>().Value;

        mvcOptions.ModelBinderProviders.OfType<Trellis.Asp.ModelBinding.ScalarValueModelBinderProvider>()
            .Should().BeEmpty("UseAsp() alone must not register the scalar value model binder provider");

        sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>>().Value
            .SuppressModelStateInvalidFilter.Should().BeFalse(
                "UseAsp() alone must not flip the model-state-invalid filter suppression — scalar validation owns that toggle");
    }

    [Fact]
    public void UseScalarValueValidation_RegistersScalarValidationInfrastructure()
    {
        // The new slot wires the binder/filter/JSON converter set that the old
        // UseAsp() registered silently. Verifying via MVC pipeline outputs because
        // AddScalarValueValidation registers via Configure<MvcOptions> rather than
        // directly into the service collection.
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddTrellis(options => options.UseScalarValueValidation());

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>().Value;

        mvcOptions.ModelBinderProviders.OfType<Trellis.Asp.ModelBinding.ScalarValueModelBinderProvider>()
            .Should().NotBeEmpty("UseScalarValueValidation() must register the scalar-value model binder provider");

        sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>>().Value
            .SuppressModelStateInvalidFilter.Should().BeTrue(
                "UseScalarValueValidation() must flip the model-state-invalid filter suppression");
    }

    [Fact]
    public void UseScalarValueValidation_AppliesIdempotently()
    {
        // Multiple opt-ins (library + application both call the slot) must result in
        // a single registration of the scalar validation infrastructure.
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddTrellis(options => options
            .UseScalarValueValidation()
            .UseScalarValueValidation());

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>().Value;

        mvcOptions.ModelBinderProviders.OfType<Trellis.Asp.ModelBinding.ScalarValueModelBinderProvider>()
            .Should().ContainSingle("the scalar value model binder provider must only be registered once");
    }

    [Fact]
    public void UseAsp_and_UseScalarValueValidation_compose()
    {
        // The two slots are independent but the canonical composition for a controller
        // host that binds value-object DTOs is both opted in. Verify they coexist
        // without conflicting.
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddTrellis(options => options
            .UseAsp()
            .UseScalarValueValidation());

        services.Should().ContainSingle(d => d.ServiceType == typeof(TrellisAspOptions));

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>().Value;

        mvcOptions.ModelBinderProviders.OfType<Trellis.Asp.ModelBinding.ScalarValueModelBinderProvider>()
            .Should().ContainSingle();
    }

    [Fact]
    public void MultipleActorProviders_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseEntraActorProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only one actor provider*");
    }

    [Fact]
    public void SameActorProviderConfiguredTwice_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseClaimsActorProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only one actor provider*");
    }

    [Fact]
    public void UseClaimsActorProvider_RegistersActorProvider()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseClaimsActorProvider());

        services.Count(d =>
            d.ServiceType == typeof(IActorProvider) &&
            d.ImplementationType?.Name == "ClaimsActorProvider").Should().Be(1);
    }

    [Fact]
    public void UseNestedJsonPathClaimsActorProvider_RegistersNestedProviderInActorProviderSlot()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseNestedJsonPathClaimsActorProvider(opts =>
            {
                opts.ActorIdClaim = "sub";
                opts.ContainerClaim = "app_metadata";
                opts.PermissionsPath = "roles";
            }));

        services.Count(d =>
            d.ServiceType == typeof(IActorProvider) &&
            d.ImplementationType != null &&
            d.ImplementationType.Name == "NestedJsonPathClaimsActorProvider").Should().Be(1);
        services.Count(d =>
            d.ServiceType == typeof(IActorProvider) &&
            d.ImplementationType != null &&
            d.ImplementationType.Name == "ClaimsActorProvider").Should().Be(0,
            "the nested-JSON registration must replace the base ClaimsActorProvider slot, not stack");
    }

    [Fact]
    public void UseNestedJsonPathClaimsActorProvider_NoConfigure_DefaultOptions_AreRegistered()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseNestedJsonPathClaimsActorProvider());
        var provider = services.BuildServiceProvider();

        var descriptor = services.Single(d => d.ServiceType == typeof(IActorProvider));
        descriptor.ImplementationType.Should().Be<NestedJsonPathClaimsActorProvider>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);

        var options = provider.GetRequiredService<IOptions<NestedJsonPathClaimsActorOptions>>();
        options.Value.ActorIdClaim.Should().Be("sub");
        options.Value.PermissionsClaim.Should().Be("permissions");
        options.Value.ContainerClaim.Should().BeEmpty();
        options.Value.ActorIdPath.Should().BeEmpty();
        options.Value.PermissionsPath.Should().BeEmpty();
    }

    [Fact]
    public void UseNestedJsonPathClaimsActorProvider_AfterUseClaimsActorProvider_Throws()
    {
        // Mutual-exclusivity with the other actor-provider selectors mirrors the existing
        // ClaimsActorProvider / EntraActorProvider / DevelopmentActorProvider rules.
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseNestedJsonPathClaimsActorProvider(opts => opts.ContainerClaim = "app_metadata"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*actor provider*already configured*");
    }

    [Fact]
    public void UseProblemDetails_RegistersTrellisProblemDetailsCustomization()
    {
        // Run the registered CustomizeProblemDetails delegate and assert it carries the
        // Trellis defaults (traceId, 405 Allow projection). Resolving through
        // IOptions<ProblemDetailsOptions> proves the full PostConfigure chain is wired,
        // not just that the boolean was set.
        var services = new ServiceCollection();
        services.AddTrellis(options => options.UseProblemDetails());

        using var sp = services.BuildServiceProvider();
        var customize = sp.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value.CustomizeProblemDetails;
        customize.Should().NotBeNull();

        var http = new DefaultHttpContext();
        http.Response.Headers["Allow"] = "GET, POST";
        var ctx = new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = StatusCodes.Status405MethodNotAllowed },
        };
        customize!.Invoke(ctx);

        ctx.ProblemDetails.Extensions["traceId"].Should().NotBeNull();
        string[] expected = ["GET", "POST"];
        ctx.ProblemDetails.Extensions["allow"].Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void UseProblemDetails_DoesNotImplyUseAsp()
    {
        // ProblemDetails customization is orthogonal to Trellis.Asp's MVC/result-mapping
        // infrastructure. Consumers should be able to opt into ProblemDetails without
        // pulling in TrellisAspOptions / scalar validation / response mapping.
        var services = new ServiceCollection();
        services.AddTrellis(options => options.UseProblemDetails());

        services.Should().NotContain(d => d.ServiceType == typeof(TrellisAspOptions));
    }

    [Fact]
    public void UseProblemDetails_MixedWithDirectAddCallStaysSingleLayer()
    {
        // The direct AddTrellisProblemDetails() and the builder slot share the same
        // sentinel-based idempotency. A consumer that calls both (shared library +
        // application composition root) must end up with exactly one Trellis
        // post-configure layer wrapping CustomizeProblemDetails — not two layers
        // doubling traceId/allow extensions.
        var services = new ServiceCollection();
        services.AddTrellisProblemDetails();
        services.AddTrellis(options => options.UseProblemDetails());

        // The marker sentinel registered by AddTrellisProblemDetails IS the
        // idempotency contract — its presence short-circuits the second call. Count
        // marker registrations directly rather than IPostConfigureOptions descriptors,
        // because the descriptor count is coupled to ASP.NET Core internals (a future
        // AddProblemDetails() release adding its own PostConfigure would break the
        // assertion even though Trellis idempotency is still correct). The marker is
        // private to Trellis.Asp, so match by type name across the assembly boundary.
        var markerRegistrationCount = services.Count(d =>
            string.Equals(d.ServiceType.Name, "TrellisProblemDetailsMarker", StringComparison.Ordinal));

        markerRegistrationCount.Should().Be(1,
            "the marker-sentinel idempotency must apply across builder + direct composition");
    }

    [Fact]
    public void UseIdempotency_RegistersOptionsAndMarker()
    {
        var services = new ServiceCollection();
        services.AddTrellis(options => options.UseIdempotency(opt => opt.HeaderName = "X-Custom-Key"));

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<Trellis.Asp.Idempotency.IdempotencyOptions>>().Value;
        opts.HeaderName.Should().Be("X-Custom-Key");

        services.Should().Contain(
            d => string.Equals(d.ServiceType.Name, "IdempotencyMarker", StringComparison.Ordinal),
            "the marker is required so UseTrellisIdempotency() can detect builder-based wiring");

        services.Should().Contain(
            d => d.ServiceType == typeof(Trellis.Asp.Idempotency.IIdempotencyScopeResolver),
            "a default scope resolver must be registered by AddTrellisIdempotency");
    }

    [Fact]
    public void UseIdempotency_DoesNotRegisterAStore()
    {
        // The builder slot deliberately does not pick a store. Hosts opt into the in-memory
        // store (or an EF-backed store) explicitly so test/dev composition is not silently
        // inherited in production.
        var services = new ServiceCollection();
        services.AddTrellis(options => options.UseIdempotency());

        services.Should().NotContain(d => d.ServiceType == typeof(Trellis.Asp.Idempotency.IIdempotencyStore));
    }

    [Fact]
    public void UseIdempotency_repeated_calls_compose_options_callbacks()
    {
        var services = new ServiceCollection();
        services.AddTrellis(options => options
            .UseIdempotency(o => o.HeaderName = "X-First")
            .UseIdempotency()
            .UseIdempotency(o => o.MaxKeyLength = 99));

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<Trellis.Asp.Idempotency.IdempotencyOptions>>().Value;

        opts.HeaderName.Should().Be("X-First",
            "the first UseIdempotency callback must not be cleared by a later call");
        opts.MaxKeyLength.Should().Be(99,
            "the third UseIdempotency callback must also apply alongside the first");
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class SecondaryDbContext : DbContext
    {
        public SecondaryDbContext(DbContextOptions<SecondaryDbContext> options)
            : base(options)
        {
        }
    }

    public sealed record ProtectedOrder(string Id, string OwnerId);

    public sealed record UpdateProtectedOrderCommand(string ResourceId)
        : ICommand<Result<string>>, IAuthorizeResource<ProtectedOrder>
    {
        public IResult Authorize(Actor actor, ProtectedOrder resource) =>
            actor.Id == resource.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("protected-order.owner") { Detail = "Only the owner can update the order." });
    }

    public sealed class UpdateProtectedOrderLoader : IResourceLoader<UpdateProtectedOrderCommand, ProtectedOrder>
    {
        public Task<Result<ProtectedOrder>> LoadAsync(UpdateProtectedOrderCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new ProtectedOrder(message.ResourceId, "owner-1")));
    }

    public sealed record SampleEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming", "CA1711:Identifiers should not have incorrect suffix",
        Justification = "Domain event handler is a DDD term of art and is unrelated to System.EventHandler.")]
    public sealed class SampleEventHandler : IDomainEventHandler<SampleEvent>
    {
        public ValueTask HandleAsync(SampleEvent domainEvent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    [Fact]
    public void UseDomainEvents_WithoutAssemblies_RegistersDispatchBehaviorAndPublisher()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseDomainEvents());

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDomainEventPublisher));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(DomainEventDispatchBehavior<,>));
    }

    [Fact]
    public void UseDomainEvents_WithAssembly_RegistersDiscoveredHandlers()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseDomainEvents(typeof(SampleEventHandler).Assembly));

        services.Should().Contain(d =>
            d.ServiceType == typeof(IDomainEventHandler<SampleEvent>) &&
            d.ImplementationType == typeof(SampleEventHandler));
    }

    [Fact]
    public void UseDomainEvents_WithUnitOfWork_PlacesDispatchBeforeTransactional()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseDomainEvents()
            .UseEntityFrameworkUnitOfWork<TestDbContext>());

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        var dispatchIndex = pipeline.IndexOf(typeof(DomainEventDispatchBehavior<,>));
        var txIndex = pipeline.IndexOf(typeof(TransactionalCommandBehavior<,>));

        dispatchIndex.Should().BeGreaterOrEqualTo(0);
        txIndex.Should().BeGreaterOrEqualTo(0);
        dispatchIndex.Should().BeLessThan(txIndex,
            "domain events must dispatch after the transaction commits");
        pipeline.Should().EndWith(typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void UseTrackedAggregateDomainEvents_WithoutAssemblies_RegistersTrackedBehaviorAndPublisher()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseTrackedAggregateDomainEvents());

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDomainEventPublisher));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(TrackedAggregateDomainEventDispatchBehavior<,>));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(DomainEventDispatchBehavior<,>));
    }

    [Fact]
    public void UseTrackedAggregateDomainEvents_WithAssembly_RegistersDiscoveredHandlers()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseTrackedAggregateDomainEvents(typeof(SampleEventHandler).Assembly));

        services.Should().Contain(d =>
            d.ServiceType == typeof(IDomainEventHandler<SampleEvent>) &&
            d.ImplementationType == typeof(SampleEventHandler));
        // Handler-scan path uses AddDomainEventDispatch internally; the registration helper
        // detects the tracked behavior is already present and skips the response-shape append.
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(DomainEventDispatchBehavior<,>));
    }

    [Fact]
    public void UseTrackedAggregateDomainEvents_WithUnitOfWork_PlacesTrackedDispatchBeforeTransactional()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseTrackedAggregateDomainEvents()
            .UseEntityFrameworkUnitOfWork<TestDbContext>());

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        var trackedIndex = pipeline.IndexOf(typeof(TrackedAggregateDomainEventDispatchBehavior<,>));
        var txIndex = pipeline.IndexOf(typeof(TransactionalCommandBehavior<,>));

        trackedIndex.Should().BeGreaterOrEqualTo(0);
        txIndex.Should().BeGreaterOrEqualTo(0);
        trackedIndex.Should().BeLessThan(txIndex,
            "tracked-aggregate dispatch must run after the transaction commits");
        pipeline.Should().EndWith(typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void UseTrackedAggregateDomainEvents_WithUnitOfWorkRegisteredBefore_PlacesTrackedDispatchBeforeTransactional()
    {
        // Inverse ordering: TX registered before tracked dispatch on the builder. The opt-in
        // must yank TX, append always-on behaviors, append tracked dispatch, and re-append
        // TX so the final order is canonical regardless of call order.
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseEntityFrameworkUnitOfWork<TestDbContext>()
            .UseTrackedAggregateDomainEvents());

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        var trackedIndex = pipeline.IndexOf(typeof(TrackedAggregateDomainEventDispatchBehavior<,>));
        var txIndex = pipeline.IndexOf(typeof(TransactionalCommandBehavior<,>));

        trackedIndex.Should().BeGreaterOrEqualTo(0);
        txIndex.Should().BeGreaterOrEqualTo(0);
        trackedIndex.Should().BeLessThan(txIndex);
        pipeline.Should().EndWith(typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void UseDomainEvents_AfterUseTrackedAggregateDomainEvents_Throws()
    {
        // Mutex: picking both dispatchers would double-dispatch for Result<TAggregate>
        // handlers. The builder enforces fail-fast misconfiguration just like the actor-provider
        // and unit-of-work slots do.
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseTrackedAggregateDomainEvents()
            .UseDomainEvents());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mutually exclusive*");
    }

    [Fact]
    public void UseTrackedAggregateDomainEvents_AfterUseDomainEvents_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseDomainEvents()
            .UseTrackedAggregateDomainEvents());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mutually exclusive*");
    }

    // -------- Round-N inspection findings (M-S1, N-S1, N-S4) --------

    [Fact]
    public void UseEntityFrameworkUnitOfWork_TwiceWithSameContext_Throws()
    {
        // Inspection finding M-S1: the actor-provider slot throws on duplicate
        // configuration to prevent silent misconfiguration. The UoW slot must
        // follow the same fail-fast policy: a user mistakenly chaining two
        // UseEntityFrameworkUnitOfWork calls is always misconfigured.
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseEntityFrameworkUnitOfWork<TestDbContext>()
            .UseEntityFrameworkUnitOfWork<TestDbContext>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unit of work*");
    }

    [Fact]
    public void UseEntityFrameworkUnitOfWork_TwiceWithDifferentContext_Throws()
    {
        // Inspection finding M-S1: chaining UseEntityFrameworkUnitOfWork<DbContextA>
        // then UseEntityFrameworkUnitOfWork<DbContextB> previously silently
        // overwrote the first registration so only DbContextB's UoW was wired.
        // That class of mistake (read/write split, multi-tenant) must fail fast.
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseEntityFrameworkUnitOfWork<TestDbContext>()
            .UseEntityFrameworkUnitOfWork<SecondaryDbContext>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unit of work*");
    }

    // -------- Outbox relay slot (UseOutbox) --------

    [Fact]
    public void UseOutbox_RegistersRelayHostedServiceOptionsAndTimeProvider()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseEntityFrameworkUnitOfWork<TestDbContext>()
            .UseOutbox<TestDbContext>());

        // OutboxRelay<TContext> is internal to the outbox package, so assert the hosted-service
        // registration by reflected type shape rather than a direct type reference.
        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType != null &&
            d.ImplementationType.Name.StartsWith("OutboxRelay", StringComparison.Ordinal) &&
            d.ImplementationType.IsGenericType &&
            d.ImplementationType.GetGenericArguments()[0] == typeof(TestDbContext));

        services.Should().ContainSingle(d => d.ServiceType == typeof(OutboxOptions));
        services.Should().Contain(d => d.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void UseOutbox_AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseOutbox<TestDbContext>(o =>
            {
                o.BatchSize = 7;
                o.MaxAttempts = 3;
            }));

        var configured = (OutboxOptions)services
            .Single(d => d.ServiceType == typeof(OutboxOptions))
            .ImplementationInstance!;

        configured.BatchSize.Should().Be(7);
        configured.MaxAttempts.Should().Be(3);
    }

    [Fact]
    public void UseOutbox_DoesNotPerturbPipelineOrder_WhetherRegisteredBeforeOrAfterUnitOfWork()
    {
        // The outbox relay is a hosted service, not a Mediator behavior, so enabling it must leave
        // the canonical pipeline order identical regardless of where UseOutbox is called relative to
        // UseEntityFrameworkUnitOfWork (the registration-API checklist requirement).
        static System.Collections.Generic.List<Type?> PipelineOf(Action<TrellisServiceBuilder> compose)
        {
            var services = new ServiceCollection();
            services.AddTrellis(compose);
            return services
                .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
                .Select(d => d.ImplementationType)
                .ToList();
        }

        var outboxBeforeUow = PipelineOf(o => o
            .UseOutbox<TestDbContext>()
            .UseEntityFrameworkUnitOfWork<TestDbContext>());

        var outboxAfterUow = PipelineOf(o => o
            .UseEntityFrameworkUnitOfWork<TestDbContext>()
            .UseOutbox<TestDbContext>());

        var withoutOutbox = PipelineOf(o => o
            .UseEntityFrameworkUnitOfWork<TestDbContext>());

        outboxBeforeUow.Should().Equal(outboxAfterUow);
        outboxBeforeUow.Should().Equal(withoutOutbox);
        outboxBeforeUow.Should().EndWith(typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void UseOutbox_Twice_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseOutbox<TestDbContext>()
            .UseOutbox<TestDbContext>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*outbox*");
    }

    [Fact]
    public void ExplicitResourceAuthorization_BeforeAddTrellis_PositionsBehaviorBeforeValidation()
    {
        // Inspection finding N-S1: the documented "explicit resource-authorization
        // registrations without scanning" use case (UseResourceAuthorization() with
        // no assemblies) requires the user to call AddResourceAuthorization<T,R,Resp>()
        // explicitly. If they do so BEFORE AddTrellis(...), the closed-generic
        // ResourceAuthorizationBehavior<,,> previously ended up at descriptor slot 0,
        // before exception/tracing/logging/static-auth/validation — outside the
        // canonical Trellis behavior envelope. AddTrellisBehaviors now re-positions
        // any pre-existing closed-generic resource-auth behaviors to sit just before
        // ValidationBehavior, mirroring the AddTrellisUnitOfWork ↔ AddDomainEventDispatch
        // symmetry.
        var services = new ServiceCollection();

        services.AddResourceAuthorization<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>();
        services.AddScoped<IResourceLoader<UpdateProtectedOrderCommand, ProtectedOrder>, UpdateProtectedOrderLoader>();
        services.AddTrellis(options => options.UseResourceAuthorization());

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)
                     || d.ServiceType == typeof(IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>))
            .ToList();

        var validationIndex = descriptors.FindIndex(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(ValidationBehavior<,>));
        var resAuthIndex = descriptors.FindIndex(d =>
            d.ServiceType == typeof(IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>));

        validationIndex.Should().BeGreaterOrEqualTo(0, "ValidationBehavior must be registered by AddTrellisBehaviors");
        resAuthIndex.Should().BeGreaterOrEqualTo(0, "explicit AddResourceAuthorization must remain registered");
        resAuthIndex.Should().Be(validationIndex - 1,
            "ResourceAuthorizationBehavior<,,> must sit immediately before ValidationBehavior in the canonical pipeline");
    }

    [Fact]
    public void UseCachingActorProvider_AfterUseClaimsActorProvider_WrapsInnerProvider()
    {
        // Inspection finding N-S4: Trellis.Asp exposes AddCachingActorProvider<T>()
        // for per-request caching of an inner IActorProvider, but the builder didn't
        // expose a slot for it. Calling AddCachingActorProvider<ClaimsActorProvider>()
        // after AddTrellis(...UseClaimsActorProvider()) works but is awkward; making
        // it a builder slot makes the composition explicit and prevents the user
        // from forgetting the order constraint.
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseCachingActorProvider<ClaimsActorProvider>());

        // The IActorProvider resolves to a delegate registration (factory-based
        // CachingActorProvider) — assert by descriptor shape that the slot is
        // factory-based rather than the bare ClaimsActorProvider implementation
        // type registered by UseClaimsActorProvider alone.
        services.Should().Contain(d =>
            d.ServiceType == typeof(IActorProvider) &&
            d.ImplementationFactory != null,
            "UseCachingActorProvider must replace the IActorProvider slot with a CachingActorProvider factory");
        services.Should().Contain(d =>
            d.ServiceType == typeof(ClaimsActorProvider),
            "the inner provider type must be registered as scoped so the caching wrapper can resolve it");
    }

    [Fact]
    public void UseCachingActorProvider_TwiceWithDifferentInner_Throws()
    {
        // Inspection finding N-S4: the caching slot must follow the same fail-fast
        // duplicate-detection pattern as the actor-provider slot itself.
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseCachingActorProvider<ClaimsActorProvider>()
            .UseCachingActorProvider<EntraActorProvider>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*caching actor provider*");
    }

    [Fact]
    public async Task UseWorkerActor_AfterUseClaimsActorProvider_ReturnsSystemActorWhenHttpContextNull()
    {
        var services = new ServiceCollection();
        var systemActor = Actor.Create(
            id: "system",
            permissions: new HashSet<string> { "reminders:dispatch" });

        services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseWorkerActor(systemActor));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        // No HttpContext set on the accessor — simulate background-worker tick.
        var actor = await scope.ServiceProvider
            .GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.HasValue.Should().BeTrue();
        actor.Value.Id.Value.Should().Be("system");
    }

    [Fact]
    public async Task UseWorkerActor_ComposesAfterCachingWrap_WorkerPathSkipsCaching()
    {
        var services = new ServiceCollection();
        var systemActor = Actor.Create(
            id: "system",
            permissions: new HashSet<string> { "reminders:dispatch" });

        services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseCachingActorProvider<ClaimsActorProvider>()
            .UseWorkerActor(systemActor));

        using var sp = services.BuildServiceProvider();
        using var workerScope = sp.CreateScope();

        // Worker tick — null HttpContext must short-circuit to the system actor
        // without traversing the caching layer.
        var actor = await workerScope.ServiceProvider
            .GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.HasValue.Should().BeTrue();
        actor.Value.Id.Value.Should().Be("system");
    }

    [Fact]
    public void UseWorkerActor_TwiceOnSameBuilder_Throws()
    {
        var services = new ServiceCollection();
        var systemActor = Actor.Create(
            id: "system",
            permissions: new HashSet<string> { "reminders:dispatch" });

        var act = () => services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseWorkerActor(systemActor)
            .UseWorkerActor(systemActor));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*worker actor*");
    }

    [Fact]
    public void UseWorkerActor_WithoutPriorActorProvider_ThrowsAtApply()
    {
        var services = new ServiceCollection();
        var systemActor = Actor.Create(
            id: "system",
            permissions: new HashSet<string> { "reminders:dispatch" });

        var act = () => services.AddTrellis(options => options.UseWorkerActor(systemActor));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a prior unkeyed IActorProvider registration*");
    }

    [Fact]
    public async Task UseWorkerActor_ChainedBeforeUnitOfWork_AppliesInCanonicalOrder()
    {
        // Builder records selections and applies them in canonical order. UseWorkerActor
        // chained BEFORE UseEntityFrameworkUnitOfWork<T>() must still leave the worker
        // wrapper as the active IActorProvider and the transactional behavior innermost.
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase("worker-order-1"));
        var systemActor = Actor.Create(id: "system", permissions: new HashSet<string> { "reminders:dispatch" });

        services.AddTrellis(options => options
            .UseClaimsActorProvider()
            .UseWorkerActor(systemActor)
            .UseEntityFrameworkUnitOfWork<TestDbContext>());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var actor = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        actor.Value.Id.Value.Should().Be("system",
            "worker wrap must be the active provider regardless of chain order");

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();
        pipeline.Should().EndWith(typeof(TransactionalCommandBehavior<,>),
            "TX behavior remains innermost regardless of UseWorkerActor chain position");
    }

    [Fact]
    public async Task UseWorkerActor_ChainedAfterUnitOfWork_AppliesInCanonicalOrder()
    {
        // Inverse chain order — UnitOfWork registered first, worker second. Apply() runs
        // worker wrap before UnitOfWork regardless, so the active actor provider must still
        // be the worker wrapper and TX behavior must still be innermost.
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase("worker-order-2"));
        var systemActor = Actor.Create(id: "system", permissions: new HashSet<string> { "reminders:dispatch" });

        services.AddTrellis(options => options
            .UseEntityFrameworkUnitOfWork<TestDbContext>()
            .UseClaimsActorProvider()
            .UseWorkerActor(systemActor));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var actor = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        actor.Value.Id.Value.Should().Be("system");

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();
        pipeline.Should().EndWith(typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public async Task UseWorkerActor_ChainedBeforeActorProviderSelector_StillResolves()
    {
        // Apply() runs the actor-provider registration before the worker wrap regardless of
        // chain order. Calling UseWorkerActor BEFORE UseClaimsActorProvider on the builder
        // must still satisfy the prior-provider requirement at Apply() time.
        var services = new ServiceCollection();
        var systemActor = Actor.Create(id: "system", permissions: new HashSet<string> { "reminders:dispatch" });

        services.AddTrellis(options => options
            .UseWorkerActor(systemActor)
            .UseClaimsActorProvider());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var actor = await scope.ServiceProvider.GetRequiredService<IActorProvider>()
            .GetCurrentActorAsync(TestContext.Current.CancellationToken);
        actor.Value.Id.Value.Should().Be("system",
            "builder chain order between UseWorkerActor and UseXxxActorProvider must not matter");
    }

    // ---------- Typed (AOT-safe) per-type overloads ----------

    public sealed record TypedSampleCommand(string Name) : ICommand<Result<string>>;

    public sealed class TypedSampleCommandValidator : global::FluentValidation.AbstractValidator<TypedSampleCommand>
    {
        // Empty validator — registration alone is the unit under test; per-rule semantics
        // are tested in Trellis.FluentValidation.Tests.
    }

    [Fact]
    public void UseFluentValidationTyped_RegistersValidatorAndAdapter()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseFluentValidation<TypedSampleCommandValidator, TypedSampleCommand>());

        services.Count(d =>
            d.ServiceType == typeof(IMessageValidator<>) &&
            d.ImplementationType?.Name == "FluentValidationMessageValidatorAdapter`1").Should().Be(1);
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(global::FluentValidation.IValidator<TypedSampleCommand>) &&
            d.ImplementationType == typeof(TypedSampleCommandValidator));
    }

    [Fact]
    public void UseFluentValidationTyped_AlongsideParameterless_CombinesAdapterAndValidator()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseFluentValidation()
            .UseFluentValidation<TypedSampleCommandValidator, TypedSampleCommand>());

        services.Count(d =>
            d.ServiceType == typeof(IMessageValidator<>) &&
            d.ImplementationType?.Name == "FluentValidationMessageValidatorAdapter`1").Should().Be(1);
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(global::FluentValidation.IValidator<TypedSampleCommand>) &&
            d.ImplementationType == typeof(TypedSampleCommandValidator));
    }

    [Fact]
    public void UseResourceAuthorizationTyped_RegistersClosedGenericBehaviorWithoutScanning()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseResourceAuthorization<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>());

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>));
        // Loader is consumer-owned for the typed path; ensure the builder does not auto-register one.
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IResourceLoader<UpdateProtectedOrderCommand, ProtectedOrder>));
    }

    [Fact]
    public void UseDomainEventsTyped_RegistersHandlerAndDispatchBehavior()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseDomainEvents<SampleEvent, SampleEventHandler>());

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDomainEventPublisher));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(DomainEventDispatchBehavior<,>));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDomainEventHandler<SampleEvent>) &&
            d.ImplementationType == typeof(SampleEventHandler));
    }

    [Fact]
    public void UseTrackedAggregateDomainEventsTyped_RegistersHandlerAndTrackedBehavior()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseTrackedAggregateDomainEvents<SampleEvent, SampleEventHandler>());

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDomainEventPublisher));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(TrackedAggregateDomainEventDispatchBehavior<,>));
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDomainEventHandler<SampleEvent>) &&
            d.ImplementationType == typeof(SampleEventHandler));
    }

    [Fact]
    public void UseDomainEventsTyped_AfterTrackedAggregate_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseTrackedAggregateDomainEvents()
            .UseDomainEvents<SampleEvent, SampleEventHandler>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mutually exclusive*");
    }

    [Fact]
    public void UseTrackedAggregateDomainEventsTyped_AfterResponseShape_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(options => options
            .UseDomainEvents()
            .UseTrackedAggregateDomainEvents<SampleEvent, SampleEventHandler>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mutually exclusive*");
    }

    [Fact]
    public void UseFluentValidationTyped_CalledTwiceForSamePair_RegistersValidatorOnce()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseFluentValidation<TypedSampleCommandValidator, TypedSampleCommand>()
            .UseFluentValidation<TypedSampleCommandValidator, TypedSampleCommand>());

        services.Count(d =>
            d.ServiceType == typeof(global::FluentValidation.IValidator<TypedSampleCommand>) &&
            d.ImplementationType == typeof(TypedSampleCommandValidator)).Should().Be(1,
            "TryAddEnumerable must dedup repeated typed validator registrations");
    }

    [Fact]
    public void UseResourceAuthorizationTyped_CalledTwiceForSameTriple_RegistersBehaviorOnce()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseResourceAuthorization<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>()
            .UseResourceAuthorization<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>());

        services.Count(d =>
            d.ServiceType == typeof(IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>) &&
            d.ImplementationType == typeof(ResourceAuthorizationBehavior<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>))
            .Should().Be(1,
            "AddResourceAuthorization<,,>() is idempotent via InsertResourceAuthorizationBehavior dedup");
    }

    [Fact]
    public void UseResourceAuthorizationTyped_RegistersV4AuthorizedResourceAccessor()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseResourceAuthorization<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>());

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IAuthorizedResource<UpdateProtectedOrderCommand, ProtectedOrder>),
            "UseResourceAuthorization<,,>() must register the v4 accessor exactly once so handlers can inject it");
    }

    [Fact]
    public void UseResourceAuthorizationTyped_WhenBehaviorAlreadyRegisteredElsewhere_StillRegistersAccessor()
    {
        // Regression for round-4 code-review finding: the prior dedup guard in
        // UseResourceAuthorization<,,> short-circuited the call to AddResourceAuthorization<,,>
        // when the closed behavior was already registered (e.g. by another module's manual
        // pipeline-behavior registration). That guard silently skipped the v4 accessor
        // registration side effect — handlers couldn't resolve IAuthorizedResource<,>. The
        // guard is now removed because AddResourceAuthorization<,,> is itself idempotent
        // (InsertResourceAuthorizationBehavior dedups by ServiceType+ImplementationType,
        // and the accessor uses TryAddScoped which is also idempotent).
        //
        // To exercise the bug we must pre-register ONLY the closed behavior descriptor (not
        // via AddResourceAuthorization<,,>, which would already register the accessor as a
        // side effect and mask the regression).
        var services = new ServiceCollection();

        services.AddScoped<
            IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>,
            ResourceAuthorizationBehavior<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>>();

        // Sanity: pre-step did NOT register the accessor — would-be-broken builder must add it.
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IAuthorizedResource<UpdateProtectedOrderCommand, ProtectedOrder>),
            "the pre-step should only register the pipeline behavior so the bug can be exercised");

        services.AddTrellis(options => options
            .UseResourceAuthorization<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>());

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IAuthorizedResource<UpdateProtectedOrderCommand, ProtectedOrder>),
            "the builder helper must register the accessor exactly once even when the closed behavior was pre-registered by another module");

        // Idempotency invariant still holds — exactly one behavior descriptor.
        services.Count(d =>
            d.ServiceType == typeof(IPipelineBehavior<UpdateProtectedOrderCommand, Result<string>>) &&
            d.ImplementationType == typeof(ResourceAuthorizationBehavior<UpdateProtectedOrderCommand, ProtectedOrder, Result<string>>))
            .Should().Be(1, "InsertResourceAuthorizationBehavior dedup must prevent duplicate behavior registration");
    }

    [Fact]
    public void UseDomainEventsTyped_CalledTwiceForSamePair_RegistersHandlerOnce()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options
            .UseDomainEvents<SampleEvent, SampleEventHandler>()
            .UseDomainEvents<SampleEvent, SampleEventHandler>());

        services.Count(d =>
            d.ServiceType == typeof(IDomainEventHandler<SampleEvent>) &&
            d.ImplementationType == typeof(SampleEventHandler)).Should().Be(1,
            "AddDomainEventHandler already uses TryAddEnumerable so the typed builder overload is idempotent");
    }
}