# Trellis — Usage Patterns, Recipes & Known Issues

> Part of the [Trellis API Reference](.). Cross-cutting patterns that span multiple packages.

---

# Known Issues & Workarounds

## Trellis.Unit vs Mediator.Unit

Projects referencing both `Trellis.Results` and `Mediator` will encounter ambiguous `Unit` references. Both libraries define a `Unit` type.

```csharp
// Preferred: Use parameterless Result.Success() — avoids referencing Unit entirely
return Result.Success();  // instead of Result.Success(Unit.Value)

// Alternative: Using alias (if you need to reference Unit directly)
using Unit = Trellis.Unit;
```

---

# Usage Patterns & Recipes

## Full Program.cs Setup

Complete example showing MVC + Mediator + Auth + EF Core registration:

```csharp
using TodoSample.AntiCorruptionLayer;
using TodoSample.Api;
using TodoSample.Api.Middleware;
using TodoSample.Application;
using Scalar.AspNetCore;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPresentation(builder.Environment)   // MVC, versioning, OpenTelemetry, SLI, DevelopmentActorProvider
    .AddApplication()                        // Mediator + TrellisBehaviors
    .AddAntiCorruptionLayer(connectionString); // DbContext + repositories + ResourceAuthorization

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();
    app.MapScalarApiReference(...);
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseScalarValueValidation();             // Middleware for scalar value validation
app.UseMiddleware<ErrorHandlingMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
```

## Create a Custom Value Object (RequiredGuid ID)

Complete example showing how to define a strongly-typed GUID identifier using `RequiredGuid<TSelf>`.

```csharp
using Trellis;

public partial class OrderId : RequiredGuid<OrderId> { }

// Usage
var id = OrderId.NewUniqueV7();
var parsed = OrderId.TryCreate("550e8400-e29b-41d4-a716-446655440000");
```

## Create a Custom Value Object (RequiredString)

Complete example showing how to define a validated string value object with optional length constraints and custom validation.

```csharp
using Trellis;

public partial class FirstName : RequiredString<FirstName> { }

// Usage
var name = FirstName.Create("Alice");
var result = FirstName.TryCreate(userInput);
```

With length constraints:

```csharp
[StringLength(50)]
public partial class ProductName : RequiredString<ProductName> { }

[StringLength(500, MinimumLength = 10)]
public partial class Description : RequiredString<Description> { }
```

With custom validation (regex, format checks):

```csharp
[StringLength(10)]
public partial class Sku : RequiredString<Sku>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!Regex.IsMatch(value, @"^SKU-\d{6}$"))
            errorMessage = "Sku must match pattern SKU-XXXXXX.";
    }
}
```

## Create a Custom Value Object (RequiredEnum — Smart Enum)

Complete example showing how to define a smart enum with static members and case-insensitive parsing.

```csharp
using Trellis;

public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new();
    public static readonly OrderStatus Pending = new();
    public static readonly OrderStatus Confirmed = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Delivered = new();
    public static readonly OrderStatus Cancelled = new();
}

// Usage
var status = OrderStatus.Draft;
var all = OrderStatus.GetAll();
var parsed = OrderStatus.TryFromName("Pending");
if (status.Is(OrderStatus.Draft, OrderStatus.Pending)) { /* ... */ }
```

## Create a Custom ScalarValueObject with Custom Validation

Complete example showing how to create a value object with fully custom validation logic by implementing `IScalarValue<TSelf, TPrimitive>` directly.

```csharp
using Trellis;

public class Temperature : ScalarValueObject<Temperature, decimal>,
    IScalarValue<Temperature, decimal>
{
    private Temperature(decimal value) : base(value) { }

    public static Result<Temperature> TryCreate(decimal value, string? fieldName = null) =>
        value.ToResult()
            .Ensure(v => v >= -273.15m, Error.Validation("Below absolute zero", fieldName ?? "temperature"))
            .Map(v => new Temperature(v));

    // Create is inherited automatically from ScalarValueObject
}
```

