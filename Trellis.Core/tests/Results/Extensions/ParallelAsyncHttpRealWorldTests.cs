namespace Trellis.Core.Tests.Results.Extensions;

using System.Net;
using Trellis.Testing;

/// <summary>
/// Real-world example demonstrating ParallelAsync with HTTP calls.
/// Shows handling of transient errors (retry) vs permanent errors (fail fast).
/// 
/// Scenario: E-commerce checkout calling multiple external services in parallel:
/// - User service (get user details)
/// - Inventory service (check stock)  
/// - Payment service (validate payment method)
/// - Shipping service (calculate rates)
/// </summary>
public class ParallelAsyncHttpRealWorldTests : TestBase
{
    #region Test Data & Helpers

    // Simulated HTTP responses
    private record UserResponse(string UserId, string Email, bool IsActive);
    private record InventoryResponse(string ProductId, int StockLevel, bool Available);
    private record PaymentResponse(string PaymentId, bool IsValid);
    private record ShippingResponse(string CarrierId, decimal Rate);

    // Checkout DTO
    private record CheckoutRequest(string UserId, string ProductId, string PaymentMethodId, string ShippingAddress);
    private record CheckoutResult(UserResponse User, InventoryResponse Inventory, PaymentResponse Payment, ShippingResponse Shipping);

    private sealed class ConcurrentStartGate(int operationCount)
    {
        private readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public async Task<Result<T>> RunAsync<T>(Func<Task<Result<T>>> operation)
        {
            if (Interlocked.Increment(ref _started) == operationCount)
                _allStarted.SetResult();

            await _allStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            return await operation();
        }
    }

    #endregion

    #region Success Scenarios

    [Fact]
    public async Task ParallelAsync_HttpCalls_AllServicesSucceed_ReturnsCheckoutData()
    {
        // Arrange - all services return success
        var request = new CheckoutRequest("user-123", "prod-456", "pm-789", "123 Main St");

        // Act - call 4 services in parallel using ParallelAsync
        var result = await Result.ParallelAsync(
            () => FetchUserAsync(request.UserId),
            () => CheckInventoryAsync(request.ProductId),
            () => ValidatePaymentAsync(request.PaymentMethodId),
            () => CalculateShippingAsync(request.ShippingAddress)
        )
        .WhenAllAsync()
        .BindAsync((user, inventory, payment, shipping) =>
            Result.Ok(new CheckoutResult(user, inventory, payment, shipping))
        );

        // Assert
        result.Should().BeSuccess();
        result.Unwrap().User.UserId.Should().Be("user-123");
        result.Unwrap().Inventory.Available.Should().BeTrue();
        result.Unwrap().Payment.IsValid.Should().BeTrue();
        result.Unwrap().Shipping.Rate.Should().BeGreaterThan(0);
    }

    #endregion

    #region Permanent Error Scenarios - Fail Fast (No Retry)

    [Fact]
    public async Task ParallelAsync_HttpCalls_UserNotFound_FailsFast()
    {
        // Arrange - user doesn't exist (404 - permanent error)
        var invalidUserId = "nonexistent-user";
        var request = new CheckoutRequest(invalidUserId, "prod-456", "pm-789", "123 Main St");

        // Act - ParallelAsync with 404 should fail immediately (no retry)
        var result = await Result.ParallelAsync(
            () => FetchUserAsync(invalidUserId),  // This will return 404 → permanent error
            () => CheckInventoryAsync(request.ProductId),
            () => ValidatePaymentAsync(request.PaymentMethodId),
            () => CalculateShippingAsync(request.ShippingAddress)
        ).WhenAllAsync();

        // Assert - should fail with new Error.NotFound(new ResourceRef("Resource", null)) { Detail = permanent failure }
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.NotFound>();
        result.Error!.Detail.Should().Contain("User not found");
    }

