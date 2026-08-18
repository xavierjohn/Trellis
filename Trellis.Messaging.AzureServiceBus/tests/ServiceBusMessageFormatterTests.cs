namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Trellis.Mediator;

public class ServiceBusMessageFormatterTests
{
    private static readonly IntegrationEventNameMap Map = new(
    [
        new KeyValuePair<string, Type>(OrderPlaced.WireName, typeof(OrderPlaced)),
    ]);

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    [Fact]
    public void ToServiceBusMessage_CarriesTheOutboxIdAsTheServiceBusMessageId()
    {
        var outboxId = Guid.CreateVersion7();
        var message = new OutboundIntegrationMessage(outboxId, SampleEvent());

        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(message, OrderPlaced.WireName, messageSource: null, Json);

        sent.MessageId.Should().Be(outboxId.ToString());
    }

    [Fact]
    public void ToServiceBusMessage_PutsTheWireNameOnTheSubject()
    {
        var message = new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent());

        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(message, OrderPlaced.WireName, messageSource: null, Json);

        sent.Subject.Should().Be(OrderPlaced.WireName);
        sent.ContentType.Should().Be(ServiceBusMessageFormat.JsonContentType);
    }

    [Fact]
    public void ToServiceBusMessage_OmitsTheSourcePropertyWhenNoSourceIsConfigured()
    {
        var message = new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent());

        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(message, OrderPlaced.WireName, messageSource: "  ", Json);

        sent.ApplicationProperties.Should().NotContainKey(ServiceBusMessageFormat.MessageSourceProperty);
    }

    [Fact]
    public void ToServiceBusMessage_SerializesTheRuntimeTypeNotTheStaticInterface()
    {
        var message = new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent());

        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(message, OrderPlaced.WireName, messageSource: null, Json);

        Encoding.UTF8.GetString(sent.Body.ToArray()).Should().Contain("orderNumber");
    }

    [Fact]
    public void RoundTrip_PreservesTheMessageIdEventAndSource()
    {
        var outboxId = Guid.CreateVersion7();
        var original = SampleEvent();
        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(
            new OutboundIntegrationMessage(outboxId, original), OrderPlaced.WireName, "orders-service", Json);

        var received = Receive(sent);

        received.TryGetValue(out var envelope, out _).Should().BeTrue();
        envelope!.MessageId.Should().Be(outboxId);
        envelope.MessageSource.Should().Be("orders-service");
        envelope.Event.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void ToEnvelope_NonGuidMessageId_Fails()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), messageId: "not-a-guid", subject: OrderPlaced.WireName);

        AssertFailsWith(received, ServiceBusConsumerErrors.UnusableMessageIdCode);
    }

    [Fact]
    public void ToEnvelope_EmptyGuidMessageId_Fails()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), messageId: Guid.Empty.ToString(), subject: OrderPlaced.WireName);

        AssertFailsWith(received, ServiceBusConsumerErrors.UnusableMessageIdCode);
    }

    [Fact]
    public void ToEnvelope_MissingSubject_Fails()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), messageId: Guid.CreateVersion7().ToString(), subject: null);

        AssertFailsWith(received, ServiceBusConsumerErrors.MissingSubjectCode);
    }

    [Fact]
    public void ToEnvelope_UnknownContract_Fails()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), messageId: Guid.CreateVersion7().ToString(), subject: "orders.unheard-of.v1");

        AssertFailsWith(received, ServiceBusConsumerErrors.UnknownContractCode);
    }

    [Fact]
    public void ToEnvelope_MalformedBody_Fails()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{ this is not json"),
            messageId: Guid.CreateVersion7().ToString(),
            subject: OrderPlaced.WireName);

        AssertFailsWith(received, ServiceBusConsumerErrors.MalformedBodyCode);
    }

    [Fact]
    public void ToEnvelope_NullBody_Fails()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("null"),
            messageId: Guid.CreateVersion7().ToString(),
            subject: OrderPlaced.WireName);

        AssertFailsWith(received, ServiceBusConsumerErrors.MalformedBodyCode);
    }

    [Fact]
    public void ToEnvelope_ContractTheSerializerCannotConstruct_FailsRatherThanThrowing()
    {
        // A contract whose member the serializer has no way to materialize throws NotSupportedException
        // rather than JsonException. It is still a message that can never be read, so it must come back as
        // a failed result and be dead-lettered — not escape as an exception the consumer treats as a
        // transient handler fault and retries until the delivery count is exhausted.
        var map = new IntegrationEventNameMap(
        [
            new KeyValuePair<string, Type>(UnreadableContract.WireName, typeof(UnreadableContract)),
        ]);

        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("""{"value":{"amount":1}}"""),
            messageId: Guid.CreateVersion7().ToString(),
            subject: UnreadableContract.WireName);

        var result = ServiceBusMessageFormatter.ToEnvelope(received, map, Json);

        result.TryGetError(out var error).Should().BeTrue();
        error!.Code.Should().Be(ServiceBusConsumerErrors.MalformedBodyCode);
    }

    private static void AssertFailsWith(ServiceBusReceivedMessage received, string reasonCode)
    {
        var result = ServiceBusMessageFormatter.ToEnvelope(received, Map, Json);

        result.TryGetError(out var error).Should().BeTrue();
        error!.Code.Should().Be(reasonCode);
        error.Detail.Should().NotBeNullOrWhiteSpace();
    }

    private static Result<IntegrationEnvelope> Receive(ServiceBusMessage sent)
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: sent.Body,
            messageId: sent.MessageId,
            subject: sent.Subject,
            contentType: sent.ContentType,
            properties: sent.ApplicationProperties);

        return ServiceBusMessageFormatter.ToEnvelope(received, Map, Json);
    }

    private static OrderPlaced SampleEvent() => new("ORD-1", DateTimeOffset.UnixEpoch);
}

[IntegrationEventName(OrderPlaced.WireName)]
public sealed record OrderPlaced(string OrderNumber, DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public const string WireName = "orders.order-placed.v1";
}

/// <summary>
/// A contract the serializer cannot deserialize into: <see cref="UnreadableValue"/> is abstract, so
/// <c>JsonSerializer</c> has no concrete type to construct and reports it as <see cref="NotSupportedException"/>
/// rather than as a malformed document.
/// </summary>
[IntegrationEventName(UnreadableContract.WireName)]
public sealed record UnreadableContract(UnreadableValue Value, DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public const string WireName = "orders.unreadable.v1";
}

/// <summary>An abstract member type, which the serializer cannot materialize.</summary>
public abstract record UnreadableValue(int Amount);