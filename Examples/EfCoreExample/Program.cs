// =============================================================================
// EF Core Example with Trellis Primitive Value Objects
// =============================================================================
// This example demonstrates seamless integration of strongly-typed value objects
// with Entity Framework Core using an in-memory database.
//
// Featured Value Objects:
// - RequiredGuid<T>  : OrderId, CustomerId, ProductId (GUID-based identifiers)
//                      Use NewUniqueV7() for time-ordered, sortable identifiers
// - RequiredString<T>: ProductName, CustomerName (non-empty string validation)
// - EmailAddress     : Built-in RFC 5322 validated email addresses
//
// Key Benefits:
// 1. Type Safety     - Cannot mix OrderId with CustomerId
// 2. Validation      - All values validated at creation time
// 3. Clean Entities  - No primitive obsession, expressive domain model
// 4. EF Core Ready   - Simple value converters for database persistence
// =============================================================================

using System.Text;
using EfCoreExample.Data;
using EfCoreExample.Entities;
using EfCoreExample.Enums;
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

// Enable UTF-8 output for Unicode characters (emojis, box-drawing, etc.)
Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  EF Core Example with Trellis Primitive Value Objects            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Configure EF Core with in-memory database
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase("EfCoreExample")
    .AddTrellisInterceptors()
    .Options;

await using var context = new AppDbContext(options);

// =============================================================================
// 1. CREATE PRODUCTS (Using RequiredGuid and RequiredString)
// =============================================================================
Console.WriteLine("📦 Creating Products...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");

var products = new List<Product>();
var productResults = new[]
{
    Product.TryCreate("MacBook Pro 16\"", 2499.99m, 50),
    Product.TryCreate("iPhone 15 Pro", 1199.99m, 100),
    Product.TryCreate("AirPods Pro", 249.99m, 200)
};

foreach (var result in productResults)
{
    result
        .Tap(product =>
        {
            products.Add(product);
            Console.WriteLine($"  ✓ Created: {product.Name} (ID: {product.Id})");
            Console.WriteLine($"             Price: ${product.Price:N2}, Stock: {product.StockQuantity}");
        })
        .TapOnFailure(error => Console.WriteLine($"  ✗ Failed: {DescribeError(error)}"));
}

context.Products.AddRange(products);
if (!await TrySaveChangesAsync(context))
    return 1;
Console.WriteLine();

// =============================================================================
// 2. CREATE CUSTOMER (Using RequiredGuid, RequiredString, EmailAddress)
// =============================================================================
Console.WriteLine("👤 Creating Customer...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");

var customerResult = Customer.TryCreate("John Doe", "john.doe@example.com");
Customer customer = null!;

customerResult
    .Tap(c =>
    {
        customer = c;
        context.Customers.Add(c);
        Console.WriteLine($"  ✓ Created: {c.Name}");
        Console.WriteLine($"             ID: {c.Id}");
        Console.WriteLine($"             Email: {c.Email}");
    })
    .TapOnFailure(error => Console.WriteLine($"  ✗ Failed: {DescribeError(error)}"));

if (!await TrySaveChangesAsync(context))
    return 1;
Console.WriteLine();

// =============================================================================
// 3. DEMONSTRATE VALIDATION (Railway Oriented Programming)
// =============================================================================
Console.WriteLine("🔒 Demonstrating Validation...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");

// Invalid email
Customer.TryCreate("Jane Doe", "not-an-email")
    .Tap(_ => Console.WriteLine("  ✓ Customer created"))
    .TapOnFailure(error => Console.WriteLine($"  ✗ Validation failed: {DescribeError(error)}"));

// Empty name
Customer.TryCreate("", "jane@example.com")
    .Tap(_ => Console.WriteLine("  ✓ Customer created"))
    .TapOnFailure(error => Console.WriteLine($"  ✗ Validation failed: {DescribeError(error)}"));

// Invalid product price
Product.TryCreate("Invalid Product", -10m, 5)
    .Tap(_ => Console.WriteLine("  ✓ Product created"))
    .TapOnFailure(error => Console.WriteLine($"  ✗ Validation failed: {DescribeError(error)}"));

Console.WriteLine();

// =============================================================================
// 4. CREATE ORDER (Using RequiredGuid with V7 - time-ordered!)
// =============================================================================
Console.WriteLine("🛒 Creating Order...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");

