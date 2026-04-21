namespace Trellis.Showcase.Domain.Aggregates;

using global::Stateless;
using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.Entities;
using Trellis.Showcase.Domain.Events;
using Trellis.Showcase.Domain.Lifecycle;
using Trellis.Showcase.Domain.ValueObjects;
using Trellis.StateMachine;

public enum AccountStatus
{
    Active,
    Frozen,
    Closed,
}

public enum AccountType
{
    Checking,
    Savings,
    MoneyMarket,
}

/// <summary>
/// Bank account aggregate. Lifecycle transitions are modeled as a state machine; all money
/// operations and state changes return <see cref="Result{T}"/> with strongly-typed
/// <see cref="Error"/> cases for failures.
/// </summary>
public class BankAccount : Aggregate<AccountId>
{
    private readonly List<Transaction> _transactions = [];
    private readonly TimeProvider _timeProvider;
    private StateMachine<AccountStatus, AccountTrigger> _lifecycle = default!;

    public CustomerId CustomerId { get; }
    public AccountType AccountType { get; }
    public Money Balance { get; private set; }
    public Money DailyWithdrawalLimit { get; }
    public Money OverdraftLimit { get; }
    public AccountStatus Status => _lifecycle.State;
    public IReadOnlyList<Transaction> Transactions => _transactions.AsReadOnly();

    private BankAccount(
        AccountId id,
        CustomerId customerId,
        AccountType accountType,
        Money initialBalance,
        Money dailyWithdrawalLimit,
        Money overdraftLimit,
        AccountStatus initialStatus,
        TimeProvider timeProvider)
        : base(id)
    {
        CustomerId = customerId;
        AccountType = accountType;
        Balance = initialBalance;
        DailyWithdrawalLimit = dailyWithdrawalLimit;
        OverdraftLimit = overdraftLimit;
        _timeProvider = timeProvider;
        ConfigureLifecycle(initialStatus);
    }

    private void ConfigureLifecycle(AccountStatus initial)
    {
        _lifecycle = new StateMachine<AccountStatus, AccountTrigger>(initial);

        _lifecycle.Configure(AccountStatus.Active)
            .Permit(AccountTrigger.Freeze, AccountStatus.Frozen)
            .Permit(AccountTrigger.Close, AccountStatus.Closed);

        _lifecycle.Configure(AccountStatus.Frozen)
            .Permit(AccountTrigger.Unfreeze, AccountStatus.Active);

        // Closed is terminal: any further trigger surfaces as Error.Conflict
        // via FireResult (Stateless throws InvalidOperationException, which
        // FireResult translates). We deliberately do NOT call .Ignore(...) here:
        // ignoring a trigger would silently succeed and let the outer Tap(...)
        // chain emit lifecycle events for a state that did not actually change.
        _lifecycle.Configure(AccountStatus.Closed);
    }

    /// <summary>
    /// Reconstructs an existing <see cref="BankAccount"/> from persisted state without raising
    /// <see cref="AccountOpened"/>. Use this from repositories or seed data; use
    /// <see cref="TryCreate"/> for new accounts that should emit creation events.
    /// </summary>
    public static BankAccount Hydrate(
        AccountId id,
        CustomerId customerId,
        AccountType accountType,
        Money balance,
        Money dailyWithdrawalLimit,
        Money overdraftLimit,
        AccountStatus status,
        TimeProvider? timeProvider = null) =>
        new(id, customerId, accountType, balance, dailyWithdrawalLimit, overdraftLimit, status, timeProvider ?? TimeProvider.System);

