using EcommerceExample.Aggregates;
using EcommerceExample.Services;
using EcommerceExample.Specifications;
using EcommerceExample.ValueObjects;
using EcommerceExample.Workflows;
using Trellis;
using Trellis.Primitives;

namespace EcommerceExample;

/// <summary>
/// Demonstrates a complete e-commerce order processing system using Railway Oriented Programming
/// and Domain-Driven Design patterns.
/// 
/// This example showcases:
/// - Value Objects (Money, OrderId, ProductId, CustomerId)
/// - Entities (OrderLine)
/// - Aggregates with Domain Events (Order)
/// - Domain Services (PaymentService, InventoryService, NotificationService)
/// - Complex Workflows with error handling
/// - recovery and retry logic
/// - Domain Events and Change Tracking (UncommittedEvents, AcceptChanges)
/// - Various Error Types (Validation, Domain, Conflict, NotFound, Unexpected)
/// - Specification Pattern for composable business rules
/// </summary>
public static class EcommerceExamples
{
    public static async Task RunExamplesAsync()
    {
        Console.WriteLine("=== E-Commerce Order Processing Examples ===");
        Console.WriteLine("=== Demonstrating FunctionalDDD Library ===\n");

        await Example1_SimpleOrderCreation();
        await Example2_CompleteOrderWorkflow();
        await Example3_PaymentFailureWithrecovery();
        await Example4_InsufficientInventory();
        await Example5_DomainEventsAndChangeTracking();
        Example6_SpecificationPatternFiltering();
    }

