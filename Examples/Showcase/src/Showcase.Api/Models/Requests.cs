namespace Trellis.Showcase.Api.Models;

using Trellis.Primitives;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Wire DTO for opening a new account. Money is a structured value object that
/// serializes as <c>{ "amount": …, "currency": … }</c>; binding fails fast at the
/// JSON layer when any amount or currency code is invalid (axiom A1b).
/// </summary>
public sealed record OpenAccountRequest(
    CustomerId CustomerId,
    AccountType AccountType,
    Money InitialDeposit,
    Money DailyWithdrawalLimit,
    Money OverdraftLimit);

public sealed record DepositRequest(Money Amount, string Description = "Deposit");

public sealed record WithdrawRequest(Money Amount, string Description = "Withdrawal");

public sealed record SecureWithdrawRequest(Money Amount, string VerificationCode);

public sealed record TransferRequest(AccountId ToAccountId, Money Amount, string Description = "Transfer");

public sealed record FreezeRequest(string Reason);

public sealed record InterestRequest(decimal AnnualRate);
