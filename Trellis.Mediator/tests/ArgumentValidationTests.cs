namespace Trellis.Mediator.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Inspection findings <b>m-1</b> (constructor null guards) and <b>m-2</b>
/// (<c>IServiceCollection</c> extension-method null guards) for
/// <c>Trellis.Mediator</c>. Every public type that takes constructor dependencies and every
/// public DI extension method on <see cref="IServiceCollection"/> must throw
/// <see cref="ArgumentNullException"/> with the offending parameter name when called with
/// a null argument — matching the framework discipline established by Trellis.Core 2.3-2 /
/// 2.3-7 and tightened in PR #458 (Trellis.Authorization) / PR #457 (Trellis.Asp i-8).
/// </summary>
public class ArgumentValidationTests
{
    #region Behavior constructor null-guards (m-1)

    [Fact]
    public void AuthorizationBehavior_NullActorProvider_Throws() =>
        FluentActions
            .Invoking(() => new AuthorizationBehavior<AdminCommand, Result<string>>(actorProvider: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "actorProvider");

    [Fact]
    public void ResourceAuthorizationBehavior_NullActorProvider_Throws() =>
        FluentActions
            .Invoking(() => new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
                actorProvider: null!,
                serviceProvider: new ServiceCollection().BuildServiceProvider()))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "actorProvider");

    [Fact]
    public void ResourceAuthorizationBehavior_NullServiceProvider_Throws() =>
        FluentActions
            .Invoking(() => new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
                actorProvider: FakeActorProvider.NoPermissions(),
                serviceProvider: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "serviceProvider");

    [Fact]
    public void ValidationBehavior_NullValidators_Throws() =>
        FluentActions
            .Invoking(() => new ValidationBehavior<TestCommand, Result<string>>(validators: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "validators");

    [Fact]
    public void LoggingBehavior_NullLogger_Throws() =>
        FluentActions
            .Invoking(() => new LoggingBehavior<TestCommand, Result<string>>(logger: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "logger");

    [Fact]
    public void ExceptionBehavior_NullLogger_Throws() =>
        FluentActions
            .Invoking(() => new ExceptionBehavior<TestCommand, Result<string>>(logger: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "logger");

    [Fact]
    public void DomainEventDispatchBehavior_NullPublisher_Throws() =>
        FluentActions
            .Invoking(() => new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
                publisher: null!,
                logger: NullLogger<DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>.Instance))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "publisher");

    [Fact]
    public void DomainEventDispatchBehavior_NullLogger_Throws() =>
        FluentActions
            .Invoking(() => new DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>(
                publisher: new InertPublisher(),
                logger: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "logger");

    /// <summary>
    /// PR #459 review feedback: the null-guard suite must also lock in the
    /// <see cref="MediatorDomainEventPublisher"/> constructor contract introduced by m-1.
    /// The publisher is internal but its constructor is reachable from the
    /// <c>Trellis.Mediator.Tests</c> assembly via InternalsVisibleTo (repo-wide convention);
    /// custom DI containers and the <c>AddDomainEventDispatch</c> registration path also reach
    /// the constructor, so an unguarded null would surface as a confusing
    /// <see cref="NullReferenceException"/> later from inside <c>PublishAsync</c>.
    /// </summary>
    [Fact]
    public void MediatorDomainEventPublisher_NullServiceProvider_Throws() =>
        FluentActions
            .Invoking(() => new MediatorDomainEventPublisher(
                serviceProvider: null!,
                logger: NullLogger<MediatorDomainEventPublisher>.Instance))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "serviceProvider");

    [Fact]
    public void MediatorDomainEventPublisher_NullLogger_Throws() =>
        FluentActions
            .Invoking(() => new MediatorDomainEventPublisher(
                serviceProvider: new ServiceCollection().BuildServiceProvider(),
                logger: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "logger");

    #endregion

    #region IServiceCollection extension-method null-guards (m-2)

    [Fact]
    public void AddTrellisBehaviors_NullServices_Throws() =>
        FluentActions
            .Invoking(() => ServiceCollectionExtensions.AddTrellisBehaviors(services: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    [Fact]
    public void AddTrellisBehaviors_Configure_NullServices_Throws() =>
        FluentActions
            .Invoking(() => ServiceCollectionExtensions.AddTrellisBehaviors(services: null!, configure: _ => { }))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    [Fact]
    public void AddResourceAuthorization_Generic_NullServices_Throws() =>
        FluentActions
            .Invoking(() => ServiceCollectionExtensions
                .AddResourceAuthorization<ResourceOwnerCommand, TestResource, Result<string>>(services: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    [Fact]
    public void AddResourceAuthorization_Scanning_NullServices_Throws() =>
        FluentActions
            .Invoking(() => ServiceCollectionExtensions
                .AddResourceAuthorization(services: null!, assemblies: typeof(ArgumentValidationTests).Assembly))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    [Fact]
    public void AddResourceLoaders_NullServices_Throws() =>
        FluentActions
            .Invoking(() => ServiceCollectionExtensions
                .AddResourceLoaders(services: null!, assembly: typeof(ArgumentValidationTests).Assembly))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    [Fact]
    public void AddResourceLoaders_NullAssembly_Throws() =>
        FluentActions
            .Invoking(() => new ServiceCollection()
                .AddResourceLoaders(assembly: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "assembly");

    [Fact]
    public void AddSharedResourceLoader_NullServices_Throws() =>
        FluentActions
            .Invoking(() => ServiceCollectionExtensions
                .AddSharedResourceLoader<IdentifiedResourceCommand, TestResource, string>(services: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    #endregion

    #region Helpers

    /// <summary>
    /// Test command implementing both <see cref="IAuthorizeResource{TResource}"/> and
    /// <see cref="IIdentifyResource{TResource, TId}"/> for the AddSharedResourceLoader null-guard test.
    /// </summary>
    internal sealed record IdentifiedResourceCommand(string ResourceId)
        : global::Mediator.ICommand<Result<string>>,
          IAuthorizeResource<TestResource>,
          IIdentifyResource<TestResource, string>
    {
        public string GetResourceId() => ResourceId;

        public IResult Authorize(Actor actor, TestResource resource) => Result.Ok();
    }

    private sealed class InertPublisher : IDomainEventPublisher
    {
        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    #endregion
}
