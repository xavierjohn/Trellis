namespace Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Test composite value object with a non-nullable value-type property (<see cref="DateTime"/>).
/// Used to verify the convention handles value-type properties that cannot be marked nullable
/// via <c>IsRequired(false)</c>.
/// </summary>
public class TestDateRange : ValueObject
{
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }
    public string Label { get; private set; }

    // ReSharper disable once UnusedMember.Local — used by EF Core for materialization
    private TestDateRange() =>
        Label = null!;

    public TestDateRange(DateTime start, DateTime end, string label)
    {
        Start = start;
        End = end;
        Label = label;
    }

    public static TestDateRange Create(DateTime start, DateTime end, string label) =>
        new(start, end, label);

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Start;
        yield return End;
        yield return Label;
    }
}
