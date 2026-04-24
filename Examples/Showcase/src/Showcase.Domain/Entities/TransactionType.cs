namespace Trellis.Showcase.Domain.Entities;

using Trellis;

/// <summary>
/// Categorizes a <see cref="Transaction"/>. Modeled as a <see cref="RequiredEnum{TSelf}"/> so the
/// values JSON-serialize as their symbolic names (<c>"Deposit"</c>) and remain extensible with
/// per-value behavior should the sample grow.
/// </summary>
public partial class TransactionType : RequiredEnum<TransactionType>
{
    public static readonly TransactionType Deposit = new();
    public static readonly TransactionType Withdrawal = new();
    public static readonly TransactionType Transfer = new();
    public static readonly TransactionType Fee = new();
    public static readonly TransactionType Interest = new();
}
