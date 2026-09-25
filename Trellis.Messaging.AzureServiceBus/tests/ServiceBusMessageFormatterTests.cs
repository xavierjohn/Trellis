namespace Trellis.Messaging.AzureServiceBus.Tests;

using System.Diagnostics;
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
    public void RoundTrip_PreservesOutboundLineageAndTraceExactly()
    {
        var id = Guid.CreateVersion7();
        var causation = Guid.NewGuid();
        var parent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
        var outbound = new OutboundIntegrationMessage(id, SampleEvent())
        {
            MessageSource = "original-service",
            CausationId = causation,
            CorrelationId = "workflow/42",
            TraceParent = parent,
            TraceState = "vendor=opaque",
        };

        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(
            outbound, OrderPlaced.WireName, messageSource: "publisher-fallback", Json);

        sent.MessageId.Should().Be(id.ToString());
        sent.CorrelationId.Should().Be("workflow/42");
        sent.ApplicationProperties[ServiceBusMessageFormat.MessageSourceProperty].Should().Be("original-service");
        sent.ApplicationProperties[ServiceBusMessageFormat.CausationIdProperty].Should().Be(causation.ToString());
        sent.ApplicationProperties[ServiceBusMessageFormat.TraceParentProperty].Should().Be(parent);
        sent.ApplicationProperties[ServiceBusMessageFormat.TraceStateProperty].Should().Be("vendor=opaque");

        var received = Receive(sent);
        received.TryGetValue(out var envelope, out _).Should().BeTrue();
        envelope!.MessageId.Should().Be(id);
        envelope.Event.Should().BeEquivalentTo(outbound.Event);
        envelope.MessageSource.Should().Be("original-service");
        envelope.CausationId.Should().Be(causation);
        envelope.CorrelationId.Should().Be("workflow/42");
        envelope.TraceParent.Should().Be(parent);
        envelope.TraceState.Should().Be("vendor=opaque");
    }

    [Fact]
    public void ToServiceBusMessage_FallsBackToPublisherSourceOnlyWhenOutboundSourceIsAbsent()
    {
        var missingSource = new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent());
        var blankSource = missingSource with { MessageSource = " " };

        var fallback = ServiceBusMessageFormatter.ToServiceBusMessage(
            missingSource, OrderPlaced.WireName, "publisher-service", Json);
        var blank = ServiceBusMessageFormatter.ToServiceBusMessage(
            blankSource, OrderPlaced.WireName, "publisher-service", Json);

        fallback.ApplicationProperties[ServiceBusMessageFormat.MessageSourceProperty].Should().Be("publisher-service");
        blank.ApplicationProperties.Should().NotContainKey(ServiceBusMessageFormat.MessageSourceProperty);
    }

    [Fact]
    public void ToServiceBusMessage_RetryPreservesPersistedMetadataRatherThanSamplingRelayActivity()
    {
        var parent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
        var outbound = new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent())
        {
            MessageSource = "original-service",
            CausationId = Guid.NewGuid(),
            CorrelationId = "workflow-42",
            TraceParent = parent,
            TraceState = "vendor=original",
        };

        ServiceBusMessage FormatAttempt()
        {
            using var relay = new Activity("relay-attempt").SetIdFormat(ActivityIdFormat.W3C).Start();
            return ServiceBusMessageFormatter.ToServiceBusMessage(
                outbound, OrderPlaced.WireName, "publisher-fallback", Json);
        }

        var first = FormatAttempt();
        var retry = FormatAttempt();

        retry.MessageId.Should().Be(first.MessageId);
        retry.CorrelationId.Should().Be(first.CorrelationId);
        retry.ApplicationProperties.Should().BeEquivalentTo(first.ApplicationProperties);
        retry.ApplicationProperties[ServiceBusMessageFormat.TraceParentProperty].Should().Be(parent);
    }

    [Fact]
    public void ToEnvelope_MalformedTraceTextIsPassedToTheInbox()
    {
        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent()), OrderPlaced.WireName, null, Json);
        sent.ApplicationProperties[ServiceBusMessageFormat.TraceParentProperty] = "not-w3c";
        sent.ApplicationProperties[ServiceBusMessageFormat.TraceStateProperty] = "invalid-tracestate";

        var received = Receive(sent);

        received.TryGetValue(out var envelope, out _).Should().BeTrue();
        envelope!.CausationId.Should().BeNull();
        envelope.MessageSource.Should().BeNull();
        envelope.CorrelationId.Should().BeNull();
        envelope.TraceParent.Should().Be("not-w3c");
        envelope.TraceState.Should().Be("invalid-tracestate");
    }

    [Theory]
    [InlineData(ServiceBusMessageFormat.MessageSourceProperty, 100)]
    [InlineData(ServiceBusMessageFormat.CausationIdProperty, "not-a-guid")]
    [InlineData(ServiceBusMessageFormat.CausationIdProperty, "00000000-0000-0000-0000-000000000000")]
    [InlineData(ServiceBusMessageFormat.CausationIdProperty, 100)]
    public void ToEnvelope_InvalidOptionalMetadata_FailsWithThePropertyName(string property, object? value)
    {
        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent()), OrderPlaced.WireName, null, Json);
        sent.ApplicationProperties[property] = value!;
        var received = Receive(sent);

        received.TryGetError(out var error).Should().BeTrue();
        error!.Code.Should().Be(ServiceBusConsumerErrors.MalformedMetadataCode);
        error.Detail.Should().Contain(property);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ToEnvelope_BlankCorrelationId_IsAbsent(string correlationId)
    {
        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent()), OrderPlaced.WireName, null, Json);
        sent.CorrelationId = correlationId;
        var received = Receive(sent);

        received.TryGetValue(out var envelope, out _).Should().BeTrue();
        envelope!.CorrelationId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ToEnvelope_BlankSource_IsAbsent(string? source)
    {
        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent()), OrderPlaced.WireName, null, Json);
        sent.ApplicationProperties[ServiceBusMessageFormat.MessageSourceProperty] = source!;
        var received = Receive(sent);

        received.TryGetValue(out var envelope, out _).Should().BeTrue();
        envelope!.MessageSource.Should().BeNull();
    }

    [Theory]
    [InlineData(ServiceBusMessageFormat.TraceParentProperty)]
    [InlineData(ServiceBusMessageFormat.TraceStateProperty)]
    public void ToEnvelope_NonStringTraceProperty_IsAbsent(string property)
    {
        var sent = ServiceBusMessageFormatter.ToServiceBusMessage(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent()), OrderPlaced.WireName, null, Json);
        sent.ApplicationProperties[property] = 100;
        sent.ApplicationProperties[
            property == ServiceBusMessageFormat.TraceParentProperty
                ? ServiceBusMessageFormat.TraceStateProperty
                : ServiceBusMessageFormat.TraceParentProperty] = "malformed-w3c";
        var received = Receive(sent);

        received.TryGetValue(out var envelope, out _).Should().BeTrue();
        if (property == ServiceBusMessageFormat.TraceParentProperty)
        {
            envelope!.TraceParent.Should().BeNull();
            envelope.TraceState.Should().Be("malformed-w3c");
        }
        else
        {
            envelope!.TraceParent.Should().Be("malformed-w3c");
            envelope.TraceState.Should().BeNull();
        }
    }

    [Fact]
    public void ToEnvelope_LegacyMessageHasNoOptionalMetadata()
    {
        var received = Receive(ServiceBusMessageFormatter.ToServiceBusMessage(
            new OutboundIntegrationMessage(Guid.CreateVersion7(), SampleEvent()), OrderPlaced.WireName, null, Json));

        received.TryGetValue(out var envelope, out _).Should().BeTrue();
        envelope!.MessageSource.Should().BeNull();
        envelope.CausationId.Should().BeNull();
        envelope.CorrelationId.Should().BeNull();
        envelope.TraceParent.Should().BeNull();
        envelope.TraceState.Should().BeNull();
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
            correlationId: sent.CorrelationId,
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