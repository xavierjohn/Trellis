namespace CookbookSnippets.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Testing;
using Crud = Recipe01;
using Returns = Recipe22;

public class RecipeBehaviorTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void Authorize_OwnerOrPermission_RespectsExplicitDenial(bool owner, bool granted, bool denied)
    {
        var order = Crud.Order.TryCreate(Crud.OrderId.NewUniqueV7(),
            new Crud.Money(100m, Crud.CurrencyCode.Create("USD")), ActorId.Create("owner")).Unwrap();
        var actor = new Actor(owner ? "owner" : "other",
            granted ? new HashSet<string>(["orders:write"]) : [],
            denied ? new HashSet<string>(["orders:write"]) : [], new Dictionary<string, string>());
        var command = new Recipe07.UpdateOrderCommand(order.Id, 50m);

        var result = command.Authorize(actor, order);

        result.IsSuccess.Should().Be(owner || (granted && !denied));
        if (!result.IsSuccess)
            result.Error.Should().BeOfType<Error.Forbidden>();
    }

    [Theory]
    [InlineData(5, "damaged", false)]
    [InlineData(6, "", false)]
    [InlineData(6, "damaged", true)]
    public async Task Return_DuplicateProductAndReason_PreflightsBeforeMutation(int reserved, string reason, bool succeeds)
    {
        var product = Returns.Product.ForTesting(Returns.ProductId.NewUniqueV7(), reserved);
        var order = Returns.Order.ForTesting(Returns.OrderId.NewUniqueV7(),
            [new(product.Id, 3), new(product.Id, 3)]);
        var handler = new Returns.ReturnOrderHandler(new OrderRepository(order),
            new ProductRepository(product), TimeProvider.System);

        var result = await handler.Handle(new(order.Id, reason), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().Be(succeeds);
        product.Reserved.Should().Be(succeeds ? reserved - 6 : reserved);
        order.IsReturned.Should().Be(succeeds);
        order.UncommittedEvents().Count.Should().Be(succeeds ? 1 : 0);
    }

    [Fact]
    public void Authorize_ExplicitCancellationDenial_OverridesGrant()
    {
        var order = new Recipe31.Order(Recipe31.OrderId.NewUniqueV7());
        var actor = new Actor("other", new HashSet<string>(["orders:cancel"]),
            new HashSet<string>(["orders:cancel"]), new Dictionary<string, string>());

        var result = new Recipe31.CancelOrderCommand(order.Id).Authorize(actor, order);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<Error.Forbidden>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Return_InvalidOrOversizedGroupedQuantity_DoesNotMutate(int quantity)
    {
        var product = Returns.Product.ForTesting(Returns.ProductId.NewUniqueV7(), int.MaxValue);
        var order = Returns.Order.ForTesting(Returns.OrderId.NewUniqueV7(),
            [new(product.Id, quantity), new(product.Id, quantity)]);
        var handler = new Returns.ReturnOrderHandler(new OrderRepository(order),
            new ProductRepository(product), TimeProvider.System);

        var result = await handler.Handle(new(order.Id, "damaged"), TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        product.Reserved.Should().Be(int.MaxValue);
        order.IsReturned.Should().BeFalse();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Return_MultipleMissingProducts_ReportsAllWithoutMutation()
    {
        var product = Returns.Product.ForTesting(Returns.ProductId.NewUniqueV7(), 6);
        var order = Returns.Order.ForTesting(Returns.OrderId.NewUniqueV7(),
            [new(product.Id, 3), new(Returns.ProductId.NewUniqueV7(), 1), new(Returns.ProductId.NewUniqueV7(), 1)]);
        var handler = new Returns.ReturnOrderHandler(new OrderRepository(order),
            new ProductRepository(product), TimeProvider.System);

        var result = await handler.Handle(new(order.Id, "damaged"), TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.Aggregate>();
        product.Reserved.Should().Be(6);
        order.IsReturned.Should().BeFalse();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Return_SingleMissingProduct_PreservesBareNotFound()
    {
        var product = Returns.Product.ForTesting(Returns.ProductId.NewUniqueV7(), 6);
        var missingId = Returns.ProductId.NewUniqueV7();
        var order = Returns.Order.ForTesting(Returns.OrderId.NewUniqueV7(),
            [new(product.Id, 3), new(missingId, 1)]);
        var handler = new Returns.ReturnOrderHandler(new OrderRepository(order),
            new ProductRepository(product), TimeProvider.System);

        var result = await handler.Handle(new(order.Id, "damaged"), TestContext.Current.CancellationToken);

        var failure = result.Should().BeFailureOfType<Error.NotFound>().Which;
        failure.Resource.Should().Be(ResourceRef.For<Returns.Product>(missingId));
        product.Reserved.Should().Be(6);
        order.IsReturned.Should().BeFalse();
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task Rehydrate_LegacyContact_MissingAndInvalidRowsStayOnResultTrack()
    {
        var id = Recipe30.ContactId.NewUniqueV7();
        var empty = new Recipe30.LegacyContactRepository([]);
        var invalid = new Recipe30.LegacyContactRepository(
            [new Recipe30.ContactRow { Id = id.Value, FirstName = null, Email = null }]);

        (await empty.FindByIdAsync(id, TestContext.Current.CancellationToken))
            .Should().BeFailureOfType<Error.NotFound>();
        var failure = (await invalid.FindByIdAsync(id, TestContext.Current.CancellationToken))
            .Should().BeFailureOfType<Error.InvalidInput>().Which;
        failure.Fields.Items.Select(field => field.Field.Path).Should().Equal(["/firstName", "/email"]);
    }

    [Fact]
    public async Task Return_AlreadyReturnedOrder_DoesNotReleaseStockAgain()
    {
        var product = Returns.Product.ForTesting(Returns.ProductId.NewUniqueV7(), 6);
        var order = Returns.Order.ForTesting(Returns.OrderId.NewUniqueV7(), [new(product.Id, 3)]);
        order.Return("damaged", TimeProvider.System.GetUtcNow()).Should().BeSuccess();
        var handler = new Returns.ReturnOrderHandler(new OrderRepository(order),
            new ProductRepository(product), TimeProvider.System);

        var result = await handler.Handle(new(order.Id, "damaged"), TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        product.Reserved.Should().Be(6);
        order.UncommittedEvents().Count.Should().Be(1);
    }

    [Fact]
    public async Task CrudMoney_SnippetContext_RoundTripsOwnedValues()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<Stubs.AppDbContext>()
            .UseSqlite(connection).AddTrellisInterceptors().Options;
        await using var db = new Stubs.AppDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var order = Crud.Order.TryCreate(Crud.OrderId.NewUniqueV7(),
            new Crud.Money(123.45m, Crud.CurrencyCode.Create("USD")), ActorId.Create("owner")).Unwrap();
        db.Orders.Add(order);
        (await db.SaveChangesResultAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        db.ChangeTracker.Clear();

        var loaded = await db.Orders.SingleAsync(TestContext.Current.CancellationToken);

        loaded.Total.Amount.Should().Be(123.45m);
        loaded.Total.Currency.Value.Should().Be("USD");
        loaded.OwnerId.Should().Be(order.OwnerId);
    }

    [Fact]
    public async Task CompositeAddress_SnippetContext_RoundTripsRequiredAndOptionalOwnedValues()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<Recipe13.AppDbContext>()
            .UseSqlite(connection).AddTrellisInterceptors().Options;
        await using var db = new Recipe13.AppDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var address = Recipe13.ShippingAddress.TryCreate("1 Main", "Redmond", "WA", "98052", "US").Unwrap();
        var customer = Recipe13.Customer.TryCreate(Recipe13.CustomerId.NewUniqueV7(), "Alice", address).Unwrap();
        customer.BillingAddress = Maybe.From(
            Recipe13.ShippingAddress.TryCreate("2 Main", "Redmond", "WA", "98052", "US").Unwrap());
        db.Customers.Add(customer);
        (await db.SaveChangesResultAsync(TestContext.Current.CancellationToken)).Should().BeSuccess();
        db.ChangeTracker.Clear();

        var loaded = await db.Customers.SingleAsync(TestContext.Current.CancellationToken);

        loaded.ShippingAddress.Street.Should().Be("1 Main");
        loaded.BillingAddress.TryGetValue(out var billing).Should().BeTrue();
        billing!.Street.Should().Be("2 Main");
    }

    [Fact]
    public async Task Idempotency_ControllerHost_StartsAndReplaysCreatedResponse()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Recipe29.PaymentsController).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        await using var app = Recipe29.IdempotencySample.Build(builder);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "\"payment-1\"");

        using var first = await client.PostAsJsonAsync("/payments",
            new Recipe29.CreatePaymentRequest(10m, "USD"), TestContext.Current.CancellationToken);
        using var replay = await client.PostAsJsonAsync("/payments",
            new Recipe29.CreatePaymentRequest(10m, "USD"), TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(first.StatusCode);
        replay.Headers.Location.Should().Be(first.Headers.Location);
        (await replay.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private sealed class OrderRepository(Returns.Order order) : Returns.IOrderRepository
    {
        public Task<Result<Returns.Order>> FindByIdAsync(Returns.OrderId id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(order));
    }

    private sealed class ProductRepository(Returns.Product product) : Returns.IProductRepository
    {
        public Task<IReadOnlyList<Returns.Product>> GetByIdsAsync(IEnumerable<Returns.ProductId> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Returns.Product>>([product]);
    }
}
