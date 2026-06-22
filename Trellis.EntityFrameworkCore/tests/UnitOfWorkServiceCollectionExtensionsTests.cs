namespace Trellis.EntityFrameworkCore.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;
using static RepositoryBaseTests;

public class UnitOfWorkServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTrellisUnitOfWork_is_idempotent_when_called_twice()
    {
        // ga-10: AddTrellisUnitOfWork is safe to call from a plug-in extension method
        // (or composed twice in test setup) without producing duplicate IUnitOfWork
        // registrations or duplicate TransactionalCommandBehavior pipeline entries.
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());

        services.AddTrellisUnitOfWork<RepoTestDbContext>();
        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        services.Where(d => d.ServiceType == typeof(IUnitOfWork)).Should().ContainSingle();
        services.Where(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TransactionalCommandBehavior<,>))
            .Should().ContainSingle();
    }

    [Fact]
    public void AddTrellisUnitOfWork_ClosedTransactionalBehaviorPreRegistered_ThrowsWithActionableMessage()
    {
        // Adding the open generic alongside a pre-existing closed-generic
        // TransactionalCommandBehavior<TMessage,TResponse> would resolve both descriptors for
        // matching commands, producing two commits per command. The helper fails fast and tells
        // the consumer the two supported resolutions.
        var services = CreateServices();
        services.AddScoped<
            IPipelineBehavior<ClosedTransactionalCommand, Result<Unit>>,
            TransactionalCommandBehavior<ClosedTransactionalCommand, Result<Unit>>>();

        var act = () => services.AddTrellisUnitOfWork<RepoTestDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TransactionalCommandBehavior*closed*generic*")
            .WithMessage("*AddTrellisUnitOfWorkWithoutBehavior*");
    }

    [Fact]
    public void AddTrellisUnitOfWork_OpenTransactionalBehaviorPreRegistered_SkipsDuplicateRegistration()
    {
        var services = CreateServices();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));

        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        TransactionalBehaviorDescriptorsFor<ClosedTransactionalCommand, Result<Unit>>(services)
            .Should().ContainSingle()
            .Which.ServiceType.Should().Be(typeof(IPipelineBehavior<,>));
    }

    [Fact]
    public void AddTrellisUnitOfWork_OpenAndClosedTransactionalBehaviorsPreRegistered_ThrowsBecauseClosedConflictsWithOpen()
    {
        // The open + closed pair was already a double-fire bug before the helper was called;
        // surface it instead of silently leaving the broken pair in place.
        var services = CreateServices();
        services.AddScoped<
            IPipelineBehavior<ClosedTransactionalCommand, Result<Unit>>,
            TransactionalCommandBehavior<ClosedTransactionalCommand, Result<Unit>>>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));

        var act = () => services.AddTrellisUnitOfWork<RepoTestDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TransactionalCommandBehavior*closed*generic*");
    }

    [Fact]
    public void AddTrellisUnitOfWorkWithoutBehavior_ClosedTransactionalBehaviorPreRegistered_DoesNotThrow()
    {
        // The without-behavior helper explicitly opts out of open-generic installation, so the
        // consumer's closed registration is intentional and not in conflict.
        var services = CreateServices();
        services.AddScoped<
            IPipelineBehavior<ClosedTransactionalCommand, Result<Unit>>,
            TransactionalCommandBehavior<ClosedTransactionalCommand, Result<Unit>>>();

        var act = () => services.AddTrellisUnitOfWorkWithoutBehavior<RepoTestDbContext>();

        act.Should().NotThrow();
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrellisUnitOfWork_ClosedTransactionalBehaviorPreRegisteredViaInstance_ThrowsWithActionableMessage()
    {
        // Singleton-style closed registration via ImplementationInstance must also be detected
        // (Copilot pre-merge review caught this gap in PR #563), AND the error must name the
        // concrete implementation type even though ImplementationType is null for instance
        // registrations (a second Copilot review round called out the empty implementation slot).
        var services = CreateServices();
        var preBuiltBehavior = new TransactionalCommandBehavior<ClosedTransactionalCommand, Result<Unit>>(
            new NoopUnitOfWork());
        services.AddSingleton<IPipelineBehavior<ClosedTransactionalCommand, Result<Unit>>>(preBuiltBehavior);

        var act = () => services.AddTrellisUnitOfWork<RepoTestDbContext>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TransactionalCommandBehavior*closed*generic*")
            .WithMessage("*TransactionalCommandBehavior`2*")
            .WithMessage("*AddTrellisUnitOfWorkWithoutBehavior*");
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<global::Trellis.Result<global::Trellis.Unit>> CommitAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(global::Trellis.Result.Ok());

        public IDisposable BeginScope() => new NoopScope();

        private sealed class NoopScope : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void AddTrellisUnitOfWork_NoTransactionalBehaviorPreRegistered_AddsOneOpenGenericRegistration()
    {
        var services = CreateServices();

        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        TransactionalBehaviorDescriptorsFor<ClosedTransactionalCommand, Result<Unit>>(services)
            .Should().ContainSingle()
            .Which.ServiceType.Should().Be(typeof(IPipelineBehavior<,>));
    }

    [Fact]
    public void AddTrellisUnitOfWork_registers_IUnitOfWork_and_behavior()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());

        // Act
        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        // Assert
        services.Should().Contain(d => d.ServiceType == typeof(IUnitOfWork));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrellisUnitOfWorkWithoutBehavior_registers_IUnitOfWork_only()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());

        // Act
        services.AddTrellisUnitOfWorkWithoutBehavior<RepoTestDbContext>();

        // Assert
        services.Should().Contain(d => d.ServiceType == typeof(IUnitOfWork));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrellisUnitOfWork_inserts_behavior_after_existing_behaviors()
    {
        // Arrange — register a fake behavior first (simulates AddTrellisBehaviors)
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FakeBehavior<,>));

        // Act
        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        // Assert — TransactionalCommandBehavior should be AFTER FakeBehavior
        var behaviorDescriptors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .ToList();

        behaviorDescriptors.Should().HaveCount(2);
        behaviorDescriptors[0].ImplementationType.Should().Be(typeof(FakeBehavior<,>));
        behaviorDescriptors[1].ImplementationType.Should().Be(typeof(TransactionalCommandBehavior<,>));
    }

    [Fact]
    public void AddTrellisUnitOfWork_before_other_behaviors_appends_at_end()
    {
        // Arrange — UoW registered first, then "other" behaviors added later
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());

        // Act — register UoW first (no other behaviors yet), then add another behavior
        services.AddTrellisUnitOfWork<RepoTestDbContext>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FakeBehavior<,>));

        // Assert — TransactionalCommandBehavior was appended first (only behavior at that time),
        // then FakeBehavior was appended after. Order: Transaction, Fake.
        // For correct ordering, AddTrellisUnitOfWork should be called AFTER other behaviors.
        var behaviorDescriptors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .ToList();

        behaviorDescriptors.Should().HaveCount(2);
        behaviorDescriptors[0].ImplementationType.Should().Be(typeof(TransactionalCommandBehavior<,>));
        behaviorDescriptors[1].ImplementationType.Should().Be(typeof(FakeBehavior<,>));
    }

    [Fact]
    public void AddTrellisUnitOfWork_resolves_IUnitOfWork_from_provider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());
        services.AddTrellisUnitOfWork<RepoTestDbContext>();
        using var provider = services.BuildServiceProvider();

        // Act
        var uow = provider.GetRequiredService<IUnitOfWork>();

        // Assert
        uow.Should().BeOfType<EfUnitOfWork<RepoTestDbContext>>();
    }

    #region Test Infrastructure

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:").IgnoreManyServiceProvidersCreatedWarning());
        return services;
    }

    private static List<ServiceDescriptor> TransactionalBehaviorDescriptorsFor<TMessage, TResponse>(
        IServiceCollection services)
        where TMessage : ICommand<TResponse>
        where TResponse : IResult, IFailureFactory<TResponse> =>
        services.Where(IsTransactionalBehaviorDescriptorFor<TMessage, TResponse>).ToList();

    private static bool IsTransactionalBehaviorDescriptorFor<TMessage, TResponse>(ServiceDescriptor descriptor)
        where TMessage : ICommand<TResponse>
        where TResponse : IResult, IFailureFactory<TResponse>
    {
        var serviceType = descriptor.ServiceType;
        var implementationType = descriptor.ImplementationType;

        return (serviceType == typeof(IPipelineBehavior<,>)
                && implementationType == typeof(TransactionalCommandBehavior<,>))
            || (serviceType == typeof(IPipelineBehavior<TMessage, TResponse>)
                && implementationType == typeof(TransactionalCommandBehavior<TMessage, TResponse>));
    }

    private sealed record ClosedTransactionalCommand : ICommand<Result<Unit>>;

    private sealed class FakeBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : IMessage
    {
        public ValueTask<TResponse> Handle(
            TMessage message,
            MessageHandlerDelegate<TMessage, TResponse> next,
            CancellationToken cancellationToken) => next(message, cancellationToken);
    }

    #endregion
}