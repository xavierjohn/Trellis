using Trellis.Primitives;

namespace BankingExample.Services;

using System.Globalization;
using BankingExample.Aggregates;
using BankingExample.ValueObjects;
using Trellis;

/// <summary>
/// Detects fraudulent transactions based on patterns.
/// Demonstrates various error types from the FunctionalDDD library.
/// </summary>
public class FraudDetectionService
{
    private const decimal SuspiciousAmountThreshold = 5000m;
    private const int MaxTransactionsPerHour = 10;

    /// <summary>
    /// Analyzes a transaction for fraud indicators.
    /// Returns Success if transaction appears legitimate, Failure if suspicious.
    /// Demonstrates: Error.Domain, Error.Validation, custom error codes
    /// </summary>
    public async Task<Result> AnalyzeTransactionAsync(
        BankAccount account,
        Money amount,
        string transactionType,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken); // Simulate API call

        return CheckSuspiciousAmount(amount)
            .Ensure(() => !IsHighFrequencyTrading(account),
                Error.Domain("Suspicious activity: Too many transactions in short period", "fraud.detected", account.Id.ToString(CultureInfo.InvariantCulture)))
            .Ensure(() => !IsUnusualPattern(account, amount),
                Error.Domain("Suspicious activity: Unusual transaction pattern", "fraud.detected", account.Id.ToString(CultureInfo.InvariantCulture)))
            .Tap(() => Console.WriteLine($"? Fraud check passed for {transactionType} of {amount}"));
    }

    /// <summary>
    /// Checks if amount exceeds suspicious threshold.
    /// Demonstrates: Error.Domain with custom code for fraud detection
    /// </summary>
    private static Result CheckSuspiciousAmount(Money amount)
    {
        if (amount.Amount > SuspiciousAmountThreshold)
        {
            Console.WriteLine($"⚠️ Large transaction detected: {amount}");
            return Result.Fail(Error.Domain(
                $"Transaction amount {amount} exceeds threshold of ${SuspiciousAmountThreshold}. Manual review required.",
                "fraud.detected",
                null
            ));
        }

        return Result.Ok();
    }

    /// <summary>
    /// Checks if account has too many recent transactions.
    /// </summary>
    private static bool IsHighFrequencyTrading(BankAccount account)
    {
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var recentCount = account.Transactions.Count(t => t.Timestamp >= oneHourAgo);

        return recentCount >= MaxTransactionsPerHour;
    }

    /// <summary>
    /// Detects unusual patterns (e.g., multiple round-number withdrawals).
    /// </summary>
    private static bool IsUnusualPattern(BankAccount account, Money amount)
    {
        // Check for multiple round-number transactions
        var recentTransactions = account.Transactions
            .Where(t => t.Timestamp >= DateTime.UtcNow.AddHours(-24))
            .ToList();

        var roundNumberCount = recentTransactions
            .Count(t => t.Amount.Amount % 1000 == 0);

        return roundNumberCount >= 3 && amount.Amount % 1000 == 0;
    }

    /// <summary>
    /// Verifies customer identity for high-value transactions.
    /// Demonstrates: Error.Unauthorized for authentication failures
    /// </summary>
    public async Task<Result> VerifyCustomerIdentityAsync(
        CustomerId customerId,
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(200, cancellationToken); // Simulate MFA verification

        if (string.IsNullOrWhiteSpace(verificationCode))
            return Result.Fail(Error.Unauthorized("Verification code required for this transaction", customerId));

        if (verificationCode.Length != 6 || !verificationCode.All(char.IsDigit))
            return Result.Fail(Error.Validation("Invalid verification code format", nameof(verificationCode)));

        // Simulate verification check
        if (verificationCode == "000000")
            return Result.Fail(Error.Unauthorized("Invalid verification code", customerId));

        Console.WriteLine($"? Customer {customerId} identity verified");
        return Result.Ok();
    }

    /// <summary>
    /// Checks if the external fraud detection service is available.
    /// Demonstrates: Error.ServiceUnavailable for external service failures
    /// </summary>
    public async Task<Result> CheckServiceHealthAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);

        // Simulate occasional service unavailability
        var random = new Random();
        if (random.Next(100) < 5) // 5% chance of service being down
        {
            return Result.Fail(Error.ServiceUnavailable(
                "Fraud detection service is temporarily unavailable. Please try again later.",
                "fraud-service"));
        }

        return Result.Ok();
    }

    /// <summary>
    /// Checks if the customer has exceeded their daily transaction rate limit.
    /// Demonstrates: Error.RateLimit for quota exceeded scenarios
    /// </summary>
    public async Task<Result> CheckRateLimitAsync(
        CustomerId customerId,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);

        // Simulate rate limiting (in real implementation, would check against a counter)
        // For demo purposes, always pass
        Console.WriteLine($"? Rate limit check passed for customer {customerId}");
        return Result.Ok();
    }
}
