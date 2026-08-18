namespace Trellis.Messaging.AzureServiceBus;

using Azure.Messaging.ServiceBus;

/// <summary>
/// How a received message is settled, abstracted away from the SDK's <see cref="ProcessMessageEventArgs"/>.
/// </summary>
/// <remarks>
/// Settlement is the consumer's most consequential decision — completing a message that was not durably
/// accounted for loses it, and abandoning one that was replays it forever — yet in the SDK it is reachable
/// only through a live receiver holding a real lock. This seam lets the decision be exercised directly, so
/// the rules are pinned by tests that run on a build server with no broker, rather than only by tests that
/// need Docker and are excluded from CI.
/// </remarks>
internal interface IMessageSettler
{
    /// <summary>Completes the message, removing it from the subscription.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the message is settled.</returns>
    Task CompleteAsync(CancellationToken cancellationToken);

    /// <summary>Dead-letters the message with a reason code and diagnosis.</summary>
    /// <param name="reason">The reason code recorded on the dead-lettered message.</param>
    /// <param name="description">The specific diagnosis.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the message is settled.</returns>
    Task DeadLetterAsync(string reason, string? description, CancellationToken cancellationToken);
}

/// <summary>Settles through the SDK's per-message event args.</summary>
internal sealed class ProcessMessageEventArgsSettler(ProcessMessageEventArgs args) : IMessageSettler
{
    public Task CompleteAsync(CancellationToken cancellationToken) =>
        args.CompleteMessageAsync(args.Message, cancellationToken);

    public Task DeadLetterAsync(string reason, string? description, CancellationToken cancellationToken) =>
        args.DeadLetterMessageAsync(args.Message, reason, description, cancellationToken);
}