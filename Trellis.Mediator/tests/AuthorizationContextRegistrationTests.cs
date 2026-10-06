namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator.Tests.Helpers;

public class AuthorizationContextRegistrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AddResourceAuthorization_MixedStandardAndTransactionOrder_RegistersOneContext(
        bool standardFirst, bool transactionFirst)
    {
        var services = new ServiceCollection();
        if (transactionFirst)
            services.AddTransactionalCommandBehavior();
        if (standardFirst)
            services.AddTrellisBehaviors();

        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();
        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();

        if (!standardFirst)
            services.AddTrellisBehaviors();
        if (!transactionFirst)
            services.AddTransactionalCommandBehavior();
        services.AddTrellisBehaviors();

        ApplicableDescriptors<ResourceOwnerCommand, Result<string>>(services).Select(descriptor => descriptor.ImplementationType)
            .Should().Equal(
                typeof(ExceptionBehavior<,>), typeof(TracingBehavior<,>), typeof(LoggingBehavior<,>),
                typeof(AuthorizationContextBehavior<,>), typeof(AuthorizationBehavior<,>),
                typeof(ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>),
                typeof(ValidationBehavior<,>), typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddResourceAuthorization_WithoutStandardBehaviors_InstallsClosedContextBeforeAuthorization()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();
        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();

        ApplicableDescriptors<ResourceOwnerCommand, Result<string>>(services).Select(descriptor => descriptor.ImplementationType)
            .Should().Equal(
                typeof(AuthorizationContextBehavior<ResourceOwnerCommand, Result<string>>),
                typeof(ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>));
    }

    [Fact]
    public void AddResourceAuthorization_ExistingClosedContext_ReusesAndRehomesBeforeAuthorization()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPipelineBehavior<ResourceOwnerCommand, Result<string>>,
            ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>>();
        var descriptor = ServiceDescriptor.Scoped<IPipelineBehavior<ResourceOwnerCommand, Result<string>>,
            AuthorizationContextBehavior<ResourceOwnerCommand, Result<string>>>();
        services.Insert(services.Count, descriptor);

        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();

        ApplicableDescriptors<ResourceOwnerCommand, Result<string>>(services).Should().HaveCount(2);
        ApplicableDescriptors<ResourceOwnerCommand, Result<string>>(services)[0].Should().BeSameAs(descriptor);
    }

    [Fact]
    public void AddRelatedResourceAuthorization_WithoutStandardBehaviors_InstallsOneClosedContext()
    {
        var services = new ServiceCollection();

        services.AddRelatedResourceAuthorization<ViaCommand, Leaf, string, Owner, string, Result<string>>(leaf => leaf.OwnerId);
        services.AddRelatedResourceAuthorization<ViaCommand, Leaf, string, Owner, string, Result<string>>(leaf => leaf.OwnerId);

        ApplicableDescriptors<ViaCommand, Result<string>>(services).Select(descriptor => descriptor.ImplementationType)
            .Should().Equal(
                typeof(AuthorizationContextBehavior<ViaCommand, Result<string>>),
                typeof(ResourceAuthorizationViaBehavior<ViaCommand, Leaf, Owner, Result<string>>));
    }

    [Fact]
    public void AddTrellisBehaviors_ClosedContextsAndUnrelatedFactory_NormalizesOnlyKnownImplementations()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPipelineBehavior<ResourceOwnerCommand, Result<string>>,
            AuthorizationContextBehavior<ResourceOwnerCommand, Result<string>>>();
        var factory = ServiceDescriptor.Scoped<IPipelineBehavior<ResourceOwnerCommand, Result<string>>>(
            _ => new ConsumerBehavior());
        services.Insert(services.Count, factory);

        services.AddTrellisBehaviors();

        services.Should().Contain(factory);
        services.Should().ContainSingle(descriptor => IsContext(descriptor));
        services.Single(IsContext).ImplementationType.Should().Be(typeof(AuthorizationContextBehavior<,>));
    }

    [Fact]
    public void AddResourceAuthorization_OptionsAndLoaderOnly_DoesNotInstallDispatchPipeline()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(options => options.HideExistence<TestResource>());
        services.AddSharedResourceLoader<ViaCommand, Leaf, string>();

        services.Should().NotContain(descriptor => IsContext(descriptor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddResourceAuthorization_WithoutStandardBehaviors_PlacesContextAndResourceBeforeTransaction(bool transactionFirst)
    {
        var services = new ServiceCollection();
        if (transactionFirst)
            services.AddTransactionalCommandBehavior();
        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();
        if (!transactionFirst)
            services.AddTransactionalCommandBehavior();

        ApplicableDescriptors<ResourceOwnerCommand, Result<string>>(services).Select(descriptor => descriptor.ImplementationType)
            .Should().Equal(
                typeof(AuthorizationContextBehavior<ResourceOwnerCommand, Result<string>>),
                typeof(ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>),
                typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddResourceAuthorization_OpenAndClosedContext_RemovesKnownClosedDuplicate()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IPipelineBehavior<ResourceOwnerCommand, Result<string>>,
            AuthorizationContextBehavior<ResourceOwnerCommand, Result<string>>>();

        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();

        ApplicableDescriptors<ResourceOwnerCommand, Result<string>>(services).Should().ContainSingle(descriptor => IsContext(descriptor));
    }

    [Fact]
    public void AddTrellisBehaviors_DuplicateKnownOpenContexts_PreservesKeyedAndFactoryRegistrations()
    {
        var services = new ServiceCollection();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationContextBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationContextBehavior<,>));
        services.AddKeyedScoped<IPipelineBehavior<ResourceOwnerCommand, Result<string>>,
            AuthorizationContextBehavior<ResourceOwnerCommand, Result<string>>>("consumer");
        var keyed = services.Last();
        var factory = ServiceDescriptor.Scoped<IPipelineBehavior<ResourceOwnerCommand, Result<string>>>(_ => new ConsumerBehavior());
        services.Insert(services.Count, factory);

        services.AddTrellisBehaviors();
        services.Where(descriptor => !descriptor.IsKeyedService)
            .Should().ContainSingle(descriptor => IsContext(descriptor));
        services.AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>();

        services.Should().Contain(keyed).And.Contain(factory);
        services.Where(descriptor => !descriptor.IsKeyedService)
            .Should().ContainSingle(descriptor => IsContext(descriptor));
    }

    private static List<ServiceDescriptor> ApplicableDescriptors<TMessage, TResponse>(IServiceCollection services)
        where TMessage : IMessage
        => services.Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            || descriptor.ServiceType == typeof(IPipelineBehavior<TMessage, TResponse>)).ToList();

    private static bool IsContext(ServiceDescriptor descriptor)
        => descriptor.ImplementationType is { IsGenericType: true } implementation
            && implementation.GetGenericTypeDefinition() == typeof(AuthorizationContextBehavior<,>);

    private sealed class ConsumerBehavior : IPipelineBehavior<ResourceOwnerCommand, Result<string>>
    {
        public ValueTask<Result<string>> Handle(ResourceOwnerCommand message,
            MessageHandlerDelegate<ResourceOwnerCommand, Result<string>> next, CancellationToken cancellationToken)
            => next(message, cancellationToken);
    }

    private sealed record ViaCommand(string Id) : ICommand<Result<string>>, IAuthorizeResourceVia<Owner>,
        IIdentifyResource<Leaf, string>
    {
        public string GetResourceId() => Id;
        public IResult Authorize(Actor actor, IReadOnlyList<Owner> owners) => Result.Ok();
    }

    private sealed record Leaf(string Id, string OwnerId) : IIdentifyRelatedResource<Owner, string>
    {
        public string GetRelatedResourceId() => OwnerId;
    }

    private sealed record Owner(string Id);
}
