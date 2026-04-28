namespace Trellis.Showcase.Application.Workflows;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Application.Persistence;
using Trellis.Showcase.Application.Services;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Application boundary for every state-changing banking use case. Each method:
/// <list type="number">
///   <item>Validates cross-aggregate / external preconditions (fraud, identity).</item>
///   <item>Mutates the aggregate via its domain methods.</item>
///   <item>On success, publishes uncommitted events and calls <see cref="IAggregate.AcceptChanges"/>.</item>
/// </list>
/// Controllers must not mutate aggregates directly — load them from <see cref="IAccountRepository"/>
/// and dispatch through this workflow so events are reliably published exactly once.
/// </summary>
public class BankingWorkflow
{
    private const decimal MfaThreshold = 1000m;

    private readonly IAccountRepository _repository;
    private readonly IFraudGateway _fraud;
    private readonly IIdentityVerifier _identity;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _timeProvider;

    public BankingWorkflow(
        IAccountRepository repository,
        IFraudGateway fraud,
        IIdentityVerifier identity,
        IEventPublisher events,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _fraud = fraud;
        _identity = identity;
        _events = events;
        _timeProvider = timeProvider;
    }

    public Task<Result<BankAccount>> OpenAccountAsync(
        CustomerId customerId,
        AccountType accountType,
        Money initialDeposit,
        Money dailyWithdrawalLimit,
        Money overdraftLimit,
        CancellationToken cancellationToken = default) =>
        BankAccount.TryCreate(customerId, accountType, initialDeposit, dailyWithdrawalLimit, overdraftLimit, _timeProvider)
            .Tap(_repository.Add)
            .TapAsync(account => CommitAsync(account, cancellationToken));

    public Task<Result<BankAccount>> DepositAsync(BankAccount account, Money amount, string description, CancellationToken cancellationToken = default) =>
        account.Deposit(amount, description)
            .TapAsync(updated => CommitAsync(updated, cancellationToken));

    public Task<Result<BankAccount>> WithdrawAsync(BankAccount account, Money amount, string description, CancellationToken cancellationToken = default) =>
        account.Withdraw(amount, description)
            .TapAsync(updated => CommitAsync(updated, cancellationToken));

    public async Task<Result<BankAccount>> SecureWithdrawAsync(
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

        return await account.Withdraw(amount, "Secure withdrawal")
            .TapAsync(updated => CommitAsync(updated, cancellationToken));
    }

    public async Task<Result<(BankAccount From, BankAccount To)>> TransferAsync(
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

        return await fromAccount.TransferTo(toAccount, amount, description)
            .TapAsync((Func<(BankAccount From, BankAccount To), Task>)(async pair =>
            {
                await CommitAsync(pair.From, cancellationToken);
                await CommitAsync(pair.To, cancellationToken);
            }));
    }

    public Task<Result<BankAccount>> FreezeAsync(BankAccount account, string reason, CancellationToken cancellationToken = default) =>
        account.Freeze(reason)
            .TapAsync(updated => CommitAsync(updated, cancellationToken));

    public Task<Result<BankAccount>> UnfreezeAsync(BankAccount account, CancellationToken cancellationToken = default) =>
        account.Unfreeze()
            .TapAsync(updated => CommitAsync(updated, cancellationToken));

    public Task<Result<BankAccount>> CloseAsync(BankAccount account, CancellationToken cancellationToken = default) =>
        account.Close()
            .TapAsync(updated => CommitAsync(updated, cancellationToken));

    public Task<Result<BankAccount>> PayInterestAsync(BankAccount account, decimal annualRate, CancellationToken cancellationToken = default)
    {
        var dailyAmount = account.Balance.Amount * (annualRate / 365m);

        return Money.TryCreate(dailyAmount, account.Balance.Currency.Value)
            .Bind(interest => account.PayInterest(interest, annualRate))
            .TapAsync(updated => CommitAsync(updated, cancellationToken));
    }

    private async Task CommitAsync(BankAccount account, CancellationToken cancellationToken)
    {
        var events = account.UncommittedEvents();
        if (events.Count == 0)
            return;

        foreach (var domainEvent in events)
            await _events.PublishAsync(domainEvent, cancellationToken);

        account.AcceptChanges();
    }
}