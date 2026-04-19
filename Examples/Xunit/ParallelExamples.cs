namespace Example.Tests;

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
        result.Value.Profile.Should().Be("Profile for user-123");
        result.Value.Orders.Should().Be("Orders for user-123");
        result.Value.Preferences.Should().Be("Preferences for user-123");
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
        result.Value.Should().Contain("order-42");
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
        result.Error.Detail.Should().Contain("Invalid payment");
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
        result.Value.Should().Contain("order-99");
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
        result.Value.Should().Contain("Saved dashboard for user-456");
    }

    #endregion

    // ----- Domain types -----

    private record Dashboard(string Profile, string Orders, string Preferences);

    // ----- Helper methods -----

    private static Task<Result<string>> CheckInventoryAsync(string orderId) =>
        Task.FromResult(Result.Ok($"Inventory OK for {orderId}"));

    private static Task<Result<string>> ValidatePaymentAsync(string orderId)
    {
        if (orderId == "invalid-payment")
            return Task.FromResult(Result.Fail<string>(Error.Validation("Invalid payment")));

        return Task.FromResult(Result.Ok($"Payment OK for {orderId}"));
    }

    private static Task<Result<string>> CalculateShippingAsync(string orderId) =>
        Task.FromResult(Result.Ok($"Shipping OK for {orderId}"));

    private static Task<Result<string>> FetchUserProfileAsync(string userId) =>
        Task.FromResult(Result.Ok($"Profile for {userId}"));

    private static Task<Result<string>> FetchUserOrdersAsync(string userId) =>
        Task.FromResult(Result.Ok($"Orders for {userId}"));

    private static Task<Result<string>> FetchUserPreferencesAsync(string userId) =>
        Task.FromResult(Result.Ok($"Preferences for {userId}"));

    private static Task<Result<string>> CreateOrderSummaryAsync(string inventory, string payment, string shipping) =>
        Task.FromResult(Result.Ok($"Order summary: {inventory}, {payment}, {shipping}"));

    private static Task<Result<string>> SaveDashboardAsync(Dashboard dashboard) =>
        Task.FromResult(Result.Ok($"Saved dashboard for {dashboard.Profile.Split(' ').Last()}"));
}