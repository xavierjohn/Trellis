using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.AddResourceLoaders"/>.
/// </summary>
public class AddResourceLoadersTests
{
    #region Scan registers concrete loaders

    [Fact]
    public void AddResourceLoaders_RegistersConcreteLoaderFromAssembly()
    {
        var services = new ServiceCollection();

        services.AddResourceLoaders(typeof(TestLoader).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<TestMessage, TestEntity>));

        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be<TestLoader>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Scan skips abstract and interface types

    [Fact]
    public void AddResourceLoaders_SkipsAbstractTypes()
    {
        var services = new ServiceCollection();

        services.AddResourceLoaders(typeof(AbstractLoader).Assembly);

        services.Should().NotContain(
            d => d.ImplementationType == typeof(AbstractLoader));
    }

    #endregion

    #region Resolved loader is functional

    [Fact]
    public async Task AddResourceLoaders_ResolvedLoaderIsUsable()
    {
        var services = new ServiceCollection();
        services.AddResourceLoaders(typeof(TestLoader).Assembly);
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IResourceLoader<TestMessage, TestEntity>>();
        var result = await loader.LoadAsync(new TestMessage("id-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Id.Should().Be("id-1");
    }

    #endregion

    #region Returns service collection for chaining

    [Fact]
    public void AddResourceLoaders_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddResourceLoaders(typeof(TestLoader).Assembly);

        returned.Should().BeSameAs(services);
    }

    #endregion

    #region Test helpers

    public sealed record TestMessage(string Id);
    public sealed record TestEntity(string Id);

    public sealed class TestLoader : IResourceLoader<TestMessage, TestEntity>
    {
        public Task<Result<TestEntity>> LoadAsync(TestMessage message, CancellationToken cancellationToken)
            => Task.FromResult(Result.Ok(new TestEntity(message.Id)));
    }

    public abstract class AbstractLoader : IResourceLoader<TestMessage, TestEntity>
    {
        public abstract Task<Result<TestEntity>> LoadAsync(TestMessage message, CancellationToken cancellationToken);
    }

    #endregion
}

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.AddResourceAuthorization{TMessage, TResource, TResponse}"/>.
/// </summary>
public class AddResourceAuthorizationTests
{
    #region Registers closed-generic behavior

    [Fact]
    public void AddResourceAuthorization_RegistersClosedGenericBehavior()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<TestAuthCommand, TestAuthResource, Result<string>>();

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IPipelineBehavior<TestAuthCommand, Result<string>>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region PipelineBehaviors does not contain 3-arity type

    [Fact]
    public void PipelineBehaviors_DoesNotContainThreeArityBehavior()
    {
        var behaviors = ServiceCollectionExtensions.PipelineBehaviors;

        behaviors.Should().NotContain(typeof(ResourceAuthorizationBehavior<,,>),
            "3-arity behavior cannot be registered as open generic against IPipelineBehavior<,>");
    }

    #endregion

    #region Returns service collection for chaining

    [Fact]
    public void AddResourceAuthorization_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddResourceAuthorization<TestAuthCommand, TestAuthResource, Result<string>>();

        returned.Should().BeSameAs(services);
    }

    #endregion

    #region Behavior resolves scoped loader with ValidateScopes enabled

    [Fact]
    public async Task AddResourceAuthorization_BehaviorResolvesWithScopeValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActorProvider>(FakeActorProvider.WithPermissions("owner-1"));
        services.AddResourceAuthorization<TestAuthCommand, TestAuthResource, Result<string>>();
        services.AddScoped<IResourceLoader<TestAuthCommand, TestAuthResource>>(_ =>
            new TestAuthResourceLoader(new TestAuthResource("res-1", "owner-1")));

        // ValidateScopes = true (ASP.NET Core default in Development)
        await using var sp = services.BuildServiceProvider(validateScopes: true);
        using var scope = sp.CreateScope();

        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<TestAuthCommand, Result<string>>>();

        behavior.Should().BeOfType<ResourceAuthorizationBehavior<TestAuthCommand, TestAuthResource, Result<string>>>();
    }

    #endregion

    #region Test helpers

    public sealed record TestAuthResource(string Id, string OwnerId);

    public sealed record TestAuthCommand(string ResourceId)
        : ICommand<Result<string>>, IAuthorizeResource<TestAuthResource>
    {
        public IResult Authorize(Actor actor, TestAuthResource resource) =>
            actor.Id == resource.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("authorization.forbidden") { Detail = "Not the owner" });
    }

    private sealed class TestAuthResourceLoader(TestAuthResource resource)
        : IResourceLoader<TestAuthCommand, TestAuthResource>
    {
        public Task<Result<TestAuthResource>> LoadAsync(TestAuthCommand message, CancellationToken cancellationToken)
            => Task.FromResult(Result.Ok(resource));
    }

    #endregion
}

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.AddResourceAuthorization(IServiceCollection, Assembly)"/>
/// (assembly-scanning overload).
/// </summary>
public class AddResourceAuthorizationScanTests
{
    #region Discovers IAuthorizeResource<T> command and registers behavior

