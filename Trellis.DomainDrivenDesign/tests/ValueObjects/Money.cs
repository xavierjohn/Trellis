namespace Trellis.DomainDrivenDesign.Tests.ValueObjects;

internal class Money : ScalarValueObject<Money, decimal>, IScalarValue<Money, decimal>
{
    public Money(decimal value) : base(value)
    {
    }

    public static Result<Money> TryCreate(decimal value, string? fieldName = null) =>
        Result.Success(new Money(value));

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Math.Round(Value, 2);
    }
}