Order.TryCreate(customer.Id)
.Bind(order => order.AddLine(products[0], 1)) // MacBook
.Bind(order => order.AddLine(products[1], 2)) // 2x iPhone
.Bind(order => order.AddLine(products[2], 3)) // 3x AirPods
.Bind(order => order.Confirm())
.Tap(order =>
{
    context.Orders.Add(order);
    Console.WriteLine($"  ✓ Order Created and Confirmed!");
    Console.WriteLine($"             Order ID: {order.Id}");
    Console.WriteLine($"             Customer: {customer.Name} ({order.CustomerId})");
    Console.WriteLine($"             State: {order.State}");
    Console.WriteLine($"             Items: {order.Lines.Count}");
    Console.WriteLine($"             Total: ${order.Total:N2}");
    Console.WriteLine();
    Console.WriteLine("    Order Lines:");
    foreach (var line in order.Lines)
        Console.WriteLine($"      - {line.ProductName} x{line.Quantity} @ ${line.UnitPrice:N2} = ${line.LineTotal:N2}");
})
.TapOnFailure(error => Console.WriteLine($"  ✗ Failed: {DescribeError(error)}"));

if (!await TrySaveChangesAsync(context))
    return 1;
Console.WriteLine();

// =============================================================================
// 5. QUERY DATA (Demonstrating EF Core Integration)
// =============================================================================
Console.WriteLine("🔍 Querying Data from Database...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");

// Query customer by ID (using strongly-typed CustomerId)
var foundCustomer = await context.Customers.FindAsync(customer.Id);
Console.WriteLine($"  Found Customer: {foundCustomer?.Name} ({foundCustomer?.Email})");

// Query all orders with their lines
var orders = await context.Orders
    .Include(o => o.Lines)
    .ToListAsync();

Console.WriteLine($"  Total Orders: {orders.Count}");
foreach (var order in orders)
{
    Console.WriteLine($"    Order {order.Id}:");
    Console.WriteLine($"      State: {order.State}, Total: ${order.Total:N2}");
    Console.WriteLine($"      Created: {order.CreatedAt:yyyy-MM-dd HH:mm:ss}");
}

Console.WriteLine();

// =============================================================================
// 6. DEMONSTRATE GUID V7 ORDERING
// =============================================================================
Console.WriteLine("📊 Demonstrating GUID V7 Ordering (Time-based Sortability)...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");

// Create a few more orders to show GUID V7 ordering
for (int i = 0; i < 3; i++)
{
    await Task.Delay(10); // Small delay to ensure different timestamps
    Order.TryCreate(customer.Id)
        .Bind(o => o.AddLine(products[0], 1))
        .Tap(o => context.Orders.Add(o));
}

if (!await TrySaveChangesAsync(context))
    return 1;

// Query orders sorted by ID (GUID V7 sorts chronologically!)
var sortedOrders = await context.Orders
    .OrderBy(o => o.Id) // GUID V7 provides natural chronological ordering
    .Select(o => new { o.Id, o.CreatedAt, o.State })
    .ToListAsync();

Console.WriteLine("  Orders sorted by GUID V7 (natural chronological order):");
foreach (var o in sortedOrders)
{
    Console.WriteLine($"    {o.Id} - Created: {o.CreatedAt:HH:mm:ss.fff} - {o.State}");
}

Console.WriteLine();

// =============================================================================
// 7. REQUIRED ENUM DEMONSTRATION (State Machine with Behavior)
// =============================================================================
Console.WriteLine("🎯 RequiredEnum Demonstration (Order State Machine)...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");

// Demonstrate full order lifecycle with state transitions
Console.WriteLine("  Full order lifecycle demonstration...");
Order.TryCreate(customer.Id)
    .Bind(order => order.AddLine(products[0], 1))
    .Tap(order =>
    {
        Console.WriteLine($"    ✓ Order created in '{order.State}' state");
        Console.WriteLine($"      CanModify: {order.State.CanModify}");
        Console.WriteLine($"      CanCancel: {order.State.CanCancel}");
        Console.WriteLine($"      IsTerminal: {order.State.IsTerminal}");
        Console.WriteLine($"      Allowed transitions: {string.Join(", ", order.State.AllowedTransitions)}");
    })
    .Bind(order => order.Confirm())
    .Tap(order =>
    {
        Console.WriteLine($"    ✓ Order confirmed -> '{order.State}'");
        Console.WriteLine($"      CanModify: {order.State.CanModify}");
        Console.WriteLine($"      Allowed transitions: {string.Join(", ", order.State.AllowedTransitions)}");
    })
    .Bind(order => order.Ship())
    .Tap(order =>
    {
        Console.WriteLine($"    ✓ Order shipped -> '{order.State}'");
        Console.WriteLine($"      CanCancel: {order.State.CanCancel} (too late to cancel!)");
    })
    .Bind(order => order.Deliver())
    .Tap(order =>
    {
        Console.WriteLine($"    ✓ Order delivered -> '{order.State}'");
        Console.WriteLine($"      IsTerminal: {order.State.IsTerminal}");
        context.Orders.Add(order);
    })
    .TapOnFailure(error => Console.WriteLine($"    ✗ Failed: {DescribeError(error)}"));

