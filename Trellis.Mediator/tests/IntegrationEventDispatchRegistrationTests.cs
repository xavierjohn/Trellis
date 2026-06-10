namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tests for <see cref="IntegrationEventDispatchServiceCollectionExtensions"/>.
/// </summary>
public class IntegrationEventDispatchRegistrationTests
{
    [Fact]
    public void AddIntegrationEventDispatch_RegistersPublisherAndCollectorAsScoped()
    {
        var services = new ServiceCollection();

        services.AddIntegrationEventDispatch();

        var publisher = services.SingleOrDefault(d => d.ServiceType == typeof(IIntegrationEventPublisher));
        publisher.Should().NotBeNull();
        publisher!.ImplementationType.Should().Be<MediatorIntegrationEventPublisher>();
        publisher.Lifetime.Should().Be(ServiceLifetime.Scoped);

        var collector = services.SingleOrDefault(d => d.ServiceType == typeof(IIntegrationEventCollector));
        collector.Should().NotBeNull();
        collector!.ImplementationType.Should().Be<IntegrationEventCollector>();
        collector.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddIntegrationEventDispatch_IsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddIntegrationEventDispatch();
        services.AddIntegrationEventDispatch();
        services.AddIntegrationEventDispatch();

        services.Count(d => d.ServiceType == typeof(IIntegrationEventPublisher)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IIntegrationEventCollector)).Should().Be(1);
    }

    [Fact]
    public void AddIntegrationEventHandler_RegistersHandlerPublisherAndCollector()
    {
        var services = new ServiceCollection();

        services.AddIntegrationEventHandler<RegEventA, RegHandlerA>();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IIntegrationEventHandler<RegEventA>)
            && d.ImplementationType == typeof(RegHandlerA)
            && d.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventCollector));
    }

    [Fact]
    public void AddIntegrationEventHandler_IsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddIntegrationEventHandler<RegEventA, RegHandlerA>();
        services.AddIntegrationEventHandler<RegEventA, RegHandlerA>();

        services.Count(d =>
            d.ServiceType == typeof(IIntegrationEventHandler<RegEventA>)
            && d.ImplementationType == typeof(RegHandlerA))
            .Should().Be(1);
    }

    [Fact]
    public void AddIntegrationEventDispatch_Scanning_RegistersAllHandlersInAssembly()
    {
        var services = new ServiceCollection();

        services.AddIntegrationEventDispatch(typeof(RegHandlerA).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IIntegrationEventHandler<RegEventA>)
            && d.ImplementationType == typeof(RegHandlerA));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IIntegrationEventHandler<RegEventB>)
            && d.ImplementationType == typeof(RegHandlerB));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventCollector));
    }

    [Fact]
    public void AddIntegrationEventDispatch_Scanning_RegistersMultiInterfaceHandlerPerInterface()
    {
        var services = new ServiceCollection();

        services.AddIntegrationEventDispatch(typeof(RegMultiHandler).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IIntegrationEventHandler<RegEventA>)
            && d.ImplementationType == typeof(RegMultiHandler));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IIntegrationEventHandler<RegEventB>)
            && d.ImplementationType == typeof(RegMultiHandler));
    }

    [Fact]
    public void AddIntegrationEventDispatch_Scanning_NullAssemblies_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddIntegrationEventDispatch((System.Reflection.Assembly[])null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddIntegrationEventDispatch_Scanning_EmptyAssemblies_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddIntegrationEventDispatch(System.Array.Empty<System.Reflection.Assembly>());

        act.Should().Throw<ArgumentException>().WithParameterName("assemblies");
    }

    [Fact]
    public void AddIntegrationEventDispatch_Scanning_NullAssemblyElement_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddIntegrationEventDispatch([null!]);

        act.Should().Throw<ArgumentException>().WithParameterName("assemblies");
    }
}

internal sealed record RegEventA(DateTimeOffset OccurredAt) : IIntegrationEvent;

internal sealed record RegEventB(DateTimeOffset OccurredAt) : IIntegrationEvent;

internal sealed class RegHandlerA : IIntegrationEventHandler<RegEventA>
{
    public ValueTask HandleAsync(RegEventA integrationEvent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class RegHandlerB : IIntegrationEventHandler<RegEventB>
{
    public ValueTask HandleAsync(RegEventB integrationEvent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class RegMultiHandler : IIntegrationEventHandler<RegEventA>, IIntegrationEventHandler<RegEventB>
{
    public ValueTask HandleAsync(RegEventA integrationEvent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask HandleAsync(RegEventB integrationEvent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
