# Domain-Driven Design - Comprehensive Examples

This document provides detailed examples and patterns for using the Trellis.DomainDrivenDesign library.

## Table of Contents

- [Entity Examples](#entity-examples)
- [Value Object Examples](#value-object-examples)
  - [Address (Multi-Property)](#address-multi-property)
  - [Temperature (Scalar)](#temperature-scalar)
  - [Money (Domain Logic)](#money-domain-logic)
- [Aggregate Examples](#aggregate-examples)
  - [Order Aggregate](#order-aggregate)
  - [Order Line Entity](#order-line-entity)
- [Domain Events](#domain-events)
  - [Event Publishing](#event-publishing)
  - [Event Handlers](#event-handlers)
- [Integration Patterns](#integration-patterns)
  - [Service Layer](#service-layer)
  - [Multiple Results with Tuple Destructuring](#multiple-results-with-tuple-destructuring)
- [Specification Pattern](#specification-pattern)
  - [Basic Specification](#basic-specification)
  - [Composing Specifications](#composing-specifications)
  - [IQueryable Integration](#iqueryable-integration)
  - [Repository Pattern with Specifications](#repository-pattern-with-specifications)

---

## Entity Examples

### Complete Customer Entity

```csharp
// Define an entity ID using ScalarValueObject
public class CustomerId : ScalarValueObject<Guid>
{
    private CustomerId(Guid value) : base(value) { }
    
    public static CustomerId NewUnique() => new(Guid.NewGuid());
    
    public static Result<CustomerId> TryCreate(Guid? value) =>
        value.ToResult(Error.Validation("Customer ID cannot be empty"))
            .Ensure(v => v != Guid.Empty, Error.Validation("Customer ID cannot be empty"))
            .Map(v => new CustomerId(v));
}

// Define the entity
public class Customer : Entity<CustomerId>
{
    public string Name { get; private set; }
    public EmailAddress Email { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime? UpdatedAt { get; private set; }
    
    private Customer(CustomerId id, string name, EmailAddress email)
        : base(id)
    {
        Name = name;
        Email = email;
        CreatedAt = DateTime.UtcNow;
    }
    
    public static Result<Customer> TryCreate(string name, EmailAddress email) =>
        name.ToResult()
            .Ensure(n => !string.IsNullOrWhiteSpace(n), 
                   Error.Validation("Name cannot be empty"))
            .Map(n => new Customer(CustomerId.NewUniqueV7(), n, email));
    
    public Result<Customer> UpdateName(string newName) =>
        newName.ToResult()
            .Ensure(n => !string.IsNullOrWhiteSpace(n),
                   Error.Validation("Name cannot be empty"))
            .Tap(n =>
            {
                Name = n;
                UpdatedAt = DateTime.UtcNow;
            })
            .Map(_ => this);
    
    public Result<Customer> UpdateEmail(EmailAddress newEmail) =>
        newEmail.ToResult()
            .Tap(e =>
            {
                Email = e;
                UpdatedAt = DateTime.UtcNow;
            })
            .Map(_ => this);
}

// Usage
var email = EmailAddress.TryCreate("john@example.com");
var customer1 = Customer.TryCreate("John Doe", email.Value);
var customer2 = Customer.TryCreate("John Doe", email.Value);

// Different instances, different IDs, not equal
customer1 != customer2; // true

// Update operations
var updated = customer1.Value
    .UpdateName("John Smith")
    .Bind(c => c.UpdateEmail(EmailAddress.Create("john.smith@example.com")));
```

---

## Value Object Examples

### Address (Multi-Property)

Complete example of a composite value object with multiple properties:

```csharp
public class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string PostalCode { get; }
    public string Country { get; }
    
    private Address(string street, string city, string state, string postalCode, string country)
    {
        Street = street;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
    }
    
    public static Result<Address> TryCreate(
        string street, 
        string city, 
        string state, 
        string postalCode,
        string country) =>
        (street, city, state, postalCode, country).ToResult()
            .Ensure(x => !string.IsNullOrWhiteSpace(x.street), 
                   Error.Validation("Street is required", nameof(street)))
            .Ensure(x => !string.IsNullOrWhiteSpace(x.city),
                   Error.Validation("City is required", nameof(city)))
            .Ensure(x => !string.IsNullOrWhiteSpace(x.state),
                   Error.Validation("State is required", nameof(state)))
            .Ensure(x => !string.IsNullOrWhiteSpace(x.postalCode),
                   Error.Validation("Postal code is required", nameof(postalCode)))
            .Ensure(x => !string.IsNullOrWhiteSpace(x.country),
                   Error.Validation("Country is required", nameof(country)))
            .Map(x => new Address(x.street, x.city, x.state, x.postalCode, x.country));
    
    // Define equality components
    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
    }
    
    // Domain behavior
    public string GetFullAddress() => $"{Street}, {City}, {State} {PostalCode}, {Country}";
    
    public bool IsSameCity(Address other) =>
        City.Equals(other.City, StringComparison.OrdinalIgnoreCase) &&
        State.Equals(other.State, StringComparison.OrdinalIgnoreCase);
}

// Usage
var address1 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701", "USA");
var address2 = Address.TryCreate("123 Main St", "Springfield", "IL", "62701", "USA");
var address3 = Address.TryCreate("456 Oak Ave", "Springfield", "IL", "62702", "USA");

// Same values, equal
address1.Value == address2.Value; // true

// Different values, not equal
address1.Value == address3.Value; // false

// Domain behavior
address1.Value.IsSameCity(address3.Value); // true
```

### Temperature (Scalar)

Example of a scalar value object with domain logic:

```csharp
public class Temperature : ScalarValueObject<decimal>
{
    private Temperature(decimal value) : base(value) { }
    
    public static Result<Temperature> TryCreate(decimal value) =>
        value.ToResult()
            .Ensure(v => v >= -273.15m, 
                   Error.Validation("Temperature cannot be below absolute zero"))
            .Ensure(v => v <= 1_000_000m,
                   Error.Validation("Temperature exceeds physical limits"))
            .Map(v => new Temperature(v));
    
    public static Temperature FromCelsius(decimal celsius) => new(celsius);
    public static Temperature FromFahrenheit(decimal fahrenheit) => new((fahrenheit - 32) * 5 / 9);
    public static Temperature FromKelvin(decimal kelvin) => new(kelvin - 273.15m);
    
    // Custom equality - round to 2 decimal places
    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Math.Round(Value, 2);
    }
    
    // Domain operations
    public Temperature Add(Temperature other) => new(Value + other.Value);
    public Temperature Subtract(Temperature other) => new(Value - other.Value);
    
    public decimal ToCelsius() => Value;
    public decimal ToFahrenheit() => (Value * 9 / 5) + 32;
    public decimal ToKelvin() => Value + 273.15m;
    
    public bool IsAboveZero => Value > 0;
    public bool IsBelowZero => Value < 0;
    public bool IsFreezing => Value <= 0;
    public bool IsBoiling => Value >= 100;
}

// Usage
var temp1 = Temperature.TryCreate(98.6m);
var temp2 = Temperature.TryCreate(98.60m);
var tempF = Temperature.FromFahrenheit(98.6m);
var tempK = Temperature.FromKelvin(310.15m);

// Rounded to same value, equal
temp1.Value == temp2.Value; // true

// Implicit conversion to decimal
decimal celsius = temp1.Value; // 98.6m

// Domain operations
var difference = temp1.Value.Subtract(tempF);
var isHot = temp1.Value.IsAboveZero; // true
```

### Money (Domain Logic)

Example of a value object with rich domain behavior:

```csharp
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }
    
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }
    
    public static Result<Money> TryCreate(decimal amount, string currency = "USD") =>
        (amount, currency).ToResult()
            .Ensure(x => x.amount >= 0, 
                   Error.Validation("Amount cannot be negative", nameof(amount)))
            .Ensure(x => !string.IsNullOrWhiteSpace(x.currency),
                   Error.Validation("Currency is required", nameof(currency)))
            .Ensure(x => x.currency.Length == 3,
                   Error.Validation("Currency must be 3-letter ISO code", nameof(currency)))
            .Map(x => new Money(x.amount, x.currency.ToUpperInvariant()));
    
    public static Money Zero(string currency = "USD") => new(0, currency);
    
    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
    
    // Domain operations
    public Result<Money> Add(Money other) =>
        Currency != other.Currency
            ? Error.Validation($"Cannot add {other.Currency} to {Currency}")
            : new Money(Amount + other.Amount, Currency).ToResult();
    
    public Result<Money> Subtract(Money other) =>
        Currency != other.Currency
            ? Error.Validation($"Cannot subtract {other.Currency} from {Currency}")
            : Amount < other.Amount
                ? Error.Validation("Result would be negative")
                : new Money(Amount - other.Amount, Currency).ToResult();
    
    public Money Multiply(decimal factor) =>
        factor < 0
            ? throw new ArgumentException("Factor cannot be negative", nameof(factor))
            : new Money(Amount * factor, Currency);
    
    public Money Divide(decimal divisor) =>
        divisor <= 0
            ? throw new ArgumentException("Divisor must be positive", nameof(divisor))
            : new Money(Amount / divisor, Currency);
    
    public Money ApplyDiscount(decimal percentage) =>
        percentage is < 0 or > 100
            ? throw new ArgumentException("Percentage must be between 0 and 100", nameof(percentage))
            : new Money(Amount * (1 - percentage / 100), Currency);
    
    public bool IsZero => Amount == 0;
    public bool IsPositive => Amount > 0;
}

// Usage
var price = Money.TryCreate(100.00m, "USD");
var discount = price.Value.ApplyDiscount(10); // $90.00
var tax = discount.Multiply(0.08m); // $7.20
var total = discount.Add(tax); // $97.20

// Currency mismatch
var euros = Money.TryCreate(50.00m, "EUR");
var invalid = price.Value.Add(euros.Value); // Returns Error
```

---

## Aggregate Examples

### Order Aggregate

Complete implementation of an aggregate root with business rules:

```csharp
// Domain Events
public record OrderCreated(OrderId OrderId, CustomerId CustomerId, DateTime OccurredAt) : IDomainEvent;
public record OrderLineAdded(OrderId OrderId, ProductId ProductId, int Quantity, DateTime OccurredAt) : IDomainEvent;
public record OrderLineRemoved(OrderId OrderId, ProductId ProductId, DateTime OccurredAt) : IDomainEvent;
public record OrderSubmitted(OrderId OrderId, Money Total, DateTime OccurredAt) : IDomainEvent;
public record OrderCancelled(OrderId OrderId, string Reason, DateTime OccurredAt) : IDomainEvent;
public record OrderShipped(OrderId OrderId, DateTime OccurredAt) : IDomainEvent;

// Aggregate Root
public class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];
    
    public CustomerId CustomerId { get; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public Money Total { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    
    private Order(OrderId id, CustomerId customerId) : base(id)
    {
        CustomerId = customerId;
        Status = OrderStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        Total = Money.Create(0m, "USD");
        
        DomainEvents.Add(new OrderCreated(id, customerId, CreatedAt));
    }
    
    public static Result<Order> TryCreate(CustomerId customerId) =>
        customerId.ToResult()
            .Map(cid => new Order(OrderId.NewUniqueV7(), cid));
    
    public Result<Order> AddLine(ProductId productId, string productName, Money price, int quantity) =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft,
                   Error.Validation("Can only add items to draft orders"))
            .Ensure(_ => quantity > 0,
                   Error.Validation("Quantity must be positive", nameof(quantity)))
            .Ensure(_ => quantity <= 1000,
                   Error.Validation("Quantity cannot exceed 1000", nameof(quantity)))
            .Tap(_ =>
            {
                var existingLine = _lines.FirstOrDefault(l => l.ProductId == productId);
                if (existingLine != null)
                {
                    existingLine.UpdateQuantity(existingLine.Quantity + quantity);
                }
                else
                {
                    var line = new OrderLine(productId, productName, price, quantity);
                    _lines.Add(line);
                }
                RecalculateTotal();
                DomainEvents.Add(new OrderLineAdded(Id, productId, quantity, DateTime.UtcNow));
            });
    
    public Result<Order> RemoveLine(ProductId productId) =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft,
                   Error.Validation("Can only remove items from draft orders"))
            .Ensure(_ => _lines.Any(l => l.ProductId == productId),
                   Error.NotFound($"Product {productId} not found in order"))
            .Tap(_ =>
            {
                var line = _lines.First(l => l.ProductId == productId);
                _lines.Remove(line);
                RecalculateTotal();
                DomainEvents.Add(new OrderLineRemoved(Id, productId, DateTime.UtcNow));
            });
    
    public Result<Order> Submit() =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft,
                   Error.Validation("Can only submit draft orders"))
            .Ensure(_ => Lines.Count > 0,
                   Error.Validation("Cannot submit empty order"))
            .Ensure(_ => Total.Amount > 0,
                   Error.Validation("Order total must be positive"))
            .Tap(_ =>
            {
                Status = OrderStatus.Submitted;
                SubmittedAt = DateTime.UtcNow;
                DomainEvents.Add(new OrderSubmitted(Id, Total, SubmittedAt.Value));
            });
    
    public Result<Order> Ship() =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Submitted,
                   Error.Validation("Can only ship submitted orders"))
            .Tap(_ =>
            {
                Status = OrderStatus.Shipped;
                ShippedAt = DateTime.UtcNow;
                DomainEvents.Add(new OrderShipped(Id, ShippedAt.Value));
            });
    
    public Result<Order> Cancel(string reason) =>
        this.ToResult()
            .Ensure(_ => Status is OrderStatus.Draft or OrderStatus.Submitted,
                   Error.Validation("Can only cancel draft or submitted orders"))
            .Ensure(_ => !string.IsNullOrWhiteSpace(reason),
                   Error.Validation("Cancellation reason is required", nameof(reason)))
            .Tap(_ =>
            {
                Status = OrderStatus.Cancelled;
                CancelledAt = DateTime.UtcNow;
                DomainEvents.Add(new OrderCancelled(Id, reason, CancelledAt.Value));
            });
    
    private void RecalculateTotal()
    {
        var total = Lines.Sum(l => l.Price.Amount * l.Quantity);
        Total = Money.Create(total, Lines.FirstOrDefault()?.Price.Currency ?? "USD");
    }
}

public enum OrderStatus
{
    Draft,
    Submitted,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
```

### Order Line Entity

Entity within an aggregate:

```csharp
public class OrderLine : Entity<Guid>
{
    public ProductId ProductId { get; }
    public string ProductName { get; }
    public Money Price { get; }
    public int Quantity { get; private set; }
    public Money LineTotal => Price.Multiply(Quantity);
    
    public OrderLine(ProductId productId, string productName, Money price, int quantity)
        : base(Guid.NewGuid())
    {
        ProductId = productId;
        ProductName = productName;
        Price = price;
        Quantity = quantity;
    }
    
    public void UpdateQuantity(int newQuantity) =>
        Quantity = newQuantity > 0
            ? newQuantity
            : throw new ArgumentException("Quantity must be positive", nameof(newQuantity));
}
```

---

## Domain Events

### Event Publishing

Pattern for working with domain events:

```csharp
// Creating and publishing events
var order = Order.TryCreate(customerId)
    .Bind(o => o.AddLine(productId, "Widget", price, 5))
    .Bind(o => o.AddLine(productId2, "Gadget", price2, 3))
    .Bind(o => o.Submit());

if (order.IsSuccess)
{
    // Get uncommitted events
    var events = order.Value.UncommittedEvents();
    
    Console.WriteLine($"Generated {events.Count} events:");
    foreach (var evt in events)
    {
        Console.WriteLine($"  - {evt.GetType().Name}");
    }
    
    // Process events (e.g., publish to event bus, update read models)
    foreach (var evt in events)
    {
        await eventPublisher.PublishAsync(evt);
    }
    
    // Mark changes as committed
    order.Value.AcceptChanges();
    
    // Save to repository
    await orderRepository.SaveAsync(order.Value);
    
    // Verify no more uncommitted events
    order.Value.IsChanged; // false
}
```

### Event Handlers

Implementing handlers for domain events:

```csharp
public class OrderEventHandler
{
    private readonly IEmailService _emailService;
    private readonly IInventoryService _inventoryService;
    private readonly IShippingService _shippingService;
    private readonly ILogger<OrderEventHandler> _logger;
    
    public OrderEventHandler(
        IEmailService emailService,
        IInventoryService inventoryService,
        IShippingService shippingService,
        ILogger<OrderEventHandler> logger)
    {
        _emailService = emailService;
        _inventoryService = inventoryService;
        _shippingService = shippingService;
        _logger = logger;
    }
    
    public async Task Handle(OrderCreated evt)
    {
        _logger.LogInformation("Order {OrderId} created for customer {CustomerId}", 
            evt.OrderId, evt.CustomerId);
        
        // Could send draft order email, update analytics, etc.
    }
    
    public async Task Handle(OrderLineAdded evt)
    {
        _logger.LogInformation("Product {ProductId} added to order {OrderId}", 
            evt.ProductId, evt.OrderId);
        
        // Could update product view counts, recommendations, etc.
    }
    
    public async Task Handle(OrderSubmitted evt)
    {
        _logger.LogInformation("Order {OrderId} submitted with total {Total}", 
            evt.OrderId, evt.Total);
        
        // Reserve inventory
        await _inventoryService.ReserveAsync(evt.OrderId);
        
        // Send confirmation email
        await _emailService.SendOrderConfirmationAsync(evt.OrderId);
        
        // Notify fulfillment team
        await _shippingService.NotifyNewOrderAsync(evt.OrderId);
    }
    
    public async Task Handle(OrderShipped evt)
    {
        _logger.LogInformation("Order {OrderId} shipped", evt.OrderId);
        
        // Send shipping notification
        await _emailService.SendShippingNotificationAsync(evt.OrderId);
        
        // Update inventory
        await _inventoryService.CommitReservationAsync(evt.OrderId);
    }
    
    public async Task Handle(OrderCancelled evt)
    {
        _logger.LogInformation("Order {OrderId} cancelled: {Reason}", 
            evt.OrderId, evt.Reason);
        
        // Release inventory
        await _inventoryService.ReleaseAsync(evt.OrderId);
        
        // Send cancellation email
        await _emailService.SendOrderCancellationAsync(evt.OrderId, evt.Reason);
        
        // Refund payment if necessary
        // await _paymentService.RefundAsync(evt.OrderId);
    }
}
```

---

## Integration Patterns

### Service Layer

Orchestrating domain operations with Railway Oriented Programming:

```csharp
public class OrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OrderService> _logger;
    
    public async Task<Result<Order>> CreateAndProcessOrderAsync(
        CustomerId customerId,
        List<OrderItemRequest> items,
        PaymentInfo paymentInfo)
    {
        return await Order.TryCreate(customerId)
            // Add all items using Traverse - cleaner than Aggregate
            .Bind(order => items.Traverse(item => 
                order.AddLine(item.ProductId, item.ProductName, item.Price, item.Quantity))
                .Map(_ => order))
            // Validate inventory
            .BindAsync(order => ValidateInventoryAsync(order))
            // Submit order
            .Bind(order => order.Submit())
            // Process payment
            .BindAsync(order => ProcessPaymentAsync(order, paymentInfo))
            // Save to repository
            .TapAsync(order => _orderRepository.SaveAsync(order))
            // Publish domain events (only if aggregate has changes)
            .TapAsync(order => PublishEventsAsync(order));
    }
    
    private async Task<Result<Order>> ValidateInventoryAsync(Order order)
    {
        foreach (var line in order.Lines)
        {
            var available = await _inventoryService.IsAvailableAsync(
                line.ProductId, 
                line.Quantity);
            
            if (!available)
                return Error.Validation($"Insufficient inventory for {line.ProductName}");
        }
        
        return order;
    }
    
    private async Task<Result<Order>> ProcessPaymentAsync(
        Order order, 
        PaymentInfo paymentInfo)
    {
        var paymentResult = await _paymentService.ProcessAsync(
            order.Id,
            order.Total,
            paymentInfo);
        
        return paymentResult.IsSuccess
            ? order.ToResult()
            : Error.Validation($"Payment failed: {paymentResult.Error.Detail}");
    }
    
    private async Task PublishEventsAsync(Order order)
    {
        // Only publish if there are uncommitted events
        if (order.IsChanged)
        {
            _logger.LogInformation("Publishing {Count} events for order {OrderId}", 
                order.UncommittedEvents().Count, order.Id);
            
            foreach (var evt in order.UncommittedEvents())
            {
                await _eventBus.PublishAsync(evt);
            }
            
            order.AcceptChanges();
        }
    }
    
    public async Task<Result<Order>> CancelOrderAsync(
        OrderId orderId,
        string reason)
    {
        return await _orderRepository.GetByIdAsync(orderId)
            .ToResultAsync(Error.NotFound($"Order {orderId} not found"))
            .Bind(order => order.Cancel(reason))
            .TapAsync(order => _orderRepository.SaveAsync(order))
            .TapAsync(order => PublishEventsAsync(order));
    }
}
```

### Multiple Results with Tuple Destructuring

Handling multiple results with pattern matching:

```csharp
public class BulkOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IDiscountService _discountService;
    
    public async Task<Result<OrderConfirmation>> ProcessMultipleOrdersAsync(
        List<OrderRequest> requests)
    {
        // Validate minimum order count
        if (requests.Count < 3)
            return Error.Validation("Bulk orders require at least 3 orders");
        
        // Create multiple orders
        var orderResults = requests
            .Select(req => Order.TryCreate(req.CustomerId))
            .ToList();
        
        // Use Combine extension method with tuple destructuring
        return orderResults[0]
            .Combine(orderResults[1])
            .Combine(orderResults[2])
            .Match(
                // Success - all three orders created
                (order1, order2, order3) => ProcessOrders(order1, order2, order3),
                // Failure - at least one order failed
                error => error
            );
    }
    
    private Result<OrderConfirmation> ProcessOrders(Order order1, Order order2, Order order3)
    {
        var totalAmount = order1.Total.Add(order2.Total).Value.Add(order3.Total).Value;
        var discount = _discountService.CalculateBulkDiscount(totalAmount);
        var finalAmount = totalAmount.ApplyDiscount(discount);
        
        return new OrderConfirmation(
            [order1.Id, order2.Id, order3.Id],
            finalAmount,
            discount).ToResult();
    }
    
    public async Task<Result<Unit>> ProcessOrderBatchAsync(List<Order> orders)
    {
        // Process each order using Traverse - automatically fails if any order fails
        return await orders.TraverseAsync(order => order.Submit())
            .Map(_ => Unit.Value);
    }
}

public record OrderConfirmation(
    List<OrderId> OrderIds,
    Money TotalAmount,
    decimal DiscountPercentage);
```

---

## Additional Patterns

### Repository Pattern with Domain Events

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id);
    Task<List<Order>> GetByCustomerIdAsync(CustomerId customerId);
    Task SaveAsync(Order order);
}

public class OrderRepository : IOrderRepository
{
    private readonly DbContext _context;
    private readonly IEventBus _eventBus;
    
    public async Task SaveAsync(Order order)
    {
        // Save aggregate
        _context.Orders.Update(order);
        await _context.SaveChangesAsync();
        
        // Publish events after successful save
        if (order.IsChanged)
        {
            foreach (var evt in order.UncommittedEvents())
            {
                await _eventBus.PublishAsync(evt);
            }
            
            order.AcceptChanges();
        }
    }
}
```

### Unit of Work Pattern

```csharp
public class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _context;
    private readonly IEventBus _eventBus;
    private readonly List<IAggregate> _aggregates = [];
    
    public void RegisterAggregate(IAggregate aggregate) => _aggregates.Add(aggregate);
    
    public async Task<Result<Unit>> CommitAsync()
    {
        try
        {
            // Save all changes
            await _context.SaveChangesAsync();
            
            // Publish all events
            foreach (var aggregate in _aggregates.Where(a => a.IsChanged))
            {
                foreach (var evt in aggregate.UncommittedEvents())
                {
                    await _eventBus.PublishAsync(evt);
                }
                
                aggregate.AcceptChanges();
            }
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Error.Unexpected("Failed to commit unit of work", ex);
        }
    }
}
```

---

## Specification Pattern

Encapsulate business rules as composable, storage-agnostic expression trees that can be evaluated in-memory or translated to SQL by EF Core.

### Basic Specification

Define a specification by inheriting from `Specification<T>` and overriding `ToExpression()`:

```csharp
public class OverdueOrderSpec(DateTimeOffset now) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Status == OrderStatus.Submitted
              && order.SubmittedAt < now.AddDays(-30);
}

public class HighValueOrderSpec(decimal threshold) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.TotalAmount > threshold;
}

public class CustomerOrderSpec(CustomerId customerId) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.CustomerId == customerId;
}

public class CancellableOrderSpec : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Status == OrderStatus.Draft
              || order.Status == OrderStatus.Submitted;
}
```

Evaluate against an in-memory instance:

```csharp
var spec = new HighValueOrderSpec(500m);

if (spec.IsSatisfiedBy(order))
    Console.WriteLine("High-value order!");
```

### Composing Specifications

Combine specifications using `And`, `Or`, and `Not`:

```csharp
// Overdue AND high-value orders
var urgentHighValue = new OverdueOrderSpec(now)
    .And(new HighValueOrderSpec(500m));

// Orders that are either overdue OR high-value
var overdueOrExpensive = new OverdueOrderSpec(now)
    .Or(new HighValueOrderSpec(1000m));

// Orders that are NOT cancellable
var notCancellable = new CancellableOrderSpec().Not();

// Complex composition: overdue high-value orders for a specific customer
var spec = new OverdueOrderSpec(now)
    .And(new HighValueOrderSpec(500m))
    .And(new CustomerOrderSpec(customerId));

// Filter in-memory collections
var matchingOrders = orders.Where(spec.IsSatisfiedBy).ToList();
```

Specifications are immutable — composition always returns a new specification:

```csharp
var overdue = new OverdueOrderSpec(now);
var highValue = new HighValueOrderSpec(500m);
var combined = overdue.And(highValue);

// Original specifications are unchanged
assert overdue != combined;
assert highValue != combined;
```

### IQueryable Integration

Specifications convert implicitly to `Expression<Func<T, bool>>` for seamless LINQ integration:

```csharp
var spec = new OverdueOrderSpec(now).And(new HighValueOrderSpec(500m));

// Implicit conversion — expression tree translates to SQL
var orders = await dbContext.Orders
    .Where(spec)  // implicit operator converts Specification<T> to Expression
    .OrderByDescending(o => o.TotalAmount)
    .ToListAsync(ct);

// Explicit conversion also works
var expression = spec.ToExpression();
var orders2 = await dbContext.Orders
    .Where(expression)
    .ToListAsync(ct);
```

### Repository Pattern with Specifications

Define a repository interface that accepts specifications:

```csharp
public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(Specification<Order> spec, CancellationToken ct);
    Task<int> CountAsync(Specification<Order> spec, CancellationToken ct);
    Task<bool> AnyAsync(Specification<Order> spec, CancellationToken ct);
}

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;

    public OrderRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Order>> ListAsync(
        Specification<Order> spec, CancellationToken ct) =>
        await _db.Orders.Where(spec).ToListAsync(ct);

    public async Task<int> CountAsync(
        Specification<Order> spec, CancellationToken ct) =>
        await _db.Orders.Where(spec).CountAsync(ct);

    public async Task<bool> AnyAsync(
        Specification<Order> spec, CancellationToken ct) =>
        await _db.Orders.Where(spec).AnyAsync(ct);
}
```

Use in application code:

```csharp
public class OrderService
{
    private readonly IOrderRepository _repo;

    public async Task<IReadOnlyList<Order>> GetUrgentOrdersAsync(
        decimal valueThreshold, CancellationToken ct)
    {
        var spec = new OverdueOrderSpec(DateTimeOffset.UtcNow)
            .And(new HighValueOrderSpec(valueThreshold));

        return await _repo.ListAsync(spec, ct);
    }

    public async Task<int> CountCustomerOrdersAsync(
        CustomerId customerId, CancellationToken ct) =>
        await _repo.CountAsync(new CustomerOrderSpec(customerId), ct);
}
```

---

## Summary

These examples demonstrate:

- ✅ **Entity identity** with typed IDs
- ✅ **Value object immutability** and equality
- ✅ **Aggregate boundaries** and consistency
- ✅ **Domain events** for loose coupling
- ✅ **Specification pattern** for composable business rules
- ✅ **Railway Oriented Programming** integration
- ✅ **Rich domain models** with behavior
- ✅ **Repository patterns** with event publishing
- ✅ **Service layer** orchestration

For more information, see the [main README](README.md).
