namespace Trellis.EntityFrameworkCore.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        services.AddTrellisUnitOfWork<RepoTestDbContext>();
        services.AddTrellisUnitOfWork<RepoTestDbContext>();

        services.Where(d => d.ServiceType == typeof(IUnitOfWork)).Should().ContainSingle();
        services.Where(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TransactionalCommandBehavior<,>))
            .Should().ContainSingle();
    }

    [Fact]
    public void AddTrellisUnitOfWork_registers_IUnitOfWork_and_behavior()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));

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
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));

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
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
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
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));

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
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddTrellisUnitOfWork<RepoTestDbContext>();
        using var provider = services.BuildServiceProvider();

        // Act
        var uow = provider.GetRequiredService<IUnitOfWork>();

        // Assert
        uow.Should().BeOfType<EfUnitOfWork<RepoTestDbContext>>();
    }

    #region Test Infrastructure

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
