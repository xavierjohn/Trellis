namespace Trellis.ServiceDefaults.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Unit = Trellis.Unit;

public class SharedResourceAuthorizationBuilderTests
{
    [Fact]
    public async Task UseSharedResourceAuthorization_Typed_Dispatch_ResolvesSharedResource()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IActorProvider, ActorProvider>();
        services.AddScoped<SharedResourceLoaderById<Resource, string>, ResourceLoader>();
        services.AddTrellis(options => options.UseSharedResourceAuthorization<Command, Resource, string, Result<Unit>>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<Command, Result<Unit>>>().ToArray();
        var behavior = behaviors.OfType<ResourceAuthorizationBehavior<Command, Resource, Result<Unit>>>().Single();
        var accessor = scope.ServiceProvider.GetRequiredService<IAuthorizedResource<Command, Resource>>();
        var handlerCalled = false;

        var result = await behavior.Handle(new Command("order-42"), (_, _) =>
        {
            handlerCalled = true;
            accessor.GetRequiredResource().Id.Should().Be("order-42");
            return ValueTask.FromResult(Result.Ok());
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        handlerCalled.Should().BeTrue();
        behaviors.Should().Contain(b => b is ValidationBehavior<Command, Result<Unit>>);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseSharedResourceAuthorization_WithScanning_CustomLoaderWinsAfterDirectRegistration(bool directFirst)
    {
        var services = new ServiceCollection();
        if (directFirst)
            services.AddSharedResourceAuthorization<Command, Resource, string, Result<Unit>>();

        services.AddTrellis(options => options
            .UseSharedResourceAuthorization<Command, Resource, string, Result<Unit>>()
            .UseResourceAuthorization(typeof(PerMessageResourceLoader).Assembly));

        if (!directFirst)
            services.AddSharedResourceAuthorization<Command, Resource, string, Result<Unit>>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IResourceLoader<Command, Resource>>()
            .Should().BeOfType<PerMessageResourceLoader>();
        services.Should().ContainSingle(d => d.ServiceType == typeof(IResourceLoader<Command, Resource>));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseSharedResourceAuthorization_Typed_DirectAndBuilderOverlap_IsIdempotent(bool directFirst)
    {
        var services = new ServiceCollection();
        if (directFirst)
            services.AddSharedResourceAuthorization<Command, Resource, string, Result<Unit>>();

        services.AddTrellis(options => options
            .UseResourceAuthorization<Command, Resource, Result<Unit>>()
            .UseSharedResourceAuthorization<Command, Resource, string, Result<Unit>>()
            .UseSharedResourceAuthorization<Command, Resource, string, Result<Unit>>());

        if (!directFirst)
            services.AddSharedResourceAuthorization<Command, Resource, string, Result<Unit>>();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IPipelineBehavior<Command, Result<Unit>>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IResourceLoader<Command, Resource>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IAuthorizedResource<Command, Resource>));
        services.Should().NotContain(d => d.ServiceType == typeof(SharedResourceLoaderById<Resource, string>));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseSharedResourceAuthorization_Typed_BeforeOrAfterUnitOfWork_PreservesCanonicalOrder(bool unitOfWorkFirst)
    {
        var services = new ServiceCollection();
        services.AddTrellis(options =>
        {
            if (unitOfWorkFirst)
                options.UseEntityFrameworkUnitOfWork<TestDbContext>();

            options.UseSharedResourceAuthorization<Command, Resource, string, Result<Unit>>().UseDomainEvents();

            if (!unitOfWorkFirst)
                options.UseEntityFrameworkUnitOfWork<TestDbContext>();
        });

        AssertCanonicalOrder(services);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AddSharedResourceAuthorization_Typed_MixedRegistrationOrder_PreservesCanonicalOrder(
        bool unitOfWorkFirst, bool mediatorFirst)
    {
        var services = new ServiceCollection();
        if (mediatorFirst)
            services.AddTrellisBehaviors();
        if (unitOfWorkFirst)
            services.AddTrellisUnitOfWork<TestDbContext>();

        services.AddSharedResourceAuthorization<Command, Resource, string, Result<Unit>>();
        services.AddDomainEventDispatch();

        if (!unitOfWorkFirst)
            services.AddTrellisUnitOfWork<TestDbContext>();
        if (!mediatorFirst)
            services.AddTrellisBehaviors();

        AssertCanonicalOrder(services);
    }

    private static void AssertCanonicalOrder(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)
                || d.ServiceType == typeof(IPipelineBehavior<Command, Result<Unit>>))
            .Select(d => d.ImplementationType)
            .Should().Equal(
            [
                typeof(ExceptionBehavior<,>),
                typeof(TracingBehavior<,>),
                typeof(LoggingBehavior<,>),
                typeof(AuthorizationBehavior<,>),
                typeof(ResourceAuthorizationBehavior<Command, Resource, Result<Unit>>),
                typeof(ValidationBehavior<,>),
                typeof(DomainEventDispatchBehavior<,>),
                typeof(TransactionalCommandBehavior<,>),
            ]);

    public sealed record Resource(string Id);

    public sealed record Command(string Id) : ICommand<Result<Unit>>, IAuthorizeResource<Resource>,
        IIdentifyResource<Resource, string>
    {
        public string GetResourceId() => Id;
        public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
    }

    public sealed class ResourceLoader : SharedResourceLoaderById<Resource, string>
    {
        public override Task<Result<Resource>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new Resource(id)));
    }

    public sealed class PerMessageResourceLoader : IResourceLoader<Command, Resource>
    {
        public Task<Result<Resource>> LoadAsync(Command message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new Resource("custom-" + message.Id)));
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    private sealed class ActorProvider : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(Actor.Create("owner-1", new HashSet<string>())));
    }
}
