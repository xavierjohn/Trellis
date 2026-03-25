namespace Trellis.Primitives.Tests;

/// <summary>
/// RequiredGuid with custom validation — rejects Version 4 GUIDs (only allows V7).
/// </summary>
public partial class V7OnlyId : RequiredGuid<V7OnlyId>
{
    static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)
    {
        // Version 7 GUIDs have the version nibble = 0x7 in the 7th byte
        if ((value.ToByteArray()[7] & 0xF0) != 0x70)
            errorMessage = "Only Version 7 GUIDs are allowed.";
    }
}

/// <summary>
/// RequiredGuid without ValidateAdditional — ensures the hook is optional.
/// </summary>
public partial class PlainId : RequiredGuid<PlainId> { }

/// <summary>
/// Tests for ValidateAdditional on RequiredGuid.
/// </summary>
public class RequiredGuidValidateAdditionalTests
{
    [Fact]
    public void V7OnlyId_V7Guid_ReturnsSuccess()
    {
        var v7 = Guid.CreateVersion7();
        var result = V7OnlyId.TryCreate(v7);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void V7OnlyId_V4Guid_ReturnsFailure()
    {
        var v4 = Guid.NewGuid();
        var result = V7OnlyId.TryCreate(v4);
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Only Version 7 GUIDs are allowed.");
    }

    [Fact]
    public void V7OnlyId_EmptyGuid_FailsBuiltIn()
    {
        var result = V7OnlyId.TryCreate(Guid.Empty);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PlainId_WithoutHook_StillWorks()
    {
        var result = PlainId.TryCreate(Guid.NewGuid());
        result.IsSuccess.Should().BeTrue();
    }
}