    /// <summary>
    /// Example 1: Simple order creation with validation and domain events.
    /// Demonstrates: Aggregate creation, domain events, ROP chaining
    /// </summary>
    private static async Task Example1_SimpleOrderCreation()
    {
        Console.WriteLine("Example 1: Simple Order Creation with Domain Events");
        Console.WriteLine("----------------------------------------------------");

        var customerId = CustomerId.NewUniqueV4();
        var productId = ProductId.NewUniqueV4();

        var result = Order.TryCreate(customerId)
            .Tap(order => Console.WriteLine($"Order created with {order.UncommittedEvents().Count} uncommitted event(s)"))
            .Bind(order => Money.TryCreate(29.99m, "USD")
                .Bind(price => order.AddLine(productId, "Wireless Mouse", price, 2)))
            .Tap(order => Console.WriteLine($"After adding line: {order.UncommittedEvents().Count} uncommitted event(s)"))
            .Bind(order => order.Submit())
            .Tap(order =>
            {
                Console.WriteLine($"After submit: {order.UncommittedEvents().Count} uncommitted event(s)");
                Console.WriteLine($"IsChanged: {order.IsChanged}");

                // Simulate saving to repository
                order.AcceptChanges();
                Console.WriteLine($"After AcceptChanges: {order.UncommittedEvents().Count} uncommitted event(s)");
            })
            .Match(
                onSuccess: ok => $"✅ Order created successfully with {ok.Lines.Count} items. Total: {ok.Total}",
                onFailure: err => $"❌ Order creation failed: {err.Detail}"
            );

        Console.WriteLine(result);
        Console.WriteLine();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Example 2: Complete order workflow with payment, inventory, and event publishing.
    /// Demonstrates: Workflow orchestration, event publishing pattern
    /// </summary>
    private static async Task Example2_CompleteOrderWorkflow()
    {
        Console.WriteLine("Example 2: Complete Order Workflow with Event Publishing");
        Console.WriteLine("---------------------------------------------------------");

        var paymentService = new PaymentService();
        var inventoryService = new InventoryService();
        var notificationService = new NotificationService();
        var workflow = new OrderWorkflow(paymentService, inventoryService, notificationService);

        var customerId = CustomerId.NewUniqueV4();
        var stock = inventoryService.GetAllStock();
        var product1 = stock.Keys.First();
        var product2 = stock.Keys.Skip(1).First();

        var items = new List<OrderLineRequest>
        {
            new(product1, "Laptop", Money.Create(999.99m, "USD"), 1),
            new(product2, "Mouse", Money.Create(29.99m, "USD"), 2)
        };

        var paymentInfo = new PaymentInfo("4111111111111111", "123", "John Doe");

        var result = await workflow.ProcessOrderAsync(customerId, items, paymentInfo)
            .MatchAsync(
                onSuccess: ok => $"✅ Order {ok.Id} processed successfully! Status: {ok.Status}, Total: {ok.Total}",
                onFailure: err => $"❌ Order processing failed: {err.Detail}"
            );

        Console.WriteLine(result);
        Console.WriteLine();
    }

    /// <summary>
    /// Example 3: Payment failure with recovery (retry logic).
    /// Demonstrates: RecoverOnFailureAsync, TapErrorAsync, error recovery
    /// </summary>
    private static async Task Example3_PaymentFailureWithrecovery()
    {
        Console.WriteLine("Example 3: Payment Failure with recovery");
        Console.WriteLine("---------------------------------------------");

        var paymentService = new PaymentService();
        var inventoryService = new InventoryService();
        var notificationService = new NotificationService();
        var workflow = new OrderWorkflow(paymentService, inventoryService, notificationService);

        var customerId = CustomerId.NewUniqueV4();
        var stock = inventoryService.GetAllStock();
        var product1 = stock.Keys.First();

        var items = new List<OrderLineRequest>
        {
            new(product1, "Gaming Console", Money.Create(499.99m, "USD"), 1)
        };

        // Card ending in 0000 will be declined
        var paymentInfo = new PaymentInfo("4111111111110000", "123", "Jane Doe");

        var result = await workflow.ProcessOrderAsync(customerId, items, paymentInfo)
            .MatchAsync(
                onSuccess: ok => $"✅ Order processed: {ok.Status}",
                onFailure: err => $"⚠️ Expected failure - Payment declined: {err.Detail}"
            );

        Console.WriteLine(result);
        Console.WriteLine();
    }

    /// <summary>
    /// Example 4: Insufficient inventory with recovery.
    /// Demonstrates: Error.Domain for business rule violations
    /// </summary>
    private static async Task Example4_InsufficientInventory()
    {
        Console.WriteLine("Example 4: Insufficient Inventory (Domain Error)");
        Console.WriteLine("-------------------------------------------------");

        var paymentService = new PaymentService();
        var inventoryService = new InventoryService();
        var notificationService = new NotificationService();
        var workflow = new OrderWorkflow(paymentService, inventoryService, notificationService);

        var customerId = CustomerId.NewUniqueV4();
        var stock = inventoryService.GetAllStock();
        var product1 = stock.Keys.First();

        // Try to order more than available
        var items = new List<OrderLineRequest>
        {
            new(product1, "Popular Item", Money.Create(99.99m, "USD"), 1000)
        };

        var paymentInfo = new PaymentInfo("4111111111111111", "123", "Bob Smith");

        var result = await workflow.ProcessOrderAsync(customerId, items, paymentInfo)
            .MatchAsync(
                onSuccess: ok => $"✅ Order processed: {ok.Status}",
                onFailure: err => $"⚠️ Expected failure - Insufficient inventory: {err.Detail}"
            );

        Console.WriteLine(result);
        Console.WriteLine();
    }

    /// <summary>
    /// Example 5: Deep dive into domain events and change tracking.
    /// Demonstrates: UncommittedEvents, AcceptChanges, IsChanged, event inspection
    /// </summary>
    private static async Task Example5_DomainEventsAndChangeTracking()
    {
        Console.WriteLine("Example 5: Domain Events and Change Tracking");
        Console.WriteLine("---------------------------------------------");

        var customerId = CustomerId.NewUniqueV4();
        var productId1 = ProductId.NewUniqueV4();
        var productId2 = ProductId.NewUniqueV4();

        // Create order - this raises OrderCreatedEvent
        var orderResult = Order.TryCreate(customerId);

        if (orderResult.IsFailure)
        {
            Console.WriteLine($"❌ Order creation failed: {orderResult.Error.Detail}");
            return;
        }

        var order = orderResult.Value;

        Console.WriteLine("After order creation:");
        Console.WriteLine($"  IsChanged: {order.IsChanged}");
        Console.WriteLine($"  Uncommitted events: {order.UncommittedEvents().Count}");
        PrintEvents(order);

        // Add lines - raises OrderLineAddedEvent for each
        var price1 = Money.Create(99.99m, "USD");
        var price2 = Money.Create(149.99m, "USD");

        order.AddLine(productId1, "Keyboard", price1, 1);
        order.AddLine(productId2, "Monitor", price2, 2);

        Console.WriteLine("\nAfter adding 2 lines:");
        Console.WriteLine($"  IsChanged: {order.IsChanged}");
        Console.WriteLine($"  Uncommitted events: {order.UncommittedEvents().Count}");
        Console.WriteLine($"  Order total: {order.Total}");
        PrintEvents(order);

        // Submit order - raises OrderSubmittedEvent
        order.Submit();

        Console.WriteLine("\nAfter submit:");
        Console.WriteLine($"  IsChanged: {order.IsChanged}");
        Console.WriteLine($"  Uncommitted events: {order.UncommittedEvents().Count}");
        Console.WriteLine($"  Order status: {order.Status}");
        PrintEvents(order);

        // Simulate saving to repository - accept changes
        Console.WriteLine("\nSimulating repository save (AcceptChanges)...");
        order.AcceptChanges();

        Console.WriteLine("\nAfter AcceptChanges:");
        Console.WriteLine($"  IsChanged: {order.IsChanged}");
        Console.WriteLine($"  Uncommitted events: {order.UncommittedEvents().Count}");

        // Process payment and confirm - new events after save
        order.ProcessPayment("TXN-12345");
        order.Confirm();

        Console.WriteLine("\nAfter payment and confirmation:");
        Console.WriteLine($"  IsChanged: {order.IsChanged}");
        Console.WriteLine($"  Uncommitted events: {order.UncommittedEvents().Count}");
        Console.WriteLine($"  Order status: {order.Status}");
        PrintEvents(order);

        // Demonstrate error types
        Console.WriteLine("\n--- Demonstrating Error Types ---");

        // Try to add line to confirmed order (Conflict error)
        var addResult = order.AddLine(productId1, "Another Item", price1, 1);
        if (addResult.IsFailure)
        {
            Console.WriteLine($"Error Type: {addResult.Error.GetType().Name}");
            Console.WriteLine($"Code: {addResult.Error.Code}");
            Console.WriteLine($"Detail: {addResult.Error.Detail}");
        }

        // Try to cancel confirmed order (Domain error)
        var cancelResult = order.Cancel("Changed my mind");
        if (cancelResult.IsFailure)
        {
            Console.WriteLine($"\nError Type: {cancelResult.Error.GetType().Name}");
            Console.WriteLine($"Code: {cancelResult.Error.Code}");
            Console.WriteLine($"Detail: {cancelResult.Error.Detail}");
        }

        Console.WriteLine();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Example 6: Specification pattern for composable business rules.
    /// Demonstrates: Specification, And/Or/Not composition, IsSatisfiedBy, IQueryable filtering
    /// </summary>
    private static void Example6_SpecificationPatternFiltering()
    {
        Console.WriteLine("Example 6: Specification Pattern — Composable Business Rules");
        Console.WriteLine("-------------------------------------------------------------");

        // --- Set up: create several orders in different states ---

        var customer1 = CustomerId.NewUniqueV4();
        var customer2 = CustomerId.NewUniqueV4();
        var productId = ProductId.NewUniqueV4();

        // Helper to create an order at a given status with a specific total
        Order CreateOrder(CustomerId cid, decimal amount, OrderStatus targetStatus)
        {
            var order = Order.Create(cid);
            order.AddLine(productId, "Widget", Money.Create(amount, "USD"), 1);

            if (targetStatus >= OrderStatus.Pending)
                order.Submit();
            if (targetStatus >= OrderStatus.PaymentProcessing)
                order.ProcessPayment($"TXN-{Guid.NewGuid():N}");
            if (targetStatus == OrderStatus.PaymentFailed)
                order.MarkPaymentFailed();
            if (targetStatus >= OrderStatus.Confirmed)
                order.Confirm();
            if (targetStatus >= OrderStatus.Shipped)
                order.MarkAsShipped();
            if (targetStatus >= OrderStatus.Delivered)
                order.MarkAsDelivered();
            if (targetStatus == OrderStatus.Cancelled)
            {
                // Reset to a cancellable state first (Draft)
                var fresh = Order.Create(cid);
                fresh.AddLine(productId, "Widget", Money.Create(amount, "USD"), 1);
                fresh.Cancel("No longer needed");
                return fresh;
            }

            return order;
        }

        var orders = new[]
        {
            CreateOrder(customer1,  49.99m, OrderStatus.Draft),           // cheap, draft
            CreateOrder(customer1, 599.99m, OrderStatus.Pending),         // expensive, pending
            CreateOrder(customer2, 199.99m, OrderStatus.Confirmed),       // mid-range, confirmed
            CreateOrder(customer2, 999.99m, OrderStatus.Shipped),         // high-value, shipped
            CreateOrder(customer1, 149.99m, OrderStatus.PaymentFailed),   // mid-range, payment failed
            CreateOrder(customer2,  29.99m, OrderStatus.Cancelled),       // cheap, cancelled
        };

        Console.WriteLine($"Total orders: {orders.Length}\n");

        // --- 1. Simple specification: find confirmed orders ---
        var confirmedSpec = new OrderStatusSpec(OrderStatus.Confirmed);
        var confirmedOrders = orders.Where(confirmedSpec.IsSatisfiedBy).ToList();
        Console.WriteLine($"Confirmed orders: {confirmedOrders.Count}");
        foreach (var o in confirmedOrders)
            Console.WriteLine($"  → {o.Id} | Total: {o.Total} | Status: {o.Status}");

        // --- 2. Composition with AND: high-value orders for customer1 ---
        var customer1HighValue = new CustomerOrderSpec(customer1)
            .And(new HighValueOrderSpec(100m));

        var customer1Expensive = orders.Where(customer1HighValue.IsSatisfiedBy).ToList();
        Console.WriteLine($"\nCustomer 1 high-value orders (>$100): {customer1Expensive.Count}");
        foreach (var o in customer1Expensive)
            Console.WriteLine($"  → {o.Id} | Total: {o.Total} | Status: {o.Status}");

        // --- 3. Composition with OR: find cancellable OR already cancelled ---
        var cancellableOrCancelled = new CancellableOrderSpec()
            .Or(new OrderStatusSpec(OrderStatus.Cancelled));

        var result = orders.Where(cancellableOrCancelled.IsSatisfiedBy).ToList();
        Console.WriteLine($"\nCancellable or already cancelled: {result.Count}");
        foreach (var o in result)
            Console.WriteLine($"  → {o.Id} | Status: {o.Status}");

        // --- 4. Composition with NOT: orders that are NOT high-value ---
        var budgetOrders = new HighValueOrderSpec(200m).Not();

        var affordable = orders.Where(budgetOrders.IsSatisfiedBy).ToList();
        Console.WriteLine($"\nBudget orders (≤$200): {affordable.Count}");
        foreach (var o in affordable)
            Console.WriteLine($"  → {o.Id} | Total: {o.Total}");

        // --- 5. Complex composition: (confirmed OR shipped) AND high-value ---
        var activeHighValue = new OrderStatusSpec(OrderStatus.Confirmed)
            .Or(new OrderStatusSpec(OrderStatus.Shipped))
            .And(new HighValueOrderSpec(100m));

        var vipOrders = orders.Where(activeHighValue.IsSatisfiedBy).ToList();
        Console.WriteLine($"\nActive high-value orders (confirmed/shipped, >$100): {vipOrders.Count}");
        foreach (var o in vipOrders)
            Console.WriteLine($"  → {o.Id} | Total: {o.Total} | Status: {o.Status}");

        // --- 6. IQueryable integration (simulating what a repository would do) ---
        var queryable = orders.AsQueryable();
        var shippedOrDelivered = new OrderStatusSpec(OrderStatus.Shipped)
            .Or(new OrderStatusSpec(OrderStatus.Delivered));

        // Use the implicit conversion to Expression<Func<Order, bool>>
        var fulfillmentQueue = queryable.Where(shippedOrDelivered.ToExpression()).ToList();
        Console.WriteLine($"\nFulfillment queue (IQueryable, shipped/delivered): {fulfillmentQueue.Count}");
        foreach (var o in fulfillmentQueue)
            Console.WriteLine($"  → {o.Id} | Status: {o.Status}");

        Console.WriteLine();
    }

    private static void PrintEvents(Order order)
    {
        var events = order.UncommittedEvents();
        foreach (var evt in events)
        {
            Console.WriteLine($"    - {evt.GetType().Name} at {evt.OccurredAt:HH:mm:ss.fff}");
        }
    }
}