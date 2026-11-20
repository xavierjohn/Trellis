namespace Trellis.Core.Tests.Primitives;

using System.Diagnostics;

public sealed class RequiredEnumAllocationTests : IDisposable
{
    private readonly ActivitySource _source = new($"Enum-Allocation-{Guid.NewGuid():N}");

    public RequiredEnumAllocationTests() => PrimitiveValueObjectTrace.SetTestActivitySource(_source);

    [Theory]
    [InlineData(null)]
    [InlineData("Phase")]
    [InlineData("phase")]
    [InlineData("")]
    public void TryCreate_ValidMember_WarmedLookup_DoesNotAllocate(string? fieldName)
    {
        _source.HasListeners().Should().BeFalse();
        for (var i = 0; i < 256; i++)
            _ = AllocationPhase.TryCreate("Ready", fieldName);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
            _ = AllocationPhase.TryCreate("Ready", fieldName);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        AllocationPhase.TryCreate("Ready", fieldName).TryGetValue(out var member).Should().BeTrue();
        member.Should().BeSameAs(AllocationPhase.Ready);
    }

    [Theory]
    [InlineData(null, null, "/allocationPhase")]
    [InlineData("missing", "Phase", "/phase")]
    [InlineData("missing", "", "")]
    public void TryCreate_InvalidMember_FieldName_PreservesNormalization(
        string? value, string? fieldName, string expectedPointer)
    {
        var result = AllocationPhase.TryCreate(value, fieldName);

        result.Error.Should().BeOfType<Error.InvalidInput>().Which
            .Fields[0].Field.Path.Should().Be(expectedPointer);
    }

    public void Dispose()
    {
        PrimitiveValueObjectTrace.ResetTestActivitySource();
        _source.Dispose();
    }
}

internal sealed class AllocationPhase : RequiredEnum<AllocationPhase>, IScalarValue<AllocationPhase, string>
{
    public static readonly AllocationPhase Ready = new();
    public static readonly AllocationPhase Done = new();
}