using Trellis.Asp;
using Trellis.Primitives;

namespace SampleMinimalApiNoAot.API;

using Trellis;

public static class MoneyRoutes
{
    public static void UseMoneyRoute(this WebApplication app)
    {
        var moneyApi = app.MapGroup("/money");

        // Money Creation
        moneyApi.MapPost("/create", (CreateMoneyRequest request) =>
            Money.TryCreate(request.Amount, request.Currency)
                .ToHttpResult());

        // Arithmetic Operations
        moneyApi.MapPost("/add", (MoneyOperationRequest request) =>
            Money.TryCreate(request.Left.Amount, request.Left.Currency)
                .Combine(Money.TryCreate(request.Right.Amount, request.Right.Currency))
                .Bind((left, right) => left.Add(right))
                .ToHttpResult());

        moneyApi.MapPost("/subtract", (MoneyOperationRequest request) =>
            Money.TryCreate(request.Left.Amount, request.Left.Currency)
                .Combine(Money.TryCreate(request.Right.Amount, request.Right.Currency))
                .Bind((left, right) => left.Subtract(right))
                .ToHttpResult());

        moneyApi.MapPost("/multiply", (MultiplyMoneyRequest request) =>
            Money.TryCreate(request.Money.Amount, request.Money.Currency)
                .Bind(money => money.Multiply(request.Multiplier))
                .ToHttpResult());

        moneyApi.MapPost("/multiplyByQuantity", (MultiplyByQuantityRequest request) =>
            Money.TryCreate(request.Money.Amount, request.Money.Currency)
                .Bind(money => money.Multiply(request.Quantity))
                .ToHttpResult());

        moneyApi.MapPost("/divide", (DivideMoneyRequest request) =>
            Money.TryCreate(request.Money.Amount, request.Money.Currency)
                .Bind(money => money.Divide(request.Divisor))
                .ToHttpResult());

        // Allocation
        moneyApi.MapPost("/allocate", (AllocateMoneyRequest request) =>
            Money.TryCreate(request.Money.Amount, request.Money.Currency)
                .Bind(money => money.Allocate(request.Ratios))
                .ToHttpResult());

        // Comparison
        moneyApi.MapPost("/compare", (CompareMoneyRequest request) =>
            Money.TryCreate(request.Left.Amount, request.Left.Currency)
                .Combine(Money.TryCreate(request.Right.Amount, request.Right.Currency))
                .Map<(Money, Money), bool>(tuple => request.Operation.ToLowerInvariant() switch
                {
                    "greaterthan" => tuple.Item1.IsGreaterThan(tuple.Item2),
                    "greaterthanorequal" => tuple.Item1.IsGreaterThanOrEqual(tuple.Item2),
                    "lessthan" => tuple.Item1.IsLessThan(tuple.Item2),
                    "lessthanorequal" => tuple.Item1.IsLessThanOrEqual(tuple.Item2),
                    _ => false
                })
                .ToHttpResult());

        // Real-World Scenarios
        moneyApi.MapPost("/CartTotal", (CartTotalRequest request) =>
        {
            if (request.Items.Length == 0)
                return Result.Fail<Money>(Error.Validation("Cart cannot be empty")).ToHttpResult();

            var firstItem = Money.TryCreate(request.Items[0].Amount, request.Items[0].Currency);
            if (firstItem.IsFailure)
                return firstItem.ToHttpResult();

            var total = firstItem.Value;
            for (int i = 1; i < request.Items.Length; i++)
            {
                var itemResult = Money.TryCreate(request.Items[i].Amount, request.Items[i].Currency)
                    .Bind(item => total.Add(item));

                if (itemResult.IsFailure)
                    return itemResult.ToHttpResult();

                total = itemResult.Value;
            }

            return Result.Ok(total).ToHttpResult();
        });

        moneyApi.MapPost("/ApplyDiscount", (ApplyDiscountRequest request) =>
            Money.TryCreate(request.OriginalPrice.Amount, request.OriginalPrice.Currency)
                .Ensure(price => request.DiscountPercent is >= 0 and <= 100,
                    Error.Validation("Discount percent must be between 0 and 100"))
                .Bind(price =>
                {
                    var discountMultiplier = 1 - (request.DiscountPercent / 100m);
                    return price.Multiply(discountMultiplier);
                })
                .ToHttpResult());

        moneyApi.MapPost("/SplitBill", (SplitBillRequest request) =>
            Money.TryCreate(request.Total.Amount, request.Total.Currency)
                .Ensure(_ => request.People > 0, Error.Validation("Number of people must be positive"))
                .Bind(total => total.Divide(request.People))
                .ToHttpResult());

        moneyApi.MapPost("/RevenueShare", (RevenueShareRequest request) =>
        {
            var totalPercent = request.PlatformPercent + request.CreatorPercent + request.ReferrerPercent;
            if (totalPercent != 100)
                return Result.Fail<RevenueShareResponse>(
                    Error.Validation($"Percentages must sum to 100, got {totalPercent}")).ToHttpResult();

            return Money.TryCreate(request.Revenue.Amount, request.Revenue.Currency)
                .Bind(revenue => revenue.Allocate(
                    (int)request.PlatformPercent,
                    (int)request.CreatorPercent,
                    (int)request.ReferrerPercent))
                .Map(shares => new RevenueShareResponse(shares[0], shares[1], shares[2]))
                .ToHttpResult();
        });
    }
}

#pragma warning disable CA1050 // Declare types in namespaces
public record MoneyDto(decimal Amount, string Currency);
public record CreateMoneyRequest(decimal Amount, string Currency);
public record MoneyOperationRequest(MoneyDto Left, MoneyDto Right);
public record MultiplyMoneyRequest(MoneyDto Money, decimal Multiplier);
public record MultiplyByQuantityRequest(MoneyDto Money, int Quantity);
public record DivideMoneyRequest(MoneyDto Money, decimal Divisor);
public record AllocateMoneyRequest(MoneyDto Money, int[] Ratios);
public record CompareMoneyRequest(MoneyDto Left, MoneyDto Right, string Operation);
public record CartTotalRequest(MoneyDto[] Items);
public record ApplyDiscountRequest(MoneyDto OriginalPrice, decimal DiscountPercent);
public record SplitBillRequest(MoneyDto Total, int People);
public record RevenueShareRequest(MoneyDto Revenue, decimal PlatformPercent, decimal CreatorPercent, decimal ReferrerPercent);
public record RevenueShareResponse(Money Platform, Money Creator, Money Referrer);
#pragma warning restore CA1050 // Declare types in namespaces