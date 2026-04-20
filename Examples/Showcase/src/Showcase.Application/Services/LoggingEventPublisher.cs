namespace Trellis.Showcase.Application.Services;

#pragma warning disable CA1873 // Sample-grade event logging; hot-path optimization not required.

using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.Showcase.Domain.Events;

/// <summary>
/// Default <see cref="IEventPublisher"/> that logs each event. Replace with a real bus binding
/// (Service Bus, MassTransit, etc.) in production code.
/// </summary>
public sealed partial class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) => _logger = logger;

    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        switch (domainEvent)
        {
            case AccountOpened e: LogAccountOpened(e.AccountId.Value, e.AccountType.ToString(), e.InitialBalance.ToString()); break;
            case MoneyDeposited e: LogMoneyDeposited(e.Amount.ToString(), e.NewBalance.ToString()); break;
            case MoneyWithdrawn e: LogMoneyWithdrawn(e.Amount.ToString(), e.NewBalance.ToString()); break;
            case TransferCompleted e: LogTransferCompleted(e.Amount.ToString(), e.FromAccountId.Value, e.ToAccountId.Value); break;
            case AccountFrozen e: LogAccountFrozen(e.AccountId.Value, e.Reason); break;
            case AccountUnfrozen e: LogAccountUnfrozen(e.AccountId.Value); break;
            case AccountClosed e: LogAccountClosed(e.AccountId.Value); break;
            case InterestPaid e: LogInterestPaid(e.InterestAmount.ToString(), e.AnnualRate); break;
            default: LogUnknown(domainEvent.GetType().Name); break;
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "AccountOpened {AccountId} type {Type} balance {Balance}")]
    private partial void LogAccountOpened(Guid accountId, string type, string balance);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "MoneyDeposited {Amount} new balance {Balance}")]
    private partial void LogMoneyDeposited(string amount, string balance);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "MoneyWithdrawn {Amount} new balance {Balance}")]
    private partial void LogMoneyWithdrawn(string amount, string balance);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "TransferCompleted {Amount} from {From} to {To}")]
    private partial void LogTransferCompleted(string amount, Guid from, Guid to);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "AccountFrozen {AccountId} reason {Reason}")]
    private partial void LogAccountFrozen(Guid accountId, string reason);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "AccountUnfrozen {AccountId}")]
    private partial void LogAccountUnfrozen(Guid accountId);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "AccountClosed {AccountId}")]
    private partial void LogAccountClosed(Guid accountId);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "InterestPaid {Amount} at rate {Rate}")]
    private partial void LogInterestPaid(string amount, decimal rate);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Domain event {Name}")]
    private partial void LogUnknown(string name);
}
