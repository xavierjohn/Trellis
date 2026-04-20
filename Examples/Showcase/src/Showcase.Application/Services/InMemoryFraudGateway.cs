namespace Trellis.Showcase.Application.Services;

using Trellis;
using Trellis.Primitives;
using Trellis.Showcase.Domain.Aggregates;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Deterministic in-memory fraud gateway used by Showcase. Real implementations would call
/// an external risk-scoring service. Rules:
/// <list type="bullet">
///   <item><description>Amount strictly greater than <see cref="SuspiciousAmountThreshold"/> → <see cref="Error.Conflict"/> with reason code <c>fraud.detected</c>.</description></item>
///   <item><description><see cref="MaxTransactionsPerHour"/> or more transactions on the account in the last hour → <see cref="Error.Conflict"/> with reason code <c>fraud.detected</c>.</description></item>
/// </list>
/// </summary>
public sealed class InMemoryFraudGateway : IFraudGateway
{
    public const decimal SuspiciousAmountThreshold = 5000m;
    public const int MaxTransactionsPerHour = 10;

    private readonly TimeProvider _timeProvider;

    public InMemoryFraudGateway(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public Task<Result> AnalyzeTransactionAsync(
        BankAccount account,
        Money amount,
        string transactionType,
        CancellationToken cancellationToken = default)
    {
        if (amount.Amount > SuspiciousAmountThreshold)
        {
            return Task.FromResult<Result>(Result.Fail(new Error.Conflict(null, "fraud.detected")
            {
                Detail = $"Transaction amount {amount} exceeds threshold of ${SuspiciousAmountThreshold}.",
            }));
        }

        var oneHourAgo = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);
        var recentCount = account.Transactions.Count(t => t.Timestamp >= oneHourAgo);
        if (recentCount >= MaxTransactionsPerHour)
        {
            return Task.FromResult<Result>(Result.Fail(new Error.Conflict(null, "fraud.detected")
            {
                Detail = "Too many transactions in the last hour.",
            }));
        }

        return Task.FromResult(Result.Ok());
    }
}
