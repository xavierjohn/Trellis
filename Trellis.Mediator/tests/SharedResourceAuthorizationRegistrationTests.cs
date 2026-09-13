namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;
using Trellis.Testing;
using Unit = global::Mediator.Unit;
using static Trellis.Mediator.Tests.SharedResourceLoaderTests;

public class SharedResourceAuthorizationRegistrationTests
{
    [Fact]
    public void AddSharedResourceAuthorization_Typed_RepeatedRegistration_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddResourceAuthorization<SharedCancelCommand, SharedOrder, Result<Unit>>();
        services.AddSharedResourceLoader<SharedCancelCommand, SharedOrder, string>();

        var returned = services.AddSharedResourceAuthorization<SharedCancelCommand, SharedOrder, string, Result<Unit>>();
        services.AddSharedResourceAuthorization<SharedCancelCommand, SharedOrder, string, Result<Unit>>();

        returned.Should().BeSameAs(services);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IPipelineBehavior<SharedCancelCommand, Result<Unit>>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IAuthorizedResource<SharedCancelCommand, SharedOrder>));
        services.Should().OnlyContain(d => d.ServiceType != typeof(SharedResourceLoaderById<SharedOrder, string>));
        services.Where(d => d.ServiceType == typeof(IPipelineBehavior<SharedCancelCommand, Result<Unit>>)
            || d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>)
            || d.ServiceType == typeof(IAuthorizedResource<SharedCancelCommand, SharedOrder>))
            .Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddSharedResourceAuthorization_Typed_WithScanning_RegistersEachComponentOnce(bool scanFirst)
    {
        var services = new ServiceCollection();
        if (scanFirst)
            services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        services.AddSharedResourceAuthorization<SharedCancelCommand, SharedOrder, string, Result<Unit>>();

        if (!scanFirst)
            services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IPipelineBehavior<SharedCancelCommand, Result<Unit>>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IAuthorizedResource<SharedCancelCommand, SharedOrder>));
    }

    [Fact]
    public async Task AddSharedResourceAuthorization_Typed_Dispatch_LoadsOnceAndPopulatesAccessor()
    {
        var order = new SharedOrder("order-42", "owner-1");
        var loader = new RecordingLoader<SharedCancelCommand>(Result.Ok(order));
        var services = new ServiceCollection();
        services.AddSingleton<IActorProvider>(FakeActorProvider.NoPermissions());
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>>(_ => loader);
        services.AddSharedResourceAuthorization<SharedCancelCommand, SharedOrder, string, Result<Unit>>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var behavior = scope.ServiceProvider.GetRequiredService<IPipelineBehavior<SharedCancelCommand, Result<Unit>>>();
        var accessor = scope.ServiceProvider.GetRequiredService<IAuthorizedResource<SharedCancelCommand, SharedOrder>>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var handlerCalled = false;

        var result = await behavior.Handle(new SharedCancelCommand(order.Id), (_, ct) =>
        {
            handlerCalled = true;
            ct.Should().Be(cancellationToken);
            accessor.GetRequiredResource().Should().BeSameAs(order);
            return ValueTask.FromResult(Result.Ok(default(Unit)));
        }, cancellationToken);

        result.Should().BeSuccess();
        handlerCalled.Should().BeTrue();
        loader.Calls.Should().Be(1);
        loader.LastId.Should().Be(order.Id);
        loader.LastToken.Should().Be(cancellationToken);
        accessor.TryGetResource(out _).Should().BeFalse();
    }

    [Fact]
    public async Task AddSharedResourceAuthorization_Typed_LoadFailure_ShortCircuitsHandler()
    {
        var error = new Error.Forbidden("orders.hidden");
        var loader = new RecordingLoader<SharedCancelCommand>(Result.Fail<SharedOrder>(error));
        var services = new ServiceCollection();
        services.AddSingleton<IActorProvider>(FakeActorProvider.NoPermissions());
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>>(_ => loader);
        services.AddSharedResourceAuthorization<SharedCancelCommand, SharedOrder, string, Result<Unit>>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var behavior = scope.ServiceProvider.GetRequiredService<IPipelineBehavior<SharedCancelCommand, Result<Unit>>>();
        var handlerCalled = false;

        var result = await behavior.Handle(new SharedCancelCommand("order-1"), (_, _) =>
        {
            handlerCalled = true;
            return ValueTask.FromResult(Result.Ok(default(Unit)));
        }, TestContext.Current.CancellationToken);

        result.UnwrapError().Should().BeSameAs(error);
        handlerCalled.Should().BeFalse();
        loader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AddSharedResourceAuthorization_Typed_PreRegisteredLoader_IsPreserved()
    {
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ExplicitLoaderCommand, SharedOrder>, ExplicitOrderLoader>();

        services.AddSharedResourceAuthorization<ExplicitLoaderCommand, SharedOrder, string, Result<Unit>>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>();
        var result = await loader.LoadAsync(new ExplicitLoaderCommand("order-1"), TestContext.Current.CancellationToken);

        loader.Should().BeOfType<ExplicitOrderLoader>();
        result.Unwrap().OwnerId.Should().Be("explicit-owner");
        services.Should().ContainSingle(d => d.ServiceType == typeof(IResourceLoader<ExplicitLoaderCommand, SharedOrder>));
    }

    [Fact]
    public void AddSharedResourceAuthorization_Typed_MissingSharedImplementation_ThrowsOnResolution()
    {
        var services = new ServiceCollection();
        services.AddSharedResourceAuthorization<SharedCancelCommand, SharedOrder, string, Result<Unit>>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IResourceLoader<SharedCancelCommand, SharedOrder>>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*SharedResourceLoaderById*");
    }

    [Fact]
    public void AddSharedResourceAuthorization_Typed_DualModeMessage_RejectsBeforeRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedResourceAuthorization<DualModeCommand<int>, SharedOrder, string, Result<Unit>>();

        act.Should().Throw<InvalidOperationException>();
        services.Should().BeEmpty();
    }

    [Fact]
    public void AddSharedResourceAuthorization_Typed_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddSharedResourceAuthorization<SharedCancelCommand, SharedOrder, string, Result<Unit>>();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddResourceAuthorization_Typed_WithoutConvenience_DoesNotRegisterLoader()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<SharedCancelCommand, SharedOrder, Result<Unit>>();

        services.Should().NotContain(d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));
    }

    // Keep the test double open-generic so unrelated assembly scans cannot discover it.
    private sealed class RecordingLoader<TMessage>(Result<SharedOrder> result) : SharedResourceLoaderById<SharedOrder, string>
    {
        public int Calls { get; private set; }
        public string? LastId { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public override Task<Result<SharedOrder>> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            Calls++;
            LastId = id;
            LastToken = cancellationToken;
            return Task.FromResult(result);
        }
    }

    private sealed record DualModeCommand<T> : IMessage, IAuthorizeResource<SharedOrder>,
        IIdentifyResource<SharedOrder, string>, IAuthorizeResourceVia<SharedOrder>
    {
        public string GetResourceId() => "order-1";
        public IResult Authorize(Actor actor, SharedOrder resource) => Result.Ok();
        public IResult Authorize(Actor actor, IReadOnlyList<SharedOrder> owners) => Result.Ok();
    }
}
