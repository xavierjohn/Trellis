namespace Trellis;

/// <summary>
/// Declares the stable, on-the-wire name of an <see cref="IIntegrationEvent"/> so producers and consumers
/// in <i>different</i> services can agree on the type a message carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a logical name is required across services.</b> In-process relaying identifies an event by its
/// <see cref="System.Type.AssemblyQualifiedName"/>, which is fine while producer and consumer are the same
/// process. On a message broker it is unusable: the consumer's assemblies differ from the producer's, and
/// the string embeds an assembly version, so it can stop resolving after a routine version bump. A logical
/// name is owned by the contract rather than by the CLR layout, so each side maps it to whatever local type
/// it likes.
/// </para>
/// <para>
/// <b>Choose a versioned name.</b> The name is a published contract: once a message carrying it exists on a
/// queue or in another team's code, changing it is a breaking change. Include a version segment
/// (<c>orders.order-placed.v1</c>) so an incompatible payload change can ship as a new name consumed
/// side-by-side, rather than as a silent break.
/// </para>
/// <para>Names are compared with the ordinal comparer — casing is significant.</para>
/// </remarks>
/// <example>
/// <code>
/// [IntegrationEventName("orders.order-placed.v1")]
/// public sealed record OrderPlaced(Guid OrderId, DateTimeOffset OccurredAt) : IIntegrationEvent;
/// </code>
/// </example>
/// <param name="name">The stable wire name, for example <c>orders.order-placed.v1</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventNameAttribute(string name) : Attribute
{
    /// <summary>The stable wire name identifying this event's contract.</summary>
    public string Name { get; } = name;
}