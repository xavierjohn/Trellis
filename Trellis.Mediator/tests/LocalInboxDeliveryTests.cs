namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public sealed class LocalInboxDeliveryTests
{
    [Theory]
    [InlineData(true, InboxDispatchOutcome.Processed)]
    [InlineData(true, InboxDispatchOutcome.SkippedDuplicate)]
    [InlineData(false, InboxDispatchOutcome.Processed)]
    [InlineData(false, InboxDispatchOutcome.SkippedDuplicate)]
    public async Task PublishAsync_SelectedBeforeOrAfterDefault_ForwardsEnvelopeAndAwaitsDispatch(
        bool selectFirst, InboxDispatchOutcome outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddScoped<RecordingInbox>();
        services.AddScoped<IInboxDispatcher>(sp => sp.GetRequiredService<RecordingInbox>());
        if (selectFirst)
            services.AddLocalInboxDelivery();
        services.AddIntegrationEventDispatch();
        if (!selectFirst)
            services.AddLocalInboxDelivery();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<RecordingInbox>();
        var publisher = scope.ServiceProvider.GetServices<IIntegrationEventPublisher>().Should().ContainSingle().Which;
        var message = new OutboundIntegrationMessage(Guid.NewGuid(), new RegEventA(DateTimeOffset.UnixEpoch))
        {
            MessageSource = "orders",
            CausationId = Guid.NewGuid(),
            CorrelationId = "workflow",
            TraceParent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            TraceState = "app=local",
        };

        var publication = publisher.PublishAsync(message, ct).AsTask();

        try
        {
            publication.IsCompleted.Should().BeFalse();
            inbox.Token.Should().Be(ct);
            inbox.Envelope.Should().Be(new IntegrationEnvelope(message.MessageId, message.Event)
            {
                MessageSource = message.MessageSource,
                CausationId = message.CausationId,
                CorrelationId = message.CorrelationId,
                TraceParent = message.TraceParent,
                TraceState = message.TraceState,
            });
        }
        finally
        {
            inbox.Completion.SetResult(outcome);
        }

        await publication.WaitAsync(ct);
    }

    [Fact]
    public void AddLocalInboxDelivery_RegisteredTwice_SelectsOnePublisher()
    {
        var services = new ServiceCollection();

        services.AddLocalInboxDelivery().AddLocalInboxDelivery();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task AddLocalInboxDelivery_Alone_RegistersCollectorForTranslators()
    {
        await using var provider = new ServiceCollection()
            .AddLocalInboxDelivery()
            .BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IIntegrationEventCollector>()
            .Should().BeOfType<IntegrationEventCollector>();
    }

    [Fact]
    public void AddLocalInboxDelivery_ThenScanningDispatch_KeepsLocalPublisherAndRegistersHandlers()
    {
        var services = new ServiceCollection();

        services.AddLocalInboxDelivery().AddIntegrationEventDispatch(typeof(RegHandlerA).Assembly);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher))
            .Which.ImplementationType.Should().Be<LocalInboxIntegrationEventPublisher>();
        services.Should().Contain(d =>
            d.ServiceType == typeof(IIntegrationEventHandler<RegEventA>) && d.ImplementationType == typeof(RegHandlerA));
    }

    [Fact]
    public async Task PublishAsync_InboxFailure_PropagatesOriginalException()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        var inbox = new RecordingInbox();
        var failure = new InvalidOperationException("Consumer failed.");
        inbox.Completion.SetException(failure);
        services.AddSingleton<IInboxDispatcher>(inbox);
        services.AddLocalInboxDelivery();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var message = new OutboundIntegrationMessage(Guid.NewGuid(), new RegEventA(DateTimeOffset.UnixEpoch));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(message, ct).AsTask());

        thrown.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task PublishAsync_UnexpectedOutcome_ReportsOutcomeAndMessageId()
    {
        var inbox = new RecordingInbox();
        var outcome = (InboxDispatchOutcome)42;
        inbox.Completion.SetResult(outcome);
        await using var provider = new ServiceCollection()
            .AddSingleton<IInboxDispatcher>(inbox)
            .AddLocalInboxDelivery()
            .BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var message = new OutboundIntegrationMessage(Guid.NewGuid(), new RegEventA(DateTimeOffset.UnixEpoch));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(message, TestContext.Current.CancellationToken).AsTask());

        thrown.Message.Should().Contain(outcome.ToString()).And.Contain(message.MessageId.ToString());
    }

    [Fact]
    public void AddLocalInboxDelivery_CustomPublisherAlreadyRegistered_RejectsConflict()
    {
        var services = new ServiceCollection();
        services.AddScoped<IIntegrationEventPublisher, ExplicitPublisher>();

        var act = () => services.AddLocalInboxDelivery();

        act.Should().Throw<InvalidOperationException>().WithMessage("*publisher*");
        services.Should().ContainSingle().Which.ImplementationType.Should().Be<ExplicitPublisher>();
    }

    private sealed class RecordingInbox : IInboxDispatcher
    {
        public TaskCompletionSource<InboxDispatchOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IntegrationEnvelope? Envelope { get; private set; }

        public CancellationToken Token { get; private set; }

        public Task<InboxDispatchOutcome> DispatchAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
        {
            Envelope = envelope;
            Token = cancellationToken;
            return Completion.Task;
        }
    }

    private sealed class ExplicitPublisher : IIntegrationEventPublisher
    {
        public ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}