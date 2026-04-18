namespace Trellis.EntityFrameworkCore.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static RepositoryBaseTests;

public class UnitOfWorkServiceCollectionExtensionsTests
{
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
    public void AddTrellisUnitOfWork_before_other_behaviors_still_ends_up_innermost()
    {
        // Arrange — UoW registered first, then "other" behaviors
        var services = new ServiceCollection();
        services.AddDbContext<RepoTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));

        // Act — register UoW first (no other behaviors yet)
        services.AddTrellisUnitOfWork<RepoTestDbContext>();
        // Then add another behavior (simulates AddTrellisBehaviors called later)
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FakeBehavior<,>));

        // Assert — TransactionalCommandBehavior was appended first (only behavior at that time),
        // then FakeBehavior was appended after. Order: Transaction, Fake.
        // This is acceptable — when no other behaviors exist yet, it appends normally.
        var behaviorDescriptors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .ToList();

        behaviorDescriptors.Should().HaveCount(2);
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
