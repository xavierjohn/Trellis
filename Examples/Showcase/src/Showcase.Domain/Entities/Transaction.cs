namespace Trellis.Showcase.Domain.Entities;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.ValueObjects;

public enum TransactionType
{
    Deposit,
    Withdrawal,
    Transfer,
    Fee,
    Interest,
}

/// <summary>
/// Represents a single transaction on an account.
/// </summary>
public class Transaction : Entity<TransactionId>
{
    public TransactionType Type { get; }
    public Money Amount { get; }
    public Money BalanceAfter { get; }
    public string Description { get; }
    public DateTime Timestamp { get; }

    private Transaction(
        TransactionId id,
        TransactionType type,
        Money amount,
        Money balanceAfter,
        string description,
        DateTime timestamp)
        : base(id)
    {
        Type = type;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Description = description;
        Timestamp = timestamp;
    }

    public static Transaction CreateDeposit(TransactionId id, Money amount, Money balanceAfter, string description, DateTime timestamp)
        => new(id, TransactionType.Deposit, amount, balanceAfter, description, timestamp);

    public static Transaction CreateWithdrawal(TransactionId id, Money amount, Money balanceAfter, string description, DateTime timestamp)
        => new(id, TransactionType.Withdrawal, amount, balanceAfter, description, timestamp);

    public static Transaction CreateTransfer(TransactionId id, Money amount, Money balanceAfter, string description, DateTime timestamp)
        => new(id, TransactionType.Transfer, amount, balanceAfter, description, timestamp);

    public static Transaction CreateInterest(TransactionId id, Money amount, Money balanceAfter, string description, DateTime timestamp)
        => new(id, TransactionType.Interest, amount, balanceAfter, description, timestamp);
}
