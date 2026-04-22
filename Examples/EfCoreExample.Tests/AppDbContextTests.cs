namespace EfCoreExample.Tests;

using Trellis.Testing;

using EfCoreExample.Data;
using EfCoreExample.Entities;
using EfCoreExample.Enums;
using EfCoreExample.ValueObjects;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Tests for the EfCoreExample data layer. Each test uses a fresh
/// in-memory database so behaviour is isolated. The intent is to prove
/// that Trellis value-object conventions (<c>ApplyTrellisConventions</c>)
/// produce a database round-trip with strongly-typed IDs intact.
/// </summary>
public class AppDbContextTests
{
    private static AppDbContext NewContext([System.Runtime.CompilerServices.CallerMemberName] string dbName = "")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"EfCoreExample-{dbName}-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Customer_round_trips_with_value_object_id_intact()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync(Ct);

        var created = Customer.TryCreate("Ada Lovelace", "ada@example.com").Unwrap();
        db.Customers.Add(created);
        await db.SaveChangesAsync(Ct);

        // Detach so the read goes through Trellis value converters, not tracked instances.
        db.ChangeTracker.Clear();

        var fetched = await db.Customers.SingleAsync(c => c.Id == created.Id, Ct);

        fetched.Id.Should().Be(created.Id);
        fetched.Name.Value.Should().Be("Ada Lovelace");
        fetched.Email.Value.Should().Be("ada@example.com");
    }

    [Fact]
    public async Task Order_query_includes_lines_and_preserves_value_object_state()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync(Ct);

        var customer = Customer.TryCreate("Grace Hopper", "grace@example.com").Unwrap();
        var product = Product.TryCreate("Compiler", 1000m, 10).Unwrap();
        db.Customers.Add(customer);
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);

        var order = Order.TryCreate(customer.Id)
            .Bind(o => o.AddLine(product, 2))
            .Bind(o => o.Confirm())
            .Unwrap();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        var fetched = await db.Orders
            .Include(o => o.Lines)
            .SingleAsync(o => o.Id == order.Id, Ct);

        fetched.CustomerId.Should().Be(customer.Id);
        fetched.State.Should().Be(OrderState.Confirmed);
        fetched.Lines.Should().ContainSingle()
            .Which.Quantity.Should().Be(2);
        fetched.Total.Should().Be(2000m);
    }

    [Fact]
    public async Task RequiredEnum_state_is_persisted_via_Trellis_value_converter()
    {
        // Demonstrates that a Trellis convention (RequiredEnum value converter
        // applied through ApplyTrellisConventions) is active: writing an Order
        // whose State is the rich OrderState VO and reading it back gives the
        // same VO instance equality, with all behaviour preserved.
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync(Ct);

        var customer = Customer.TryCreate("Alan Turing", "alan@example.com").Unwrap();
        var product = Product.TryCreate("Bombe", 50m, 5).Unwrap();
        db.Customers.AddRange(customer);
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);

        var order = Order.TryCreate(customer.Id)
            .Bind(o => o.AddLine(product, 1))
            .Bind(o => o.Confirm())
            .Bind(o => o.Ship())
            .Unwrap();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);

        // Detach so the next read goes through the value converter rather than the
        // tracked instance.
        db.ChangeTracker.Clear();

        var fetched = await db.Orders.SingleAsync(o => o.Id == order.Id, Ct);

        fetched.State.Should().BeSameAs(OrderState.Shipped);
        fetched.State.CanCancel.Should().BeFalse();
        fetched.State.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task Product_TryCreate_with_negative_price_fails_validation()
    {
        // Sanity check that domain validation (Result<T> Railway pattern) is
        // wired up before EF Core ever sees the entity.
        var result = Product.TryCreate("Bad", -1m, 0);

        result.IsSuccess.Should().BeFalse();
        await Task.CompletedTask;
    }
}
