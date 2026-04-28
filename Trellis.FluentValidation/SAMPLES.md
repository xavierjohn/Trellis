# Fluent Validation Extension - Comprehensive Examples

This document provides detailed examples and advanced patterns for integrating FluentValidation with Railway Oriented Programming in Trellis.

## Table of Contents

- [Advanced Validation Patterns](#advanced-validation-patterns)
  - [Async Validation](#async-validation)
  - [Integration with Combine](#integration-with-combine)
  - [Conditional Validation](#conditional-validation)
  - [Custom Error Messages](#custom-error-messages)
  - [Collection Validation](#collection-validation)
- [Complete Real-World Examples](#complete-real-world-examples)
  - [User Registration with Multi-Step Validation](#user-registration-with-multi-step-validation)
  - [Order Validation with Business Rules](#order-validation-with-business-rules)
  - [Nested Object Validation](#nested-object-validation)
  - [Product Catalog Validation](#product-catalog-validation)
- [Integration Patterns](#integration-patterns)
  - [Service Layer Integration](#service-layer-integration)
  - [ASP.NET Core Integration](#aspnet-core-integration)
  - [Repository Pattern Integration](#repository-pattern-integration)
- [Advanced Scenarios](#advanced-scenarios)
  - [RuleSets for Different Scenarios](#rulesets-for-different-scenarios)
  - [Cross-Property Validation](#cross-property-validation)
  - [Dependent Validation](#dependent-validation)
  - [Transform and Validate](#transform-and-validate)

## Advanced Validation Patterns

### Async Validation

For validation that requires external checks (database, API calls):

```csharp
public class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    private readonly IUserRepository _repository;
    private readonly IEmailService _emailService;
    
    public CreateUserValidator(IUserRepository repository, IEmailService emailService)
    {
        _repository = repository;
        _emailService = emailService;
        
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email is required")
            .EmailAddress()
            .WithMessage("Invalid email format")
            .MustAsync(BeUniqueEmail)
            .WithMessage("Email is already registered")
            .MustAsync(BeValidDomain)
            .WithMessage("Email domain is not allowed");
            
        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(3, 20)
            .Matches("^[a-zA-Z0-9_]+$")
            .WithMessage("Username can only contain letters, numbers, and underscores")
            .MustAsync(BeUniqueUsername)
            .WithMessage("Username is already taken");
    }
    
    private async Task<bool> BeUniqueEmail(string email, CancellationToken ct)
    {
        var existing = await _repository.FindByEmailAsync(email, ct);
        return existing == null;
    }
    
    private async Task<bool> BeUniqueUsername(string username, CancellationToken ct)
    {
        var existing = await _repository.FindByUsernameAsync(username, ct);
        return existing == null;
    }
    
    private async Task<bool> BeValidDomain(string email, CancellationToken ct)
    {
        var domain = email.Split('@').LastOrDefault();
        if (string.IsNullOrEmpty(domain))
            return false;
            
        return await _emailService.IsValidDomainAsync(domain, ct);
    }
}

// Usage in service layer
public class UserService
{
    private readonly CreateUserValidator _validator;
    private readonly IUserRepository _repository;
    
    public async Task<Result<User>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        return await _validator.ValidateToResultAsync(request, cancellationToken)
            .BindAsync(
                async (req, ct) =>
                    await EmailAddress.TryCreate(req.Email)
                        .Combine(Username.TryCreate(req.Username))
                        .BindAsync(
                            async (email, username, innerCt) =>
                                await User.TryCreateAsync(email, username, req.Password, innerCt),
                            ct),
                cancellationToken)
            .TapAsync(
                async (user, ct) => await _repository.AddAsync(user, ct),
                cancellationToken);
    }
}
```

### Integration with Combine

Validate multiple value objects before creating an aggregate:

```csharp
public class Order
{
    public OrderId Id { get; }
    public CustomerId CustomerId { get; }
    public Address ShippingAddress { get; }
    public Address BillingAddress { get; }
    public List<OrderLine> Items { get; }
    public Money Total { get; private set; }
    
    // Multi-step validation: value objects ? aggregate invariants
    public static Result<Order> Create(
        string customerId,
        string shippingStreet,
        string shippingCity,
        string shippingZip,
        string billingStreet,
        string billingCity,
        string billingZip,
        List<OrderLineRequest> itemRequests)
    {
        // Step 1: Validate and create value objects
        return CustomerId.TryCreate(customerId)
            .Combine(Address.TryCreate(shippingStreet, shippingCity, shippingZip))
            .Combine(Address.TryCreate(billingStreet, billingCity, billingZip))
            .Combine(itemRequests.Traverse(req =>
                ProductId.TryCreate(req.ProductId)
                    .Combine(Quantity.TryCreate(req.Quantity))
                    .Combine(Price.TryCreate(req.Price))
                    .Bind((productId, quantity, price) =>
                        OrderLine.TryCreate(productId, req.ProductName, price, quantity))))
            // Step 2: Create aggregate
            .Bind((custId, shipping, billing, items) =>
                TryCreate(custId, shipping, billing, items));
    }
    
    private static Result<Order> TryCreate(
        CustomerId customerId,
        Address shippingAddress,
        Address billingAddress,
        IEnumerable<OrderLine> items)
    {
        var order = new Order(customerId, shippingAddress, billingAddress, items.ToList());
        order.CalculateTotal();
        
        // Step 3: Validate aggregate invariants
        return Validator.ValidateToResult(order);
    }
    
    private void CalculateTotal()
    {
        Total = Money.FromDecimal(Items.Sum(i => i.Price.Amount * i.Quantity.Value));
    }
    
    // Aggregate-level validation
    private static readonly InlineValidator<Order> Validator = new()
    {
        v => v.RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("Order must contain at least one item"),
            
        v => v.RuleFor(x => x.Items)
            .Must(items => items.Count <= 100)
            .WithMessage("Order cannot exceed 100 items"),
            
        v => v.RuleFor(x => x.Total.Amount)
            .GreaterThan(0)
            .WithMessage("Order total must be positive"),
            
        v => v.RuleFor(x => x.Total.Amount)
            .LessThanOrEqualTo(50000)
            .WithMessage("Order total cannot exceed $50,000"),
            
        v => v.RuleFor(x => x)
            .Must(order => order.ShippingAddress != order.BillingAddress || 
                          order.Items.All(i => i.Quantity.Value <= 10))
            .WithMessage("Large quantities require separate shipping address"),
            
        v => v.RuleForEach(x => x.Items)
            .Must(item => item.Quantity.Value > 0 && item.Quantity.Value <= 1000)
            .WithMessage("Item quantity must be between 1 and 1,000")
    };
}
```

### Conditional Validation

Apply rules based on conditions:

```csharp
public class OrderValidator : AbstractValidator<Order>
{
    public OrderValidator()
    {
        // Required for physical products
        RuleFor(x => x.ShippingAddress)
            .NotNull()
            .WithMessage("Shipping address is required for physical orders")
            .When(x => x.OrderType == OrderType.Physical);
            
        // Required for digital products
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .When(x => x.OrderType == OrderType.Digital)
            .WithMessage("Email is required for digital orders");
            
        // International orders require additional info
        RuleFor(x => x.CustomsDeclaration)
            .NotNull()
            .WithMessage("Customs declaration required for international shipments")
            .When(x => x.ShippingAddress?.Country != "US");
            
        // Express shipping has restrictions
        RuleFor(x => x.Total.Amount)
            .LessThanOrEqualTo(10000)
            .When(x => x.ShippingMethod == ShippingMethod.Express)
            .WithMessage("Express shipping not available for orders over $10,000");
            
        // Business orders require tax ID
        RuleFor(x => x.TaxId)
            .NotEmpty()
            .When(x => x.CustomerType == CustomerType.Business)
            .WithMessage("Tax ID required for business orders");
            
        // Conditional based on multiple properties
        RuleFor(x => x.SignatureRequired)
            .Equal(true)
            .When(x => x.Total.Amount > 5000 || x.ContainsRestrictedItems)
            .WithMessage("Signature required for high-value or restricted items");
    }
}

// Complex conditional validation with custom logic
public class SubscriptionValidator : AbstractValidator<Subscription>
{
    public SubscriptionValidator()
    {
        // Different rules for different tiers
        When(x => x.Tier == SubscriptionTier.Free, () =>
        {
            RuleFor(x => x.MaxUsers)
                .LessThanOrEqualTo(5)
                .WithMessage("Free tier limited to 5 users");
                
            RuleFor(x => x.StorageGB)
                .LessThanOrEqualTo(10)
                .WithMessage("Free tier limited to 10GB storage");
        });
        
        When(x => x.Tier == SubscriptionTier.Pro, () =>
        {
            RuleFor(x => x.MaxUsers)
                .LessThanOrEqualTo(50)
                .WithMessage("Pro tier limited to 50 users");
                
            RuleFor(x => x.StorageGB)
                .LessThanOrEqualTo(500)
                .WithMessage("Pro tier limited to 500GB storage");
        });
        
        // Enterprise has custom rules
        When(x => x.Tier == SubscriptionTier.Enterprise, () =>
        {
            RuleFor(x => x.ContractNumber)
                .NotEmpty()
                .WithMessage("Contract number required for Enterprise tier");
                
            RuleFor(x => x.DedicatedSupport)
                .Equal(true)
                .WithMessage("Dedicated support mandatory for Enterprise tier");
        });
    }
}
```

### Custom Error Messages

Customize error messages with property values:

```csharp
public class ProductValidator : AbstractValidator<Product>
{
    public ProductValidator()
    {
        // Include actual values in error messages
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .WithMessage(x => $"Price ${x.Price} must be greater than $0");
            
        RuleFor(x => x.Stock)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Stock cannot be negative")
            .LessThanOrEqualTo(10000)
            .WithMessage(x => $"Stock level {x.Stock} exceeds maximum of 10,000");
            
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(3, 100)
            .WithMessage(x => 
                $"Product name must be between 3 and 100 characters (current: {x.Name?.Length ?? 0})");
                
        RuleFor(x => x.Weight)
            .InclusiveBetween(0.01m, 1000m)
            .WithMessage(x => 
                $"Weight {x.Weight}kg is outside valid range (0.01kg - 1000kg)");
                
        // Complex messages with multiple properties
        RuleFor(x => x.Discount)
            .LessThanOrEqualTo(x => x.Price * 0.5m)
            .WithMessage(x => 
                $"Discount ${x.Discount} exceeds 50% of price ${x.Price}");
                
        // Dynamic messages based on product type
        RuleFor(x => x.ExpirationDate)
            .NotNull()
            .When(x => x.IsPerishable)
            .WithMessage(x => 
                $"Perishable product '{x.Name}' requires expiration date");
    }
}

// Using state for context-rich messages
public class OrderLineValidator : AbstractValidator<OrderLine>
{
    public OrderLineValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithMessage(x => 
                $"Quantity for '{x.ProductName}' must be at least 1")
            .LessThanOrEqualTo(x => x.AvailableStock)
            .WithMessage(x => 
                $"Quantity {x.Quantity} for '{x.ProductName}' exceeds available stock of {x.AvailableStock}");
                
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .WithMessage(x => 
                $"Price for '{x.ProductName}' must be greater than $0")
            .LessThanOrEqualTo(x => x.ListPrice)
            .WithMessage(x => 
                $"Price ${x.Price} for '{x.ProductName}' exceeds list price ${x.ListPrice}");
    }
}
```

### Collection Validation

Validating collections and their elements:

```csharp
public class BatchOrderValidator : AbstractValidator<BatchOrder>
{
    public BatchOrderValidator()
    {
        // Validate collection itself
        RuleFor(x => x.Orders)
            .NotEmpty()
            .WithMessage("Batch must contain at least one order")
            .Must(orders => orders.Count <= 100)
            .WithMessage(x => 
                $"Batch contains {x.Orders.Count} orders, maximum is 100");
                
        // Validate each element
        RuleForEach(x => x.Orders)
            .SetValidator(new OrderValidator());
            
        // Complex collection validation
        RuleFor(x => x.Orders)
            .Must(orders => orders.Select(o => o.CustomerId).Distinct().Count() == 1)
            .WithMessage("All orders in batch must be for the same customer");
            
        // Validate collection totals
        RuleFor(x => x.Orders)
            .Must(orders => orders.Sum(o => o.Total.Amount) <= 100000)
            .WithMessage(x => 
                $"Batch total ${x.Orders.Sum(o => o.Total.Amount)} exceeds maximum of $100,000");
    }
}

// Advanced collection validation with indices
public class ImportValidator : AbstractValidator<ImportRequest>
{
    public ImportValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("Import must contain at least one item");
            
        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.ProductCode)
                    .NotEmpty()
                    .WithMessage((parent, productCode, context) =>
                    {
                        var index = parent.Items.IndexOf(context.InstanceToValidate as ImportItem);
                        return $"Product code is required for item at index {index}";
                    });
                    
                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithMessage((parent, quantity, context) =>
                    {
                        var importItem = context.InstanceToValidate as ImportItem;
                        var index = parent.Items.IndexOf(importItem);
                        return $"Quantity for item '{importItem.ProductCode}' at index {index} must be positive";
                    });
            });
            
        // Validate no duplicates
        RuleFor(x => x.Items)
            .Must(items => 
                items.Select(i => i.ProductCode).Distinct().Count() == items.Count)
            .WithMessage("Import contains duplicate product codes");
    }
}
```

## Complete Real-World Examples

### User Registration with Multi-Step Validation

Complete registration flow with comprehensive validation:

```csharp
public record RegisterUserRequest(
    string Email,
    string FirstName,
    string LastName,
    string Password,
    string ConfirmPassword,
    int Age,
    string PhoneNumber,
    bool AcceptTerms
);

public class RegisterUserValidator : AbstractValidator<RegisterUserRequest>
{
    private readonly IUserRepository _repository;
    
    public RegisterUserValidator(IUserRepository repository)
    {
        _repository = repository;
        
        // Email validation
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email is required")
            .EmailAddress()
            .WithMessage("Invalid email format")
            .MaximumLength(100)
            .WithMessage("Email cannot exceed 100 characters")
            .MustAsync(BeUniqueEmail)
            .WithMessage("This email is already registered");
            
        // Name validation
        RuleFor(x => x.FirstName)
            .NotEmpty()
            .WithMessage("First name is required")
            .Length(2, 50)
            .WithMessage("First name must be between 2 and 50 characters")
            .Matches("^[a-zA-Z ]+$")
            .WithMessage("First name can only contain letters and spaces");
            
        RuleFor(x => x.LastName)
            .NotEmpty()
            .WithMessage("Last name is required")
            .Length(2, 50)
            .WithMessage("Last name must be between 2 and 50 characters")
            .Matches("^[a-zA-Z ]+$")
            .WithMessage("Last name can only contain letters and spaces");
            
        // Password validation
        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters")
            .Matches(@"[A-Z]")
            .WithMessage("Password must contain at least one uppercase letter")
            .Matches(@"[a-z]")
            .WithMessage("Password must contain at least one lowercase letter")
            .Matches(@"[0-9]")
            .WithMessage("Password must contain at least one digit")
            .Matches(@"[@$!%*?&#]")
            .WithMessage("Password must contain at least one special character (@$!%*?&#)");
            
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password)
            .WithMessage("Passwords do not match");
            
        // Age validation
        RuleFor(x => x.Age)
            .GreaterThanOrEqualTo(18)
            .WithMessage("You must be at least 18 years old")
            .LessThan(120)
            .WithMessage(x => $"Age {x.Age} is not realistic");
            
        // Phone validation
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Phone number is required")
            .Matches(@"^\+?[1-9]\d{1,14}$")
            .WithMessage("Invalid phone number format");
            
        // Terms acceptance
        RuleFor(x => x.AcceptTerms)
            .Equal(true)
            .WithMessage("You must accept the terms and conditions");
    }
    
    private async Task<bool> BeUniqueEmail(string email, CancellationToken ct)
    {
        var existing = await _repository.FindByEmailAsync(email, ct);
        return existing == null;
    }
}

public class UserService
{
    private readonly RegisterUserValidator _validator;
    private readonly IUserRepository _repository;
    private readonly IEmailService _emailService;
    
    public async Task<Result<User>> RegisterUserAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        // Step 1: Validate request
        return await _validator.ValidateToResultAsync(request, cancellationToken)
            // Step 2: Create value objects
            .BindAsync(
                async (req, ct) =>
                    await EmailAddress.TryCreate(req.Email)
                        .Combine(FirstName.TryCreate(req.FirstName))
                        .Combine(LastName.TryCreate(req.LastName))
                        .Combine(PhoneNumber.TryCreate(req.PhoneNumber))
                        .Map(tuple => (req, tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4)),
                cancellationToken)
            // Step 3: Create user entity
            .BindAsync(
                async (tuple, ct) =>
                    await User.TryCreateAsync(
                        tuple.Item2, // email
                        tuple.Item3, // firstName
                        tuple.Item4, // lastName
                        tuple.Item5, // phoneNumber
                        tuple.req.Password,
                        tuple.req.Age,
                        ct),
                cancellationToken)
            // Step 4: Save user
            .TapAsync(
                async (user, ct) => await _repository.AddAsync(user, ct),
                cancellationToken)
            // Step 5: Send welcome email (non-critical)
            .TapAsync(
                async (user, ct) =>
                {
                    try
                    {
                        await _emailService.SendWelcomeEmailAsync(user.Email, ct);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail registration
                        Console.WriteLine($"Failed to send welcome email: {ex.Message}");
                    }
                },
                cancellationToken);
    }
}
```

### Order Validation with Business Rules

Complete order processing with comprehensive validation:

```csharp
public class Order
{
    public OrderId Id { get; }
    public CustomerId CustomerId { get; }
    public List<OrderLine> Items { get; }
    public Money Subtotal { get; private set; }
    public Money Tax { get; private set; }
    public Money Total { get; private set; }
    public Address ShippingAddress { get; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; }
    
    public static Result<Order> TryCreate(
        CustomerId customerId,
        List<OrderLine> items,
        Address shippingAddress)
    {
        var order = new Order(customerId, items, shippingAddress);
        order.CalculateTotals();
        
        return Validator.ValidateToResult(order);
    }
    
    private void CalculateTotals()
    {
        Subtotal = Money.FromDecimal(Items.Sum(i => i.Price.Amount * i.Quantity.Value));
        Tax = Money.FromDecimal(Subtotal.Amount * 0.08m); // 8% tax
        Total = Money.FromDecimal(Subtotal.Amount + Tax.Amount);
    }
    
    private static readonly InlineValidator<Order> Validator = new()
    {
        // Basic rules
        v => v.RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("Order must contain at least one item"),
            
        v => v.RuleFor(x => x.Items)
            .Must(items => items.Count <= 100)
            .WithMessage(x => 
                $"Order contains {x.Items.Count} items, maximum is 100"),
            
        // Amount rules
        v => v.RuleFor(x => x.Total.Amount)
            .GreaterThan(0)
            .WithMessage("Order total must be positive"),
            
        v => v.RuleFor(x => x.Total.Amount)
            .LessThanOrEqualTo(50000)
            .WithMessage(x => 
                $"Order total ${x.Total.Amount} exceeds maximum of $50,000"),
            
        // Minimum order value
        v => v.RuleFor(x => x.Subtotal.Amount)
            .GreaterThanOrEqualTo(10)
            .WithMessage(x => 
                $"Order subtotal ${x.Subtotal.Amount} is below minimum of $10"),
            
        // Item quantity rules
        v => v.RuleForEach(x => x.Items)
            .Must(item => item.Quantity.Value > 0 && item.Quantity.Value <= 1000)
            .WithMessage("Item quantity must be between 1 and 1,000"),
            
        // No duplicate products
        v => v.RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .WithMessage("Order contains duplicate products"),
            
        // Business rule: large orders require multiple items
        v => v.RuleFor(x => x)
            .Must(order => order.Total.Amount < 1000 || order.Items.Count >= 3)
            .WithMessage("Orders over $1,000 must contain at least 3 different items"),
            
        // Business rule: express shipping restrictions
        v => v.RuleFor(x => x)
            .Must(order => 
                !order.Items.Any(i => i.IsFragile) || 
                order.ShippingAddress.Country == "US")
            .WithMessage("Fragile items can only be shipped within the US")
    };
}

public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventoryService;
    private readonly ICustomerService _customerService;
    
    public async Task<Result<Order>> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Validate customer
        return await _customerService.GetByIdAsync(request.CustomerId, cancellationToken)
            .ToResultAsync(new Error.NotFound(ResourceRef.For("Customer", request.CustomerId)) { Detail = $"Customer {request.CustomerId} not found" })
            .EnsureAsync(
                async (customer, ct) => await customer.IsActiveAsync(ct),
                new Error.Conflict(null, "conflict") { Detail = "Customer account is inactive" },
                cancellationToken)
            // Validate and create order lines
            .BindAsync(
                async (customer, ct) =>
                    await request.Items.TraverseAsync(
                        async (itemReq, innerCt) =>
                            await _inventoryService.GetProductAsync(itemReq.ProductId, innerCt)
                                .ToResultAsync(new Error.NotFound(ResourceRef.For("Product", itemReq.ProductId)) { Detail = $"Product {itemReq.ProductId} not found" })
                                .EnsureAsync(
                                    async (product, productCt) => 
                                        await _inventoryService.HasStockAsync(product.Id, itemReq.Quantity, productCt),
                                    new Error.Conflict(null, "conflict") { Detail = $"Insufficient stock for product {itemReq.ProductId}" },
                                    innerCt)
                                .BindAsync(
                                    async (product, productCt) =>
                                        await OrderLine.TryCreateAsync(
                                            product.Id,
                                            product.Name,
                                            product.Price,
                                            itemReq.Quantity,
                                            productCt),
                                    innerCt),
                        ct)
                    .Map(items => (customer, items.ToList())),
                cancellationToken)
            // Create shipping address
            .BindAsync(
                async (tuple, ct) =>
                    await Address.TryCreateAsync(
                        request.ShippingStreet,
                        request.ShippingCity,
                        request.ShippingZip,
                        request.ShippingCountry,
                        ct)
                    .Map(address => (tuple.customer.Id, tuple.Item2, address)),
                cancellationToken)
            // Create order
            .Bind(tuple => Order.TryCreate(tuple.Item1, tuple.Item2, tuple.Item3))
            // Save order
            .TapAsync(
                async (order, ct) => await _repository.AddAsync(order, ct),
                cancellationToken)
            // Reserve inventory
            .TapAsync(
                async (order, ct) => await _inventoryService.ReserveAsync(order.Items, ct),
                cancellationToken);
    }
}
```

### Nested Object Validation

Validating complex object graphs:

```csharp
// Address validation
public class AddressValidator : AbstractValidator<Address>
{
    public AddressValidator()
    {
        RuleFor(x => x.Street)
            .NotEmpty()
            .WithMessage("Street is required")
            .MaximumLength(100)
            .WithMessage("Street cannot exceed 100 characters");
            
        RuleFor(x => x.City)
            .NotEmpty()
            .WithMessage("City is required")
            .MaximumLength(50)
            .WithMessage("City cannot exceed 50 characters");
            
        RuleFor(x => x.PostalCode)
            .NotEmpty()
            .WithMessage("Postal code is required")
            .Matches(@"^\d{5}(-\d{4})?$")
            .WithMessage("Invalid postal code format (use 12345 or 12345-6789)");
            
        RuleFor(x => x.Country)
            .NotEmpty()
            .WithMessage("Country is required")
            .Length(2)
            .WithMessage("Country must be 2-letter ISO code");
    }
}

// Contact information validation
public class ContactInfoValidator : AbstractValidator<ContactInfo>
{
    public ContactInfoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(100);
            
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+?[1-9]\d{1,14}$")
            .WithMessage("Invalid phone number format");
            
        RuleFor(x => x.AlternatePhone)
            .Matches(@"^\+?[1-9]\d{1,14}$")
            .WithMessage("Invalid alternate phone number format")
            .When(x => !string.IsNullOrEmpty(x.AlternatePhone));
    }
}

// Customer with nested validation
public class Customer
{
    public CustomerId Id { get; }
    public string Name { get; }
    public ContactInfo ContactInfo { get; }
    public Address BillingAddress { get; }
    public Address? ShippingAddress { get; }
    public List<PaymentMethod> PaymentMethods { get; }
}

public class CustomerValidator : AbstractValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required")
            .MaximumLength(100)
            .WithMessage("Name cannot exceed 100 characters");
            
        // Nested object validation
        RuleFor(x => x.ContactInfo)
            .NotNull()
            .WithMessage("Contact information is required")
            .SetValidator(new ContactInfoValidator());
            
        RuleFor(x => x.BillingAddress)
            .NotNull()
            .WithMessage("Billing address is required")
            .SetValidator(new AddressValidator());
            
        // Optional nested validation
        RuleFor(x => x.ShippingAddress)
            .SetValidator(new AddressValidator()!)
            .When(x => x.ShippingAddress != null);
            
        // Collection validation
        RuleFor(x => x.PaymentMethods)
            .NotEmpty()
            .WithMessage("At least one payment method is required")
            .Must(methods => methods.Count <= 5)
            .WithMessage("Cannot have more than 5 payment methods");
            
        RuleForEach(x => x.PaymentMethods)
            .SetValidator(new PaymentMethodValidator());
            
        // Cross-property validation
        RuleFor(x => x)
            .Must(customer => 
                customer.ShippingAddress == null || 
                customer.ShippingAddress.Country == customer.BillingAddress.Country)
            .WithMessage("Shipping and billing addresses must be in the same country");
    }
}

// Payment method validation
public class PaymentMethodValidator : AbstractValidator<PaymentMethod>
{
    public PaymentMethodValidator()
    {
        When(x => x.Type == PaymentType.CreditCard, () =>
        {
            RuleFor(x => x.CardNumber)
                .NotEmpty()
                .CreditCard()
                .WithMessage("Invalid credit card number");
                
            RuleFor(x => x.ExpirationDate)
                .GreaterThan(DateTime.Now)
                .WithMessage("Credit card has expired");
                
            RuleFor(x => x.CVV)
                .NotEmpty()
                .Matches(@"^\d{3,4}$")
                .WithMessage("CVV must be 3 or 4 digits");
        });
        
        When(x => x.Type == PaymentType.BankAccount, () =>
        {
            RuleFor(x => x.RoutingNumber)
                .NotEmpty()
                .Matches(@"^\d{9}$")
                .WithMessage("Routing number must be 9 digits");
                
            RuleFor(x => x.AccountNumber)
                .NotEmpty()
                .Matches(@"^\d{6,17}$")
                .WithMessage("Account number must be 6-17 digits");
        });
    }
}
```

### Product Catalog Validation

Complex product validation with variants and categories:

```csharp
public class ProductValidator : AbstractValidator<Product>
{
    private readonly ICategoryRepository _categoryRepository;
    
    public ProductValidator(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
        
        // Basic product info
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(3, 100)
            .WithMessage(x => 
                $"Product name must be between 3 and 100 characters (current: {x.Name?.Length ?? 0})");
                
        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(1000)
            .WithMessage("Description cannot exceed 1,000 characters");
            
        RuleFor(x => x.SKU)
            .NotEmpty()
            .Matches(@"^[A-Z0-9-]+$")
            .WithMessage("SKU can only contain uppercase letters, numbers, and hyphens");
            
        // Pricing
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .WithMessage(x => $"Price ${x.Price} must be greater than $0")
            .LessThanOrEqualTo(100000)
            .WithMessage(x => $"Price ${x.Price} exceeds maximum of $100,000");
            
        RuleFor(x => x.CompareAtPrice)
            .GreaterThan(x => x.Price)
            .When(x => x.CompareAtPrice.HasValue)
            .WithMessage(x => 
                $"Compare-at price ${x.CompareAtPrice} must be greater than price ${x.Price}");
                
        // Inventory
        RuleFor(x => x.Stock)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Stock cannot be negative")
            .LessThanOrEqualTo(100000)
            .WithMessage(x => $"Stock {x.Stock} exceeds maximum of 100,000");
            
        RuleFor(x => x.ReorderPoint)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Stock)
            .WithMessage(x => 
                $"Reorder point {x.ReorderPoint} cannot exceed current stock {x.Stock}");
                
        // Physical attributes
        RuleFor(x => x.Weight)
            .GreaterThan(0)
            .When(x => x.RequiresShipping)
            .WithMessage("Weight is required for products that require shipping");
            
        RuleFor(x => x.Dimensions)
            .NotNull()
            .When(x => x.RequiresShipping)
            .WithMessage("Dimensions are required for products that require shipping")
            .SetValidator(new DimensionsValidator()!)
            .When(x => x.Dimensions != null);
            
        // Category validation (async)
        RuleFor(x => x.CategoryId)
            .NotEmpty()
            .WithMessage("Category is required")
            .MustAsync(CategoryExists)
            .WithMessage(x => $"Category {x.CategoryId} does not exist");
            
        // Variants
        RuleFor(x => x.Variants)
            .NotEmpty()
            .When(x => x.HasVariants)
            .WithMessage("Product must have at least one variant");
            
        RuleForEach(x => x.Variants)
            .SetValidator(new ProductVariantValidator())
            .When(x => x.HasVariants);
            
        // Images
        RuleFor(x => x.Images)
            .NotEmpty()
            .WithMessage("Product must have at least one image")
            .Must(images => images.Count <= 10)
            .WithMessage(x => $"Product has {x.Images.Count} images, maximum is 10");
            
        RuleForEach(x => x.Images)
            .SetValidator(new ProductImageValidator());
    }
    
    private async Task<bool> CategoryExists(string categoryId, CancellationToken ct)
    {
        var category = await _categoryRepository.GetByIdAsync(categoryId, ct);
        return category != null;
    }
}

public class ProductVariantValidator : AbstractValidator<ProductVariant>
{
    public ProductVariantValidator()
    {
        RuleFor(x => x.SKU)
            .NotEmpty()
            .Matches(@"^[A-Z0-9-]+$");
            
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .WithMessage(x => $"Variant price ${x.Price} must be greater than $0");
            
        RuleFor(x => x.Stock)
            .GreaterThanOrEqualTo(0);
            
        RuleFor(x => x.Options)
            .NotEmpty()
            .WithMessage("Variant must have at least one option (size, color, etc.)");
            
        RuleForEach(x => x.Options)
            .SetValidator(new ProductOptionValidator());
    }
}

public class DimensionsValidator : AbstractValidator<Dimensions>
{
    public DimensionsValidator()
    {
        RuleFor(x => x.Length)
            .GreaterThan(0)
            .WithMessage("Length must be positive");
            
        RuleFor(x => x.Width)
            .GreaterThan(0)
            .WithMessage("Width must be positive");
            
        RuleFor(x => x.Height)
            .GreaterThan(0)
            .WithMessage("Height must be positive");
            
        // Volume validation
        RuleFor(x => x)
            .Must(d => d.Length * d.Width * d.Height <= 1000000)
            .WithMessage(x => 
                $"Product volume {x.Length * x.Width * x.Height}cm� exceeds maximum of 1,000,000cm�");
    }
}
```

## Integration Patterns

### Service Layer Integration

```csharp
public class OrderService : IOrderService
{
    private readonly IOrderRepository _repository;
    private readonly IOrderValidator _validator;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    
    public async Task<Result<Order>> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Validate request
        return await _validator.ValidateToResultAsync(request, cancellationToken)
            // Create domain objects
            .BindAsync(
                async (req, ct) => await CreateOrderFromRequestAsync(req, ct),
                cancellationToken)
            // Reserve inventory
            .TapAsync(
                async (order, ct) => await _inventoryService.ReserveAsync(order.Items, ct),
                cancellationToken)
            // Save order
            .TapAsync(
                async (order, ct) => await _repository.AddAsync(order, ct),
                cancellationToken);
    }
    
    public async Task<Result> SubmitOrderAsync(
        string orderId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(orderId, cancellationToken)
            .ToResultAsync(new Error.NotFound(ResourceRef.For("Order", orderId)) { Detail = $"Order {orderId} not found" })
            .BindAsync(
                async (order, ct) => await order.SubmitAsync(ct),
                cancellationToken)
            .BindAsync(
                async (order, ct) => await _paymentService.ProcessAsync(order, ct),
                cancellationToken)
            .TapAsync(
                async (order, ct) => await _repository.UpdateAsync(order, ct),
                cancellationToken)
            .Map(_ => Result.Ok());
    }
}
```

### ASP.NET Core Integration

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IProductValidator _validator;
    
    [HttpPost]
    public async Task<ActionResult<Product>> CreateProductAsync(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken) =>
        await _validator.ValidateToResultAsync(request, cancellationToken)
            .BindAsync(
                async (req, ct) => await _productService.CreateAsync(req, ct),
                cancellationToken)
            .ToHttpResponseAsync()
            .AsActionResultAsync<Product>();
    
    [HttpPut("{id}")]
    public async Task<ActionResult<Product>> UpdateProductAsync(
        string id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken) =>
        await _validator.ValidateToResultAsync(request, cancellationToken)
            .BindAsync(
                async (req, ct) => await _productService.UpdateAsync(id, req, ct),
                cancellationToken)
            .ToHttpResponseAsync()
            .AsActionResultAsync<Product>();
}
```

### Repository Pattern Integration

```csharp
public class UserRepository : IUserRepository
{
    private readonly DbContext _context;
    private readonly IUserValidator _validator;
    
    public async Task<Result<User>> AddAsync(
        User user,
        CancellationToken cancellationToken)
    {
        // Validate before saving
        return await _validator.ValidateToResultAsync(user, cancellationToken)
            .BindAsync(
                async (validUser, ct) =>
                {
                    try
                    {
                        _context.Users.Add(validUser);
                        await _context.SaveChangesAsync(ct);
                        return validUser.ToResult();
                    }
                    catch (DbUpdateException ex)
                    {
                        return new Error.InternalServerError("fault-id") { Detail = "Failed to save user" };
                    }
                },
                cancellationToken);
    }
}
```

## Advanced Scenarios

### RuleSets for Different Scenarios

```csharp
public class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        // Common rules (always applied)
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();
            
        RuleFor(x => x.FirstName)
            .NotEmpty()
            .Length(2, 50);
            
        // Create-specific rules
        RuleSet("Create", () =>
        {
            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8);
                
            RuleFor(x => x.Id)
                .Empty()
                .WithMessage("ID should not be set for new users");
        });
        
        // Update-specific rules
        RuleSet("Update", () =>
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .WithMessage("ID is required for updates");
                
            RuleFor(x => x.Password)
                .Empty()
                .WithMessage("Use separate endpoint to change password");
        });
        
        // Delete-specific rules
        RuleSet("Delete", () =>
        {
            RuleFor(x => x.HasActiveOrders)
                .Equal(false)
                .WithMessage("Cannot delete user with active orders");
                
            RuleFor(x => x.HasPendingPayments)
                .Equal(false)
                .WithMessage("Cannot delete user with pending payments");
        });
    }
}

// Usage
public async Task<Result<User>> CreateUserAsync(
    User user,
    CancellationToken cancellationToken) =>
    await _validator.ValidateToResultAsync(
        user,
        options => options.IncludeRuleSets("Create"),
        cancellationToken);

public async Task<Result<User>> UpdateUserAsync(
    User user,
    CancellationToken cancellationToken) =>
    await _validator.ValidateToResultAsync(
        user,
        options => options.IncludeRuleSets("Update"),
        cancellationToken);
```

### Cross-Property Validation

```csharp
public class BookingValidator : AbstractValidator<Booking>
{
    public BookingValidator()
    {
        RuleFor(x => x.CheckInDate)
            .NotEmpty()
            .GreaterThan(DateTime.Now)
            .WithMessage("Check-in date must be in the future");
            
        RuleFor(x => x.CheckOutDate)
            .NotEmpty()
            .GreaterThan(x => x.CheckInDate)
            .WithMessage("Check-out date must be after check-in date");
            
        // Minimum stay validation
        RuleFor(x => x)
            .Must(booking => 
                (booking.CheckOutDate - booking.CheckInDate).Days >= booking.MinimumStay)
            .WithMessage(x => 
                $"Booking requires minimum {x.MinimumStay} night stay");
            
        // Maximum stay validation
        RuleFor(x => x)
            .Must(booking => 
                (booking.CheckOutDate - booking.CheckInDate).Days <= 30)
            .WithMessage("Maximum stay is 30 nights");
            
        // Peak season validation
        RuleFor(x => x)
            .Must(booking => 
                !IsPeakSeason(booking.CheckInDate) || 
                booking.GuestCount <= booking.Room.PeakSeasonMaxGuests)
            .WithMessage(x => 
                $"Peak season allows maximum {x.Room.PeakSeasonMaxGuests} guests");
    }
    
    private bool IsPeakSeason(DateTime date) =>
        (date.Month >= 6 && date.Month <= 8) || // Summer
        (date.Month == 12); // December
}
```

### Dependent Validation

```csharp
public class ShippingValidator : AbstractValidator<ShippingInfo>
{
    public ShippingValidator()
    {
        // Standard shipping address validation
        RuleFor(x => x.Address)
            .NotNull()
            .SetValidator(new AddressValidator());
            
        // Additional validation for international shipping
        When(x => x.Address?.Country != "US", () =>
        {
            RuleFor(x => x.CustomsDeclaration)
                .NotNull()
                .WithMessage("Customs declaration required for international shipments");
                
            RuleFor(x => x.CustomsValue)
                .GreaterThan(0)
                .WithMessage("Customs value must be declared");
                
            RuleFor(x => x.HarmonizedCode)
                .NotEmpty()
                .Matches(@"^\d{6,10}$")
                .WithMessage("Valid harmonized tariff code required");
        });
        
        // Express shipping restrictions
        When(x => x.ShippingMethod == ShippingMethod.Express, () =>
        {
            RuleFor(x => x.Address.PostalCode)
                .Must(BeInServiceArea)
                .WithMessage("Express shipping not available in this area");
                
            RuleFor(x => x.TotalWeight)
                .LessThanOrEqualTo(50)
                .WithMessage("Express shipping limited to 50 lbs");
        });
    }
    
    private bool BeInServiceArea(string postalCode)
    {
        // Check if postal code is in express service area
        return true; // Simplified
    }
}
```

### Transform and Validate

```csharp
public class ImportService
{
    private readonly IImportValidator _validator;
    
    public async Task<Result<List<Product>>> ImportProductsAsync(
        List<ProductImportRow> rows,
        CancellationToken cancellationToken)
    {
        // Transform and validate each row
        return await rows.TraverseAsync(
            async (row, ct) =>
                await TransformRowAsync(row, ct)
                    .BindAsync(
                        async (product, innerCt) =>
                            await _validator.ValidateToResultAsync(product, innerCt),
                        ct),
            cancellationToken);
    }
    
    private async Task<Result<Product>> TransformRowAsync(
        ProductImportRow row,
        CancellationToken cancellationToken)
    {
        return await ProductName.TryCreate(row.Name)
            .Combine(ProductDescription.TryCreate(row.Description))
            .Combine(Price.TryCreate(row.Price))
            .Combine(SKU.TryCreate(row.SKU))
            .BindAsync(
                async (name, description, price, sku, ct) =>
                    await Product.TryCreateAsync(name, description, price, sku, ct),
                cancellationToken);
    }
}
```
