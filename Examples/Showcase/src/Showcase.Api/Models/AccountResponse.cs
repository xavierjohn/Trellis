namespace Trellis.Showcase.Api.Models;

using Trellis.Showcase.Domain.Aggregates;

public sealed record AccountResponse(
    Guid Id,
    Guid CustomerId,
    string AccountType,
    decimal Balance,
    string Currency,
    string Status,
    decimal DailyWithdrawalLimit,
    decimal OverdraftLimit)
{
    public static AccountResponse From(BankAccount account) => new(
        account.Id.Value,
        account.CustomerId.Value,
        account.AccountType.ToString(),
        account.Balance.Amount,
        account.Balance.Currency.Value,
        account.Status.ToString(),
        account.DailyWithdrawalLimit.Amount,
        account.OverdraftLimit.Amount);
}
