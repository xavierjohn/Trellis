namespace Trellis.ServiceDefaults.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp;
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
}