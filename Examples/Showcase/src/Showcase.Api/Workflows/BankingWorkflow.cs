namespace Trellis.Showcase.Api.Workflows;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Api.Services;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Orchestrates banking operations across the domain aggregate, fraud gateway, identity verifier,
/// and event publisher. The class is pure orchestration: every collaborator is injected, every
/// failure path produces a specific <see cref="Error"/> case.
/// </summary>
public class BankingWorkflow
{
    private const decimal MfaThreshold = 1000m;

    private readonly IFraudGateway _fraud;
    private readonly IIdentityVerifier _identity;
    private readonly IEventPublisher _events;

    public BankingWorkflow(IFraudGateway fraud, IIdentityVerifier identity, IEventPublisher events)
    {
        _fraud = fraud;
        _identity = identity;
        _events = events;
    }

    /// <summary>
    /// Withdraws money from <paramref name="account"/> after fraud screening and (for amounts
    /// over <see cref="MfaThreshold"/>) identity verification.
    /// </summary>
    public async Task<Result<BankAccount>> ProcessSecureWithdrawalAsync(
        BankAccount account,
        Money amount,
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        var fraudResult = await _fraud.AnalyzeTransactionAsync(account, amount, "withdrawal", cancellationToken);
        if (fraudResult.TryGetError(out var fraudError))
            return Result.Fail<BankAccount>(fraudError);

        if (amount.Amount > MfaThreshold)
        {
            var mfaResult = await _identity.VerifyAsync(account.CustomerId, verificationCode, cancellationToken);
            if (mfaResult.TryGetError(out var mfaError))
                return Result.Fail<BankAccount>(mfaError);
        }

        var withdrawal = account.Withdraw(amount, "Secure withdrawal");
        if (withdrawal.IsSuccess)
            await PublishAndAcceptAsync(account, cancellationToken);

        return withdrawal;
    }

    /// <summary>
    /// Transfers money between two accounts after fraud screening on both sides.
    /// </summary>
    public async Task<Result<(BankAccount From, BankAccount To)>> ProcessTransferAsync(
        BankAccount fromAccount,
        BankAccount toAccount,
        Money amount,
        string description,
        CancellationToken cancellationToken = default)
    {
        var fromCheck = _fraud.AnalyzeTransactionAsync(fromAccount, amount, "transfer-out", cancellationToken);
        var toCheck = _fraud.AnalyzeTransactionAsync(toAccount, amount, "transfer-in", cancellationToken);
        await Task.WhenAll(fromCheck, toCheck);

        var combined = fromCheck.Result.Combine(toCheck.Result);
        if (combined.TryGetError(out var combinedError))
            return Result.Fail<(BankAccount From, BankAccount To)>(combinedError);

        var transfer = fromAccount.TransferTo(toAccount, amount, description);
        if (transfer.IsSuccess)
        {
            await PublishAndAcceptAsync(fromAccount, cancellationToken);
            await PublishAndAcceptAsync(toAccount, cancellationToken);
        }

        return transfer;
    }

    /// <summary>
    /// Pays one day of interest on a savings account at the supplied annual rate.
    /// </summary>
    public async Task<Result<BankAccount>> ProcessInterestPaymentAsync(
        BankAccount account,
        decimal annualRate,
        CancellationToken cancellationToken = default)
    {
        var preconditions = account.ToResult()
            .Ensure(acc => acc.AccountType == AccountType.Savings,
                new Error.Conflict(null, "interest.savings.only") { Detail = "Interest is only paid on savings accounts." })
            .Ensure(acc => acc.Status == AccountStatus.Active,
                new Error.Conflict(null, "account.not.active") { Detail = $"Cannot pay interest to {account.Status} account." })
            .Ensure(acc => acc.Balance.Amount > 0,
                new Error.Conflict(null, "interest.zero.balance") { Detail = "No interest on accounts with zero balance." });

        if (preconditions.TryGetError(out var error))
            return Result.Fail<BankAccount>(error);

        var dailyAmount = account.Balance.Amount * (annualRate / 365m);
        var interest = Money.TryCreate(dailyAmount, account.Balance.Currency.Value);
        if (interest.TryGetError(out var moneyError))
            return Result.Fail<BankAccount>(moneyError);

        var deposit = account.Deposit(interest.Value, $"Daily interest at {annualRate:P2} APR");
        if (deposit.IsSuccess)
            await PublishAndAcceptAsync(account, cancellationToken);

        return deposit;
    }

    private async Task PublishAndAcceptAsync(BankAccount account, CancellationToken cancellationToken)
    {
        var events = account.UncommittedEvents();
        if (events.Count == 0)
            return;

        foreach (var domainEvent in events)
            await _events.PublishAsync(domainEvent, cancellationToken);

        account.AcceptChanges();
    }
}
