using Trellis.Primitives;

namespace EcommerceExample.Services;

using EcommerceExample.Aggregates;
using EcommerceExample.ValueObjects;
using Trellis;

/// <summary>
/// Simulates a payment gateway service.
/// </summary>
public class PaymentService
{
    public async Task<Result<string>> ProcessPaymentAsync(Order order, string cardNumber, string cvv, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(ValidateCardNumber(cardNumber)
            .Combine(ValidateCVV(cvv))
            .Ensure(() => order.Total.Amount >= 0.01m, Error.Validation("Payment amount must be at least 0.01")))
            .BindAsync(async () => await ChargeCardAsync(order.Total, cardNumber, cancellationToken))
            .TapAsync(async transactionId => await LogPaymentSuccessAsync(order.Id, transactionId, cancellationToken))
            .TapOnFailureAsync(async error => await LogPaymentFailureAsync(order.Id, error, cancellationToken));
    }

    public async Task<Result> RefundPaymentAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            return Result.Fail(Error.Validation("Transaction ID is required"));

        await Task.Delay(100, cancellationToken); // Simulate API call

        return Result.Ok();
    }

    private static Result ValidateCardNumber(string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
            return Result.Fail(Error.Validation("Card number is required", nameof(cardNumber)));

        var digitsOnly = new string(cardNumber.Where(char.IsDigit).ToArray());

        if (digitsOnly.Length is < 13 or > 19)
            return Result.Fail(Error.Validation("Card number must be between 13 and 19 digits", nameof(cardNumber)));

        return Result.Ok();
    }

    private static Result ValidateCVV(string cvv)
    {
        if (string.IsNullOrWhiteSpace(cvv))
            return Result.Fail(Error.Validation("CVV is required", nameof(cvv)));

        if (cvv.Length < 3 || cvv.Length > 4 || !cvv.All(char.IsDigit))
            return Result.Fail(Error.Validation("CVV must be 3 or 4 digits", nameof(cvv)));

        return Result.Ok();
    }

    private static async Task<Result<string>> ChargeCardAsync(Money amount, string cardNumber, CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken); // Simulate payment gateway call

        // Simulate occasional failures
        if (cardNumber.EndsWith("0000", StringComparison.Ordinal))
            return Result.Fail<string>(Error.Validation("Card declined - insufficient funds"));

        if (cardNumber.EndsWith("9999", StringComparison.Ordinal))
            return Result.Fail<string>(Error.Unexpected("Payment gateway timeout"));

        return Result.Ok($"TXN-{Guid.NewGuid():N}");
    }

    private static async Task LogPaymentSuccessAsync(OrderId orderId, string transactionId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        Console.WriteLine($"Payment successful for order {orderId}: {transactionId}");
    }

    private static async Task LogPaymentFailureAsync(OrderId orderId, Error error, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        Console.WriteLine($"Payment failed for order {orderId}: {error.Detail}");
    }
}
