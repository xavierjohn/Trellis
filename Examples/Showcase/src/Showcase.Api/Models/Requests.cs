namespace Trellis.Showcase.Api.Models;

using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

public sealed record OpenAccountRequest(
    CustomerId CustomerId,
    AccountType AccountType,
    decimal InitialDeposit,
    decimal DailyWithdrawalLimit,
    decimal OverdraftLimit,
    string Currency = "USD");

public sealed record DepositRequest(decimal Amount, string Currency = "USD", string Description = "Deposit");

public sealed record WithdrawRequest(decimal Amount, string Currency = "USD", string Description = "Withdrawal");

public sealed record SecureWithdrawRequest(decimal Amount, string VerificationCode, string Currency = "USD");

public sealed record TransferRequest(AccountId ToAccountId, decimal Amount, string Description = "Transfer", string Currency = "USD");

public sealed record FreezeRequest(string Reason);

public sealed record InterestRequest(decimal AnnualRate);
