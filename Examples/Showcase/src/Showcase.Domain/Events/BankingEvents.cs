namespace Trellis.Showcase.Domain.Events;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

public record AccountOpened(
    AccountId AccountId,
    CustomerId CustomerId,
    AccountType AccountType,
    Money InitialBalance,
    DateTime OccurredAt) : IDomainEvent;

public record MoneyDeposited(
    AccountId AccountId,
    Money Amount,
    Money NewBalance,
    string Description,
    DateTime OccurredAt) : IDomainEvent;

public record MoneyWithdrawn(
    AccountId AccountId,
    Money Amount,
    Money NewBalance,
    string Description,
    DateTime OccurredAt) : IDomainEvent;

public record TransferCompleted(
    AccountId FromAccountId,
    AccountId ToAccountId,
    Money Amount,
    string Description,
    DateTime OccurredAt) : IDomainEvent;

public record AccountFrozen(
    AccountId AccountId,
    string Reason,
    DateTime OccurredAt) : IDomainEvent;

public record AccountUnfrozen(
    AccountId AccountId,
    DateTime OccurredAt) : IDomainEvent;

public record AccountClosed(
    AccountId AccountId,
    DateTime OccurredAt) : IDomainEvent;

public record InterestPaid(
    AccountId AccountId,
    Money InterestAmount,
    Money NewBalance,
    decimal AnnualRate,
    DateTime OccurredAt) : IDomainEvent;