    public static Result<BankAccount> TryCreate(
        CustomerId customerId,
        AccountType accountType,
        Money initialDeposit,
        Money dailyWithdrawalLimit,
        Money overdraftLimit,
        TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        var violations = new List<FieldViolation>();
        if (initialDeposit.Amount < 0)
            violations.Add(new FieldViolation(InputPointer.ForProperty(nameof(initialDeposit)), "validation.range") { Detail = "Initial deposit must be non-negative" });
        if (dailyWithdrawalLimit.Amount <= 0)
            violations.Add(new FieldViolation(InputPointer.ForProperty(nameof(dailyWithdrawalLimit)), "validation.range") { Detail = "Daily withdrawal limit must be positive" });
        if (overdraftLimit.Amount < 0)
            violations.Add(new FieldViolation(InputPointer.ForProperty(nameof(overdraftLimit)), "validation.range") { Detail = "Overdraft limit must be non-negative" });

        if (violations.Count > 0)
            return Result.Fail<BankAccount>(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())));

        var account = new BankAccount(
            AccountId.NewUniqueV4(),
            customerId,
            accountType,
            initialDeposit,
            dailyWithdrawalLimit,
            overdraftLimit,
            AccountStatus.Active,
            timeProvider);

        account.DomainEvents.Add(new AccountOpened(
            account.Id,
            customerId,
            accountType,
            initialDeposit,
            timeProvider.GetUtcNow().UtcDateTime));

        return Result.Ok(account);
    }

    public Result<BankAccount> Deposit(Money amount, string description = "Deposit") =>
        this.ToResult()
            .Ensure(_ => Status == AccountStatus.Active,
                new Error.Conflict(null, "account.not.active") { Detail = $"Cannot deposit to {Status} account" })
            .Ensure(_ => amount.Amount > 0,
                new Error.UnprocessableContent(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(nameof(amount)), "validation.range") { Detail = "Deposit amount must be positive" })))
            .Ensure(_ => amount.Amount <= 10000,
                new Error.Conflict(null, "deposit.limit.exceeded") { Detail = "Single deposit cannot exceed $10,000" })
            .Bind(_ => Balance.Add(amount))
            .Tap(newBalance =>
            {
                Balance = newBalance;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                _transactions.Add(Transaction.CreateDeposit(TransactionId.NewUniqueV4(), amount, Balance, description, now));
                DomainEvents.Add(new MoneyDeposited(Id, amount, Balance, description, now));
            })
            .Map(_ => this);

    public Result<BankAccount> Withdraw(Money amount, string description = "Withdrawal")
    {
        var todayTotal = GetTodayWithdrawals();

        return this.ToResult()
            .Ensure(_ => Status == AccountStatus.Active,
                new Error.Conflict(null, "account.not.active") { Detail = $"Cannot withdraw from {Status} account" })
            .Ensure(_ => amount.Amount > 0,
                new Error.UnprocessableContent(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(nameof(amount)), "validation.range") { Detail = "Withdrawal amount must be positive" })))
            .Bind(_ => todayTotal.Add(amount))
            .Ensure(totalWithToday => !totalWithToday.IsGreaterThanOrEqual(DailyWithdrawalLimit),
                new Error.Conflict(null, "withdrawal.daily.limit") { Detail = $"Daily withdrawal limit of {DailyWithdrawalLimit} would be exceeded" })
            .Bind(_ => Balance.Subtract(amount))
            .Ensure(newBalance => newBalance.Amount >= -OverdraftLimit.Amount,
                new Error.Conflict(null, "withdrawal.overdraft.exceeded") { Detail = $"Withdrawal would exceed overdraft limit of {OverdraftLimit}" })
            .Tap(newBalance =>
            {
                Balance = newBalance;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                _transactions.Add(Transaction.CreateWithdrawal(TransactionId.NewUniqueV4(), amount, Balance, description, now));
                DomainEvents.Add(new MoneyWithdrawn(Id, amount, Balance, description, now));
            })
            .Map(_ => this);
    }

    public Result<BankAccount> PayInterest(Money interestAmount, decimal annualRate)
    {
        if (AccountType != AccountType.Savings)
            return Result.Fail<BankAccount>(
                new Error.Conflict(null, "interest.savings.only") { Detail = "Interest is only paid on savings accounts." });

        if (Status != AccountStatus.Active)
            return Result.Fail<BankAccount>(
                new Error.Conflict(null, "account.not.active") { Detail = $"Cannot pay interest to {Status} account." });

        if (Balance.Amount <= 0)
            return Result.Fail<BankAccount>(
                new Error.Conflict(null, "interest.zero.balance") { Detail = "No interest on accounts with zero balance." });

        if (interestAmount.Amount <= 0)
            return Result.Fail<BankAccount>(
                new Error.UnprocessableContent(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(nameof(interestAmount)), "validation.range") { Detail = "Interest amount must be positive" })));

        return Balance.Add(interestAmount)
            .Tap(newBalance =>
            {
                Balance = newBalance;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var description = $"Interest at {annualRate:P2} APR";
                _transactions.Add(Transaction.CreateInterest(TransactionId.NewUniqueV4(), interestAmount, Balance, description, now));
                DomainEvents.Add(new InterestPaid(Id, interestAmount, Balance, annualRate, now));
            })
            .Map(_ => this);
    }

    public Result<(BankAccount From, BankAccount To)> TransferTo(BankAccount toAccount, Money amount, string description = "Transfer")
    {
        ArgumentNullException.ThrowIfNull(toAccount);

        if (Id.Equals(toAccount.Id))
            return Result.Fail<(BankAccount From, BankAccount To)>(
                new Error.Conflict(null, "transfer.same.account") { Detail = "Cannot transfer to the same account" });

        // Pre-validate destination so a successful Withdraw is not followed by a failing Deposit,
        // which would leave the source account debited while the destination is unchanged.
        if (toAccount.Status != AccountStatus.Active)
            return Result.Fail<(BankAccount From, BankAccount To)>(
                new Error.Conflict(null, "account.not.active") { Detail = $"Cannot transfer to {toAccount.Status} account" });

        if (amount.Amount > 10000)
            return Result.Fail<(BankAccount From, BankAccount To)>(
                new Error.Conflict(null, "deposit.limit.exceeded") { Detail = "Single deposit cannot exceed $10,000" });

        if (!toAccount.Balance.Currency.Equals(amount.Currency))
            return Result.Fail<(BankAccount From, BankAccount To)>(
                new Error.UnprocessableContent(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(nameof(amount)), "validation.currency")
                    { Detail = $"Cannot deposit {amount.Currency} into {toAccount.Balance.Currency} account" })));

        return Withdraw(amount, $"{description} to {toAccount.Id}")
            .Bind(_ => toAccount.Deposit(amount, $"{description} from {Id}"))
            .Tap(_ => DomainEvents.Add(new TransferCompleted(Id, toAccount.Id, amount, description, _timeProvider.GetUtcNow().UtcDateTime)))
            .Map(_ => (this, toAccount));
    }

    public Result<BankAccount> Freeze(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Fail<BankAccount>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(nameof(reason)), "validation.required") { Detail = "Freeze reason is required" })));
        }

        return _lifecycle.FireResult(AccountTrigger.Freeze)
            .Tap(_ => DomainEvents.Add(new AccountFrozen(Id, reason, _timeProvider.GetUtcNow().UtcDateTime)))
            .Map(_ => this);
    }

    public Result<BankAccount> Unfreeze() =>
        _lifecycle.FireResult(AccountTrigger.Unfreeze)
            .Tap(_ => DomainEvents.Add(new AccountUnfrozen(Id, _timeProvider.GetUtcNow().UtcDateTime)))
            .Map(_ => this);

    public Result<BankAccount> Close()
    {
        if (Balance.Amount != 0)
        {
            return Result.Fail<BankAccount>(
                new Error.Conflict(null, "account.close.nonzero.balance") { Detail = "Account balance must be zero to close" });
        }

        return _lifecycle.FireResult(AccountTrigger.Close)
            .Tap(_ => DomainEvents.Add(new AccountClosed(Id, _timeProvider.GetUtcNow().UtcDateTime)))
            .Map(_ => this);
    }

    public Money GetAvailableBalance() => Money.Create(Balance.Amount + OverdraftLimit.Amount, Balance.Currency);

    private Money GetTodayWithdrawals()
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var total = _transactions
            .Where(t => t.Type == TransactionType.Withdrawal && t.Timestamp.Date == today)
            .Sum(t => t.Amount.Amount);
        return Money.Create(total, Balance.Currency);
    }
}
