namespace Trellis.Core.Tests.DomainDrivenDesign.ValueObjects;

internal class Money : ScalarValueObject<Money, decimal>, IScalarValue<Money, decimal>
{
    public Money(decimal value) : base(value)
    {
    }

    public static Result<Money> TryCreate(decimal value, string? fieldName = null) =>
        Result.Ok(new Money(value));

    public static Result<Money> TryCreate(string? value, string? fieldName = null) =>
        throw new NotImplementedException();

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Math.Round(Value, 2);
    }
}