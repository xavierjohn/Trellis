namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Mediator;

/// <summary>
/// End-to-end tests over a real Azure Service Bus emulator: publish through the outbox's publish seam and
/// consume back into the inbox dispatch seam.
/// </summary>
/// <remarks>
/// <para>
/// These exist because the property that matters cannot be observed in a unit test. The transport's job is
/// to carry the producer's outbox message id verbatim so that a redelivered outbox row still deduplicates at
/// the consumer, and only a real broker round trip proves the id survives serialization, AMQP, and the
/// receive path.
/// </para>
/// <para>
/// Duplicate detection is deliberately <b>off</b> on the emulator topic. If the broker collapsed duplicate
/// message ids for us, these tests would pass even if the transport minted a fresh id per publish — the
/// assertion is that Trellis carries the id, not that Service Bus can be configured to hide the problem.
/// The trace round trip also proves that persisted W3C context and business lineage survive the broker.
/// </para>
/// <para>
/// Start the broker with
/// <c>docker compose -f Trellis.Messaging.AzureServiceBus/tests/emulator/docker-compose.yml up -d</c>.
/// Without it the tests skip visibly rather than passing.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ServiceBusTransportIntegrationTests(ServiceBusEmulator emulator) : IClassFixture<ServiceBusEmulator>
{
    private static readonly IntegrationEventNameMap Map = new(
    [
        new KeyValuePair<string, Type>(OrderPlaced.WireName, typeof(OrderPlaced)),
    ]);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RoundTrip_DeliversTheEventAndItsSourceToTheInboxDispatcher()
    {
        var client = await RequireEmulatorAsync();
        const string Subscription = "roundtrip";
        await DrainAsync(client, Subscription);

        var recorder = new RecordingInboxDispatcher();
        await using var host = BuildConsumer(client, Subscription, recorder);
        await host.StartAsync(CancellationToken.None);

        var placed = new OrderPlaced($"ORD-{Guid.NewGuid():N}", DateTimeOffset.UnixEpoch);
        var messageId = Guid.CreateVersion7();
        await PublishAsync(client, new OutboundIntegrationMessage(messageId, placed), messageSource: "orders-service");

        var envelope = await recorder.WaitForAsync(messageId);

        envelope.Event.Should().BeEquivalentTo(placed);
        envelope.MessageSource.Should().Be("orders-service");
    }

    [Fact]
    public async Task RoundTrip_PreservesPersistedW3CTraceAndLineageAcrossRelayRetries()
    {
        var client = await RequireEmulatorAsync();
        const string Subscription = "roundtrip";
        await DrainAsync(client, Subscription);

        var recorder = new RecordingInboxDispatcher();
        await using var host = BuildConsumer(client, Subscription, recorder);
        await host.StartAsync(CancellationToken.None);

        var traceParent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
        var message = new OutboundIntegrationMessage(
            Guid.CreateVersion7(),
            new OrderPlaced($"ORD-{Guid.NewGuid():N}", DateTimeOffset.UnixEpoch))
        {
            MessageSource = "orders-service",
            CausationId = Guid.CreateVersion7(),
            CorrelationId = "workflow-42",
            TraceParent = traceParent,
            TraceState = "vendor=original",
        };

        await PublishAsync(client, message, messageSource: "ignored-fallback");
        await PublishAsync(client, message, messageSource: "ignored-fallback");

        await recorder.WaitForCountAsync(message.MessageId, 2);
        var deliveries = recorder.Received.Where(e => e.MessageId == message.MessageId).ToArray();
        deliveries.Should().HaveCount(2);
        foreach (var envelope in deliveries)
        {
            envelope.MessageSource.Should().Be(message.MessageSource);
            envelope.CausationId.Should().Be(message.CausationId);
            envelope.CorrelationId.Should().Be(message.CorrelationId);
            envelope.TraceParent.Should().Be(traceParent);
            envelope.TraceState.Should().Be(message.TraceState);
        }
    }

    [Fact]
    public async Task RepublishingTheSameOutboxRow_ArrivesTwiceUnderOneMessageId()
    {
        var client = await RequireEmulatorAsync();
        const string Subscription = "duplicate";
        await DrainAsync(client, Subscription);

        var recorder = new RecordingInboxDispatcher();
        await using var host = BuildConsumer(client, Subscription, recorder);
        await host.StartAsync(CancellationToken.None);

        // One outbox row, published twice: exactly what an at-least-once relay does when it crashes
        // between publishing and saving its bookkeeping.
        var row = new OutboundIntegrationMessage(
            Guid.CreateVersion7(), new OrderPlaced($"ORD-{Guid.NewGuid():N}", DateTimeOffset.UnixEpoch));

        await PublishAsync(client, row, messageSource: null);
        await PublishAsync(client, row, messageSource: null);

        await recorder.WaitForCountAsync(row.MessageId, 2);

        // Both copies carry the producer's id, so the inbox sees one (ConsumerId, MessageId) pair and
        // deduplicates. Had the transport generated its own id, these would be two distinct messages and
        // the handlers would run twice.
        recorder.Received.Select(e => e.MessageId).Should().AllBeEquivalentTo(row.MessageId);
    }

    [Fact]
    public async Task ADuplicateTheInboxSkips_IsCompletedNotRedelivered()
    {
        var client = await RequireEmulatorAsync();
        const string Subscription = "duplicate";
        await DrainAsync(client, Subscription);

        var recorder = new RecordingInboxDispatcher { Outcome = InboxDispatchOutcome.SkippedDuplicate };
        var row = new OutboundIntegrationMessage(
            Guid.CreateVersion7(), new OrderPlaced($"ORD-{Guid.NewGuid():N}", DateTimeOffset.UnixEpoch));

        await using (var host = BuildConsumer(client, Subscription, recorder))
        {
            await host.StartAsync(CancellationToken.None);
            await PublishAsync(client, row, messageSource: null);
            await recorder.WaitForAsync(row.MessageId);
        }

        // SkippedDuplicate means the message is durably accounted for. Abandoning it instead would loop
        // forever, because every redelivery would reach the same conclusion.
        recorder.CountFor(row.MessageId).Should().Be(1);
        await AssertMessageAbsentAsync(client, Subscription, row.MessageId, SubQueue.None);
        await AssertMessageAbsentAsync(client, Subscription, row.MessageId, SubQueue.DeadLetter);
    }

    [Fact]
    public async Task AMessageWhoseContractIsUnknown_IsDeadLetteredWithADiagnosis()
    {
        var client = await RequireEmulatorAsync();
        const string Subscription = "poison";
        await DrainAsync(client, Subscription);

        var recorder = new RecordingInboxDispatcher();
        await using var host = BuildConsumer(client, Subscription, recorder);
        await host.StartAsync(CancellationToken.None);

        var messageId = Guid.CreateVersion7();
        await using (var sender = client.CreateSender(ServiceBusEmulator.Topic))
        {
            await sender.SendMessageAsync(
                new ServiceBusMessage(BinaryData.FromString("{}"))
                {
                    MessageId = messageId.ToString(),
                    Subject = "orders.never-heard-of.v1",
                },
                TestContext.Current.CancellationToken);
        }

        var deadLettered = await WaitForDeadLetterAsync(client, Subscription, messageId);

        deadLettered.DeadLetterReason.Should().Be(ServiceBusConsumerErrors.UnknownContractCode);
        deadLettered.DeadLetterErrorDescription.Should().Contain("orders.never-heard-of.v1");
        await AssertMessageAbsentAsync(client, Subscription, messageId, SubQueue.None);
        // Every test publishes to the shared topic, so an earlier test's message can still reach this subscription.
        recorder.CountFor(messageId).Should().Be(0);
    }

    [Fact]
    public async Task AMessageWhoseHandlerThrows_IsRedelivered()
    {
        var client = await RequireEmulatorAsync();
        const string Subscription = "failing";
        await DrainAsync(client, Subscription);

        var recorder = new RecordingInboxDispatcher { ThrowOnFirstAttempt = true };
        await using var host = BuildConsumer(client, Subscription, recorder);
        await host.StartAsync(CancellationToken.None);

        var row = new OutboundIntegrationMessage(
            Guid.CreateVersion7(), new OrderPlaced($"ORD-{Guid.NewGuid():N}", DateTimeOffset.UnixEpoch));
        await PublishAsync(client, row, messageSource: null);

        // The dispatcher rolled back, so nothing was applied; the SDK abandons an unsettled message whose
        // handler threw and Service Bus redelivers it.
        await recorder.WaitForCountAsync(row.MessageId, 2);
    }

    private async Task<ServiceBusClient> RequireEmulatorAsync()
    {
        var client = await emulator.TryGetClientAsync();
        Assert.SkipWhen(
            client is null,
            "No Azure Service Bus emulator reachable on localhost:5672, so the transport integration tests did not run. " +
            "Start it with: docker compose -f Trellis.Messaging.AzureServiceBus/tests/emulator/docker-compose.yml up -d");

        return client!;
    }

    private static async Task PublishAsync(ServiceBusClient client, OutboundIntegrationMessage message, string? messageSource)
    {
        await using var publisher = new ServiceBusIntegrationEventPublisher(
            client,
            Map,
            Options.Create(new AzureServiceBusPublisherOptions { MessageSource = messageSource }),
            NullLogger<ServiceBusIntegrationEventPublisher>.Instance);

        await publisher.PublishAsync(message, CancellationToken.None);
    }

    internal static ConsumerHost BuildConsumer(ServiceBusClient client, string subscription, RecordingInboxDispatcher recorder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(client);
        services.AddSingleton<IInboxDispatcher>(recorder);
        services.AddAzureServiceBusIntegrationEventConsumer(
            Map, o => o.Subscribe(ServiceBusEmulator.Topic, subscription));

        return new ConsumerHost(services.BuildServiceProvider());
    }

    private static async Task DrainAsync(ServiceBusClient client, string subscription)
    {
        await using var receiver = client.CreateReceiver(ServiceBusEmulator.Topic, subscription);

        while (true)
        {
            var batch = await receiver.ReceiveMessagesAsync(50, TimeSpan.FromMilliseconds(500));
            if (batch.Count == 0)
                return;

            foreach (var message in batch)
                await receiver.CompleteMessageAsync(message);
        }
    }

    private static async Task<ServiceBusReceivedMessage> WaitForDeadLetterAsync(
        ServiceBusClient client, string subscription, Guid messageId)
    {
        await using var receiver = client.CreateReceiver(
            ServiceBusEmulator.Topic, subscription, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        // Peek takes no lock and removes nothing, so a settlement race on the dead-lettered message cannot fail the test.
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            long fromSequenceNumber = 0;
            while (true)
            {
                var batch = await receiver.PeekMessagesAsync(50, fromSequenceNumber, TestContext.Current.CancellationToken);
                if (batch.Count == 0)
                    break;

                var match = batch.FirstOrDefault(message => message.MessageId == messageId.ToString());
                if (match is not null)
                    return match;

                fromSequenceNumber = batch[^1].SequenceNumber + 1;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Message {messageId} was not dead-lettered within {Patience}.");
    }

    private static async Task AssertMessageAbsentAsync(
        ServiceBusClient client, string subscription, Guid messageId, SubQueue subQueue)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Patience);
        await using var receiver = client.CreateReceiver(
            ServiceBusEmulator.Topic, subscription, new ServiceBusReceiverOptions { SubQueue = subQueue });

        // Peek includes locked and deferred messages; an empty receive would not prove completion.
        long fromSequenceNumber = 0;
        while (true)
        {
            var batch = await receiver.PeekMessagesAsync(50, fromSequenceNumber, timeout.Token);
            if (batch.Count == 0)
                return;

            batch.Should().NotContain(
                message => message.MessageId == messageId.ToString(),
                "the skipped duplicate must be completed, not left in {0}", subQueue);
            fromSequenceNumber = batch[^1].SequenceNumber + 1;
        }
    }

    internal sealed class ConsumerHost(ServiceProvider services) : IAsyncDisposable
    {
        private ServiceBusInboxConsumer? _consumer;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _consumer = services.GetServices<IHostedService>().OfType<ServiceBusInboxConsumer>().Single();
            return _consumer.StartAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_consumer is not null)
                {
                    using var shutdown = new CancellationTokenSource(Patience);
                    await _consumer.StopAsync(shutdown.Token);
                }
            }
            finally
            {
                await services.DisposeAsync();
            }
        }
    }

    internal sealed class RecordingInboxDispatcher : IInboxDispatcher
    {
        private readonly ConcurrentQueue<IntegrationEnvelope> _received = new();
        private readonly ConcurrentDictionary<Guid, int> _attempts = new();

        public IReadOnlyCollection<IntegrationEnvelope> Received => _received;

        public InboxDispatchOutcome Outcome { get; init; } = InboxDispatchOutcome.Processed;

        public bool ThrowOnFirstAttempt { get; init; }

        public int CountFor(Guid messageId) => _received.Count(e => e.MessageId == messageId);

        public Task<InboxDispatchOutcome> DispatchAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _received.Enqueue(envelope);
            var attempt = _attempts.AddOrUpdate(envelope.MessageId, 1, static (_, n) => n + 1);

            if (ThrowOnFirstAttempt && attempt == 1)
                throw new InvalidOperationException("Simulated handler failure.");

            return Task.FromResult(Outcome);
        }

        public Task<IntegrationEnvelope> WaitForAsync(Guid messageId) => WaitForCountAsync(messageId, 1);

        public async Task<IntegrationEnvelope> WaitForCountAsync(Guid messageId, int count)
        {
            var deadline = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < deadline)
            {
                var matches = _received.Where(e => e.MessageId == messageId).ToList();
                if (matches.Count >= count)
                    return matches[^1];

                await Task.Delay(200);
            }

            throw new TimeoutException(
                $"Expected {count} delivery/deliveries of message {messageId} within {Patience}, saw {CountFor(messageId)}.");
        }
    }
}