    [Fact]
    public async Task ParallelAsync_HttpCalls_OutOfStock_FailsFast()
    {
        // Arrange - product out of stock (conflict/domain error - permanent)
        var outOfStockProductId = "prod-out-of-stock";
        var request = new CheckoutRequest("user-123", outOfStockProductId, "pm-789", "123 Main St");

        // Act
        var result = await Result.ParallelAsync(
            () => FetchUserAsync(request.UserId),
            () => CheckInventoryAsync(outOfStockProductId),  // This returns conflict error
            () => ValidatePaymentAsync(request.PaymentMethodId),
            () => CalculateShippingAsync(request.ShippingAddress)
        ).WhenAllAsync();

        // Assert - should fail with new Error.Conflict(null, "conflict") { Detail = permanent business rule violation }
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.Conflict>();
        result.Error!.Detail.Should().Contain("Out of stock");
    }

    [Fact]
    public async Task ParallelAsync_HttpCalls_InvalidPayment_FailsFast()
    {
        // Arrange - payment method invalid (validation error - permanent)
        var invalidPaymentId = "pm-expired";
        var request = new CheckoutRequest("user-123", "prod-456", invalidPaymentId, "123 Main St");

        // Act
        var result = await Result.ParallelAsync(
            () => FetchUserAsync(request.UserId),
            () => CheckInventoryAsync(request.ProductId),
            () => ValidatePaymentAsync(invalidPaymentId),  // This returns validation error
            () => CalculateShippingAsync(request.ShippingAddress)
        ).WhenAllAsync();

        // Assert - should fail with Error.InvalidInput (permanent business rule)
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.InvalidInput>();
        result.Error!.Detail.Should().Contain("expired");
    }

    [Fact]
    public async Task ParallelAsync_HttpCalls_MultipleServiceFailures_CombinesErrors()
    {
        // Arrange - multiple services fail with different error types
        var request = new CheckoutRequest("nonexistent-user", "prod-out-of-stock", "pm-expired", "123 Main St");

        // Act - 3 out of 4 services fail
        var result = await Result.ParallelAsync(
            () => FetchUserAsync("nonexistent-user"),      // Error.NotFound
            () => CheckInventoryAsync("prod-out-of-stock"), // Error.Conflict
            () => ValidatePaymentAsync("pm-expired"),       // Error.InvalidInput
            () => CalculateShippingAsync(request.ShippingAddress)  // Success
        ).WhenAllAsync();

        // Assert - should aggregate different error types
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.Aggregate>();
        var aggregateError = (Error.Aggregate)result.Error!;
        aggregateError.Errors.Items.Should().HaveCount(3);
        aggregateError.Errors.Items.Should().ContainSingle(e => e is Error.NotFound);
        aggregateError.Errors.Items.Should().ContainSingle(e => e is Error.Conflict);
        aggregateError.Errors.Items.Should().ContainSingle(e => e is Error.InvalidInput);
    }

    #endregion

    #region Transient Error Scenarios - With Retry (Using RecoverOnFailureAsync)

    [Fact]
    public async Task ParallelAsync_HttpCalls_TransientError_RetriesAndSucceeds()
    {
        // Arrange - simulate service that fails once then succeeds (transient 503 error)
        var attemptCount = 0;

        async Task<Result<InventoryResponse>> CheckInventoryWithRetry(string productId)
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                // First attempt: transient error (503 Service Unavailable)
                await Task.Delay(10); // Simulate network delay
                return Result.Fail<InventoryResponse>(new Error.Unavailable() { Detail = "Inventory service temporarily unavailable" });
            }

