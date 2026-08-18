namespace Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Exploration fixture: a composite value object that mixes a Trellis scalar VO
/// (<see cref="TestOrderStatus"/> — a <c>RequiredEnum</c>) with three optional C#-native
/// shapes that are NOT scalar value objects:
/// <list type="bullet">
///   <item><description><see cref="Count"/> — <c>Maybe&lt;int&gt;</c> (primitive value type)</description></item>
///   <item><description><see cref="Label"/> — <c>Maybe&lt;string&gt;</c> (primitive reference type)</description></item>
///   <item><description><see cref="Snapshots"/> — <c>Maybe&lt;DateTime[]&gt;</c> (array of primitive value type)</description></item>
/// </list>
/// <para>
/// Used to discover which property shapes are supported on a composite VO interior
/// for both EF Core persistence and JSON round-trip through
/// <c>CompositeValueObjectJsonConverter&lt;T&gt;</c>.
/// </para>
/// </summary>
[OwnedEntity]
public partial class TestMixedTypeVo : ValueObject
{
    /// <summary>Required Trellis scalar VO (RequiredEnum).</summary>
    public TestOrderStatus Status { get; private set; } = null!;

    /// <summary>Optional primitive value type.</summary>
    public partial Maybe<int> Count { get; private set; }

    /// <summary>Optional primitive reference type.</summary>
    public partial Maybe<string> Label { get; private set; }

    /// <summary>Optional array of primitive value type.</summary>
    public partial Maybe<DateTime[]> Snapshots { get; private set; }

    public TestMixedTypeVo(
        TestOrderStatus status,
        Maybe<int> count,
        Maybe<string> label,
        Maybe<DateTime[]> snapshots)
    {
        Status = status;
        Count = count;
        Label = label;
        Snapshots = snapshots;
    }

    public static TestMixedTypeVo Create(
        TestOrderStatus status,
        Maybe<int> count,
        Maybe<string> label,
        Maybe<DateTime[]> snapshots) =>
        new(status, count, label, snapshots);

    /// <summary>
    /// Required by <c>CompositeValueObjectJsonConverter&lt;TestMixedTypeVo&gt;</c>.
    /// The converter takes the JSON-side primitive types ('string' for the
    /// <see cref="TestOrderStatus"/> RequiredEnum, untouched for the three
    /// <c>Maybe&lt;&gt;</c> properties) and invokes this static factory to construct.
    /// </summary>
    public static Result<TestMixedTypeVo> TryCreate(
        string status,
        Maybe<int> count,
        Maybe<string> label,
        Maybe<DateTime[]> snapshots) =>
        TestOrderStatus.TryCreate(status)
            .Map(s => new TestMixedTypeVo(s, count, label, snapshots));

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Status.Value);
        components.Add(Count.HasValue ? (int?)Count.Value : null);
        components.Add(Label.HasValue ? Label.Value : null);
        // Yield each element so structural equality compares arrays element-wise
        // (length-only comparison would let two arrays with the same length but
        // different contents compare equal — wrong for a ValueObject).
        if (Snapshots.HasValue)
        {
            foreach (var dt in Snapshots.Value)
                components.Add(dt);
        }
        else
        {
            components.Add(null);
        }
    }
}