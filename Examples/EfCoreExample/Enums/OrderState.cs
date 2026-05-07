namespace EfCoreExample.Enums;

using Trellis;

/// <summary>
/// Enum value object demonstrating rich domain behavior for order state.
/// Unlike a regular C# enum, this encapsulates business rules about state transitions.
/// Value defaults to the field name and can be overridden with EnumValueAttribute only when the external name must differ.
/// Note: JsonConverter is automatically added by the source generator.
/// </summary>
public partial class OrderState : RequiredEnum<OrderState>
{
    // Pure domain - symbolic values currently follow field names by default
    public static readonly OrderState Draft = new(canModify: true, canCancel: true, isTerminal: false);
    public static readonly OrderState Confirmed = new(canModify: false, canCancel: true, isTerminal: false);
    public static readonly OrderState Shipped = new(canModify: false, canCancel: false, isTerminal: false);
    public static readonly OrderState Delivered = new(canModify: false, canCancel: false, isTerminal: true);
    public static readonly OrderState Cancelled = new(canModify: false, canCancel: false, isTerminal: true);

    public bool CanModify { get; }
    public bool CanCancel { get; }
    public bool IsTerminal { get; }

    private OrderState(bool canModify, bool canCancel, bool isTerminal)
    {
        CanModify = canModify;
        CanCancel = canCancel;
        IsTerminal = isTerminal;
    }

    /// <summary>
    /// Gets the allowed transitions from this state.
    /// </summary>
    public IReadOnlyList<OrderState> AllowedTransitions => this switch
    {
        _ when this == Draft => [Confirmed, Cancelled],
        _ when this == Confirmed => [Shipped, Cancelled],
        _ when this == Shipped => [Delivered],
        _ => []
    };

    public bool CanTransitionTo(OrderState newState) => AllowedTransitions.Contains(newState);

    public Result<OrderState> TryTransitionTo(OrderState newState)
    {
        if (CanTransitionTo(newState))
            return Result.Ok(newState);

        return Result.Fail<OrderState>(Error.UnprocessableContent.ForField(
            "state",
            "validation.error",
            $"Cannot transition from '{this}' to '{newState}'. Allowed transitions: {string.Join(", ", AllowedTransitions)}"));
    }
}
