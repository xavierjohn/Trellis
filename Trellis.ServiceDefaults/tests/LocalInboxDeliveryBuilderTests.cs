namespace Trellis.ServiceDefaults.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

public sealed class LocalInboxDeliveryBuilderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LocalInboxDelivery_BeforeOrAfterUnitOfWork_ReplacesDefaultWithoutChangingPipeline(
        bool useBuilder, bool localFirst)
    {
        var baseline = new ServiceCollection();
        baseline.AddLogging();
        baseline.AddTrellis(builder => ConfigureMessaging(builder).UseEntityFrameworkUnitOfWork<DeliveryDbContext>());

        var services = new ServiceCollection();
        services.AddLogging();
        if (useBuilder)
        {
            services.AddTrellis(builder =>
            {
                ConfigureMessaging(builder);
                if (localFirst)
                    builder.UseLocalInboxDelivery();
                builder.UseEntityFrameworkUnitOfWork<DeliveryDbContext>();
                if (!localFirst)
                    builder.UseLocalInboxDelivery();
            });
        }
        else
        {
            services.AddTrellis(builder => ConfigureMessaging(builder));
            if (localFirst)
                services.AddLocalInboxDelivery();
            services.AddTrellisUnitOfWork<DeliveryDbContext>();
            if (!localFirst)
                services.AddLocalInboxDelivery();
        }

        services.AddIntegrationEventDispatch();
        services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)).Select(d => d.ImplementationType)
            .Should().Equal(baseline.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
                .Select(d => d.ImplementationType));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        using var baselineProvider = baseline.BuildServiceProvider(validateScopes: true);
        using var baselineScope = baselineProvider.CreateScope();
        var selected = scope.ServiceProvider.GetServices<IIntegrationEventPublisher>().Should().ContainSingle().Which;
        var defaultPublisher = baselineScope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        selected.GetType().Should().NotBe(defaultPublisher.GetType());
        scope.ServiceProvider.GetRequiredService<IInboxDispatcher>().Should().NotBeNull();
    }

    private static TrellisServiceBuilder ConfigureMessaging(TrellisServiceBuilder builder) =>
        builder.UseMediator()
            .UseDomainEvents()
            .UseIntegrationEvents()
            .UseInbox<DeliveryDbContext>(o => o.ConsumerId = "local");

    private sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options);
}