    [Fact]
    public void AddResourceAuthorization_with_null_assembly_element_throws_with_index()
    {
        // Mirrors the guard already present in AddTrellisFluentValidation: a null entry in
        // the params Assembly[] must surface as ArgumentException pointing at the offending
        // index, NOT a NullReferenceException from Assembly.GetTypes() inside GetLoadableTypes.
        var services = new ServiceCollection();

        var act = () => services.AddResourceAuthorization(typeof(ScanTestCommand).Assembly, null!);

        act.Should().Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "assemblies")
            .And.Message.Should().Contain("[1]");
    }

    [Fact]
    public void AddResourceAuthorization_Assembly_RegistersBehaviorForCommand()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(ScanTestCommand).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IPipelineBehavior<ScanTestCommand, Result<string>>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Discovers and registers IResourceLoader implementations

    [Fact]
    public void AddResourceAuthorization_Assembly_RegistersResourceLoaders()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(ScanTestLoader).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<ScanTestCommand, ScanTestResource>));

        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be<ScanTestLoader>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Skips abstract and interface types

    [Fact]
    public void AddResourceAuthorization_Assembly_SkipsAbstractTypes()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(AbstractScanCommand).Assembly);

        services.Should().NotContain(
            d => d.ImplementationType == typeof(AbstractScanCommand));
    }

    #endregion

    #region Returns service collection for chaining

    [Fact]
    public void AddResourceAuthorization_Assembly_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddResourceAuthorization(typeof(ScanTestCommand).Assembly);

        returned.Should().BeSameAs(services);
    }

    #endregion

    #region Discovers IQuery-based commands

    [Fact]
    public void AddResourceAuthorization_Assembly_RegistersBehaviorForQuery()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(ScanTestQuery).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IPipelineBehavior<ScanTestQuery, Result<string>>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddResourceAuthorization_Assembly_ResolvesResourceAuthorizationBeforeValidation()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddResourceAuthorization(typeof(ValidatedFullAuthResourceCommand).Assembly);
        services.AddSingleton<IActorProvider>(FakeActorProvider.WithPermissions("owner-1", "Resources.Write"));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        using var serviceProvider = services.BuildServiceProvider();

        var behaviors = serviceProvider
            .GetServices<IPipelineBehavior<ValidatedFullAuthResourceCommand, Result<string>>>()
            .Select(static behavior => behavior.GetType().GetGenericTypeDefinition())
            .ToArray();

        behaviors.Should().ContainInOrder(
            typeof(ExceptionBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ResourceAuthorizationBehavior<,,>),
            typeof(ValidationBehavior<,>));
    }

    #endregion

    #region Test helpers

    public sealed record ScanTestResource(string Id, string OwnerId);

    public sealed record ScanTestCommand(string ResourceId)
        : ICommand<Result<string>>, IAuthorizeResource<ScanTestResource>
    {
        public IResult Authorize(Actor actor, ScanTestResource resource) =>
            actor.Id == resource.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("authorization.forbidden") { Detail = "Not the owner" });
    }

    public sealed record ScanTestQuery(string ResourceId)
        : IQuery<Result<string>>, IAuthorizeResource<ScanTestResource>
    {
        public IResult Authorize(Actor actor, ScanTestResource resource) =>
            actor.Id == resource.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("authorization.forbidden") { Detail = "Not the owner" });
    }

    public abstract record AbstractScanCommand(string ResourceId)
        : ICommand<Result<string>>, IAuthorizeResource<ScanTestResource>
    {
        public IResult Authorize(Actor actor, ScanTestResource resource) =>
            Result.Ok();
    }

    public sealed class ScanTestLoader : IResourceLoader<ScanTestCommand, ScanTestResource>
    {
        public Task<Result<ScanTestResource>> LoadAsync(ScanTestCommand message, CancellationToken cancellationToken)
            => Task.FromResult(Result.Ok(new ScanTestResource(message.ResourceId, "owner-1")));
    }

    #endregion
}