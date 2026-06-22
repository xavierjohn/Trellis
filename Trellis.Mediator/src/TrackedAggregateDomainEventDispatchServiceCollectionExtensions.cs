namespace Trellis.Mediator;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Opt-in registration for <see cref="TrackedAggregateDomainEventDispatchBehavior{TMessage, TResponse}"/>.
/// </summary>
public static class TrackedAggregateDomainEventDispatchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TrackedAggregateDomainEventDispatchBehavior{TMessage, TResponse}"/> as
    /// an open-generic pipeline behavior together with the default <see cref="IDomainEventPublisher"/>
    /// implementation. The behavior auto-dispatches domain events from every aggregate the unit of
    /// work tracked at commit time, irrespective of the command's response shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The opt-in REPLACES any registered <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>
    /// to prevent double-dispatch on <c>Result&lt;TAggregate&gt;</c> handlers (which the response-shape
    /// behavior would also drain). Subsequent <see cref="DomainEventDispatchServiceCollectionExtensions.AddDomainEventHandler{TEvent, THandler}(IServiceCollection)"/>
    /// calls remain safe: the underlying <c>AddDomainEventDispatch</c> registration skips the
    /// response-shape append when the tracked behavior is already present.
    /// </para>
    /// <para>
    /// <b>Pipeline ordering.</b> Inserted as the innermost of the always-on Trellis behaviors,
    /// running after <c>ValidationBehavior</c>
    /// (Exception → Tracing → Logging → Authorization → Validation → TrackedAggregateDispatch).
    /// If <c>TransactionalCommandBehavior</c> (from <c>Trellis.Mediator</c>) is
    /// already registered, this method temporarily yanks it, ensures the always-on Trellis
    /// behaviors are present, appends tracked dispatch, and re-appends the transactional behavior
    /// as innermost. The result is order-independent: events fire after the transaction commits
    /// regardless of whether <c>AddTrellisUnitOfWork&lt;TContext&gt;()</c> was called before
    /// or after this method.
    /// </para>
    /// <para>
    /// <b>Resolution requirement.</b> The behavior resolves <see cref="ITrackedAggregateSource"/>
    /// at construction time. <c>AddTrellisUnitOfWork&lt;TContext&gt;()</c> registers the EF Core
    /// forwarder; consumers using a custom <c>IUnitOfWork</c> must either ensure it
    /// implements <see cref="ITrackedAggregateSource"/> (the forwarder casts through it) or
    /// register <see cref="ITrackedAggregateSource"/> explicitly. Resolution throws
    /// <see cref="InvalidOperationException"/> if the requirement is not met.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once registers the behavior and publisher
    /// exactly once.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddTrackedAggregateDomainEventDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IDomainEventPublisher, MediatorDomainEventPublisher>();

        // Remove any pre-existing response-shape dispatch behavior so a Result<TAggregate>
        // handler doesn't dispatch twice (once via the response-shape behavior, once via the
        // tracked source).
        DomainEventDispatchServiceCollectionExtensions.RemoveResponseShapeDispatchBehavior(services);

        // Same yank-and-reappend dance as AddDomainEventDispatch so the transactional behavior
        // stays innermost regardless of registration order.
        var transactionalDescriptor = DomainEventDispatchServiceCollectionExtensions.TryRemoveTransactionalBehavior(services);

        services.AddTrellisBehaviors();
        AppendTrackedDispatchBehavior(services);

        if (transactionalDescriptor is not null)
            services.Add(transactionalDescriptor);

        return services;
    }

    private static void AppendTrackedDispatchBehavior(IServiceCollection services)
    {
        // Idempotent: skip if already registered.
        if (DomainEventDispatchServiceCollectionExtensions.HasTrackedAggregateDispatchBehavior(services))
            return;

        services.Add(ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>),
            typeof(TrackedAggregateDomainEventDispatchBehavior<,>)));
    }
}
