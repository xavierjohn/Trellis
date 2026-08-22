namespace Trellis.Mediator;

/// <summary>
/// Single source of truth for the mediator pipeline's activity source name.
/// </summary>
/// <remarks>
/// <see cref="TracingBehavior{TMessage, TResponse}"/> emits from this name and
/// <see cref="MediatorTraceProviderBuilderExtensions.AddTrellisMediatorInstrumentation"/> listens
/// to it. Holding it here rather than repeating the literal keeps the pair from drifting: a helper
/// that registers a name nothing emits from fails silently, which is the failure the helper exists
/// to remove. The public constant stays on <c>TracingBehavior</c>, where consumers already find it;
/// this type is internal because it adds no capability beyond that constant. Trellis.Core and
/// Trellis.Primitives anchor their sources the same way, in <c>RopTrace</c> and
/// <c>PrimitiveValueObjectTrace</c>.
/// </remarks>
internal static class MediatorTrace
{
    internal const string ActivitySourceName = "Trellis.Mediator";
}