## Define an Aggregate

Complete example showing how to define a DDD aggregate with domain methods, invariant enforcement, and domain event publishing.

```csharp
using Trellis;

public class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];
    public CustomerId CustomerId { get; }
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public Money Total { get; private set; }

    private Order(CustomerId customerId) : base(OrderId.NewUniqueV7())
    {
        CustomerId = customerId;
        Total = Money.Create(0m, "USD");
        DomainEvents.Add(new OrderCreated(Id, customerId, DateTime.UtcNow));
    }

    public static Result<Order> TryCreate(CustomerId customerId) =>
        Result.Success(new Order(customerId));

    public Result<Order> AddLine(ProductId productId, string name, Money price, int quantity) =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft, Error.Conflict("Cannot modify non-draft order"))
            .Bind(_ => OrderLine.TryCreate(productId, name, price, quantity))
            .Tap(line => _lines.Add(line))
            .Bind(_ => RecalculateTotal())
            .Map(_ => this);

    public Result<Order> Submit() =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft, Error.Conflict($"Cannot submit order in {Status} status"))
            .Ensure(_ => _lines.Count > 0, Error.Domain("Cannot submit empty order"))
            .Tap(_ =>
            {
                Status = OrderStatus.Pending;
                DomainEvents.Add(new OrderSubmitted(Id, DateTime.UtcNow));
            })
            .Map(_ => this);

    private Result<Unit> RecalculateTotal() =>
        _lines.Select(l => l.LineTotal)
            .Aggregate(Money.Zero("USD"), (acc, next) => acc.Bind(a => a.Add(next)))
            .Tap(total => Total = total)
            .Map(_ => Unit.Value);
}
```

## Build an ROP Pipeline

Complete example showing how to compose validation, transformation, and side effects using the Railway Oriented Programming pipeline.

```csharp
// Validation + transformation
var result = EmailAddress.TryCreate(dto.Email)
    .Combine(FirstName.TryCreate(dto.FirstName))
    .Combine(LastName.TryCreate(dto.LastName))
    .Bind((email, first, last) => CreateUser(email, first, last));

// Async pipeline with side effects
var result = await OrderId.TryCreate(request.OrderId)
    .BindAsync(id => _repository.GetByIdAsync(id, ct))
    .EnsureAsync(order => order.Status == OrderStatus.Draft, Error.Conflict("Order already submitted"))
    .BindAsync(order => order.Submit())
    .CheckAsync(order => _repository.SaveAsync(order, ct))
    .TapAsync(order => _eventBus.PublishAsync(order.UncommittedEvents(), ct));

// Recovery
var result = await ProcessPayment(order, paymentInfo)
    .RecoverOnFailureAsync(
        predicate: err => err is ServiceUnavailableError,
        funcAsync: () => RetryPaymentAsync(order, paymentInfo));
```

## Use Maybe\<T\> for Optional Fields

Complete example showing how to model optional fields with `Maybe<T>` in requests, validation, and EF Core persistence.

```csharp
public record CreateProfileRequest(
    string Email,
    string FirstName,
    string? MiddleName,    // optional
    string LastName,
    Url? Website           // optional value object
);

// Validation with Optional
var result = EmailAddress.TryCreate(dto.Email)
    .Combine(Maybe.Optional(dto.MiddleName, MiddleName.TryCreate))
    .Bind((email, middleName) => CreateProfile(email, middleName));

// EF Core persistence — use partial Maybe<T> property (see trellis-api-efcore.md Maybe<T> Property Mapping)
public partial class Profile
{
    public partial Maybe<Url> Website { get; set; }
}
// MaybeConvention auto-configures the backing field — no OnModelCreating needed
```

## Convert Result to HTTP Response

Complete example showing how to map `Result<T>` to HTTP responses in both MVC controllers and Minimal API endpoints.

