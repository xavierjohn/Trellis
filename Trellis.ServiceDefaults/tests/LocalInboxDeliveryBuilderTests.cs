namespace Trellis.ServiceDefaults.Tests;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;

public sealed class LocalInboxDeliveryBuilderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LocalInboxDelivery_BeforeOrAfterUnitOfWork_DispatchesThroughInboxWithoutChangingPipeline(
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

        var inbox = new RecordingInbox();
        services.Replace(ServiceDescriptor.Singleton<IInboxDispatcher>(inbox));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var selected = scope.ServiceProvider.GetServices<IIntegrationEventPublisher>().Should().ContainSingle().Which;
        var message = new OutboundIntegrationMessage(Guid.NewGuid(), new DeliveryEvent(DateTimeOffset.UnixEpoch));

        await selected.PublishAsync(message, TestContext.Current.CancellationToken);

        inbox.Received.Should().Be(new IntegrationEnvelope(message.MessageId, message.Event));
    }

    [Fact]
    public void UseLocalInboxDelivery_WithoutInbox_NamesRequiredRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellis(builder => builder.UseLocalInboxDelivery());

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseInbox<TContext>()*");
    }

    [Fact]
    public void UseLocalInboxDelivery_WithExistingCustomPublisher_RejectsConflict()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventPublisher, CustomPublisher>();

        var act = () => services.AddTrellis(builder => ConfigureMessaging(builder).UseLocalInboxDelivery());

        act.Should().Throw<InvalidOperationException>().WithMessage("*custom integration-event publisher*");
    }

    [Fact]
    public async Task UseLocalInboxDelivery_WithPreviouslyRegisteredDispatcher_UsesIt()
    {
        var services = new ServiceCollection();
        var inbox = new RecordingInbox();
        services.AddSingleton<IInboxDispatcher>(inbox);
        services.AddTrellis(builder => builder.UseLocalInboxDelivery());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var message = new OutboundIntegrationMessage(Guid.NewGuid(), new DeliveryEvent(DateTimeOffset.UnixEpoch));

        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
            .PublishAsync(message, TestContext.Current.CancellationToken);

        inbox.Received.Should().Be(new IntegrationEnvelope(message.MessageId, message.Event));
    }

    private static TrellisServiceBuilder ConfigureMessaging(TrellisServiceBuilder builder) =>
        builder.UseMediator()
            .UseDomainEvents()
            .UseIntegrationEvents()
            .UseInbox<DeliveryDbContext>(o => o.ConsumerId = "local");

    private sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options);

    private sealed record DeliveryEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;

    private sealed class RecordingInbox : IInboxDispatcher
    {
        public IntegrationEnvelope? Received { get; private set; }

        public Task<InboxDispatchOutcome> DispatchAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
        {
            Received = envelope;
            return Task.FromResult(InboxDispatchOutcome.Processed);
        }
    }

    private sealed class CustomPublisher : IIntegrationEventPublisher
    {
        public ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}