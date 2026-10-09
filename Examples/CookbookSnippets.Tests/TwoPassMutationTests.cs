namespace CookbookSnippets.Tests;

using Trellis.Testing;
using Stock = Recipe25;

public class TwoPassMutationTests
{
    private static readonly DateTimeOffset Deadline = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Submit_TwoPass_LaterProductRejects_LeavesEveryAggregateUnchanged()
    {
        var first = Stock.Product.ForTesting(Stock.ProductId.NewUniqueV7(), 10);
        var last = Stock.Product.ForTesting(Stock.ProductId.NewUniqueV7(), 0);
        var order = Stock.Order.ForTesting(Stock.OrderId.NewUniqueV7(), [new(first.Id, 3), new(last.Id, 1)]);
        var handler = new Stock.SubmitOrderHandler(new OrderRepository(order), new ProductRepository(first, last));

        var result = await handler.Handle(new(order.Id), TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.InvalidInput>();
        first.Stock.Should().Be(10);
        last.Stock.Should().Be(0);
        order.IsSubmitted.Should().BeFalse();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData(5, false, 5)]
    [InlineData(6, true, 0)]
    [InlineData(7, true, 1)]
    public async Task Submit_TwoPass_DuplicateProduct_ValidatesAndReservesGroupedTotal(
        int stock, bool succeeds, int remaining)
    {
        var product = Stock.Product.ForTesting(Stock.ProductId.NewUniqueV7(), stock);
        var order = Stock.Order.ForTesting(Stock.OrderId.NewUniqueV7(), [new(product.Id, 3), new(product.Id, 3)]);
        var handler = new Stock.SubmitOrderHandler(new OrderRepository(order), new ProductRepository(product));

        var result = await handler.Handle(new(order.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().Be(succeeds);
        product.Stock.Should().Be(remaining);
        order.IsSubmitted.Should().Be(succeeds);
        if (!succeeds)
            result.Should().BeFailureOfType<Error.InvalidInput>();
    }

    [Fact]
    public async Task Submit_TwoPass_MissingProduct_FailsBeforeAnyReservation()
    {
        var product = Stock.Product.ForTesting(Stock.ProductId.NewUniqueV7(), 10);
        var missingId = Stock.ProductId.NewUniqueV7();
        var order = Stock.Order.ForTesting(Stock.OrderId.NewUniqueV7(), [new(product.Id, 3), new(missingId, 1)]);
        var handler = new Stock.SubmitOrderHandler(new OrderRepository(order), new ProductRepository(product));

        var result = await handler.Handle(new(order.Id), TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.NotFound>().Which.Resource
            .Should().Be(ResourceRef.For<Stock.Product>(missingId));
        product.Stock.Should().Be(10);
        order.IsSubmitted.Should().BeFalse();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_TwoPass_EmptyOrder_DoesNotSubmit()
    {
        var order = Stock.Order.ForTesting(Stock.OrderId.NewUniqueV7(), []);
        var handler = new Stock.SubmitOrderHandler(new OrderRepository(order), new ProductRepository());

        var result = await handler.Handle(new(order.Id), TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.InvalidInput>();
        order.IsSubmitted.Should().BeFalse();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Return_StockFirst_DeadlineChangesAfterPreflight_LeavesStockAndEventOnFailure()
    {
        var order = new Stock.ReturnWindowOrder(Stock.OrderId.NewUniqueV7(), Deadline);
        var inventory = new Stock.ReturnInventory(Stock.ProductId.NewUniqueV7(), 3);
        var time = new AdvancingTimeProvider(Deadline.AddTicks(-1), TimeSpan.FromTicks(2));
        var preflightAt = time.GetUtcNow();
        order.CanReturn(preflightAt).Should().BeSuccess();
        inventory.CanReleaseStock(3).Should().BeSuccess();

        var result = inventory.ReleaseStock(3, preflightAt)
            .Bind(_ => order.Return(time.GetUtcNow()));

        AssertExpired(result);
        time.CallCount.Should().Be(2);
        inventory.Reserved.Should().Be(0);
        inventory.UncommittedEvents().Should().ContainSingle().Which
            .Should().Be(new Stock.StockReleased(inventory.Id, 3, preflightAt));
        order.ReturnedAt.Should().BeNull();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Release_TwoPass_EarlierFreezeInvalidatesLaterGuard_LeavesMetadataAndEventOnFailure()
    {
        var inventory = new Stock.ReturnInventory(Stock.ProductId.NewUniqueV7(), 3);
        inventory.CanFreeze().Should().BeSuccess();
        inventory.CanReleaseStock(3).Should().BeSuccess();

        var result = inventory.Freeze(Deadline)
            .Bind(_ => inventory.ReleaseStock(3, Deadline));

        result.Should().BeFailureOfType<Error.InvalidInput>().Which.Rules.Items
            .Should().ContainSingle().Which.ReasonCode.Should().Be("stock.release-not-allowed");
        inventory.Reserved.Should().Be(3);
        inventory.IsFrozen.Should().BeTrue();
        inventory.UncommittedEvents().Should().ContainSingle().Which
            .Should().Be(new Stock.InventoryFrozen(inventory.Id, Deadline));
    }

    [Fact]
    public void Release_TwoPass_ReleaseBeforeFreeze_PreservesBothPreflightGuards()
    {
        var inventory = new Stock.ReturnInventory(Stock.ProductId.NewUniqueV7(), 3);
        inventory.CanReleaseStock(3).Should().BeSuccess();
        inventory.CanFreeze().Should().BeSuccess();

        var result = inventory.ReleaseStock(3, Deadline)
            .Bind(_ => inventory.Freeze(Deadline));

        result.Should().BeSuccess();
        inventory.Reserved.Should().Be(0);
        inventory.IsFrozen.Should().BeTrue();
        inventory.UncommittedEvents().Should().Equal(
            [new Stock.StockReleased(inventory.Id, 3, Deadline), new Stock.InventoryFrozen(inventory.Id, Deadline)]);
    }

    [Fact]
    public void Return_ExecutionTime_DeadlineChangesAfterPreflight_LeavesStockMetadataAndEventsUnchanged()
    {
        var order = new Stock.ReturnWindowOrder(Stock.OrderId.NewUniqueV7(), Deadline);
        var inventory = new Stock.ReturnInventory(Stock.ProductId.NewUniqueV7(), 3);
        var time = new AdvancingTimeProvider(Deadline.AddTicks(-1), TimeSpan.FromTicks(2));

        var result = Stock.ReturnPolicies.AtExecutionTime(order, inventory, 3, time);

        AssertExpired(result);
        time.CallCount.Should().Be(2);
        inventory.Reserved.Should().Be(3);
        inventory.IsFrozen.Should().BeFalse();
        inventory.UncommittedEvents().Should().BeEmpty();
        order.ReturnedAt.Should().BeNull();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Return_SnapshotTime_ClockAdvancesPastDeadline_AcceptsCapturedDecisionInstant()
    {
        var order = new Stock.ReturnWindowOrder(Stock.OrderId.NewUniqueV7(), Deadline);
        var inventory = new Stock.ReturnInventory(Stock.ProductId.NewUniqueV7(), 3);
        var decisionAt = Deadline.AddTicks(-1);
        var time = new AdvancingTimeProvider(decisionAt, TimeSpan.FromTicks(2));

        var result = Stock.ReturnPolicies.AtSnapshotTime(order, inventory, 3, time);

        result.Should().BeSuccess();
        time.CallCount.Should().Be(1);
        time.UtcNow.Should().BeAfter(Deadline);
        inventory.Reserved.Should().Be(0);
        inventory.UncommittedEvents().Should().ContainSingle().Which
            .Should().Be(new Stock.StockReleased(inventory.Id, 3, decisionAt));
        order.ReturnedAt.Should().Be(decisionAt);
        order.UncommittedEvents().Should().ContainSingle().Which
            .Should().Be(new Stock.ReturnCompleted(order.Id, decisionAt));
    }

    [Theory]
    [InlineData(false, -1, true)]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, -1, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    public void Return_TimePolicy_DeadlineBoundary_UsesInclusiveDeadline(
        bool snapshot, int ticksFromDeadline, bool succeeds)
    {
        var order = new Stock.ReturnWindowOrder(Stock.OrderId.NewUniqueV7(), Deadline);
        var inventory = new Stock.ReturnInventory(Stock.ProductId.NewUniqueV7(), 3);
        var decisionAt = Deadline.AddTicks(ticksFromDeadline);
        var time = new AdvancingTimeProvider(decisionAt, TimeSpan.Zero);

        var result = snapshot
            ? Stock.ReturnPolicies.AtSnapshotTime(order, inventory, 3, time)
            : Stock.ReturnPolicies.AtExecutionTime(order, inventory, 3, time);

        result.IsSuccess.Should().Be(succeeds);
        inventory.Reserved.Should().Be(succeeds ? 0 : 3);
        order.ReturnedAt.Should().Be(succeeds ? decisionAt : null);
        inventory.UncommittedEvents().Count.Should().Be(succeeds ? 1 : 0);
        order.UncommittedEvents().Count.Should().Be(succeeds ? 1 : 0);
        if (!succeeds)
            AssertExpired(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Return_TimePolicy_StockPreflightRejects_LeavesEveryAggregateUnchanged(bool snapshot)
    {
        var order = new Stock.ReturnWindowOrder(Stock.OrderId.NewUniqueV7(), Deadline);
        var inventory = new Stock.ReturnInventory(Stock.ProductId.NewUniqueV7(), 2);
        var time = new AdvancingTimeProvider(Deadline.AddTicks(-1), TimeSpan.Zero);

        var result = snapshot
            ? Stock.ReturnPolicies.AtSnapshotTime(order, inventory, 3, time)
            : Stock.ReturnPolicies.AtExecutionTime(order, inventory, 3, time);

        result.Should().BeFailureOfType<Error.InvalidInput>();
        inventory.Reserved.Should().Be(2);
        inventory.UncommittedEvents().Should().BeEmpty();
        order.ReturnedAt.Should().BeNull();
        order.UncommittedEvents().Should().BeEmpty();
    }

    private static void AssertExpired(Result<Trellis.Unit> result) =>
        result.Should().BeFailureOfType<Error.InvalidInput>().Which.Rules.Items
            .Should().ContainSingle().Which.ReasonCode.Should().Be("order.return-window-expired");

    private sealed class AdvancingTimeProvider(DateTimeOffset firstRead, TimeSpan step) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = firstRead;

        public int CallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            var now = UtcNow;
            UtcNow += step;
            CallCount++;
            return now;
        }
    }

    private sealed class OrderRepository(Stock.Order order) : Stock.IOrderRepository
    {
        public Task<Result<Stock.Order>> FindByIdAsync(Stock.OrderId id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(order));
    }

    private sealed class ProductRepository(params Stock.Product[] products) : Stock.IProductRepository
    {
        public Task<IReadOnlyList<Stock.Product>> GetByIdsAsync(
            IEnumerable<Stock.ProductId> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Stock.Product>>(products);
    }
}
