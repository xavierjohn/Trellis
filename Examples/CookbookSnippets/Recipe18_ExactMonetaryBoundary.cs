// Cookbook Recipe 18: exact-input monetary validation before normalization.
namespace CookbookSnippets.Recipe18;

using System;
using Trellis;
using Trellis.Primitives;

internal static class PaymentAmountBoundary
{
    public static Result<MonetaryAmount> TryCreateMatchingQuote(
        decimal? amount, MonetaryAmount quotedAmount)
    {
        ArgumentNullException.ThrowIfNull(quotedAmount);

        return Result.EnsureNotNull(amount, "amount", "Amount is required.")
            .Ensure(value => value == quotedAmount.Value,
                static _ => Error.InvalidInput.ForField(
                    code: "payment.amount-mismatch", field: "amount",
                    detail: "Amount must match the quote exactly."))
            .Bind(static value => MonetaryAmount.TryCreate(value, "amount"));
    }
}