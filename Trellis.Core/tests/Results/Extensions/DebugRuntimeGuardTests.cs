namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Core.Tests.Helpers;

/// <summary>
/// Tests for <see cref="ResultDebugSettings.EnableDebugTracing"/> runtime guard.
/// Runs in a dedicated non-parallel collection because it mutates a global static flag.
/// </summary>
[Collection("DebugSettingsTests")]
[CollectionDefinition("DebugSettingsTests", DisableParallelization = true)]
public class DebugRuntimeGuardTests
{
#if DEBUG
    [Fact]
    public void Debug_RuntimeGuardDisabled_DoesNotEmitActivity()
    {
        using var activityTest = new ActivityTestHelper();
        var originalValue = ResultDebugSettings.EnableDebugTracing;
        try
        {
            ResultDebugSettings.EnableDebugTracing = false;

            var result = Result.Ok("Test");
            result.Debug("should be suppressed");

            activityTest.CapturedActivities.Should().BeEmpty("no activity should be emitted when runtime guard is disabled");
        }
        finally
        {
            ResultDebugSettings.EnableDebugTracing = originalValue;
        }
    }

    [Fact]
    public void Debug_RuntimeGuardEnabled_EmitsActivity()
    {
        using var activityTest = new ActivityTestHelper();
        var originalValue = ResultDebugSettings.EnableDebugTracing;
        try
        {
            ResultDebugSettings.EnableDebugTracing = true;

            var result = Result.Ok("Test");
            result.Debug("should emit");

            activityTest.CapturedActivities.Should().NotBeEmpty("activity should be emitted when runtime guard is enabled");
        }
        finally
        {
            ResultDebugSettings.EnableDebugTracing = originalValue;
        }
    }

    [Fact]
    public void DebugDetailed_RuntimeGuardDisabled_DoesNotEmitActivity()
    {
        using var activityTest = new ActivityTestHelper();
        var originalValue = ResultDebugSettings.EnableDebugTracing;
        try
        {
            ResultDebugSettings.EnableDebugTracing = false;

            var result = Result.Ok("Test");
            result.DebugDetailed("should be suppressed");

            activityTest.CapturedActivities.Should().BeEmpty();
        }
        finally
        {
            ResultDebugSettings.EnableDebugTracing = originalValue;
        }
    }

    [Fact]
    public void DebugOnSuccess_RuntimeGuardDisabled_ActionNotInvoked()
    {
        var originalValue = ResultDebugSettings.EnableDebugTracing;
        try
        {
            ResultDebugSettings.EnableDebugTracing = false;
            var actionInvoked = false;

            var result = Result.Ok("Test");
            result.DebugOnSuccess(_ => actionInvoked = true);

            actionInvoked.Should().BeFalse("action should not be invoked when runtime guard is disabled");
        }
        finally
        {
            ResultDebugSettings.EnableDebugTracing = originalValue;
        }
    }

    [Fact]
    public void DebugOnFailure_RuntimeGuardDisabled_ActionNotInvoked()
    {
        var originalValue = ResultDebugSettings.EnableDebugTracing;
        try
        {
            ResultDebugSettings.EnableDebugTracing = false;
            var actionInvoked = false;

            var result = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "test" });
            result.DebugOnFailure(_ => actionInvoked = true);

            actionInvoked.Should().BeFalse("action should not be invoked when runtime guard is disabled");
        }
        finally
        {
            ResultDebugSettings.EnableDebugTracing = originalValue;
        }
    }
#endif
}