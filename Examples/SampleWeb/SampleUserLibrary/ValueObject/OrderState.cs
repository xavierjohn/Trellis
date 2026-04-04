using Trellis;

namespace SampleUserLibrary;

/// <summary>
/// Enum value object for order state demonstrating RequiredEnum with ASP.NET Core.
/// Encapsulates business rules about state transitions, modification, and cancellation.
/// </summary>
public partial class OrderState : RequiredEnum<OrderState>
{
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
            return newState;

        var allowed = AllowedTransitions;
        var msg = allowed.Count > 0
            ? $"Cannot transition from '{this}' to '{newState}'. Allowed: {string.Join(", ", allowed)}"
            : $"Cannot transition from '{this}' — this is a terminal state.";
        return Error.Validation(msg, "state");
    }
}