            // Retry succeeds
            await Task.Delay(10);
            return Result.Ok(new InventoryResponse(productId, 100, true));
        }

        var request = new CheckoutRequest("user-123", "prod-456", "pm-789", "123 Main St");

        // Act - use RecoverOnFailureAsync to retry transient errors
        var result = await Result.ParallelAsync(
            () => FetchUserAsync(request.UserId),
            () => CheckInventoryWithRetry(request.ProductId)
                .RecoverOnFailureAsync(
                    predicate: error => error is Error.Unavailable,
                    funcAsync: async () =>
                    {
                        await Task.Delay(50); // Backoff delay
                        return await CheckInventoryWithRetry(request.ProductId); // Retry
                    }),
            () => ValidatePaymentAsync(request.PaymentMethodId),
            () => CalculateShippingAsync(request.ShippingAddress)
        ).WhenAllAsync();

        // Assert - should succeed after retry
        result.Should().BeSuccess();
        attemptCount.Should().Be(2); // Verify retry happened (first call + one retry)
    }

    [Fact]
    public async Task ParallelAsync_HttpCalls_TransientError_RetriesMultipleTimes()
    {
        // Arrange - service fails twice then succeeds
        var attemptCount = 0;

        async Task<Result<PaymentResponse>> ValidatePaymentWithRetries(string paymentId)
        {
            attemptCount++;
            if (attemptCount <= 2)
            {
                // Fail first 2 attempts (transient)
                await Task.Delay(10);
                return Result.Fail<PaymentResponse>(new Error.Unavailable() { Detail = "Payment service timeout" });
            }

            // Third attempt succeeds
            await Task.Delay(10);
            return Result.Ok(new PaymentResponse(paymentId, true));
        }

        // Act - chain RecoverOnFailureAsync for multiple retries
        var result = await ValidatePaymentWithRetries("pm-789")
            .RecoverOnFailureAsync(
                predicate: error => error is Error.Unavailable,
                funcAsync: async () =>
                {
                    await Task.Delay(50); // First retry delay
                    return await ValidatePaymentWithRetries("pm-789")
                        .RecoverOnFailureAsync(
                            predicate: error => error is Error.Unavailable,
                            funcAsync: async () =>
                            {
                                await Task.Delay(100); // Second retry delay (exponential backoff)
                                return await ValidatePaymentWithRetries("pm-789");
                            });
                });

        // Assert - should succeed on 3rd attempt
        result.Should().BeSuccess();
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task ParallelAsync_HttpCalls_TransientError_ExceedsMaxRetries_FailsPermanently()
    {
        // Arrange - service always returns transient error
        var attemptCount = 0;

        async Task<Result<ShippingResponse>> CalculateShippingAlwaysFails(string address)
        {
            attemptCount++;
            await Task.Delay(10);
            return Result.Fail<ShippingResponse>(new Error.Unavailable() { Detail = "Shipping service down" });
        }

        // Act - try with 2 retries (3 total attempts)
        var result = await CalculateShippingAlwaysFails("123 Main St")
            .RecoverOnFailureAsync(
                predicate: error => error is Error.Unavailable,
                funcAsync: async () =>
                {
                    await Task.Delay(50); // First retry
                    return await CalculateShippingAlwaysFails("123 Main St")
                        .RecoverOnFailureAsync(
                            predicate: error => error is Error.Unavailable,
                            funcAsync: async () =>
                            {
                                await Task.Delay(100); // Second retry
                                return await CalculateShippingAlwaysFails("123 Main St");
                                // No more retries after this
                            });
                });

        // Assert - should fail after all retries exhausted
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.Unavailable>();
        attemptCount.Should().Be(3); // Initial + 2 retries
    }

    #endregion

    #region Concurrent Execution

    [Fact]
    public async Task ParallelAsync_HttpCalls_ExecutesInParallel_NotSequentially()
    {
        var startGate = new ConcurrentStartGate(4);

        Task<Result<string>> TrackedTask(string name) =>
            startGate.RunAsync(() => Task.FromResult(Result.Ok(name)));

        var result = await Result.ParallelAsync(
            () => TrackedTask("user"),
            () => TrackedTask("inventory"),
            () => TrackedTask("payment"),
            () => TrackedTask("shipping")
        ).WhenAllAsync();

        result.Should().BeSuccess();
        result.Unwrap().Should().Be(("user", "inventory", "payment", "shipping"));
        startGate.Started.Should().Be(4);
    }

    #endregion

    #region Multi-Stage Parallel Execution - Real-World Choreography

    [Fact]
    public async Task ParallelAsync_MultiStage_FetchDataThenProcessInParallel()
    {
        // Scenario: E-commerce checkout with 2 stages:
        // Stage 1: Fetch user, inventory, payment in parallel
        // Stage 2: Use results to run fraud detection + shipping calculation in parallel

        // Arrange
        var request = new CheckoutRequest("user-123", "prod-456", "pm-789", "123 Main St");

        // Act - Multi-stage parallel execution
        var result = await Result.ParallelAsync(
            () => FetchUserAsync(request.UserId),
            () => CheckInventoryAsync(request.ProductId),
            () => ValidatePaymentAsync(request.PaymentMethodId)
        )
        .WhenAllAsync()  // ✅ Wait for Stage 1 to complete

        // Stage 2: Now we have (user, inventory, payment) - run fraud & shipping in parallel
        .BindAsync((user, inventory, payment) =>
            Result.ParallelAsync(
                () => RunFraudDetectionAsync(user, payment, inventory),  // Uses all 3 results
                () => CalculateShippingWithWeightAsync(request.ShippingAddress, inventory)  // Uses inventory
            )
            .WhenAllAsync()
            .BindAsync((fraudCheck, shipping) =>
                Result.Ok(new
                {
                    User = user,
                    Inventory = inventory,
                    Payment = payment,
                    FraudCheck = fraudCheck,
                    Shipping = shipping
                })
            )
        );

        // Assert - all stages completed successfully
        result.Should().BeSuccess();
        result.Unwrap().User.UserId.Should().Be("user-123");
        result.Unwrap().Inventory.Available.Should().BeTrue();
        result.Unwrap().Payment.IsValid.Should().BeTrue();
        result.Unwrap().FraudCheck.IsSafe.Should().BeTrue();
        result.Unwrap().Shipping.Rate.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ParallelAsync_MultiStage_Stage1Failure_SkipsStage2()
    {
        // Scenario: If Stage 1 fails, Stage 2 should not execute (short-circuit)

        // Arrange - user doesn't exist (Stage 1 will fail)
        var request = new CheckoutRequest("nonexistent-user", "prod-456", "pm-789", "123 Main St");
        var stage2Executed = false;

        // Act
        var result = await Result.ParallelAsync(
            () => FetchUserAsync("nonexistent-user"),  // ❌ This fails
            () => CheckInventoryAsync(request.ProductId),
            () => ValidatePaymentAsync(request.PaymentMethodId)
        )
        .WhenAllAsync()

        // Stage 2 should NOT execute because Stage 1 failed
        .BindAsync((user, inventory, payment) =>
        {
            stage2Executed = true;  // Track if this executes
            return Result.ParallelAsync(
                () => RunFraudDetectionAsync(user, payment, inventory),
                () => CalculateShippingWithWeightAsync(request.ShippingAddress, inventory)
            ).WhenAllAsync();
        });

        // Assert - Stage 1 failed, Stage 2 never ran
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.NotFound>();
        stage2Executed.Should().BeFalse();  // ✅ Stage 2 was skipped (short-circuit)
    }

    [Fact]
    public async Task ParallelAsync_MultiStage_Stage2Failure_ReturnsStage2Error()
    {
        // Scenario: Stage 1 succeeds, but Stage 2 fails (e.g., fraud detected)

        // Arrange - high-risk transaction triggers fraud detection
        var request = new CheckoutRequest("user-high-risk", "prod-expensive", "pm-789", "123 Main St");

        // Act
        var result = await Result.ParallelAsync(
            () => FetchUserAsync("user-high-risk"),
            () => CheckInventoryAsync("prod-expensive"),
            () => ValidatePaymentAsync(request.PaymentMethodId)
        )
        .WhenAllAsync()

        // Stage 2: Fraud detection will fail
        .BindAsync((user, inventory, payment) =>
            Result.ParallelAsync(
                () => RunFraudDetectionAsync(user, payment, inventory),  // ❌ Will fail (fraud)
                () => CalculateShippingWithWeightAsync(request.ShippingAddress, inventory)
            )
            .WhenAllAsync()
        );

        // Assert - Stage 2 fraud check failed
        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.Forbidden>();
        result.Error!.Detail.Should().Contain("Suspicious transaction");
    }

    [Fact]
    public async Task ParallelAsync_ThreeStages_CascadingParallelExecution()
    {
        // Scenario: 3 stages of parallel execution
        // Stage 1: Fetch user + inventory (2 parallel)
        // Stage 2: Validate payment + check fraud (2 parallel, needs user from Stage 1)
        // Stage 3: Calculate shipping + reserve inventory (2 parallel, needs results from Stage 2)

        // Arrange
        var userId = "user-123";
        var productId = "prod-456";
        var paymentId = "pm-789";
        var address = "123 Main St";

        // Act - 3-stage pipeline
        var result = await Result.ParallelAsync(
            () => FetchUserAsync(userId),
            () => CheckInventoryAsync(productId)
        )
        .WhenAllAsync()  // Stage 1 complete

        .BindAsync((user, inventory) =>
            Result.ParallelAsync(
                () => ValidatePaymentAsync(paymentId),
                () => RunFraudDetectionAsync(user, new PaymentResponse(paymentId, true), inventory)
            )
            .WhenAllAsync()  // Stage 2 complete

            .BindAsync((payment, fraudCheck) =>
                Result.ParallelAsync(
                    () => CalculateShippingWithWeightAsync(address, inventory),
                    () => ReserveInventoryAsync(inventory)
                )
                .WhenAllAsync()  // Stage 3 complete

                .BindAsync((shipping, reservation) =>
                    Result.Ok(new
                    {
                        User = user,
                        Inventory = inventory,
                        Payment = payment,
                        FraudCheck = fraudCheck,
                        Shipping = shipping,
                        Reservation = reservation
                    })
                )
            )
        );

        // Assert - all 3 stages completed
        result.Should().BeSuccess();
        result.Unwrap().User.UserId.Should().Be("user-123");
        result.Unwrap().Reservation.IsReserved.Should().BeTrue();
    }

    [Fact]
    public async Task ParallelAsync_MultiStage_IndependentOperations_OverlapWithinEachStage()
    {
        var fetchGate = new ConcurrentStartGate(3);
        var processGate = new ConcurrentStartGate(2);

        var result = await Result.ParallelAsync(
            () => fetchGate.RunAsync(() => FetchUserAsync("user-123")),
            () => fetchGate.RunAsync(() => CheckInventoryAsync("prod-456")),
            () => fetchGate.RunAsync(() => ValidatePaymentAsync("pm-789"))
        )
        .WhenAllAsync()
        .BindAsync((user, inventory, payment) =>
            Result.ParallelAsync(
                () => processGate.RunAsync(() => RunFraudDetectionAsync(user, payment, inventory)),
                () => processGate.RunAsync(() => CalculateShippingWithWeightAsync("123 Main St", inventory))
            ).WhenAllAsync()
        );

        result.Should().BeSuccess();
        result.Unwrap().Should().Be((new FraudCheckResponse(true, "Low"), new ShippingResponse("USPS", 12.99m)));
        fetchGate.Started.Should().Be(3);
        processGate.Started.Should().Be(2);
    }

    #endregion

    #region Helper Methods - Simulated HTTP Calls

    private record FraudCheckResponse(bool IsSafe, string RiskLevel);
    private record InventoryReservation(string ReservationId, bool IsReserved);

    private static async Task<Result<FraudCheckResponse>> RunFraudDetectionAsync(
        UserResponse user,
        PaymentResponse payment,
        InventoryResponse inventory)
    {
        await Task.Delay(30); // Simulate fraud detection latency

        // High-risk scenarios
        if (user.UserId == "user-high-risk")
            return Result.Fail<FraudCheckResponse>(new Error.Forbidden("authorization.forbidden") { Detail = "Suspicious transaction detected: high-risk user" });

        if (inventory.ProductId == "prod-expensive" && inventory.StockLevel < 10)
            return Result.Fail<FraudCheckResponse>(new Error.Forbidden("authorization.forbidden") { Detail = "Suspicious transaction: expensive item with low stock" });

        return Result.Ok(new FraudCheckResponse(true, "Low"));
    }

    private static async Task<Result<ShippingResponse>> CalculateShippingWithWeightAsync(
        string address,
        InventoryResponse inventory)
    {
        await Task.Delay(40); // Simulate shipping calculation

        if (string.IsNullOrWhiteSpace(address))
            return Result.Fail<ShippingResponse>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Shipping address required" });

        // Calculate based on inventory (heavier items cost more)
        var rate = inventory.StockLevel > 50 ? 12.99m : 9.99m;
        return Result.Ok(new ShippingResponse("USPS", rate));
    }

    private static async Task<Result<InventoryReservation>> ReserveInventoryAsync(
        InventoryResponse inventory)
    {
        await Task.Delay(25); // Simulate reservation

        if (!inventory.Available)
            return Result.Fail<InventoryReservation>(new Error.Conflict(Resource: null, Code: "conflict") { Detail = "Cannot reserve unavailable inventory" });

        return Result.Ok(new InventoryReservation($"res-{Guid.NewGuid():N}", true));
    }

    private static async Task<Result<UserResponse>> FetchUserAsync(string userId)
    {
        await Task.Delay(50); // Simulate network latency

        return userId switch
        {
            "nonexistent-user" => Result.Fail<UserResponse>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = $"User not found: {userId}" }),
            "user-123" => Result.Ok(new UserResponse(userId, "user@example.com", true)),
            "user-high-risk" => Result.Ok(new UserResponse(userId, "highrisk@example.com", true)),
            _ => Result.Fail<UserResponse>(new Error.Unexpected("test") { Detail = "Unknown user ID" })
        };
    }

    private static async Task<Result<InventoryResponse>> CheckInventoryAsync(string productId)
    {
        await Task.Delay(50);

        return productId switch
        {
            "prod-out-of-stock" => Result.Fail<InventoryResponse>(new Error.Conflict(Resource: null, Code: "conflict") { Detail = "Out of stock: product is unavailable" }),
            "prod-456" => Result.Ok(new InventoryResponse(productId, 100, true)),
            "prod-expensive" => Result.Ok(new InventoryResponse(productId, 5, true)),
            _ => Result.Fail<InventoryResponse>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = $"Product not found: {productId}" })
        };
    }

    private static async Task<Result<PaymentResponse>> ValidatePaymentAsync(string paymentMethodId)
    {
        await Task.Delay(50);

        return paymentMethodId switch
        {
            "pm-expired" => Result.Fail<PaymentResponse>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Payment method expired" }),
            "pm-789" => Result.Ok(new PaymentResponse(paymentMethodId, true)),
            _ => Result.Fail<PaymentResponse>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = $"Payment method not found: {paymentMethodId}" })
        };
    }

    private static async Task<Result<ShippingResponse>> CalculateShippingAsync(string address)
    {
        await Task.Delay(50);

        if (string.IsNullOrWhiteSpace(address))
            return Result.Fail<ShippingResponse>(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Shipping address required" });

        return Result.Ok(new ShippingResponse("USPS", 9.99m));
    }

    #endregion
}