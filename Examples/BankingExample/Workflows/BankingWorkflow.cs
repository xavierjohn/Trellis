using Trellis.Primitives;

namespace BankingExample.Workflows;

using BankingExample.Aggregates;
using BankingExample.Events;
using BankingExample.Services;
using BankingExample.ValueObjects;
using Trellis;

/// <summary>
/// Orchestrates banking operations with fraud detection, validation, and domain event publishing.
/// </summary>
public class BankingWorkflow
{
    private readonly FraudDetectionService _fraudDetection;

    public BankingWorkflow(FraudDetectionService fraudDetection)
    {
        _fraudDetection = fraudDetection;
    }

    /// <summary>
    /// Processes a secure withdrawal with fraud detection.
    /// Demonstrates: Ensure, Bind, EnsureAsync, TapAsync, RecoverOnFailure, Domain Events
    /// </summary>
    public async Task<Result<BankAccount>> ProcessSecureWithdrawalAsync(
        BankAccount account,
        Money amount,
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        async Task<Result<BankAccount>> PerformChecks(BankAccount acc)
        {
            // Fraud detection
            var fraudResult = await _fraudDetection.AnalyzeTransactionAsync(acc, amount, "withdrawal", cancellationToken);
            if (fraudResult.TryGetError(out var fraudError))
                return Result.Fail<BankingExample.Aggregates.BankAccount>(fraudError);

            // Require MFA for large withdrawals
            if (amount.Amount > 1000)
            {
                var mfaResult = await _fraudDetection.VerifyCustomerIdentityAsync(
                    acc.CustomerId,
                    verificationCode,
                    cancellationToken
                );

                if (mfaResult.TryGetError(out var mfaError))
                    return Result.Fail<BankingExample.Aggregates.BankAccount>(mfaError);
            }

            return acc.ToResult();
        }

        return await account.ToResult()
            .BindAsync(PerformChecks)
            .BindAsync(acc => Task.FromResult(acc.Withdraw(amount, "ATM Withdrawal")))
            .TapAsync(acc => PublishEventsAndAcceptChangesAsync(acc, cancellationToken))
            .RecoverOnFailureAsync(
                predicate: error => error.Code == "fraud.detected",
                funcAsync: async error =>
                {
                    await Task.FromResult(account.Freeze("Suspicious activity detected"));
                    await PublishEventsAndAcceptChangesAsync(account, cancellationToken);
                    await NotifySecurityTeamAsync(account.CustomerId, error, cancellationToken);
                    return Result.Fail<BankingExample.Aggregates.BankAccount>(error); // Still return error after recovery
                }
            );
    }

    /// <summary>
    /// Processes a transfer between accounts with full validation.
    /// Demonstrates: Complex workflow with multiple validations, parallel operations using ParallelAsync, and domain events.
    /// </summary>
    public async Task<Result<(BankAccount From, BankAccount To)>> ProcessTransferAsync(
        BankAccount fromAccount,
        BankAccount toAccount,
        Money amount,
        string description,
        CancellationToken cancellationToken = default)
    {
        // Validate both accounts in parallel
        var fromTask = _fraudDetection.AnalyzeTransactionAsync(fromAccount, amount, "transfer-out", cancellationToken);
        var toTask = _fraudDetection.AnalyzeTransactionAsync(toAccount, amount, "transfer-in", cancellationToken);
        await Task.WhenAll(fromTask, toTask);
        var validationResult = fromTask.Result.Combine(toTask.Result);

        if (validationResult.TryGetError(out var validationError))
            return Result.Fail<(BankingExample.Aggregates.BankAccount From, BankingExample.Aggregates.BankAccount To)>(validationError);

        // Perform transfer
        return await fromAccount.TransferTo(toAccount, amount, description)
            .TapAsync((Func<(BankAccount From, BankAccount To), Task>)(async accounts =>
            {
                // Publish events from both accounts
                await PublishEventsAndAcceptChangesAsync(accounts.From, cancellationToken);
                await PublishEventsAndAcceptChangesAsync(accounts.To, cancellationToken);
            }))
            .TapAsync(accounts => NotifyTransferCompleteAsync(
                fromAccount.CustomerId,
                toAccount.CustomerId,
                amount,
                cancellationToken
            ));
    }

