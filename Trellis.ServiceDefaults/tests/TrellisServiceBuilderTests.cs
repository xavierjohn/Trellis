namespace Trellis.ServiceDefaults.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

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

        var act = () => services.AddTrellis(options => options.UseResourceAuthorization(null!));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("assemblies");
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
    public void UseAsp_RegistersTrellisAspOptionsAndScalarValidationInfrastructure()
    {
        var services = new ServiceCollection();

        services.AddTrellis(options => options.UseAsp());

        services.Should().ContainSingle(d => d.ServiceType == typeof(TrellisAspOptions));
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
}