if (!await TrySaveChangesAsync(context))
    return 1;
Console.WriteLine();

// Demonstrate invalid transitions
Console.WriteLine("  Testing invalid state transitions...");
Order.TryCreate(customer.Id)
    .Bind(order => order.AddLine(products[1], 1))
    .Bind(order =>
    {
        // Try to ship without confirming first - should fail!
        Console.WriteLine("    Trying to ship an unconfirmed order...");
        return order.Ship();
    })
    .Tap(_ => Console.WriteLine("    ✓ Unexpected success"))
    .TapOnFailure(error => Console.WriteLine($"    ✗ Correctly rejected: {DescribeError(error)}"));

Console.WriteLine();

// Demonstrate trying to cancel a shipped order
Order.TryCreate(customer.Id)
    .Bind(order => order.AddLine(products[2], 1))
    .Bind(order => order.Confirm())
    .Bind(order => order.Ship())
    .Bind(order =>
    {
        Console.WriteLine("    Trying to cancel a shipped order...");
        return order.Cancel();
    })
    .Tap(_ => Console.WriteLine("    ✓ Unexpected success"))
    .TapOnFailure(error => Console.WriteLine($"    ✗ Correctly rejected: {DescribeError(error)}"));

Console.WriteLine();

// Query Orders and show state is persisted
Console.WriteLine("  Querying Orders from database (showing state persistence)...");
var persistedOrders = await context.Orders.OrderByDescending(o => o.CreatedAt).Take(3).ToListAsync();
foreach (var o in persistedOrders)
{
    Console.WriteLine($"    Order {o.Id}: State={o.State}, CanCancel={o.State.CanCancel}");
}

Console.WriteLine();

// =============================================================================
// 8. TYPE SAFETY DEMONSTRATION
// =============================================================================
Console.WriteLine("🛡️  Type Safety Demonstration...");
Console.WriteLine("────────────────────────────────────────────────────────────────────");
Console.WriteLine("  The following would NOT compile:");
Console.WriteLine("    // OrderId orderId = CustomerId.NewUniqueV4();  // Error!");
Console.WriteLine("    // ProductId productId = OrderId.NewUniqueV7(); // Error!");
Console.WriteLine("    // context.Customers.Find(orderId);           // Error!");
Console.WriteLine("    // var status = (OrderState)999;              // Error! RequiredEnum prevents this");
Console.WriteLine();
Console.WriteLine("  This is the power of strongly-typed value objects:");
Console.WriteLine("    - Cannot accidentally mix OrderId with CustomerId");
Console.WriteLine("    - Cannot pass ProductId where CustomerId is expected");
Console.WriteLine("    - RequiredEnum prevents invalid enum values at compile/runtime");
Console.WriteLine("    - Compile-time safety prevents runtime bugs");
Console.WriteLine();

// =============================================================================
// SUMMARY
// =============================================================================
Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  Summary                                                          ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
Console.WriteLine("║  ✓ RequiredGuid<T>   - GUID identifiers (V4 random, V7 ordered)  ║");
Console.WriteLine("║  ✓ RequiredString<T> - Non-empty string validation               ║");
Console.WriteLine("║  ✓ EmailAddress      - RFC 5322 email validation                 ║");
Console.WriteLine("║  ✓ RequiredEnum<T>    - Type-safe enums with behavior            ║");
Console.WriteLine("║  ✓ EF Core           - Seamless persistence with converters      ║");
Console.WriteLine("║  ✓ ROP               - Railway Oriented Programming for errors   ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

return 0;

static async Task<bool> TrySaveChangesAsync(AppDbContext context)
{
    var result = await context.SaveChangesResultUnitAsync();
    result.TapOnFailure(error => Console.WriteLine($"  ✗ Save failed: {DescribeError(error)}"));
    return result.IsSuccess;
}

static string DescribeError(Error error) =>
    error switch
    {
        Error.UnprocessableContent validation when validation.Fields.Items.Length > 0 =>
            string.Join("; ", validation.Fields.Items.Select(field => field.Detail ?? field.ReasonCode)),
        Error.UnprocessableContent validation when validation.Rules.Items.Length > 0 =>
            string.Join("; ", validation.Rules.Items.Select(rule => rule.Detail ?? rule.ReasonCode)),
        _ => error.Detail ?? error.Code
    };