```csharp
// MVC Controller
[HttpGet("{id}")]
public async Task<ActionResult<OrderDto>> GetOrder(OrderId id)
{
    return await _service.GetOrderAsync(id)
        .ToActionResultAsync(this, OrderDto.From);
}

[HttpPost]
public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request)
{
    return await _service.CreateOrderAsync(request)
        .ToCreatedAtActionResultAsync(this, nameof(GetOrder), dto => new { id = dto.Id }, OrderDto.From);
}

// Minimal API
app.MapGet("/orders/{id}", async (OrderId id, IOrderService service) =>
    await service.GetOrderAsync(id)
        .MapAsync(order => order.ToDto())
        .ToHttpResultAsync());
```

## HTTP Client → Result Pipeline

Complete example showing how to chain HTTP status handling and JSON deserialization into a `Result<T>` pipeline.

```csharp
var result = await _httpClient.GetAsync($"/api/orders/{id}", ct)
    .HandleNotFoundAsync(Error.NotFound($"Order {id} not found"))
    .HandleUnauthorizedAsync(Error.Unauthorized("Authentication required"))
    .EnsureSuccessAsync()
    .ReadResultFromJsonAsync(JsonContext.Default.OrderDto, ct);
```

## EF Core Integration

Complete example showing how to configure `DbContext` with Trellis conventions and implement a repository using Result-returning queries.

```csharp
// DbContext configuration
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
}

// Repository
public async Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken cancellationToken) =>
    await _dbContext.Orders
        .FirstOrDefaultResultAsync(o => o.Id == id, Error.NotFound($"Order {id} not found"), ct);

public async Task<Maybe<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken) =>
    await _dbContext.Orders.FirstOrDefaultMaybeAsync(o => o.Id == id, ct);

public async Task<Result<Unit>> SaveAsync(Order order, CancellationToken cancellationToken)
{
    _dbContext.Orders.Update(order);
    return await _dbContext.SaveChangesResultUnitAsync(ct);
}

// Specification queries
var highValueOrders = await _dbContext.Orders
    .Where(new HighValueOrderSpec(1000m).And(new OrderStatusSpec(OrderStatus.Confirmed)))
    .ToListAsync(ct);
```

#### Value Object LINQ Comparisons

In LINQ queries, compare value objects to value objects — the value converter registered by `ApplyTrellisConventions` handles SQL translation automatically.

```csharp
// ✅ Correct — value object to value object
var customer = await _dbContext.Customers
    .FirstOrDefaultResultAsync(c => c.Email == EmailAddress.Create("alice@example.com"), notFoundError, ct);

// ✅ Also correct (with ScalarValueQueryInterceptor registered via AddTrellisInterceptors)
// The interceptor rewrites .Value access to the primitive for SQL translation
var customer = await _dbContext.Customers
    .FirstOrDefaultResultAsync(c => c.Email.Value == "alice@example.com", notFoundError, ct);
```

## CQRS Command with Authorization

Complete example showing how to define a CQRS command with permission-based authorization, self-validation, and a handler using the ROP pipeline.

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record CreateOrderCommand(CustomerId CustomerId, List<OrderLineDto> Items)
    : ICommand<Result<Order>>, IAuthorize, IValidate
{
    // Permission-based authorization
    public IReadOnlyList<string> RequiredPermissions => ["Orders.Create"];

    // Self-validation
    public IResult Validate() =>
        Result.Ensure(Items.Count > 0, Error.Validation("At least one item required", "items"));
}

public sealed class CreateOrderHandler(IOrderRepository repo)
    : ICommandHandler<CreateOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(CreateOrderCommand command, CancellationToken cancellationToken) =>
        await Order.TryCreate(command.CustomerId)
            .BindAsync(order => AddItemsAsync(order, command.Items, ct))
            .BindAsync(order => order.Submit())
            .CheckAsync(order => repo.SaveAsync(order, ct));
}
```
