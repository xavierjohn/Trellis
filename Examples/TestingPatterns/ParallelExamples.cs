using Trellis.Testing;
namespace TestingPatterns;

using Trellis;
using Xunit;

/// <summary>
/// Examples demonstrating parallel execution of async Result operations
/// and chaining with Bind, Map, and Tap on the resulting tuple.
/// </summary>
public class ParallelExamples : IClassFixture<TraceFixture>
{
    #region Tuple syntax — fetch in parallel, then chain with Map

    [Fact]
    public async Task TupleSyntax_ParallelFetch_ThenMap_BuildsDashboard()
    {
        // Fetch user data in parallel using the concise tuple syntax,
        // then Map the 3-element tuple into a single Dashboard object.
        var userId = "user-123";

        var result = await (
            FetchUserProfileAsync(userId),
            FetchUserOrdersAsync(userId),
            FetchUserPreferencesAsync(userId)
        )
        .WhenAllAsync()
        .MapAsync((profile, orders, prefs) =>
            new Dashboard(profile, orders, prefs));

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Profile.Should().Be("Profile for user-123");
        result.Unwrap().Orders.Should().Be("Orders for user-123");
        result.Unwrap().Preferences.Should().Be("Preferences for user-123");
    }

    #endregion

    #region Tuple syntax — fetch in parallel, Tap for logging, then Bind

    [Fact]
    public async Task TupleSyntax_ParallelFetch_TapThenBind_ProcessesOrder()
    {
        // Fetch order data in parallel, Tap to log, then Bind to process.
        var orderId = "order-42";
        var logged = false;

        var result = await (
            CheckInventoryAsync(orderId),
            ValidatePaymentAsync(orderId),
            CalculateShippingAsync(orderId)
        )
        .WhenAllAsync()
        .TapAsync((inventory, payment, shipping) => logged = true)
        .BindAsync(CreateOrderSummaryAsync);

        result.IsSuccess.Should().BeTrue();
        logged.Should().BeTrue("Tap should execute on the success path");
        result.Unwrap().Should().Contain("order-42");
    }

    #endregion

    #region Tuple syntax — one failure short-circuits Bind and Tap

    [Fact]
    public async Task TupleSyntax_OneFailure_SkipsBindAndTap()
    {
        // When one parallel operation fails, subsequent Tap and Bind are skipped.
        var orderId = "invalid-payment";
        var tapInvoked = false;
        var bindInvoked = false;

        var result = await (
            CheckInventoryAsync(orderId),
            ValidatePaymentAsync(orderId), // This will fail
            CalculateShippingAsync(orderId)
        )
        .WhenAllAsync()
        .TapAsync((inventory, payment, shipping) => tapInvoked = true)
        .BindAsync((inventory, payment, shipping) =>
        {
            bindInvoked = true;
            return CreateOrderSummaryAsync(inventory, payment, shipping);
        });

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("Invalid payment");
        tapInvoked.Should().BeFalse("Tap should not execute on the failure path");
        bindInvoked.Should().BeFalse("Bind should not execute on the failure path");
    }

    #endregion

    #region ParallelAsync — explicit factory syntax with chaining

    [Fact]
    public async Task ParallelAsync_WithBind_TransformsToSummary()
    {
        // ParallelAsync uses factory functions for explicit parallel intent,
        // then Bind transforms the tuple into a new Result type.
        var orderId = "order-99";

        var result = await Result.ParallelAsync(
            () => CheckInventoryAsync(orderId),
            () => ValidatePaymentAsync(orderId),
            () => CalculateShippingAsync(orderId)
        )
        .WhenAllAsync()
        .BindAsync(CreateOrderSummaryAsync);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Contain("order-99");
    }

    #endregion

    #region Full pipeline — parallel fetch → Tap → Map → Bind

    [Fact]
    public async Task FullPipeline_ParallelFetch_Tap_Map_Bind()
    {
        // A realistic pipeline that fetches data in parallel, logs via Tap,
        // maps the tuple into a domain object, then binds to a final operation.
        var userId = "user-456";

        var result = await (
            FetchUserProfileAsync(userId),
            FetchUserOrdersAsync(userId)
        )
        .WhenAllAsync()
        .TapAsync((profile, orders) => { /* Side effect: audit log, metrics, etc. */ })
        .MapAsync((profile, orders) =>
            new Dashboard(profile, orders, "default-prefs"))
        .BindAsync(SaveDashboardAsync);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Contain("Saved dashboard for user-456");
    }

    #endregion

    [Theory]
    [InlineData("order-42", true)]
    [InlineData("invalid-payment", false)]
    public async Task MultiStage_ParallelValidation_FailureSkipsDependentStage(string orderId, bool succeeds)
    {
        var secondStageStarted = false;

        var result = await Result.ParallelAsync(
                () => CheckInventoryAsync(orderId),
                () => ValidatePaymentAsync(orderId))
            .WhenAllAsync()
            .BindAsync((inventory, payment) =>
            {
                secondStageStarted = true;
                return Result.ParallelAsync(
                        () => CalculateShippingAsync(orderId),
                        () => FetchUserPreferencesAsync("user-123"))
                    .WhenAllAsync()
                    .MapAsync((shipping, preferences) => $"{inventory}, {payment}, {shipping}, {preferences}");
            });

        secondStageStarted.Should().Be(succeeds);
        if (succeeds)
            result.Should().BeSuccess().Which.Should().Contain("Shipping OK for order-42");
        else
            result.Should().BeFailureOfType<Error.InvalidInput>().Which.Detail.Should().Be("Invalid payment");
    }

    // ----- Domain types -----

    private record Dashboard(string Profile, string Orders, string Preferences);

    // ----- Helper methods -----

    private static Task<Result<string>> CheckInventoryAsync(string orderId) =>
        Result.Ok($"Inventory OK for {orderId}").AsTask();

    private static Task<Result<string>> ValidatePaymentAsync(string orderId) =>
        Result.Ensure(orderId != "invalid-payment",
                () => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid payment" })
            .Map(_ => $"Payment OK for {orderId}")
            .AsTask();

    private static Task<Result<string>> CalculateShippingAsync(string orderId) =>
        Result.Ok($"Shipping OK for {orderId}").AsTask();

    private static Task<Result<string>> FetchUserProfileAsync(string userId) =>
        Result.Ok($"Profile for {userId}").AsTask();

    private static Task<Result<string>> FetchUserOrdersAsync(string userId) =>
        Result.Ok($"Orders for {userId}").AsTask();

    private static Task<Result<string>> FetchUserPreferencesAsync(string userId) =>
        Result.Ok($"Preferences for {userId}").AsTask();

    private static Task<Result<string>> CreateOrderSummaryAsync(string inventory, string payment, string shipping) =>
        Result.Ok($"Order summary: {inventory}, {payment}, {shipping}").AsTask();

    private static Task<Result<string>> SaveDashboardAsync(Dashboard dashboard) =>
        Result.Ok($"Saved dashboard for {dashboard.Profile.Split(' ').Last()}").AsTask();
}