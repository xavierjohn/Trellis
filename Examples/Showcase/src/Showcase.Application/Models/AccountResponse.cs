namespace Trellis.Showcase.Application.Models;

using Trellis.Primitives;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Wire representation of a bank account. Uses VO types directly so that the JSON shape
/// reflects the same constraints the domain enforces.
/// </summary>
public sealed record AccountResponse(
    AccountId Id,
    CustomerId CustomerId,
    AccountType AccountType,
    Money Balance,
    AccountStatus Status,
    Money DailyWithdrawalLimit,
    Money OverdraftLimit)
{
    public static AccountResponse From(BankAccount account) => new(
        account.Id,
        account.CustomerId,
        account.AccountType,
        account.Balance,
        account.Status,
        account.DailyWithdrawalLimit,
        account.OverdraftLimit);
}
