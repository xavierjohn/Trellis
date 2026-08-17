namespace Trellis.Messaging.AzureServiceBus;

/// <summary>
/// The ways a received message can violate the transport contract. Each is a property of the message bytes
/// themselves, so redelivering the same message can never produce a different outcome — the processor
/// dead-letters rather than abandoning, and the reason code below becomes the dead-letter reason.
/// </summary>
internal static class ServiceBusConsumerErrors
{
    /// <summary>Reason code for a message whose <c>MessageId</c> is absent or not a usable GUID.</summary>
    public const string UnusableMessageIdCode = "servicebus_unusable_message_id";

    /// <summary>Reason code for a message with no <c>Subject</c> to identify its contract.</summary>
    public const string MissingSubjectCode = "servicebus_missing_subject";

    /// <summary>Reason code for a contract name this application does not know.</summary>
    public const string UnknownContractCode = "servicebus_unknown_contract";

    /// <summary>Reason code for a body that does not deserialize to its declared contract.</summary>
    public const string MalformedBodyCode = "servicebus_malformed_body";

    /// <summary>
    /// The message carries no usable dedup identity. Processing it would defeat the inbox: without a stable
    /// id the consumer cannot tell a redelivery from a new message, so handlers would run again on every
    /// delivery attempt.
    /// </summary>
    public static Error UnusableMessageId(string? messageId) =>
        new Error.InvariantViolation(UnusableMessageIdCode)
        {
            Detail = $"MessageId '{messageId}' is not a non-empty GUID, so the message has no stable inbox dedup key.",
        };

    /// <summary>The message does not say which contract it carries, so no type can be chosen to deserialize it.</summary>
    public static Error MissingSubject { get; } =
        new Error.InvariantViolation(MissingSubjectCode)
        {
            Detail = "Subject is empty; Trellis carries the integration event's wire name there.",
        };

    /// <summary>
    /// The contract name is well-formed but unknown here. This is normal traffic on a shared topic — a
    /// producer may emit contracts this subscriber does not model — so it is a routing decision, not a bug.
    /// </summary>
    public static Error UnknownContract(string wireName) =>
        new Error.InvariantViolation(UnknownContractCode)
        {
            Detail = $"No integration event type is registered for wire name '{wireName}'.",
        };

    /// <summary>The declared contract is known but the body does not match it.</summary>
    public static Error MalformedBody(string wireName, string reason) =>
        new Error.InvariantViolation(MalformedBodyCode)
        {
            Detail = $"The body of '{wireName}' could not be deserialized: {reason}",
        };
}
