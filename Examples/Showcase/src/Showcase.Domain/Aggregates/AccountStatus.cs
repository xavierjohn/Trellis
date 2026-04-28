namespace Trellis.Showcase.Domain.Aggregates;

using Trellis;

/// <summary>
/// Lifecycle status of a <see cref="BankAccount"/>. Modeled as a <see cref="RequiredEnum{TSelf}"/>
/// so each value can carry its own behavior (e.g., whether it is terminal) and so JSON / model
/// binding round-trip the value as its symbolic name (<c>"Active"</c>) rather than an opaque integer.
/// </summary>
public partial class AccountStatus : RequiredEnum<AccountStatus>
{
    /// <summary>The account is open and accepts deposits, withdrawals, and transfers.</summary>
    public static readonly AccountStatus Active = new(isTerminal: false);

    /// <summary>The account is temporarily blocked from money operations and can be unfrozen.</summary>
    public static readonly AccountStatus Frozen = new(isTerminal: false);

    /// <summary>The account is permanently closed; no further transitions are permitted.</summary>
    public static readonly AccountStatus Closed = new(isTerminal: true);

    /// <summary>
    /// True when no further lifecycle transitions are permitted from this status.
    /// </summary>
    public bool IsTerminal { get; }

    private AccountStatus(bool isTerminal) => IsTerminal = isTerminal;
}