# ASP Extension - Comprehensive Examples

> [!IMPORTANT]
> **This document is historical.** The sample
> code below uses legacy verbs (`ToActionResult`, `ToHttpResult`,
> `ToCreatedAtActionResult`, `ToCreatedAtRouteHttpResult`, `ToPaged*`,
> etc.) that were **removed** in v3. The single supported verb is now
> `result.ToHttpResponse(...)` / `result.ToHttpResponseAsync(...)`, with
> `.AsActionResult<T>()` for MVC typed signatures. See
> [`docs/docfx_project/articles/asp-tohttpresponse.md`](../docs/docfx_project/articles/asp-tohttpresponse.md)
> for the canonical guide and patterns; the snippets there
> supersede every example in this file.

This document provides detailed examples and advanced patterns for using the ASP extension with Railway Oriented Programming in ASP.NET Core applications.

## Table of Contents

- [MVC Controllers](#mvc-controllers)
  - [Basic CRUD Operations](#basic-crud-operations)
  - [Async Operations](#async-operations)
  - [Complex Validation Scenarios](#complex-validation-scenarios)
  - [Custom Status Codes](#custom-status-codes)
  - [Pagination Support](#pagination-support)
- [Minimal API](#minimal-api)
  - [Basic Endpoints](#basic-endpoints)
  - [Async Operations](#async-operations-1)
  - [Custom Status Codes](#custom-status-codes-in-minimal-api)
  - [Advanced Patterns](#advanced-patterns)
- [Error Handling Patterns](#error-handling-patterns)
  - [Validation Errors](#validation-errors)
  - [Authorization Errors](#authorization-errors)
  - [Not Found Handling](#not-found-handling)
  - [Conflict Resolution](#conflict-resolution)
- [Integration Patterns](#integration-patterns)
  - [Service Layer Integration](#service-layer-integration)
  - [Repository Pattern](#repository-pattern)
  - [Unit of Work](#unit-of-work)
- [Advanced Scenarios](#advanced-scenarios)
  - [Batch Operations](#batch-operations)
  - [Conditional Processing](#conditional-processing)
  - [Side Effects with Tap](#side-effects-with-tap)
- [Optional Value Objects with Maybe<T>](#optional-value-objects-with-maybet)

## MVC Controllers

### Basic CRUD Operations

Complete CRUD controller demonstrating all HTTP verbs:

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserRepository repository,
        IEmailService emailService,
        ILogger<UsersController> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _logger = logger;
    }

    // CREATE with 201 Created
    [HttpPost]
    public ActionResult<User> Create([FromBody] CreateUserRequest request) =>
        FirstName.TryCreate(request.FirstName)
            .Combine(LastName.TryCreate(request.LastName))
            .Combine(EmailAddress.TryCreate(request.Email))
            .Bind((firstName, lastName, email) => 
                User.TryCreate(firstName, lastName, email, request.Password))
            .Tap(user => _repository.Add(user))
            .ToCreatedAtActionResult(this,
                actionName: nameof(GetById),
                routeValues: user => new { id = user.Id });

    // READ single resource
    [HttpGet("{id}")]
    public ActionResult<User> GetById(string id) =>
        _repository.GetById(id)
            .ToResult(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
            .ToActionResult(this);

    // READ collection
    [HttpGet]
    public ActionResult<IEnumerable<User>> GetAll() =>
        _repository.GetAll()
            .ToActionResult(this);

    // UPDATE
    [HttpPut("{id}")]
    public ActionResult<User> Update(
        string id,
        [FromBody] UpdateUserRequest request) =>
        _repository.GetById(id)
            .ToResult(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
            .Bind(user => user.UpdateName(request.FirstName, request.LastName))
            .Bind(user => user.UpdateEmail(request.Email))
            .Tap(user => _repository.Update(user))
            .ToActionResult(this);

    // DELETE with 204 No Content
    [HttpDelete("{id}")]
    public ActionResult Delete(string id) =>
        _repository.GetById(id)
            .ToResult(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
            .Ensure(user => user.CanDelete, new Error.Conflict(null, "conflict") { Detail = "User has active orders" })
            .Tap(user => _repository.Delete(user))
            .Map(_ => Result.Ok())
            .ToActionResult(this);
}
```

### Async Operations

Best practices for async operations:

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    private readonly IShippingService _shippingService;
    private readonly IEmailService _emailService;

    // Async CREATE with side effects
    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrderAsync(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct) =>
        await ValidateOrderAsync(request, ct)
            .BindAsync(validatedRequest => _orderService.CreateAsync(validatedRequest, ct))
            .TapAsync(order => _inventoryService.ReserveAsync(order.Items, ct))
            .TapAsync(order => _emailService.SendOrderConfirmationAsync(order, ct))
            .ToActionResultAsync(this);

    // Async READ with complex validation
    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrderAsync(string id, CancellationToken ct) =>
        await _orderService.GetByIdAsync(id, ct)
            .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
            .EnsureAsync(
                order => CanAccessOrderAsync(User, order, ct),
                new Error.Forbidden("policy.id") { Detail = "Access denied to this order" })
            .ToActionResultAsync(this);

    // Async UPDATE with multiple steps
    [HttpPut("{id}/submit")]
    public async Task<ActionResult<Order>> SubmitOrderAsync(string id, CancellationToken ct) =>
        await _orderService.GetByIdAsync(id, ct)
            .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
            .BindAsync(order => order.SubmitAsync(ct))
            .BindAsync(order => _paymentService.ProcessAsync(order, ct))
            .TapAsync(order => _orderService.SaveAsync(order, ct))
            .TapAsync(order => _shippingService.ScheduleAsync(order, ct))
            .ToActionResultAsync(this);

    // Async DELETE with cleanup
    [HttpDelete("{id}")]
    public async Task<ActionResult> CancelOrderAsync(string id, CancellationToken ct) =>
        await _orderService.GetByIdAsync(id, ct)
            .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
            .EnsureAsync(
                order => order.CanCancelAsync(ct),
                new Error.Conflict(null, "conflict") { Detail = "Order cannot be cancelled" })
            .TapAsync(order => _inventoryService.ReleaseAsync(order.Items, ct))
            .TapAsync(order => _paymentService.RefundAsync(order, ct))
            .TapAsync(order => _orderService.DeleteAsync(order, ct))
            .Map(_ => Result.Ok())
            .ToActionResultAsync(this);

    private async Task<Result<CreateOrderRequest>> ValidateOrderAsync(
        CreateOrderRequest request,
        CancellationToken ct) =>
        await _inventoryService.CheckAvailabilityAsync(request.Items, ct)
            .EnsureAsync(
                available => Task.FromResult(available),
                new Error.Conflict(null, "conflict") { Detail = "Some items are out of stock" })
            .Map(_ => request);

    private async Task<bool> CanAccessOrderAsync(
        ClaimsPrincipal user,
        Order order,
        CancellationToken ct) =>
        await Task.FromResult(
            user.IsInRole("Admin") || 
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value == order.UserId);
}
```

### Complex Validation Scenarios

Handling multiple validation steps with clear error messages:

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductRepository _repository;
    private readonly ICategoryRepository _categoryRepository;

    [HttpPost]
    public ActionResult<Product> Create([FromBody] CreateProductRequest request) =>
        // Validate all value objects
        ProductName.TryCreate(request.Name)
            .Combine(ProductDescription.TryCreate(request.Description))
            .Combine(Price.TryCreate(request.Price))
            .Combine(Quantity.TryCreate(request.Quantity))
            // Validate category exists
            .Bind((name, description, price, quantity) =>
                _categoryRepository.GetById(request.CategoryId)
                    .ToResult(new Error.NotFound(ResourceRef.For("Category", request.CategoryId)) { Detail = $"Category {request.CategoryId} not found" })
                    .Map(category => (name, description, price, quantity, category)))
            // Validate unique product name
            .Ensure(
                tuple => !_repository.ExistsByName(tuple.name),
                new Error.Conflict(null, "conflict") { Detail = "A product with this name already exists" })
            // Create product
            .Bind(tuple => Product.TryCreate(
                tuple.name,
                tuple.description,
                tuple.price,
                tuple.quantity,
                tuple.category))
            // Save
            .Tap(product => _repository.Add(product))
            .ToCreatedAtActionResult(this,
                actionName: nameof(GetById),
                routeValues: product => new { id = product.Id });

    [HttpGet("{id}")]
    public ActionResult<Product> GetById(string id) =>
        _repository.GetById(id)
            .ToResult(new Error.NotFound(ResourceRef.For("Product", id)) { Detail = $"Product {id} not found" })
            .ToActionResult(this);

    [HttpPut("{id}/price")]
    public ActionResult<Product> UpdatePrice(
        string id,
        [FromBody] UpdatePriceRequest request) =>
        _repository.GetById(id)
            .ToResult(new Error.NotFound(ResourceRef.For("Product", id)) { Detail = $"Product {id} not found" })
            .Bind(product => Price.TryCreate(request.NewPrice)
                .Bind(newPrice => product.UpdatePrice(newPrice)))
            .Tap(product => _repository.Update(product))
            .ToActionResult(this);
}
```

### Custom Status Codes

> **Tip:** For 201 Created responses, prefer `ToCreatedAtActionResult` (MVC) or `ToCreatedAtRouteHttpResult` (Minimal API) — see CREATE examples above. Use `Finally` only for other custom status codes like 202 Accepted.

```csharp
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJobService _jobService;

    // 202 Accepted for async processing
    [HttpPost]
    public ActionResult<JobStatus> StartJob([FromBody] StartJobRequest request) =>
        _jobService.QueueJob(request)
            .Finally(
                ok => Accepted(new { jobId = ok.Id, status = "queued" }),
                err => err.ToErrorActionResult<JobStatus>(this));

    // 201 Created with Location header
    [HttpPost("batch")]
    public ActionResult<BatchJob> CreateBatch([FromBody] CreateBatchRequest request) =>
        _jobService.CreateBatch(request)
            .ToCreatedAtActionResult(this,
                actionName: nameof(GetBatchStatus),
                routeValues: batch => new { batchId = batch.Id });

    // 204 No Content for successful operation without content
    [HttpPost("{id}/cancel")]
    public ActionResult CancelJob(string id) =>
        _jobService.GetById(id)
            .ToResult(new Error.NotFound(ResourceRef.For("Job", id)) { Detail = $"Job {id} not found" })
            .Bind(job => job.Cancel())
            .Map(_ => Result.Ok())
            .ToActionResult(this);

    // Custom error codes
    [HttpPost("{id}/retry")]
    public ActionResult<Job> RetryJob(string id) =>
        _jobService.GetById(id)
            .ToResult(new Error.NotFound(ResourceRef.For("Job", id)) { Detail = $"Job {id} not found" })
            .Ensure(
                job => job.CanRetry,
                new Error.Conflict(null, "conflict") { Detail = "Job cannot be retried" })
            .Bind(job => job.Retry())
            .ToActionResult(this);

    [HttpGet("batch/{batchId}/status")]
    public ActionResult<BatchStatus> GetBatchStatus(string batchId) =>
        _jobService.GetBatchStatus(batchId)
            .ToResult(new Error.NotFound(ResourceRef.For("Batch", batchId)) { Detail = $"Batch {batchId} not found" })
            .ToActionResult(this);
}
```

### Pagination Support

RFC9110-compliant pagination with Content-Range headers:

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _repository;

    // Basic pagination
    [HttpGet]
    public async Task<ActionResult<IEnumerable<User>>> GetUsersAsync(
        [FromQuery] int? from = null,
        [FromQuery] int? to = null,
        CancellationToken ct = default)
    {
        var totalCount = await _repository.CountAsync(ct);
        var fromIndex = from ?? 0;
        var toIndex = to ?? Math.Min(fromIndex + 49, totalCount - 1);

        return await _repository.GetAllAsync(ct)
            .MapAsync(users => users
                .Skip(fromIndex)
                .Take(toIndex - fromIndex + 1))
            .ToActionResultAsync(this, toIndex, fromIndex, totalCount);
    }

    // Pagination with filtering
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<User>>> SearchUsersAsync(
        [FromQuery] string query,
        [FromQuery] int? from = null,
        [FromQuery] int? to = null,
        CancellationToken ct = default)
    {
        var filtered = await _repository.SearchAsync(query, ct);
        var totalCount = filtered.Count();
        var fromIndex = from ?? 0;
        var toIndex = to ?? Math.Min(fromIndex + 49, totalCount - 1);

        return filtered
            .Skip(fromIndex)
            .Take(toIndex - fromIndex + 1)
            .ToResult()
            .ToActionResult(this, toIndex, fromIndex, totalCount);
    }

    // Pagination with sorting
    [HttpGet("sorted")]
    public async Task<ActionResult<IEnumerable<User>>> GetSortedUsersAsync(
        [FromQuery] string sortBy = "name",
        [FromQuery] bool descending = false,
        [FromQuery] int? from = null,
        [FromQuery] int? to = null,
        CancellationToken ct = default)
    {
        var sorted = sortBy.ToLower() switch
        {
            "email" => descending
                ? (await _repository.GetAllAsync(ct)).OrderByDescending(u => u.Email)
                : (await _repository.GetAllAsync(ct)).OrderBy(u => u.Email),
            "created" => descending
                ? (await _repository.GetAllAsync(ct)).OrderByDescending(u => u.CreatedAt)
                : (await _repository.GetAllAsync(ct)).OrderBy(u => u.CreatedAt),
            _ => descending
                ? (await _repository.GetAllAsync(ct)).OrderByDescending(u => u.Name)
                : (await _repository.GetAllAsync(ct)).OrderBy(u => u.Name)
        };

        var totalCount = sorted.Count();
        var fromIndex = from ?? 0;
        var toIndex = to ?? Math.Min(fromIndex + 49, totalCount - 1);

        return sorted
            .Skip(fromIndex)
            .Take(toIndex - fromIndex + 1)
            .ToResult()
            .ToActionResult(this, toIndex, fromIndex, totalCount);
    }
}
```

**Response Headers Example:**
- Partial: `206 Partial Content` with `Content-Range: items 0-49/1000`
- Complete: `200 OK` with no Content-Range header

## Minimal API

### Basic Endpoints

Simple CRUD operations using Minimal API:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var userApi = app.MapGroup("/api/users");

// CREATE
userApi.MapPost("/", (
    CreateUserRequest request,
    IUserRepository repository) =>
    FirstName.TryCreate(request.FirstName)
        .Combine(LastName.TryCreate(request.LastName))
        .Combine(EmailAddress.TryCreate(request.Email))
        .Bind((firstName, lastName, email) => 
            User.TryCreate(firstName, lastName, email, request.Password))
        .Tap(user => repository.Add(user))
        .ToCreatedAtRouteHttpResult(
            routeName: "GetUserById",
            routeValues: user => new RouteValueDictionary(new { id = user.Id })));

// READ
userApi.MapGet("/{id}", (
    string id,
    IUserRepository repository) =>
    repository.GetById(id)
        .ToResult(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
        .ToHttpResult());

// UPDATE
userApi.MapPut("/{id}", (
    string id,
    UpdateUserRequest request,
    IUserRepository repository) =>
    repository.GetById(id)
        .ToResult(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
        .Bind(user => user.UpdateName(request.FirstName, request.LastName))
        .Bind(user => user.UpdateEmail(request.Email))
        .Tap(user => repository.Update(user))
        .ToHttpResult());

// DELETE
userApi.MapDelete("/{id}", (
    string id,
    IUserRepository repository) =>
    repository.GetById(id)
        .ToResult(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
        .Ensure(user => user.CanDelete, new Error.Conflict(null, "conflict") { Detail = "User has active orders" })
        .Tap(user => repository.Delete(user))
        .Map(_ => Result.Ok())
        .ToHttpResult());

app.Run();
```

### Async Operations

Async endpoints:

```csharp
var orderApi = app.MapGroup("/api/orders");

// Async CREATE
orderApi.MapPost("/", async (
    CreateOrderRequest request,
    IOrderService orderService,
    IInventoryService inventoryService,
    IEmailService emailService,
    CancellationToken ct) =>
    await orderService.CreateAsync(request, ct)
        .TapAsync(order => inventoryService.ReserveAsync(order.Items, ct))
        .TapAsync(order => emailService.SendConfirmationAsync(order, ct))
        .ToHttpResultAsync());

// Async READ
orderApi.MapGet("/{id}", async (
    string id,
    IOrderService orderService,
    CancellationToken ct) =>
    await orderService.GetByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
        .ToHttpResultAsync());

// Async UPDATE
orderApi.MapPut("/{id}/submit", async (
    string id,
    IOrderService orderService,
    IPaymentService paymentService,
    CancellationToken ct) =>
    await orderService.GetByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
        .BindAsync(order => order.SubmitAsync(ct))
        .BindAsync(order => paymentService.ProcessAsync(order, ct))
        .TapAsync(order => orderService.SaveAsync(order, ct))
        .ToHttpResultAsync());

// Async DELETE
orderApi.MapDelete("/{id}", async (
    string id,
    IOrderService orderService,
    IInventoryService inventoryService,
    IPaymentService paymentService,
    CancellationToken ct) =>
    await orderService.GetByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
        .TapAsync(order => inventoryService.ReleaseAsync(order.Items, ct))
        .TapAsync(order => paymentService.RefundAsync(order, ct))
        .TapAsync(order => orderService.DeleteAsync(order, ct))
        .Map(_ => Result.Ok())
        .ToHttpResultAsync());
```

### Custom Status Codes in Minimal API

> **Tip:** For 201 Created responses, prefer `ToCreatedAtRouteHttpResult` — see CREATE examples above. Use `Finally` only for other custom status codes like 202 Accepted.

```csharp
var productApi = app.MapGroup("/api/products");

// 201 Created with Location
productApi.MapPost("/", (
    CreateProductRequest request,
    IProductRepository repository) =>
    ProductName.TryCreate(request.Name)
        .Combine(Price.TryCreate(request.Price))
        .Bind((name, price) => Product.TryCreate(name, price))
        .Tap(product => repository.Add(product))
        .ToCreatedAtRouteHttpResult(
            routeName: "GetProductById",
            routeValues: product => new RouteValueDictionary(new { id = product.Id })))
    .WithName("GetProductById");

// 202 Accepted
productApi.MapPost("/import", (
    ImportRequest request,
    IImportService importService) =>
    importService.QueueImport(request)
        .Finally(
            ok => Results.Accepted($"/api/import/{ok.JobId}", ok),
            err => err.ToErrorResult()));

// 204 No Content
productApi.MapDelete("/{id}", (
    string id,
    IProductRepository repository) =>
    repository.Delete(id)
        .Map(_ => Result.Ok())
        .Finally(
            _ => Results.NoContent(),
            err => err.ToErrorResult()));

// Custom validation response
productApi.MapPost("/validate", (
    ValidateProductRequest request,
    IProductValidator validator) =>
    validator.Validate(request)
        .Finally(
            ok => Results.Ok(new { valid = true, product = ok }),
            err => err.ToErrorResult()));
```

### Advanced Patterns

Complex orchestration in Minimal API:

```csharp
// Batch processing with Traverse
var batchApi = app.MapGroup("/api/batch");

batchApi.MapPost("/users", async (
    List<CreateUserRequest> requests,
    IUserRepository repository,
    CancellationToken ct) =>
    await requests.TraverseAsync(
        async (request, cancellationToken) =>
            await FirstName.TryCreate(request.FirstName)
                .Combine(LastName.TryCreate(request.LastName))
                .Combine(EmailAddress.TryCreate(request.Email))
                .BindAsync(async (firstName, lastName, email) =>
                    await User.TryCreateAsync(firstName, lastName, email, request.Password, cancellationToken))
                .TapAsync(user => repository.AddAsync(user, cancellationToken)),
        ct)
    .ToHttpResultAsync());

// Complex validation chain
var validationApi = app.MapGroup("/api/validate");

validationApi.MapPost("/order", async (
    CreateOrderRequest request,
    IInventoryService inventoryService,
    ICustomerService customerService,
    IPricingService pricingService,
    CancellationToken ct) =>
    await customerService.ValidateCustomerAsync(request.CustomerId, ct)
        .BindAsync(async customer => 
            (await inventoryService.ValidateItemsAsync(request.Items, ct))
                .Map(_ => customer))
        .BindAsync(async customer =>
            (await pricingService.CalculateTotalAsync(request.Items, ct))
                .Map(total => (customer, total)))
        .Map(tuple => new OrderValidationResult(
            tuple.customer,
            tuple.total,
            IsValid: true))
        .ToHttpResultAsync());

// Conditional processing
var processingApi = app.MapGroup("/api/process");

processingApi.MapPost("/order/{id}/complete", async (
    string id,
    IOrderService orderService,
    IPaymentService paymentService,
    IShippingService shippingService,
    CancellationToken ct) =>
    await orderService.GetByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
        .BindAsync(async order =>
        {
            if (order.RequiresPayment)
                return await paymentService.ProcessAsync(order, ct);
            return order.ToResult();
        })
        .BindAsync(async order =>
        {
            if (order.RequiresShipping)
                return await shippingService.ScheduleAsync(order, ct);
            return order.ToResult();
        })
        .TapAsync(order => orderService.SaveAsync(order, ct))
        .ToHttpResultAsync());
```

## Error Handling Patterns

### Validation Errors

Collecting multiple validation errors:

```csharp
[HttpPost]
public ActionResult<Order> CreateOrder([FromBody] CreateOrderRequest request) =>
    // Combine all validations - collects ALL errors
    CustomerId.TryCreate(request.CustomerId)
        .Combine(ShippingAddress.TryCreate(request.ShippingAddress))
        .Combine(BillingAddress.TryCreate(request.BillingAddress))
        .Combine(request.Items.Traverse(item =>
            ProductId.TryCreate(item.ProductId)
                .Combine(Quantity.TryCreate(item.Quantity))
                .Combine(Price.TryCreate(item.Price))
                .Bind((productId, quantity, price) =>
                    OrderLine.TryCreate(productId, quantity, price))))
        .Bind((customerId, shipping, billing, items) =>
            Order.TryCreate(customerId, shipping, billing, items))
        .ToActionResult(this);
```

**Validation Error Response:**
```json
{
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "One or more validation errors occurred.",
    "status": 400,
    "errors": {
        "customerId": ["Customer ID is required"],
        "shippingAddress.zipCode": ["Invalid ZIP code format"],
        "items[0].quantity": ["Quantity must be positive"],
        "items[2].price": ["Price must be greater than zero"]
    }
}
```

### Authorization Errors

Handling authentication and authorization:

```csharp
[HttpGet("{id}")]
[Authorize]
public async Task<ActionResult<Order>> GetOrderAsync(string id, CancellationToken ct) =>
    await _orderService.GetByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
        // Check ownership
        .EnsureAsync(
            order => IsOwnerAsync(User, order, ct),
            new Error.Forbidden("policy.id") { Detail = "You don't have permission to view this order" })
        // Check subscription level
        .EnsureAsync(
            order => HasRequiredSubscriptionAsync(User, order, ct),
            new Error.Forbidden("policy.id") { Detail = "Your subscription level doesn't allow viewing this order type" })
        .ToActionResultAsync(this);

private async Task<bool> IsOwnerAsync(
    ClaimsPrincipal user,
    Order order,
    CancellationToken ct)
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    return await Task.FromResult(
        user.IsInRole("Admin") || userId == order.UserId);
}

private async Task<bool> HasRequiredSubscriptionAsync(
    ClaimsPrincipal user,
    Order order,
    CancellationToken ct)
{
    var subscription = await _subscriptionService.GetUserSubscriptionAsync(
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value, ct);
    return subscription.Level >= order.RequiredSubscriptionLevel;
}
```

### Not Found Handling

Consistent 404 handling patterns:

```csharp
[HttpGet("{id}")]
public ActionResult<Product> GetProduct(string id) =>
    _repository.GetById(id)
        .ToResult(new Error.NotFound(ResourceRef.For("Product", id)) { Detail = $"Product {id} not found" })
        .ToActionResult(this);

[HttpGet("category/{categoryId}/products")]
public ActionResult<IEnumerable<Product>> GetProductsByCategory(string categoryId) =>
    _categoryRepository.GetById(categoryId)
        .ToResult(new Error.NotFound(ResourceRef.For("Category", categoryId)) { Detail = $"Category {categoryId} not found" })
        .Bind(category => _productRepository.GetByCategory(category))
        .Ensure(
            products => products.Any(),
            new Error.NotFound(ResourceRef.For("Category", categoryId)) { Detail = $"No products found in category {categoryId}" })
        .ToActionResult(this);

// Multiple resources
[HttpGet("order/{orderId}/items/{itemId}")]
public ActionResult<OrderItem> GetOrderItem(string orderId, string itemId) =>
    _orderRepository.GetById(orderId)
        .ToResult(new Error.NotFound(ResourceRef.For("Order", orderId)) { Detail = $"Order {orderId} not found" })
        .Bind(order => order.GetItem(itemId)
            .ToResult(new Error.NotFound(ResourceRef.For("OrderItem", itemId)) { Detail = $"Item {itemId} not found in order {orderId}" }))
        .ToActionResult(this);
```

### Conflict Resolution

Handling 409 Conflict errors:

```csharp
[HttpPost]
public ActionResult<User> Register([FromBody] RegisterRequest request) =>
    EmailAddress.TryCreate(request.Email)
        .Ensure(
            email => !_userRepository.ExistsByEmail(email),
            new Error.Conflict(null, "conflict") { Detail = "A user with this email already exists" })
        .Bind(email => User.TryCreate(
            request.FirstName,
            request.LastName,
            email,
            request.Password))
        .ToActionResult(this);

[HttpPut("{id}/email")]
public async Task<ActionResult<User>> UpdateEmailAsync(
    string id,
    [FromBody] UpdateEmailRequest request) =>
    await _userRepository.GetByIdAsync(id)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
        .BindAsync(async user =>
            await EmailAddress.TryCreate(request.NewEmail)
                .EnsureAsync(
                    async email => !await _userRepository.ExistsByEmailAsync(email),
                    new Error.Conflict(null, "conflict") { Detail = "Email already in use" })
                .BindAsync(email => user.UpdateEmailAsync(email)))
        .TapAsync(user => _userRepository.SaveAsync(user))
        .ToActionResultAsync(this);

[HttpDelete("{id}")]
public ActionResult DeleteUser(string id) =>
    _userRepository.GetById(id)
        .ToResult(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
        .Ensure(
            user => !user.HasActiveOrders,
            new Error.Conflict(null, "conflict") { Detail = "Cannot delete user with active orders" })
        .Ensure(
            user => !user.HasPendingPayments,
            new Error.Conflict(null, "conflict") { Detail = "Cannot delete user with pending payments" })
        .Tap(user => _userRepository.Delete(user))
        .Map(_ => Result.Ok())
        .ToActionResult(this);
```

## Integration Patterns

### Service Layer Integration

Clean separation between API and service layers:

```csharp
// Service Layer
public class OrderService : IOrderService
{
    private readonly IOrderRepository _repository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInventoryService _inventoryService;
    private readonly IDomainEventDispatcher _eventDispatcher;

    public async Task<Result<Order>> CreateAsync(CreateOrderRequest request, CancellationToken ct) =>
        await ValidateCustomerAsync(request.CustomerId, ct)
            .BindAsync(async customer => 
                (await CreateOrderLinesAsync(request.Items, ct))
                    .Map(items => (customer, items)))
            .BindAsync(async tuple => 
                await Order.TryCreateAsync(tuple.customer.Id, tuple.items, ct))
            .TapAsync(order => _repository.AddAsync(order, ct))
            .TapAsync(order => _eventDispatcher.DispatchAsync(order.DomainEvents, ct));

    private async Task<Result<Customer>> ValidateCustomerAsync(string customerId, CancellationToken ct) =>
        await _customerRepository.GetByIdAsync(customerId, ct)
            .ToResultAsync(new Error.NotFound(ResourceRef.For("Customer", customerId)) { Detail = $"Customer {customerId} not found" })
            .EnsureAsync(
                customer => customer.IsActiveAsync(ct),
                new Error.Conflict(null, "conflict") { Detail = "Customer account is inactive" });

    private async Task<Result<IEnumerable<OrderLine>>> CreateOrderLinesAsync(
        IEnumerable<OrderLineRequest> requests,
        CancellationToken ct) =>
        await requests.TraverseAsync(
            async (request, cancellationToken) =>
                await _inventoryService.GetProductAsync(request.ProductId, cancellationToken)
                    .ToResultAsync(new Error.NotFound(ResourceRef.For("Product", request.ProductId)) { Detail = $"Product {request.ProductId} not found" })
                    .BindAsync(product => 
                        OrderLine.TryCreateAsync(
                            product.Id,
                            product.Name,
                            product.Price,
                            request.Quantity,
                            cancellationToken)),
            ct);
}

// Controller Layer
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrderAsync(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct) =>
        await _orderService.CreateAsync(request, ct)
            .ToActionResultAsync(this);

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrderAsync(string id, CancellationToken ct) =>
        await _orderService.GetByIdAsync(id, ct)
            .ToActionResultAsync(this);
}
```

### Repository Pattern

Repository returning Result<T>:

```csharp
public class UserRepository : IUserRepository
{
    private readonly DbContext _context;

    public Result<User> GetById(string id)
    {
        var user = _context.Users.Find(id);
        return user is not null
            ? user.ToResult()
            : new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" };
    }

    public async Task<Result<User>> GetByIdAsync(string id, CancellationToken ct)
    {
        var user = await _context.Users.FindAsync(new object[] { id }, ct);
        return user is not null
            ? user.ToResult()
            : new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" };
    }

    public async Task<Result<User>> GetByEmailAsync(EmailAddress email, CancellationToken ct)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        return user is not null
            ? user.ToResult()
            : new Error.NotFound(ResourceRef.For("User", email)) { Detail = $"User with email {email} not found" };
    }

    public async Task<Result<IEnumerable<User>>> GetAllAsync(CancellationToken ct) =>
        (await _context.Users.ToListAsync(ct)).ToResult();

    public async Task<Result> AddAsync(User user, CancellationToken ct)
    {
        try
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (DbUpdateException ex)
        {
            return new Error.InternalServerError("fault-id") { Detail = "Failed to add user" };
        }
    }
}
```

### Unit of Work

Transactional operations with UnitOfWork:

```csharp
public class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _context;
    private int _scopeDepth;

    public async Task<Result> CommitAsync(CancellationToken ct)
    {
        // Defer until the outermost scope unwinds so a nested command's success
        // doesn't commit a partially-completed outer command's staged changes.
        if (Volatile.Read(ref _scopeDepth) > 1)
            return Result.Ok();

        try
        {
            await _context.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return new Error.Conflict(null, "conflict") { Detail = "Concurrency conflict detected" };
        }
        catch (DbUpdateException ex)
        {
            return new Error.InternalServerError("fault-id") { Detail = "Database update failed" };
        }
    }

    public IDisposable BeginScope()
    {
        Interlocked.Increment(ref _scopeDepth);
        return new ScopeReleaser(this);
    }

    private sealed class ScopeReleaser : IDisposable
    {
        private readonly UnitOfWork _owner;
        private bool _disposed;

        public ScopeReleaser(UnitOfWork owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Decrement(ref _owner._scopeDepth);
        }
    }
}

// Usage in controller
[HttpPost("transfer")]
public async Task<ActionResult<TransferResult>> TransferFundsAsync(
    [FromBody] TransferRequest request,
    CancellationToken ct) =>
    await _accountRepository.GetByIdAsync(request.FromAccountId, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Account", request.FromAccountId)) { Detail = $"Account {request.FromAccountId} not found" })
        .BindAsync(async fromAccount =>
            (await _accountRepository.GetByIdAsync(request.ToAccountId, ct))
                .ToResult(new Error.NotFound(ResourceRef.For("Account", request.ToAccountId)) { Detail = $"Account {request.ToAccountId} not found" })
                .Map(toAccount => (fromAccount, toAccount)))
        .BindAsync(async tuple =>
            (await tuple.fromAccount.WithdrawAsync(request.Amount, ct))
                .BindAsync(_ => tuple.toAccount.DepositAsync(request.Amount, ct))
                .Map(_ => (tuple.fromAccount, tuple.toAccount)))
        .BindAsync(async tuple => 
            (await _unitOfWork.CommitAsync(ct))
                .Map(_ => new TransferResult(tuple.fromAccount.Id, tuple.toAccount.Id, request.Amount)))
        .ToActionResultAsync(this);
```

## Advanced Scenarios

### Batch Operations

Processing multiple items with Traverse:

```csharp
[HttpPost("batch/users")]
public async Task<ActionResult<BatchResult<User>>> CreateUsersAsync(
    [FromBody] List<CreateUserRequest> requests,
    CancellationToken ct) =>
    await requests.TraverseAsync(
        async (request, cancellationToken) =>
            await FirstName.TryCreate(request.FirstName)
                .Combine(LastName.TryCreate(request.LastName))
                .Combine(EmailAddress.TryCreate(request.Email))
                .BindAsync(async (firstName, lastName, email) =>
                    await User.TryCreateAsync(firstName, lastName, email, request.Password, cancellationToken))
                .TapAsync(user => _repository.AddAsync(user, cancellationToken)),
        ct)
    .Map(users => new BatchResult<User>(users.Count(), users))
    .ToActionResultAsync(this);

[HttpPut("batch/activate")]
public async Task<ActionResult> ActivateUsersAsync(
    [FromBody] List<string> userIds,
    CancellationToken ct) =>
    await userIds.TraverseAsync(
        async (id, cancellationToken) =>
            await _repository.GetByIdAsync(id, cancellationToken)
                .ToResultAsync(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" })
                .BindAsync(user => user.ActivateAsync(cancellationToken))
                .TapAsync(user => _repository.UpdateAsync(user, cancellationToken)),
        ct)
    .Map(_ => Result.Ok())
    .ToActionResultAsync(this);
```

### Conditional Processing

Conditional logic in pipelines:

```csharp
[HttpPost("{id}/process")]
public async Task<ActionResult<Order>> ProcessOrderAsync(string id, CancellationToken ct) =>
    await _orderRepository.GetByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", id)) { Detail = $"Order {id} not found" })
        // Conditionally apply discount
        .BindAsync(async order =>
        {
            if (order.Customer.IsVIP)
                return await order.ApplyDiscountAsync(0.1m, ct);
            return order.ToResult();
        })
        // Conditionally process payment
        .BindAsync(async order =>
        {
            if (order.Total > 0)
                return await _paymentService.ProcessAsync(order, ct);
            return order.ToResult();
        })
        // Conditionally schedule shipping
        .BindAsync(async order =>
        {
            if (order.RequiresShipping)
                return await _shippingService.ScheduleAsync(order, ct);
            return order.ToResult();
        })
        .TapAsync(order => _orderRepository.SaveAsync(order, ct))
        .ToActionResultAsync(this);
```

### Side Effects with Tap

Executing side effects that shouldn't affect the pipeline:

```csharp
[HttpPost]
public async Task<ActionResult<User>> RegisterAsync(
    [FromBody] RegisterRequest request,
    CancellationToken ct) =>
    await FirstName.TryCreate(request.FirstName)
        .Combine(LastName.TryCreate(request.LastName))
        .Combine(EmailAddress.TryCreate(request.Email))
        .BindAsync(async (firstName, lastName, email) =>
            await User.TryCreateAsync(firstName, lastName, email, request.Password, ct))
        // Save user (critical)
        .TapAsync(user => _repository.AddAsync(user, ct))
        // Send welcome email (non-critical, failures don't affect result)
        .TapAsync(async user =>
        {
            try
            {
                await _emailService.SendWelcomeEmailAsync(user.Email, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send welcome email to {Email}", user.Email);
            }
        })
        // Log analytics (non-critical)
        .TapAsync(async user =>
        {
            try
            {
                await _analyticsService.TrackUserRegistrationAsync(user, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to track registration for {UserId}", user.Id);
            }
        })
        .ToActionResultAsync(this);

[HttpPut("{id}")]
public async Task<ActionResult<Product>> UpdateProductAsync(
    string id,
    [FromBody] UpdateProductRequest request,
    CancellationToken ct) =>
    await _repository.GetByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For("Product", id)) { Detail = $"Product {id} not found" })
        .BindAsync(product => product.UpdateAsync(request, ct))
        // Save changes (critical)
        .TapAsync(product => _repository.UpdateAsync(product, ct))
        // Invalidate cache (best effort)
        .TapAsync(async product =>
        {
            try
            {
                await _cache.InvalidateAsync($"product:{product.Id}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate cache for product {ProductId}", product.Id);
            }
        })
        // Notify subscribers (best effort)
        .TapAsync(async product =>
        {
            try
            {
                await _notificationService.NotifyProductUpdateAsync(product, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify subscribers for product {ProductId}", product.Id);
            }
        })
        .ToActionResultAsync(this);

## Optional Value Objects with Maybe<T>

### DTOs with Optional Properties

Use `Maybe<T>` for optional value object properties instead of `T?`:

```csharp
public record RegisterUserDto
{
    public FirstName FirstName { get; init; } = null!;   // Required
    public LastName LastName { get; init; } = null!;      // Required
    public EmailAddress Email { get; init; } = null!;     // Required
    public Maybe<Url> Website { get; init; }              // Optional
}

public record UpdateOrderDto
{
    public Maybe<FirstName> AssignedTo { get; init; }     // Optional
}
```

### JSON Behavior

```json
// All required + optional present
{ "firstName": "Jane", "lastName": "Doe", "email": "jane@example.com", "website": "https://jane.dev" }
// → Website = Maybe.From(Url("https://jane.dev"))

// Required only, optional null
{ "firstName": "Jane", "lastName": "Doe", "email": "jane@example.com", "website": null }
// → Website = Maybe.None

// Required only, optional absent
{ "firstName": "Jane", "lastName": "Doe", "email": "jane@example.com" }
// → Website = Maybe.None

// Invalid optional value
{ "firstName": "Jane", "lastName": "Doe", "email": "jane@example.com", "website": "not-valid" }
// → 400 Bad Request with errors: { "Website": ["Url is not valid."] }
```

### Using Maybe<T> in Domain Logic

```csharp
[HttpPost]
public ActionResult<User> Register(RegisterUserDto dto)
{
    // dto properties are already validated by AddScalarValueValidation()
    return User.TryCreate(dto.FirstName, dto.LastName, dto.Email, dto.Website)
        .Tap(user => _repository.Add(user))
        .ToActionResult(this);
}
```

In the domain aggregate:

```csharp
public class User : Aggregate<UserId>
{
    public FirstName FirstName { get; }
    public LastName LastName { get; }
    public EmailAddress Email { get; }
    public Maybe<Url> Website { get; }

    public static Result<User> TryCreate(
        FirstName firstName, LastName lastName,
        EmailAddress email, Maybe<Url> website = default)
    {
        // website defaults to Maybe.None if not provided
        return new User(UserId.NewUniqueV7(), firstName, lastName, email, website)
            .ToResult();
    }
}
```

### Map and Match on Maybe

```csharp
// Transform optional value
Maybe<Url> website = Maybe.From(Url.Create("https://example.com"));
Maybe<string> urlString = website.Map(url => url.Value);
// → Maybe.From("https://example.com")

Maybe<Url> none = Maybe.None<Url>();
Maybe<string> noneString = none.Map(url => url.Value);
// → Maybe.None

// Pattern match
string display = website.Match(
    url => $"Visit: {url.Value}",
    () => "No website");
// → "Visit: https://example.com"
```
