namespace Trellis.Mediator.Tests;

using global::Mediator;
using Trellis.Authorization;

/// <summary>
/// Tests for the <c>EnsureResourceTypeIsReferenceType</c> scan-time guard. v4 typed
/// accessor closed generics require <c>where TResource : class</c> /
/// <c>where TLeaf : class</c>; without this guard the scan path's
/// <c>MakeGenericType</c> call would throw a cryptic <c>ArgumentException</c> naming
/// a synthesised generic-arg position rather than the offending command. The guard
/// fires before <c>MakeGenericType</c> in both the direct and via scan branches.
/// </summary>
public class ResourceAuthorizationValueTypeRejectionTests
{
    [Fact]
    public void EnsureResourceTypeIsReferenceType_ClassResource_DoesNotThrow()
    {
        var act = () => ServiceCollectionExtensions.EnsureResourceTypeIsReferenceType(
            messageType: typeof(DummyMessage),
            resourceType: typeof(string),
            markerInterfaceName: "IAuthorizeResource");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureResourceTypeIsReferenceType_RecordClassResource_DoesNotThrow()
    {
        var act = () => ServiceCollectionExtensions.EnsureResourceTypeIsReferenceType(
            messageType: typeof(DummyMessage),
            resourceType: typeof(DummyClassResource),
            markerInterfaceName: "IAuthorizeResource");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureResourceTypeIsReferenceType_StructResource_ThrowsFriendlyDiagnostic_NamingCommandAndType()
    {
        var act = () => ServiceCollectionExtensions.EnsureResourceTypeIsReferenceType(
            messageType: typeof(DummyMessage),
            resourceType: typeof(DummyStructResource),
            markerInterfaceName: "IAuthorizeResource");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DummyMessage*")
            .WithMessage("*IAuthorizeResource<DummyStructResource>*")
            .WithMessage("*value type*")
            .WithMessage("*IAuthorizedResource<TMessage, TResource>*")
            .WithMessage("*reference-typed*");
    }

    [Fact]
    public void EnsureResourceTypeIsReferenceType_RecordStructResource_ThrowsFriendlyDiagnostic()
    {
        var act = () => ServiceCollectionExtensions.EnsureResourceTypeIsReferenceType(
            messageType: typeof(DummyMessage),
            resourceType: typeof(DummyRecordStructResource),
            markerInterfaceName: "IAuthorizeResource");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DummyRecordStructResource*")
            .WithMessage("*value type*");
    }

    [Fact]
    public void EnsureResourceTypeIsReferenceType_PrimitiveResource_ThrowsFriendlyDiagnostic()
    {
        // Defensive: int is the canonical value-type case. The friendly diagnostic should
        // fire identically whether the resource is a primitive, a struct, or a record struct.
        var act = () => ServiceCollectionExtensions.EnsureResourceTypeIsReferenceType(
            messageType: typeof(DummyMessage),
            resourceType: typeof(int),
            markerInterfaceName: "IAuthorizeResource");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Int32*")
            .WithMessage("*value type*");
    }

    [Fact]
    public void EnsureResourceTypeIsReferenceType_StructLeaf_ThrowsFriendlyDiagnostic_WithViaMarkerName()
    {
        // The via path passes "IIdentifyResource" as the marker name (because the leaf type
        // comes from the IIdentifyResource<TLeaf, TLeafId> declaration on the command, not
        // from IAuthorizeResourceVia). Confirm the diagnostic preserves whichever marker name
        // the caller supplied.
        var act = () => ServiceCollectionExtensions.EnsureResourceTypeIsReferenceType(
            messageType: typeof(DummyMessage),
            resourceType: typeof(DummyStructResource),
            markerInterfaceName: "IIdentifyResource");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IIdentifyResource<DummyStructResource>*");
    }

    // ── fixtures ──────────────────────────────────────────────────────────────

    private sealed record DummyMessage : ICommand<Result<string>>;

    private sealed class DummyClassResource;

    private struct DummyStructResource;

    private readonly record struct DummyRecordStructResource(int Value);
}
