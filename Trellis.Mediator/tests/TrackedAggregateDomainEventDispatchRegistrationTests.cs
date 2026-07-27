namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="TrackedAggregateDomainEventDispatchServiceCollectionExtensions"/> and
/// the interaction with <see cref="DomainEventDispatchServiceCollectionExtensions"/>.
/// </summary>
public class TrackedAggregateDomainEventDispatchRegistrationTests
{
    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_registers_behavior_and_publisher()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddTrackedAggregateDomainEventDispatch();

        var publisher = services.SingleOrDefault(d => d.ServiceType == typeof(IDomainEventPublisher));
        publisher.Should().NotBeNull();
        publisher!.ImplementationType.Should().Be<MediatorDomainEventPublisher>();
        publisher.Lifetime.Should().Be(ServiceLifetime.Scoped);

        var trackedBehaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)
                && d.ImplementationType == typeof(TrackedAggregateDomainEventDispatchBehavior<,>))
            .ToArray();
        trackedBehaviors.Should().ContainSingle();
        trackedBehaviors[0].Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_is_idempotent()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddTrackedAggregateDomainEventDispatch();
        services.AddTrackedAggregateDomainEventDispatch();
        services.AddTrackedAggregateDomainEventDispatch();

        services.Count(d => d.ServiceType == typeof(IDomainEventPublisher)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TrackedAggregateDomainEventDispatchBehavior<,>)).Should().Be(1);
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_replaces_existing_response_shape_dispatch()
    {
        // If the user previously called AddDomainEventDispatch() and then opts in to the
        // tracked dispatcher, the response-shape behavior must be removed so a
        // Result<TAggregate> handler doesn't see both behaviors fire.
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddDomainEventDispatch();
        services.AddTrackedAggregateDomainEventDispatch();

        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(DomainEventDispatchBehavior<,>)).Should().Be(0);
        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TrackedAggregateDomainEventDispatchBehavior<,>)).Should().Be(1);
    }

    [Fact]
    public void AddDomainEventDispatch_after_tracked_does_not_reintroduce_response_shape_behavior()
    {
        // Subsequent AddDomainEventHandler<TEvent, THandler>() implicitly calls
        // AddDomainEventDispatch; that path must detect the tracked behavior and skip the
        // response-shape append (rubber-duck finding #2). Otherwise a Result<TAggregate>
        // handler would dispatch twice.
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddTrackedAggregateDomainEventDispatch();
        services.AddDomainEventDispatch();
        services.AddDomainEventHandler<TestEventA, RecordingHandlerA>();

        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(DomainEventDispatchBehavior<,>)).Should().Be(0);
        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TrackedAggregateDomainEventDispatchBehavior<,>)).Should().Be(1);
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDomainEventHandler<TestEventA>)
            && d.ImplementationType == typeof(RecordingHandlerA));
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_canonical_pipeline_order()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddTrackedAggregateDomainEventDispatch();

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
            typeof(TrackedAggregateDomainEventDispatchBehavior<,>));
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_keeps_transactional_innermost_when_registered_after()
    {
        // Canonical order: AddTrellisBehaviors → AddTrellisUnitOfWork (simulated TX) → AddTrackedAggregateDomainEventDispatch.
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddTrellisBehaviors();
        services.AddSingleton(
            typeof(IPipelineBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));

        services.AddTrackedAggregateDomainEventDispatch();

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
            typeof(TrackedAggregateDomainEventDispatchBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_keeps_transactional_innermost_when_registered_before()
    {
        // Inverse: tracked dispatch first, then transactional is appended innermost.
        var services = new ServiceCollection();
        AddNullLogging(services);

        services.AddTrackedAggregateDomainEventDispatch();
        services.AddSingleton(
            typeof(IPipelineBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));

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
            typeof(TrackedAggregateDomainEventDispatchBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_after_transactional_without_prior_behaviors_rebuilds_canonical_order()
    {
        // Reviewer scenario: user registers TX first (no AddTrellisBehaviors yet) and then
        // calls AddTrackedAggregateDomainEventDispatch. The opt-in must yank TX, append the
        // always-on behaviors, append tracked dispatch, and re-append TX so the final order is canonical.
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddSingleton(
            typeof(IPipelineBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));

        services.AddTrackedAggregateDomainEventDispatch();

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
            typeof(TrackedAggregateDomainEventDispatchBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_ClosedTransactionalBehaviorPreRegistered_PlacesDispatchOutsideTransaction()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddScoped<
            IPipelineBehavior<AggregateCommand, Result<TestAggregate>>,
            TransactionalCommandBehavior<AggregateCommand, Result<TestAggregate>>>();

        services.AddTrackedAggregateDomainEventDispatch();

        PipelineImplementationTypes(services).Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(TrackedAggregateDomainEventDispatchBehavior<,>),
            typeof(TransactionalCommandBehavior<AggregateCommand, Result<TestAggregate>>));
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_CalledTwiceWithPreRegisteredTransaction_DoesNotDuplicateDescriptors()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddSingleton(
            typeof(IPipelineBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));

        services.AddTrackedAggregateDomainEventDispatch();
        services.AddTrackedAggregateDomainEventDispatch();

        PipelineImplementationTypes(services).Should().Equal(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(TrackedAggregateDomainEventDispatchBehavior<,>),
            typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrackedAggregateDomainEventDispatch_null_services_throws()
    {
        var act = () => TrackedAggregateDomainEventDispatchServiceCollectionExtensions
            .AddTrackedAggregateDomainEventDispatch(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static void AddNullLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private static Type?[] PipelineImplementationTypes(IServiceCollection services) =>
        services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
            .Select(d => d.ImplementationType)
            .ToArray();
}
