namespace Trellis.Showcase.Application.Services;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Adapter to an external fraud-detection service. Boundary errors map onto Trellis Error cases:
/// <list type="bullet">
///   <item><description>Suspicious transaction → <see cref="Error.Conflict"/> with reason code <c>fraud.detected</c>.</description></item>
///   <item><description>Service unreachable → <see cref="Error.ServiceUnavailable"/>.</description></item>
/// </list>
/// </summary>
public interface IFraudGateway
{
    Task<Result<Unit>> AnalyzeTransactionAsync(BankAccount account, Money amount, string transactionType, CancellationToken cancellationToken = default);
}

public interface IIdentityVerifier
{
    Task<Result<Unit>> VerifyAsync(CustomerId customerId, string verificationCode, CancellationToken cancellationToken = default);
}

public interface IEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}