namespace Trellis.Mediator.Tests;

public class OutboundIntegrationMessageTests
{
    [Fact]
    public void Ctor_NullEvent_Throws()
    {
        var act = () => new OutboundIntegrationMessage(Guid.CreateVersion7(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("Event");
    }

    [Fact]
    public void Ctor_EmptyMessageId_Throws()
    {
        var act = () => new OutboundIntegrationMessage(Guid.Empty, new SampleIntegrationEvent(DateTimeOffset.UnixEpoch));

        act.Should().Throw<ArgumentException>().WithParameterName("MessageId");
    }

    [Fact]
    public void With_NullEvent_Throws()
    {
        var message = new OutboundIntegrationMessage(Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch));

        var act = () => message with { Event = null! };

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void With_EmptyMessageId_Throws()
    {
        var message = new OutboundIntegrationMessage(Guid.CreateVersion7(), new SampleIntegrationEvent(DateTimeOffset.UnixEpoch));

        var act = () => message with { MessageId = Guid.Empty };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_ValidArguments_ExposesBoth()
    {
        var id = Guid.CreateVersion7();
        var integrationEvent = new SampleIntegrationEvent(DateTimeOffset.UnixEpoch);

        var message = new OutboundIntegrationMessage(id, integrationEvent);

        message.MessageId.Should().Be(id);
        message.Event.Should().BeSameAs(integrationEvent);
    }

    private sealed record SampleIntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;
}