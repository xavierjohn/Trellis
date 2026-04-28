namespace Trellis.Showcase.Domain.Aggregates;

using Trellis;

/// <summary>
/// Product type of a <see cref="BankAccount"/>. Modeled as a <see cref="RequiredEnum{TSelf}"/>
/// so each product can carry its own rules (e.g., whether it earns interest) and so JSON / model
/// binding round-trip the value as its symbolic name (<c>"Checking"</c>) rather than an opaque integer.
/// </summary>
public partial class AccountType : RequiredEnum<AccountType>
{
    /// <summary>Standard transactional account; does not earn interest.</summary>
    public static readonly AccountType Checking = new(earnsInterest: false);

    /// <summary>Interest-bearing savings account.</summary>
    public static readonly AccountType Savings = new(earnsInterest: true);

    /// <summary>Higher-yield deposit account; treated like savings for interest purposes.</summary>
    public static readonly AccountType MoneyMarket = new(earnsInterest: true);

    /// <summary>
    /// True when this product accrues and can be paid interest.
    /// </summary>
    public bool EarnsInterest { get; }

    private AccountType(bool earnsInterest) => EarnsInterest = earnsInterest;
}