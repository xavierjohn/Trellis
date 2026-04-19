using Trellis.Asp;
using Trellis.Primitives;

namespace SampleWebApplication.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;

[ApiController]
[Route("[controller]")]
public class MoneyController : ControllerBase
{
    // Money Creation
    [HttpPost("[action]")]
    public ActionResult<Money> Create([FromBody] CreateMoneyRequest request) =>
        Money.TryCreate(request.Amount, request.Currency)
            .ToActionResult(this);

    // Arithmetic Operations
    [HttpPost("[action]")]
    public ActionResult<Money> Add([FromBody] MoneyOperationRequest request) =>
        Money.TryCreate(request.Left.Amount, request.Left.Currency)
            .Combine(Money.TryCreate(request.Right.Amount, request.Right.Currency))
            .Bind((left, right) => left.Add(right))
            .ToActionResult(this);

    [HttpPost("[action]")]
    public ActionResult<Money> Subtract([FromBody] MoneyOperationRequest request) =>
        Money.TryCreate(request.Left.Amount, request.Left.Currency)
            .Combine(Money.TryCreate(request.Right.Amount, request.Right.Currency))
            .Bind((left, right) => left.Subtract(right))
            .ToActionResult(this);

    [HttpPost("[action]")]
    public ActionResult<Money> Multiply([FromBody] MultiplyMoneyRequest request) =>
        Money.TryCreate(request.Money.Amount, request.Money.Currency)
            .Bind(money => money.Multiply(request.Multiplier))
            .ToActionResult(this);

    [HttpPost("[action]")]
    public ActionResult<Money> MultiplyByQuantity([FromBody] MultiplyByQuantityRequest request) =>
        Money.TryCreate(request.Money.Amount, request.Money.Currency)
            .Bind(money => money.Multiply(request.Quantity))
            .ToActionResult(this);

    [HttpPost("[action]")]
    public ActionResult<Money> Divide([FromBody] DivideMoneyRequest request) =>
        Money.TryCreate(request.Money.Amount, request.Money.Currency)
            .Bind(money => money.Divide(request.Divisor))
            .ToActionResult(this);

    // Allocation
    [HttpPost("[action]")]
    public ActionResult<Money[]> Allocate([FromBody] AllocateMoneyRequest request) =>
        Money.TryCreate(request.Money.Amount, request.Money.Currency)
            .Bind(money => money.Allocate(request.Ratios))
            .ToActionResult(this);

    // Comparison
    [HttpPost("[action]")]
    public ActionResult<bool> Compare([FromBody] CompareMoneyRequest request) =>
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
            .ToActionResult(this);

    // Real-World Scenarios
    [HttpPost("[action]")]
    public ActionResult<Money> CartTotal([FromBody] CartTotalRequest request)
    {
        if (request.Items.Length == 0)
            return Result.Fail<Money>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Cart cannot be empty" }).ToActionResult(this);

        var firstItem = Money.TryCreate(request.Items[0].Amount, request.Items[0].Currency);
        if (!firstItem.TryGetValue(out var total))
            return firstItem.ToActionResult(this);

        for (int i = 1; i < request.Items.Length; i++)
        {
            var itemResult = Money.TryCreate(request.Items[i].Amount, request.Items[i].Currency)
                .Bind(item => total!.Add(item));

            if (!itemResult.TryGetValue(out var nextTotal))
                return itemResult.ToActionResult(this);

            total = nextTotal;
        }

        return Result.Ok(total!).ToActionResult(this);
    }

    [HttpPost("[action]")]
    public ActionResult<Money> ApplyDiscount([FromBody] ApplyDiscountRequest request) =>
        Money.TryCreate(request.OriginalPrice.Amount, request.OriginalPrice.Currency)
            .Ensure(price => request.DiscountPercent is >= 0 and <= 100,
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Discount percent must be between 0 and 100" })
            .Bind(price =>
            {
                var discountMultiplier = 1 - (request.DiscountPercent / 100m);
                return price.Multiply(discountMultiplier);
            })
            .ToActionResult(this);

    [HttpPost("[action]")]
    public ActionResult<Money> SplitBill([FromBody] SplitBillRequest request) =>
        Money.TryCreate(request.Total.Amount, request.Total.Currency)
            .Ensure(_ => request.People > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Number of people must be positive" })
            .Bind(total => total.Divide(request.People))
            .ToActionResult(this);

    [HttpPost("[action]")]
    public ActionResult<RevenueShareResponse> RevenueShare([FromBody] RevenueShareRequest request)
    {
        var totalPercent = request.PlatformPercent + request.CreatorPercent + request.ReferrerPercent;
        if (totalPercent != 100)
            return Result.Fail<RevenueShareResponse>(
                new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Percentages must sum to 100, got {totalPercent}" }).ToActionResult(this);

        return Money.TryCreate(request.Revenue.Amount, request.Revenue.Currency)
            .Bind(revenue => revenue.Allocate(
                (int)request.PlatformPercent,
                (int)request.CreatorPercent,
                (int)request.ReferrerPercent))
            .Map(shares => new RevenueShareResponse(shares[0], shares[1], shares[2]))
            .ToActionResult(this);
    }
}

public record MoneyDto(decimal Amount, CurrencyCode Currency);
public record CreateMoneyRequest(decimal Amount, CurrencyCode Currency);
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