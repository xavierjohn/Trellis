namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1707 // readable xUnit test names

/// <summary>
/// Covers the startup guardrail in <see cref="ServiceCollectionExtensions.AddTrellisBehaviors(IServiceCollection)"/>
/// that fails fast when the Mediator is registered <see cref="ServiceLifetime.Singleton"/>, since Trellis's
/// pipeline behaviors are Scoped (the authorization behavior reads the per-request <c>Actor</c>) and a
/// root-bound Singleton Mediator cannot resolve them.
/// </summary>
public class MediatorLifetimeGuardTests
{
    // Mimics how AddMediator registers IMediator/ISender with the configured lifetime; the factory is never
    // invoked because the guard only inspects the descriptor's lifetime.
    private static ServiceDescriptor MediatorDescriptor(Type serviceType, ServiceLifetime lifetime) =>
        new(serviceType, _ => null!, lifetime);

    [Fact]
    public void AddTrellisBehaviors_throws_when_Mediator_is_registered_Singleton()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(MediatorDescriptor(typeof(IMediator), ServiceLifetime.Singleton));

        var act = () => services.AddTrellisBehaviors();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ServiceLifetime.Singleton*")
            .WithMessage("*ServiceLifetime.Scoped*");
    }

    [Fact]
    public void AddTrellisBehaviors_also_detects_a_Singleton_ISender()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(MediatorDescriptor(typeof(ISender), ServiceLifetime.Singleton));

        var act = () => services.AddTrellisBehaviors();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Singleton*");
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void AddTrellisBehaviors_succeeds_when_Mediator_is_not_Singleton(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(MediatorDescriptor(typeof(IMediator), lifetime));

        var act = () => services.AddTrellisBehaviors();

        act.Should().NotThrow("Scoped and Transient both resolve the pipeline within the request scope");
    }

    [Fact]
    public void AddTrellisBehaviors_throws_for_a_Singleton_ISender_even_when_IMediator_is_not_Singleton()
    {
        IServiceCollection services = new ServiceCollection();
        // Mixed lifetimes: a non-Singleton IMediator must not mask a Singleton ISender.
        services.Add(MediatorDescriptor(typeof(IMediator), ServiceLifetime.Transient));
        services.Add(MediatorDescriptor(typeof(ISender), ServiceLifetime.Singleton));

        var act = () => services.AddTrellisBehaviors();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Singleton*");
    }

    [Fact]
    public void AddTrellisBehaviors_succeeds_when_no_Mediator_is_registered()
    {
        IServiceCollection services = new ServiceCollection();

        var act = () => services.AddTrellisBehaviors();

        act.Should().NotThrow("the guard only fires for an already-registered Singleton Mediator");
    }

    [Fact]
    public void AddTrellisBehaviors_ignores_unrelated_singleton_registrations()
    {
        IServiceCollection services = new ServiceCollection();
        // Infrastructure such as a CosmosClient is correctly registered Singleton; the guard inspects only the
        // IMediator/ISender lifetime, so an unrelated Singleton alongside a Scoped Mediator is fine.
        services.AddSingleton<SingletonInfrastructure>();
        services.Add(MediatorDescriptor(typeof(IMediator), ServiceLifetime.Scoped));

        var act = () => services.AddTrellisBehaviors();

        act.Should().NotThrow("the guard never touches non-Mediator service registrations");
    }

    // Stand-in for Singleton infrastructure (for example a CosmosClient) the guard must leave untouched.
    private sealed class SingletonInfrastructure;
}

#pragma warning restore CA1707
