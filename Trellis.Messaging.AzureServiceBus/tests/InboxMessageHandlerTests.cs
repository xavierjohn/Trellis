namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator;

/// <summary>
/// Pins the settlement rules — the consumer's most consequential logic — without a broker, so a build server
/// with no Docker still enforces them.
/// </summary>
public class InboxMessageHandlerTests
{
    private static readonly IntegrationEventNameMap Map = new(
    [
        new KeyValuePair<string, Type>(OrderPlaced.WireName, typeof(OrderPlaced)),
    ]);

    [Fact]
    public async Task Processed_CompletesTheMessage()
    {
        var (settler, dispatcher) = await HandleAsync(ValidMessage(), InboxDispatchOutcome.Processed);

        settler.Completed.Should().BeTrue();
        settler.DeadLetterReason.Should().BeNull();
        dispatcher.Dispatched.Should().ContainSingle();
    }

    [Fact]
    public async Task SkippedDuplicate_CompletesTheMessageRatherThanAbandoningItIntoAnEndlessRedeliveryLoop()
    {
        var (settler, _) = await HandleAsync(ValidMessage(), InboxDispatchOutcome.SkippedDuplicate);

        // Abandoning would redeliver forever: every attempt reaches the same "already processed" conclusion.
        settler.Completed.Should().BeTrue();
        settler.DeadLetterReason.Should().BeNull();
    }

    [Fact]
    public async Task HandlerFailure_PropagatesAndLeavesTheMessageUnsettled()
    {
        var settler = new RecordingSettler();
        var handler = HandlerFor(new ThrowingDispatcher());

        var act = async () => await handler.HandleAsync(
            ValidMessage(), settler, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Unsettled is the point: the SDK abandons it and the broker redelivers, because the dispatcher
        // rolled back and nothing was applied.
        settler.Completed.Should().BeFalse();
        settler.DeadLetterReason.Should().BeNull();
    }

    [Theory]
    [InlineData(null, OrderPlaced.WireName, ServiceBusConsumerErrors.UnusableMessageIdCode)]
    [InlineData("not-a-guid", OrderPlaced.WireName, ServiceBusConsumerErrors.UnusableMessageIdCode)]
    [InlineData(null, null, ServiceBusConsumerErrors.UnusableMessageIdCode)]
    public async Task UnusableMessage_DeadLettersWithTheReasonCode(string? messageId, string? subject, string expected)
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: messageId,
            subject: subject);

        var (settler, dispatcher) = await HandleAsync(message, InboxDispatchOutcome.Processed);

        settler.DeadLetterReason.Should().Be(expected);
        settler.Completed.Should().BeFalse();
        dispatcher.Dispatched.Should().BeEmpty("an unreadable message must never reach a handler");
    }

    [Fact]
    public async Task MissingSubject_DeadLetters()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.CreateVersion7().ToString(),
            subject: null);

        var (settler, _) = await HandleAsync(message, InboxDispatchOutcome.Processed);

        settler.DeadLetterReason.Should().Be(ServiceBusConsumerErrors.MissingSubjectCode);
    }

    [Fact]
    public async Task UnknownContract_DeadLetters()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.CreateVersion7().ToString(),
            subject: "orders.never-heard-of-it.v1");

        var (settler, _) = await HandleAsync(message, InboxDispatchOutcome.Processed);

        settler.DeadLetterReason.Should().Be(ServiceBusConsumerErrors.UnknownContractCode);
    }

    [Fact]
    public async Task MalformedBody_DeadLetters()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{ not json"),
            messageId: Guid.CreateVersion7().ToString(),
            subject: OrderPlaced.WireName);

        var (settler, _) = await HandleAsync(message, InboxDispatchOutcome.Processed);

        settler.DeadLetterReason.Should().Be(ServiceBusConsumerErrors.MalformedBodyCode);
        settler.DeadLetterDescription.Should().NotBeNullOrWhiteSpace("the dead-letter must carry the diagnosis");
    }

    [Fact]
    public async Task TheEnvelopeHandedToTheInboxCarriesTheServiceBusMessageId()
    {
        var messageId = Guid.CreateVersion7();
        var message = ValidMessage(messageId);

        var (_, dispatcher) = await HandleAsync(message, InboxDispatchOutcome.Processed);

        // This is the whole point of the transport: the id the producer staged is the id the inbox dedups on.
        dispatcher.Dispatched.Should().ContainSingle().Which.MessageId.Should().Be(messageId);
    }

    private static async Task<(RecordingSettler Settler, RecordingDispatcher Dispatcher)> HandleAsync(
        ServiceBusReceivedMessage message,
        InboxDispatchOutcome outcome)
    {
        var settler = new RecordingSettler();
        var dispatcher = new RecordingDispatcher(outcome);

        await HandlerFor(dispatcher).HandleAsync(message, settler, TestContext.Current.CancellationToken);

        return (settler, dispatcher);
    }

    private static InboxMessageHandler HandlerFor(IInboxDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dispatcher);

        return new InboxMessageHandler(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Map,
            JsonSerializerOptions.Web,
            NullLogger.Instance);
    }

    private static ServiceBusReceivedMessage ValidMessage(Guid? messageId = null) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{"orderNumber":"ORD-1","occurredAt":"1970-01-01T00:00:00+00:00"}"""),
            messageId: (messageId ?? Guid.CreateVersion7()).ToString(),
            subject: OrderPlaced.WireName);

    private sealed class RecordingSettler : IMessageSettler
    {
        public bool Completed { get; private set; }

        public string? DeadLetterReason { get; private set; }

        public string? DeadLetterDescription { get; private set; }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(string reason, string? description, CancellationToken cancellationToken)
        {
            DeadLetterReason = reason;
            DeadLetterDescription = description;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDispatcher(InboxDispatchOutcome outcome) : IInboxDispatcher
    {
        public List<IntegrationEnvelope> Dispatched { get; } = [];

        public Task<InboxDispatchOutcome> DispatchAsync(
            IntegrationEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Dispatched.Add(envelope);
            return Task.FromResult(outcome);
        }
    }

    private sealed class ThrowingDispatcher : IInboxDispatcher
    {
        public Task<InboxDispatchOutcome> DispatchAsync(
            IntegrationEnvelope envelope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("handler blew up");
    }
}