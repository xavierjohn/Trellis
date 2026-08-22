namespace Trellis.Mediator.Tests;

/// <summary>
/// Serializes the test classes that exercise <see cref="TracingBehavior{TMessage, TResponse}"/>.
/// </summary>
/// <remarks>
/// Subscription to an <see cref="System.Diagnostics.ActivitySource"/> is process-wide and by
/// source name, so a listener or <c>TracerProvider</c> in one test class observes the spans
/// emitted by every other class running at the same time. Two classes drive the behaviour and
/// both assert on span counts, so running them in parallel lets one class's spans land in the
/// other's sink — a failure that surfaces as an unrelated, intermittently red test.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerializedMediatorActivitySource
{
    /// <summary>The collection name shared by every test class that emits mediator spans.</summary>
    public const string Name = "Trellis.Mediator ActivitySource";
}
