namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="DomainEventDispatchServiceCollectionExtensions"/>.
/// </summary>
public class DomainEventDispatchRegistrationTests
{
    [Fact]
    public void AddDomainEventDispatch_RegistersBehaviorAndPublisher()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventDispatch();

        var publisher = services.SingleOrDefault(d => d.ServiceType == typeof(IDomainEventPublisher));
        publisher.Should().NotBeNull();
        publisher!.ImplementationType.Should().Be<MediatorDomainEventPublisher>();
        publisher.Lifetime.Should().Be(ServiceLifetime.Scoped);

        var dispatchBehaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)
                && d.ImplementationType == typeof(DomainEventDispatchBehavior<,>))
            .ToArray();
        dispatchBehaviors.Should().ContainSingle();
        dispatchBehaviors[0].Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddDomainEventDispatch_IsIdempotent()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventDispatch();
        services.AddDomainEventDispatch();
        services.AddDomainEventDispatch();

        services.Count(d => d.ServiceType == typeof(IDomainEventPublisher)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(DomainEventDispatchBehavior<,>)).Should().Be(1);
    }

    [Fact]
    public void AddDomainEventDispatch_AppendsAfterAlwaysOnBehaviors()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventDispatch();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToArray();

        behaviors.Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(DomainEventDispatchBehavior<,>));
    }

    [Fact]
    public void AddDomainEventHandler_RegistersHandlerAndDispatch()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventHandler<TestEventA, RecordingHandlerA>();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IDomainEventHandler<TestEventA>)
            && d.ImplementationType == typeof(RecordingHandlerA)
            && d.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(DomainEventDispatchBehavior<,>));
    }

    [Fact]
    public void AddDomainEventHandler_IsIdempotent()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventHandler<TestEventA, RecordingHandlerA>();
        services.AddDomainEventHandler<TestEventA, RecordingHandlerA>();

        services.Count(d =>
            d.ServiceType == typeof(IDomainEventHandler<TestEventA>)
            && d.ImplementationType == typeof(RecordingHandlerA))
            .Should().Be(1);
    }

    [Fact]
    public void AddDomainEventDispatch_Scanning_RegistersAllHandlersInAssembly()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventDispatch(typeof(RecordingHandlerA).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IDomainEventHandler<TestEventA>)
            && d.ImplementationType == typeof(RecordingHandlerA));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDomainEventHandler<TestEventA>)
            && d.ImplementationType == typeof(ThrowingHandlerA));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDomainEventHandler<TestEventB>)
            && d.ImplementationType == typeof(RecordingHandlerB));
    }

    [Fact]
    public void AddDomainEventDispatch_Scanning_NullAssemblies_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddDomainEventDispatch((System.Reflection.Assembly[])null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddDomainEventDispatch_Scanning_EmptyAssemblies_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddDomainEventDispatch(System.Array.Empty<System.Reflection.Assembly>());

        act.Should().Throw<ArgumentException>().WithParameterName("assemblies");
    }

    [Fact]
    public void AddDomainEventDispatch_InsertsBefore_AlreadyRegistered_TransactionalBehavior()
    {
        // Canonical order: AddTrellisBehaviors → AddTrellisUnitOfWork (here simulated) → AddDomainEventDispatch.
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddTrellisBehaviors();
        services.AddSingleton(
            typeof(IPipelineBehavior<,>),
            typeof(EntityFrameworkCore.TransactionalCommandBehavior<,>));

        services.AddDomainEventDispatch();

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToArray();

        pipeline.Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(DomainEventDispatchBehavior<,>),
            typeof(EntityFrameworkCore.TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddDomainEventDispatch_AfterTransactional_WithoutPriorTrellisBehaviors_RebuildsCanonicalOrder()
    {
        // Reviewer scenario: user registers TX first (no AddTrellisBehaviors yet) and
        // then calls AddDomainEventDispatch. The dispatch helper must yank TX, append the
        // always-on behaviors, append dispatch, and re-append TX so the final order is canonical.
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddSingleton(
            typeof(IPipelineBehavior<,>),
            typeof(EntityFrameworkCore.TransactionalCommandBehavior<,>));

        services.AddDomainEventDispatch();

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToArray();

        pipeline.Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(DomainEventDispatchBehavior<,>),
            typeof(EntityFrameworkCore.TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddDomainEventDispatch_BeforeTransactional_NaturalAppendKeepsTransactionInnermost()
    {
        // Canonical inverse: AddDomainEventDispatch → AddTrellisUnitOfWork (here simulated).
        // AddDomainEventDispatch appends behaviors; subsequent TX registration appends after
        // dispatch. End state matches the same canonical order.
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventDispatch();
        // Simulate AddTrellisUnitOfWork's behavior: append TX innermost.
        services.AddSingleton(
            typeof(IPipelineBehavior<,>),
            typeof(EntityFrameworkCore.TransactionalCommandBehavior<,>));

        var pipeline = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToArray();

        pipeline.Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(DomainEventDispatchBehavior<,>),
            typeof(EntityFrameworkCore.TransactionalCommandBehavior<,>));
    }

    private static void AddNullLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }
}
