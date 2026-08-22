namespace Trellis.Primitives.Tests;

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Trellis.Primitives;
using Trellis.Primitives.Tests.Helpers;
using Xunit;

/// <summary>
/// Tests for PvoTracingExtensions to verify OpenTelemetry integration.
/// </summary>
public class PvoTracingExtensionsTests : IDisposable
{
    private readonly PvoActivityTestHelper _activityHelper = new();

    [Fact]
    public void AddTrellisPrimitivesInstrumentation_RegistersActivitySource()
    {
        // Arrange
        var builder = Sdk.CreateTracerProviderBuilder();

        // Act
        var result = builder.AddTrellisPrimitivesInstrumentation();

        // Assert - Method should return builder for chaining
        result.Should().BeSameAs(builder);
        result.Should().NotBeNull();
    }

    [Fact]
    public void AddTrellisPrimitivesInstrumentation_EnablesActivityCapture()
    {
        // Arrange
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddTrellisPrimitivesInstrumentation()
            .Build();

        // Act
        var emailResult = EmailAddress.TryCreate("test@example.com");

        // Assert
        _activityHelper.WaitForActivityCount(1).Should().BeTrue("activity should be captured");
        emailResult.IsSuccess.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle();
        var activity = activities[0];
        activity.DisplayName.Should().Be("EmailAddress.TryCreate");
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void AddTrellisPrimitivesInstrumentation_SupportsMethodChaining()
    {
        // Arrange & Act
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddTrellisPrimitivesInstrumentation()
            .AddSource("TestSource")  // Chain another call
            .Build();

        // Assert
        tracerProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddTrellisPrimitivesInstrumentation_RegistersCorrectActivitySourceName()
    {
        // Arrange
        var expectedSourceName = "Trellis.Primitives";

        // Act
        var actualSourceName = PrimitiveValueObjectTrace.ActivitySourceName;

        // Assert
        actualSourceName.Should().Be(expectedSourceName);
    }

    [Fact]
    public void EmailAddress_WithTracing_CreatesActivityWithCorrectName()
    {
        // Act
        var _ = EmailAddress.TryCreate("user@domain.com");

        // Assert
        var activity = _activityHelper.WaitForActivity("EmailAddress.TryCreate");
        activity.Should().NotBeNull("activity should be captured");
        activity!.OperationName.Should().Be("EmailAddress.TryCreate");

        var activities = _activityHelper.CapturedActivities;
        activity.Source.Name.Should().Be(activities[0].Source.Name);
    }

    [Fact]
    public void EmailAddress_SuccessfulCreation_SetsOkStatus()
    {
        // Act
        var emailResult = EmailAddress.TryCreate("test@example.com");

        // Assert
        _activityHelper.WaitForActivityCount(1).Should().BeTrue("activity should be captured");
        emailResult.IsSuccess.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle();
        var activity = activities[0];
        activity.DisplayName.Should().Be("EmailAddress.TryCreate");
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void EmailAddress_ValidationFailure_SetsErrorStatus()
    {
        // Act
        var emailResult = EmailAddress.TryCreate("invalid-email");

        // Assert
        var waited = _activityHelper.WaitForActivityCount(1);
        waited.Should().BeTrue($"activity should be captured. Activity count: {_activityHelper.ActivityCount}");
        emailResult.IsFailure.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"Expected 1 activity, but got {activities.Count}");
        var activity = activities[0];
        activity.DisplayName.Should().Be("EmailAddress.TryCreate");
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void EmailAddress_MultipleOperations_CapturesAllActivities()
    {
        // Act
        var email1 = EmailAddress.TryCreate("valid@example.com");
        var email2 = EmailAddress.TryCreate("invalid");
        var email3 = EmailAddress.TryCreate("another@test.com");

        // Assert
        _activityHelper.WaitForActivityCount(3).Should().BeTrue("all activities should be captured");

        var activities = _activityHelper.CapturedActivities;
        activities.Should().HaveCount(3);
        activities.Should().AllSatisfy(a => a.DisplayName.Should().Be("EmailAddress.TryCreate"));

        // Verify statuses
        activities[0].Status.Should().Be(ActivityStatusCode.Ok);  // valid
        activities[1].Status.Should().Be(ActivityStatusCode.Error); // invalid
        activities[2].Status.Should().Be(ActivityStatusCode.Ok);  // valid
    }

    [Fact]
    public void PrimitiveValueObjectTrace_HasCorrectVersion()
    {
        // Act
        var version = PrimitiveValueObjectTrace.Version;

        // Assert
        version.Should().NotBeNull();
        version.Should().Be(PrimitiveValueObjectTrace.AssemblyName.Version);
    }

    [Fact]
    public void ActivitySource_IsNotNull()
    {
        // Act
        var activitySource = PrimitiveValueObjectTrace.ActivitySource;

        // Assert
        activitySource.Should().NotBeNull();
        activitySource.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PrimitiveValueObjectTrace_ActivitySourceName_MatchesConstant()
    {
        // Arrange
        var expectedName = "Trellis.Primitives";

        // Act & Assert
        PrimitiveValueObjectTrace.ActivitySourceName.Should().Be(expectedName);
    }

    [Fact]
    public void AddTrellisPrimitivesInstrumentation_NullBuilder_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => PrimitiveValueObjectTraceProviderBuilderExtensions
            .AddTrellisPrimitivesInstrumentation(builder: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "builder");

    // --- Single-span invariant: every public factory call emits exactly one activity,
    //     on both success and failure, regardless of which overload is the entry point. ---

    [Fact]
    public void MonetaryAmount_NullableDecimal_Null_EmitsSingleErrorActivity()
    {
        var result = MonetaryAmount.TryCreate((decimal?)null);

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("a validation failure must still emit one activity");
        result.IsFailure.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"exactly one span per call; got {activities.Count}");
        activities[0].DisplayName.Should().Be("MonetaryAmount.TryCreate");
        activities[0].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void MonetaryAmount_NullableDecimal_Valid_EmitsSingleOkActivity()
    {
        var result = MonetaryAmount.TryCreate((decimal?)10.5m);

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("activity should be captured");
        result.IsSuccess.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"a non-leaf overload must not nest a second span; got {activities.Count}");
        activities[0].DisplayName.Should().Be("MonetaryAmount.TryCreate");
        activities[0].Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void MonetaryAmount_String_Invalid_EmitsSingleErrorActivity()
    {
        var result = MonetaryAmount.TryCreate("not-a-number");

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("a parse failure must still emit one activity");
        result.IsFailure.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"exactly one span per call; got {activities.Count}");
        activities[0].DisplayName.Should().Be("MonetaryAmount.TryCreate");
        activities[0].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void Age_String_Invalid_EmitsSingleErrorActivity()
    {
        var result = Age.TryCreate("not-a-number");

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("a parse failure must still emit one activity");
        result.IsFailure.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"exactly one span per call; got {activities.Count}");
        activities[0].DisplayName.Should().Be("Age.TryCreate");
        activities[0].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void Age_String_Valid_EmitsSingleOkActivity()
    {
        var result = Age.TryCreate("42");

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("activity should be captured");
        result.IsSuccess.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"a non-leaf overload must not nest a second span; got {activities.Count}");
        activities[0].DisplayName.Should().Be("Age.TryCreate");
        activities[0].Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void Percentage_String_Invalid_EmitsSingleErrorActivity()
    {
        var result = Percentage.TryCreate("not-a-number");

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("a parse failure must still emit one activity");
        result.IsFailure.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"exactly one span per call; got {activities.Count}");
        activities[0].DisplayName.Should().Be("Percentage.TryCreate");
        activities[0].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void Percentage_FromFraction_OutOfRange_EmitsSingleErrorActivity()
    {
        var result = Percentage.FromFraction(2m);

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("an out-of-range fraction must still emit one activity");
        result.IsFailure.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"exactly one span per call; got {activities.Count}");
        activities[0].DisplayName.Should().Be("Percentage.FromFraction");
        activities[0].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void Percentage_FromFraction_Valid_EmitsSingleOkActivity()
    {
        var result = Percentage.FromFraction(0.5m);

        _activityHelper.WaitForActivityCount(1).Should().BeTrue("activity should be captured");
        result.IsSuccess.Should().BeTrue();

        var activities = _activityHelper.CapturedActivities;
        activities.Should().ContainSingle($"FromFraction must emit exactly one span; got {activities.Count}");
        activities[0].DisplayName.Should().Be("Percentage.FromFraction");
        activities[0].Status.Should().Be(ActivityStatusCode.Ok);
    }

    public void Dispose()
    {
        _activityHelper.Dispose();
        GC.SuppressFinalize(this);
    }
}