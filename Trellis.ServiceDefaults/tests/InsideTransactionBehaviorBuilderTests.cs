namespace Trellis.ServiceDefaults.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

public class InsideTransactionBehaviorBuilderTests
{
    public static IEnumerable<TheoryDataRow<string, bool, bool>> RegistrationOrders()
    {
        foreach (var order in Permutations(["mediator", "transaction", "inside", "events", "resource"]))
            foreach (var tracked in new[] { false, true })
                foreach (var open in new[] { false, true })
                    yield return new(string.Join(',', order), tracked, open);
    }

    [Theory]
    [MemberData(nameof(RegistrationOrders))]
    public void AddInsideTransactionBehavior_RegistrationPermutations_PreservesTransactionEnvelope(
        string order, bool tracked, bool open)
    {
        var services = new ServiceCollection();
        foreach (var registration in order.Split(','))
        {
            switch (registration)
            {
                case "mediator":
                    services.AddTrellisBehaviors();
                    break;
                case "transaction":
                    services.AddTrellisUnitOfWork<TestDbContext>();
                    break;
                case "inside":
                    if (open)
                        services.AddInsideTransactionBehavior(typeof(Behavior<,>));
                    else
                        services.AddInsideTransactionBehavior<Command, Result<string>, Behavior<Command, Result<string>>>();
                    break;
                case "events":
                    if (tracked)
                        services.AddTrackedAggregateDomainEventDispatch();
                    else
                        services.AddDomainEventDispatch();
                    break;
                case "resource":
                    services.AddResourceAuthorization<Command, Resource, Result<string>>();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(order));
            }
        }

        AssertCanonicalOrder(services, tracked, open);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UseInsideTransactionBehavior_BeforeOrAfterUnitOfWork_DeduplicatesDirectBindings(
        bool unitOfWorkFirst, bool open)
    {
        var services = new ServiceCollection();
        if (unitOfWorkFirst)
            services.AddTrellisUnitOfWork<TestDbContext>();
        RegisterDirect(services, open);

        services.AddTrellis(options =>
        {
            if (open)
                options.UseInsideTransactionBehavior(typeof(Behavior<,>))
                    .UseInsideTransactionBehavior(typeof(Behavior<,>));
            else
                options.UseInsideTransactionBehavior<Command, Result<string>, Behavior<Command, Result<string>>>()
                    .UseInsideTransactionBehavior<Command, Result<string>, Behavior<Command, Result<string>>>();
            options.UseDomainEvents().UseResourceAuthorization<Command, Resource, Result<string>>();
        });

        if (!unitOfWorkFirst)
            services.AddTrellisUnitOfWork<TestDbContext>();
        RegisterDirect(services, open);
        services.AddDomainEventDispatch();
        services.AddTrellisBehaviors();

        AssertCanonicalOrder(services, tracked: false, open);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseInsideTransactionBehavior_FluentCallOrder_PreservesTransactionEnvelope(bool unitOfWorkFirst)
    {
        var services = new ServiceCollection();
        services.AddTrellis(options =>
        {
            if (unitOfWorkFirst)
                options.UseEntityFrameworkUnitOfWork<TestDbContext>();
            options.UseInsideTransactionBehavior(typeof(Behavior<,>)).UseTrackedAggregateDomainEvents()
                .UseResourceAuthorization<Command, Resource, Result<string>>();
            if (!unitOfWorkFirst)
                options.UseEntityFrameworkUnitOfWork<TestDbContext>();
        });

        AssertCanonicalOrder(services, tracked: true, open: true);
    }

    private static void RegisterDirect(IServiceCollection services, bool open)
    {
        if (open)
            services.AddInsideTransactionBehavior(typeof(Behavior<,>));
        else
            services.AddInsideTransactionBehavior<Command, Result<string>, Behavior<Command, Result<string>>>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(typeof(string))]
    public void UseInsideTransactionBehavior_InvalidOpenType_Throws(Type? behaviorType)
    {
        var act = () => new ServiceCollection().AddTrellis(options =>
            options.UseInsideTransactionBehavior(behaviorType!));

        if (behaviorType is null)
            act.Should().Throw<ArgumentNullException>().WithParameterName(nameof(behaviorType));
        else
            act.Should().Throw<ArgumentException>().WithParameterName(nameof(behaviorType));
    }

    private static void AssertCanonicalOrder(IServiceCollection services, bool tracked, bool open) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>)
                || descriptor.ServiceType == typeof(IPipelineBehavior<Command, Result<string>>))
            .Select(descriptor => descriptor.ImplementationType)
            .Should().Equal(
            [
                typeof(ExceptionBehavior<,>),
                typeof(TracingBehavior<,>),
                typeof(LoggingBehavior<,>),
                typeof(AuthorizationContextBehavior<,>),
                typeof(AuthorizationBehavior<,>),
                typeof(ResourceAuthorizationBehavior<Command, Resource, Result<string>>),
                typeof(ValidationBehavior<,>),
                tracked ? typeof(TrackedAggregateDomainEventDispatchBehavior<,>) : typeof(DomainEventDispatchBehavior<,>),
                typeof(TransactionalCommandBehavior<,>),
                open ? typeof(Behavior<,>) : typeof(Behavior<Command, Result<string>>),
            ]);

    private static IEnumerable<string[]> Permutations(string[] values)
    {
        if (values.Length == 0)
        {
            yield return [];
            yield break;
        }

        foreach (var first in values)
            foreach (var rest in Permutations(values.Where(value => value != first).ToArray()))
                yield return [first, .. rest];
    }

    private sealed record Resource;
    private sealed record Command : ICommand<Result<string>>, IAuthorizeResource<Resource>
    {
        public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
    }

    private sealed class Behavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : ICommand<TResponse>
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        public ValueTask<TResponse> Handle(
            TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken) =>
            next(message, cancellationToken);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
