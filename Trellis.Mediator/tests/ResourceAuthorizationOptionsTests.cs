namespace Trellis.Mediator.Tests;

public sealed class ResourceAuthorizationOptionsTests
{
    [Fact]
    public void DefaultExposurePolicy_NoConfiguration_ReturnsPropagate()
    {
        var options = new ResourceAuthorizationOptions();

        options.DefaultExposurePolicy.Should().Be(AuthFailureExposurePolicy.Propagate);
    }

    [Fact]
    public void Resolve_NoOverride_ReturnsDefaultPolicyAndLookupTypeForPublicAndId()
    {
        var options = new ResourceAuthorizationOptions();

        var entry = options.Resolve(typeof(SampleResource));

        entry.Policy.Should().Be(AuthFailureExposurePolicy.Propagate);
        entry.PublicResourceType.Should().Be<SampleResource>();
        entry.IdResourceType.Should().Be<SampleResource>();
    }

    [Fact]
    public void Resolve_DefaultPolicyChanged_FlowsThroughForUnconfiguredResource()
    {
        var options = new ResourceAuthorizationOptions
        {
            DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound,
        };

        var entry = options.Resolve(typeof(SampleResource));

        entry.Policy.Should().Be(AuthFailureExposurePolicy.HideAsNotFound);
    }

    [Fact]
    public void HideExistence_SingleType_SetsHideAsNotFoundForThatResource()
    {
        var options = new ResourceAuthorizationOptions()
            .HideExistence<SampleResource>();

        var entry = options.Resolve(typeof(SampleResource));

        entry.Policy.Should().Be(AuthFailureExposurePolicy.HideAsNotFound);
        entry.PublicResourceType.Should().Be<SampleResource>();
        entry.IdResourceType.Should().Be<SampleResource>();
    }

    [Fact]
    public void HideExistence_SingleType_DoesNotAffectOtherResources()
    {
        var options = new ResourceAuthorizationOptions()
            .HideExistence<SampleResource>();

        var otherEntry = options.Resolve(typeof(OtherResource));

        otherEntry.Policy.Should().Be(AuthFailureExposurePolicy.Propagate);
    }

    [Fact]
    public void HideExistence_ProjectionOverload_DecouplesAuthorizationFromPublicResourceType()
    {
        var options = new ResourceAuthorizationOptions()
            .HideExistence<SampleResource, PublicSampleResource>();

        var entry = options.Resolve(typeof(SampleResource));

        entry.Policy.Should().Be(AuthFailureExposurePolicy.HideAsNotFound);
        entry.PublicResourceType.Should().Be<PublicSampleResource>();
        entry.IdResourceType.Should().Be<PublicSampleResource>();
    }

    [Fact]
    public void Propagate_OverridesNonPropagateDefault()
    {
        var options = new ResourceAuthorizationOptions
        {
            DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound,
        };
        options.Propagate<SampleResource>();

        var entry = options.Resolve(typeof(SampleResource));

        entry.Policy.Should().Be(AuthFailureExposurePolicy.Propagate);
    }

    [Fact]
    public void HideExistence_CalledTwice_LastWins()
    {
        // No explicit "only the first registration wins" guarantee — last call replaces, mirroring
        // the standard IDictionary.Add-then-overwrite mental model. Documented here so a future
        // change to throw-on-duplicate is caught as a behavior change.
        var options = new ResourceAuthorizationOptions()
            .HideExistence<SampleResource>()
            .Propagate<SampleResource>();

        var entry = options.Resolve(typeof(SampleResource));

        entry.Policy.Should().Be(AuthFailureExposurePolicy.Propagate);
    }

    [Fact]
    public void HideExistence_ReturnsOptionsForChaining()
    {
        var options = new ResourceAuthorizationOptions();

        var returned = options.HideExistence<SampleResource>();

        returned.Should().BeSameAs(options);
    }

    [Fact]
    public void Propagate_ReturnsOptionsForChaining()
    {
        var options = new ResourceAuthorizationOptions();

        var returned = options.Propagate<SampleResource>();

        returned.Should().BeSameAs(options);
    }

    private sealed class SampleResource;
    private sealed class OtherResource;
    private sealed class PublicSampleResource;
}