    /// <summary>
    /// Processes daily interest payment for savings accounts.
    /// Demonstrates: Domain events for interest payments.
    /// </summary>
    public async Task<Result<BankAccount>> ProcessInterestPaymentAsync(
        BankAccount account,
        decimal interestRate,
        CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(account.ToResult()
            .Ensure(acc => acc.AccountType == AccountType.Savings,
                Error.Domain("Interest is only paid on savings accounts"))
            .Ensure(acc => acc.Status == AccountStatus.Active,
                Error.Domain($"Cannot process interest for {account.Status} account"))
            .Ensure(acc => acc.Balance.Amount > 0,
                Error.Domain("No interest on accounts with zero or negative balance"))
            .Bind(acc =>
            {
                var interestAmount = acc.Balance.Amount * (interestRate / 365m); // Daily interest
                return Money.TryCreate(interestAmount, "USD");
            }))
            .BindAsync(async interest =>
            {
                await Task.Delay(50, cancellationToken);
                return account.Deposit(interest, $"Daily interest at {interestRate:P2} APR");
            })
            .TapAsync(acc => PublishEventsAndAcceptChangesAsync(acc, cancellationToken));
    }

    /// <summary>
    /// Processes multiple transactions in batch with rollback on any failure.
    /// Demonstrates: Transaction-like behavior with recovery and event handling.
    /// </summary>
    public async Task<Result<BankAccount>> ProcessBatchTransactionsAsync(
        BankAccount account,
        List<(Money Amount, string Description)> transactions,
        CancellationToken cancellationToken = default)
    {
        var processedCount = 0;

        foreach (var (amount, description) in transactions)
        {
            var result = account.Deposit(amount, description);

            if (result.TryGetError(out var depositError))
            {
                // Don't accept changes - events will be discarded
                Console.WriteLine($"? Transaction failed at item {processedCount + 1}, discarding {account.UncommittedEvents().Count} uncommitted events");
                return Result.Fail<BankingExample.Aggregates.BankAccount>(Error.Domain($"Batch transaction failed at item {processedCount + 1}: {depositError.Detail}"));
            }

            processedCount++;
        }

        // All successful - publish events and accept changes
        await PublishEventsAndAcceptChangesAsync(account, cancellationToken);
        Console.WriteLine($"? Successfully processed {processedCount} transactions");
        return Result.Ok(account);
    }

    /// <summary>
    /// Demonstrates the repository pattern with domain event publishing.
    /// This simulates what a real repository would do.
    /// </summary>
    private static async Task PublishEventsAndAcceptChangesAsync(
        BankAccount account,
        CancellationToken cancellationToken)
    {
        // 1. Get uncommitted events before accepting changes
        var events = account.UncommittedEvents();

        if (events.Count == 0)
            return;

        // 2. Simulate persisting the aggregate (in real code, this would save to database)
        await Task.Delay(20, cancellationToken);

        // 3. Publish each domain event
        foreach (var domainEvent in events)
        {
            await PublishEventAsync(domainEvent, cancellationToken);
        }

        // 4. Accept changes - clears the uncommitted events list
        account.AcceptChanges();

        Console.WriteLine($"📢 Published {events.Count} domain event(s) for account {account.Id}");
    }

    private static async Task PublishEventAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);

        // Log the event type and key information
        var eventInfo = domainEvent switch
        {
            AccountOpened e => $"AccountOpened: {e.AccountId}, Type: {e.AccountType}, Balance: {e.InitialBalance}",
            MoneyDeposited e => $"MoneyDeposited: {e.Amount} -> Balance: {e.NewBalance}",
            MoneyWithdrawn e => $"MoneyWithdrawn: {e.Amount} -> Balance: {e.NewBalance}",
            TransferCompleted e => $"TransferCompleted: {e.Amount} from {e.FromAccountId} to {e.ToAccountId}",
            AccountFrozen e => $"AccountFrozen: {e.AccountId}, Reason: {e.Reason}",
            AccountUnfrozen e => $"AccountUnfrozen: {e.AccountId}",
            AccountClosed e => $"AccountClosed: {e.AccountId}",
            InterestPaid e => $"InterestPaid: {e.InterestAmount} at {e.AnnualRate:P2}",
            _ => domainEvent.GetType().Name
        };

        Console.WriteLine($"   📋 Event: {eventInfo}");
    }

    private static async Task NotifyTransferCompleteAsync(
        CustomerId fromCustomerId,
        CustomerId toCustomerId,
        Money amount,
        CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
        Console.WriteLine($"📧 Transfer notification sent: {amount} from {fromCustomerId} to {toCustomerId}");
    }

    private static async Task NotifySecurityTeamAsync(
        CustomerId customerId,
        Error error,
        CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        Console.WriteLine($"🚨 Security alert for customer {customerId}: {error.Detail}");
    }
}
