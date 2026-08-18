namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using Trellis.Primitives;

/// <summary>
/// Counterpart to <see cref="TestMixedTypeVo"/>: a composite value object that mixes
/// raw C# primitives from the supported allowed list with a Trellis scalar VO
/// (<see cref="TestOrderStatus"/>) — and explicitly NO <c>Maybe&lt;&gt;</c> fields.
/// This is the all-required mixed VO shape that round-trips cleanly through both
/// EF Core <em>and</em> <c>CompositeValueObjectJsonConverter</c>.
/// </summary>
/// <remarks>
/// The converter's <c>WritePrimitive</c> / <c>ReadPrimitive</c> methods support exactly:
/// <c>string</c>, <c>decimal</c>, <c>int</c>, <c>long</c>, <c>short</c>, <c>byte</c>,
/// <c>double</c>, <c>float</c>, <c>bool</c>, <c>Guid</c>, <c>DateTime</c>,
/// <c>DateTimeOffset</c>. Trellis scalar VOs flatten to their underlying primitive on
/// the wire and so are also supported. ANY <c>Maybe&lt;T&gt;</c> on the composite VO
/// interior breaks JSON round-trip (regardless of the inner type) because the
/// converter has no <c>Maybe&lt;&gt;</c> handling — see <see cref="TestMixedTypeVo"/>.
/// </remarks>
[OwnedEntity]
public partial class TestSupportedMixedVo : ValueObject
{
    public TestOrderStatus Status { get; private set; } = null!;
    public string Label { get; private set; } = string.Empty;
    public int Count { get; private set; }
    public Guid Id { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public decimal Score { get; private set; }
    public bool IsActive { get; private set; }

    public TestSupportedMixedVo(
        TestOrderStatus status,
        string label,
        int count,
        Guid id,
        DateTimeOffset createdAt,
        decimal score,
        bool isActive)
    {
        Status = status;
        Label = label;
        Count = count;
        Id = id;
        CreatedAt = createdAt;
        Score = score;
        IsActive = isActive;
    }

    public static TestSupportedMixedVo Create(
        TestOrderStatus status,
        string label,
        int count,
        Guid id,
        DateTimeOffset createdAt,
        decimal score,
        bool isActive) =>
        new(status, label, count, id, createdAt, score, isActive);

    /// <summary>
    /// Required by <c>CompositeValueObjectJsonConverter&lt;TestSupportedMixedVo&gt;</c>.
    /// The <c>status</c> parameter is <c>string</c> — the Trellis scalar VO
    /// <see cref="TestOrderStatus"/> flattens to its primitive on the wire.
    /// </summary>
    public static Result<TestSupportedMixedVo> TryCreate(
        string status,
        string label,
        int count,
        Guid id,
        DateTimeOffset createdAt,
        decimal score,
        bool isActive) =>
        TestOrderStatus.TryCreate(status)
            .Map(s => new TestSupportedMixedVo(s, label, count, id, createdAt, score, isActive));

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Status.Value);
        components.Add(Label);
        components.Add(Count);
        components.Add(Id);
        components.Add(CreatedAt);
        components.Add(Score);
        components.Add(IsActive);
    }
}