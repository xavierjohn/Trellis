# Primitive Value Objects - Comprehensive Examples

This document provides detailed examples and patterns for using Primitive Value Objects with source code generation in domain-driven design applications.

## Table of Contents

- [RequiredString Examples](#requiredstring-examples)
  - [Basic Usage](#basic-usage)
  - [Custom Validation](#custom-validation)
  - [Integration with Entities](#integration-with-entities)
- [RequiredGuid Examples](#requiredguid-examples)
  - [Identity Value Objects](#identity-value-objects)
  - [Aggregate IDs](#aggregate-ids)
  - [Parsing and Conversion](#parsing-and-conversion)
- [EmailAddress Examples](#emailaddress-examples)
  - [User Registration](#user-registration)
  - [Email Validation](#email-validation)
- [Generated Code Deep Dive](#generated-code-deep-dive)
  - [RequiredString Generation](#requiredstring-generation)
  - [RequiredGuid Generation](#requiredguid-generation)
  - [IParsable Implementation](#iparsable-implementation)
- [Advanced Patterns](#advanced-patterns)
  - [Combining Value Objects](#combining-value-objects)
  - [Custom Field Names](#custom-field-names)
- [Integration Examples](#integration-examples)
  - [Entity Framework Core](#entity-framework-core)
  - [ASP.NET Core](#aspnet-core)
  - [JSON Serialization](#json-serialization)

## RequiredString Examples

### Basic Usage

Creating strongly-typed string identifiers:

```csharp
// Define value objects using source code generation
public partial class OrderNumber : RequiredString
{
}

public partial class ProductSKU : RequiredString
{
}

public partial class CustomerName : RequiredString
{
}

// Usage
public class OrderService
{
    public Result<Order> CreateOrder(string orderNumber, string productSKU, string customerName)
    {
        // Validate all strings using TryCreate
        return OrderNumber.TryCreate(orderNumber)
            .Combine(ProductSKU.TryCreate(productSKU))
            .Combine(CustomerName.TryCreate(customerName))
            .Bind((orderNum, sku, custName) =>
                Order.TryCreate(orderNum, sku, custName));
    }
}

// Explicit casting for convenience (throws on failure)
public void ProcessOrder()
{
    var orderNumber = (OrderNumber)"ORD-2024-001";
    var productSKU = (ProductSKU)"PROD-SKU-123";
    var customerName = (CustomerName)"John Doe";
    
    // Use in domain logic
    var order = CreateOrder(orderNumber, productSKU, customerName);
}

// Safe processing
public void SafeProcessing(string input)
{
    var result = OrderNumber.TryCreate(input);
    
    if (result.IsSuccess)
    {
        Console.WriteLine($"Valid order number: {result.Value}");
    }
    else
    {
        Console.WriteLine($"Invalid: {result.Error.Detail}");
        // Output: "Order Number cannot be empty."
    }
}

// IParsable support
public void ParseFromString()
{
    // Parse method (throws FormatException on failure)
    var orderNumber = OrderNumber.Parse("ORD-2024-001", null);
    
    // TryParse method (returns bool)
    if (OrderNumber.TryParse("ORD-2024-001", null, out var result))
    {
        Console.WriteLine($"Parsed: {result}");
    }
}
```

### String Length Constraints

Use `[StringLength]` to enforce min/max length declaratively — the source generator includes length validation in the generated `TryCreate`:

```csharp
// Maximum length only
[StringLength(20)]
public partial class ShortCode : RequiredString<ShortCode> { }

ShortCode.TryCreate("ABC-123");                 // ✅ Success
ShortCode.TryCreate(new string('x', 21));       // ❌ "Short Code must be 20 characters or fewer."

// Both minimum and maximum length
[StringLength(100, MinimumLength = 3)]
public partial class DisplayName : RequiredString<DisplayName> { }

DisplayName.TryCreate("Jo");                    // ❌ "Display Name must be at least 3 characters."
DisplayName.TryCreate("John Doe");              // ✅ Success
```

### Custom Validation

For validation beyond length (format, pattern, etc.), extend with a custom factory method:

```csharp
// Length is handled by [StringLength], format by custom factory method
[StringLength(15, MinimumLength = 10)]
public partial class ContactPhone : RequiredString<ContactPhone>
{
    public static Result<ContactPhone> TryCreateWithValidation(string? value) =>
        TryCreate(value) // Generated: validates non-empty + length 10–15
            .Ensure(
                phone => phone.Value.All(c => char.IsDigit(c) || c == '-' || c == ' '),
                new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("contactPhone"), "validation.error") { Detail = "Phone number can only contain digits, dashes, and spaces" })));
}

public partial class ZipCode : RequiredString<ZipCode>
{
    public static Result<ZipCode> TryCreateWithValidation(string? value) =>
        TryCreate(value)
            .Ensure(
                zip => zip.Value.Length == 5 || zip.Value.Length == 10,
                new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("zipCode"), "validation.error") { Detail = "Zip code must be 5 or 10 characters (with dash)" })))
            .Ensure(
                zip => Regex.IsMatch(zip.Value, @"^\d{5}(-\d{4})?$"),
                new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("zipCode"), "validation.error") { Detail = "Invalid zip code format (use 12345 or 12345-6789)" })));
}

[StringLength(20, MinimumLength = 3)]
public partial class PartNumber : RequiredString<PartNumber>
{
    public static Result<PartNumber> TryCreateWithValidation(string? value) =>
        TryCreate(value) // Generated: validates non-empty + length 3–20
            .Ensure(
                code => Regex.IsMatch(code.Value, @"^[A-Z0-9-]+$"),
                new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("partNumber"), "validation.error") { Detail = "Part number can only contain uppercase letters, digits, and dashes" })));
}

// Usage
public void ValidateCustom()
{
    // Phone validation
    var phone = ContactPhone.TryCreateWithValidation("555-1234");
    // Error: "Contact Phone must be at least 10 characters."
    
    var validPhone = ContactPhone.TryCreateWithValidation("555-123-4567");
    // Success
    
    // Zip code validation
    var zip = ZipCode.TryCreateWithValidation("12345").Value;
    var zipPlus4 = ZipCode.TryCreateWithValidation("12345-6789").Value;
    
    // Part number validation
    var partNumber = PartNumber.TryCreateWithValidation("PROD-ABC-123").Value;
    var invalid = PartNumber.TryCreateWithValidation("prod-abc");
    // Error: "Part number can only contain uppercase letters, digits, and dashes"
}
```

### Integration with Entities

Using RequiredString value objects in entities:

```csharp
public partial class OrderId : RequiredGuid
{
}

public partial class OrderNumber : RequiredString
{
}

public partial class CustomerName : RequiredString
{
}

public partial class ProductSKU : RequiredString
{
}

public class Order : Entity<OrderId>
{
    public OrderNumber OrderNumber { get; }
    public CustomerName CustomerName { get; }
    public ProductSKU ProductSKU { get; }
    public DateTime OrderDate { get; }
    
    private Order(
        OrderId id,
        OrderNumber orderNumber,
        CustomerName customerName,
        ProductSKU productSKU)
        : base(id)
    {
        OrderNumber = orderNumber;
        CustomerName = customerName;
        ProductSKU = productSKU;
        OrderDate = DateTime.UtcNow;
    }
    
    public static Result<Order> TryCreate(
        OrderNumber orderNumber,
        CustomerName customerName,
        ProductSKU productSKU) =>
        new Order(
            OrderId.NewUniqueV7(),
            orderNumber,
            customerName,
            productSKU).ToResult();
    
    // Factory from strings
    public static Result<Order> TryCreate(
        string orderNumber,
        string customerName,
        string productSKU) =>
        OrderNumber.TryCreate(orderNumber)
            .Combine(CustomerName.TryCreate(customerName))
            .Combine(ProductSKU.TryCreate(productSKU))
            .Bind((orderNum, custName, sku) =>
                TryCreate(orderNum, custName, sku));
}
```

## RequiredGuid Examples

### Identity Value Objects

Creating strongly-typed identity value objects:

```csharp
public partial class UserId : RequiredGuid
{
}

public partial class OrderId : RequiredGuid
{
}

public partial class ProductId : RequiredGuid
{
}

public partial class CustomerId : RequiredGuid
{
}

// Usage
public class User : Entity<UserId>
{
    public EmailAddress Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    
    private User(UserId id, EmailAddress email, string firstName, string lastName)
        : base(id)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }
    
    public static Result<User> TryCreate(
        EmailAddress email,
        string firstName,
        string lastName) =>
        new User(UserId.NewUniqueV7(), email, firstName, lastName).ToResult();
}

// Creating new IDs
public void CreateNewIdentities()
{
    var userId = UserId.NewUniqueV7();
    var orderId = OrderId.NewUniqueV7();
    var productId = ProductId.NewUniqueV7();
    
    Console.WriteLine($"User ID: {userId}");
    // Output: User ID: 550e8400-e29b-41d4-a716-446655440000
}

// Parsing from Guid
public void ParseFromGuid()
{
    var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
    
    var result = UserId.TryCreate(guid);
    if (result.IsSuccess)
    {
        var userId = result.Value;
        Console.WriteLine($"User ID: {userId}");
    }
    
    // Explicit cast
    var userId2 = (UserId)guid;
}

// Parsing from string
public void ParseFromString()
{
    var result = UserId.TryCreate("550e8400-e29b-41d4-a716-446655440000");
    
    if (result.IsSuccess)
    {
        var userId = result.Value;
        Console.WriteLine($"User ID: {userId}");
    }
    else
    {
        Console.WriteLine($"Error: {result.Error.Detail}");
    }
    
    // IParsable support
    var userId2 = UserId.Parse("550e8400-e29b-41d4-a716-446655440000", null);
    
    if (UserId.TryParse("550e8400-e29b-41d4-a716-446655440000", null, out var userId3))
    {
        Console.WriteLine($"Parsed: {userId3}");
    }
}
```

### Aggregate IDs

Using RequiredGuid for aggregate root identities:

```csharp
public partial class OrderId : RequiredGuid
{
}

public partial class OrderLineId : RequiredGuid
{
}

public class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    
    private Order(OrderId id) : base(id)
    {
    }
    
    public static Result<Order> TryCreate() =>
        new Order(OrderId.NewUniqueV7()).ToResult();
    
    public Result<Order> AddLine(ProductId productId, int quantity, decimal price)
    {
        var line = new OrderLine(OrderLineId.NewUniqueV7(), productId, quantity, price);
        _lines.Add(line);
        return this.ToResult();
    }
}

public class OrderLine : Entity<OrderLineId>
{
    public ProductId ProductId { get; }
    public int Quantity { get; }
    public decimal Price { get; }
    
    internal OrderLine(OrderLineId id, ProductId productId, int quantity, decimal price)
        : base(id)
    {
        ProductId = productId;
        Quantity = quantity;
        Price = price;
    }
}
```

### Parsing and Conversion

Comprehensive parsing and conversion examples:

```csharp
public partial class EntityId : RequiredGuid
{
}

public class EntityService
{
    // Parsing from various sources
    public Result<EntityId> ParseFromString(string input)
    {
        // Method 1: TryCreate (returns Result<T>)
        var result1 = EntityId.TryCreate(input);
        
        // Method 2: Parse (throws on failure)
        try
        {
            var result2 = EntityId.Parse(input, null);
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"Parse failed: {ex.Message}");
        }
        
        // Method 3: TryParse (returns bool)
        if (EntityId.TryParse(input, null, out var result3))
        {
            return result3.ToResult();
        }
        
        return new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("entityId"), "validation.error") { Detail = "Invalid entity ID" }));
    }
    
    // Parsing from Guid
    public Result<EntityId> ParseFromGuid(Guid guid)
    {
        // TryCreate validates that Guid is not empty
        return EntityId.TryCreate(guid);
    }
    
    // Validation scenarios
    public void ValidationScenarios()
    {
        // Empty string fails
        var result1 = EntityId.TryCreate("");
        // Error: "Entity Id cannot be empty."
        
        // Invalid GUID format fails
        var result2 = EntityId.TryCreate("not-a-guid");
        // Error: "Guid should contain 32 digits with 4 dashes..."
        
        // Empty GUID fails
        var result3 = EntityId.TryCreate(Guid.Empty);
        // Error: "Entity Id cannot be empty."
        
        // Valid GUID succeeds
        var result4 = EntityId.TryCreate("550e8400-e29b-41d4-a716-446655440000");
        // Success
        
        // NewUniqueV7 always succeeds
        var result5 = EntityId.NewUniqueV7();
        // Success: new unique GUID
    }
}
```

## EmailAddress Examples

### User Registration

Using EmailAddress in user registration:

```csharp
public class User : Entity<UserId>
{
    public EmailAddress Email { get; private set; }
    public string FirstName { get; }
    public string LastName { get; }
    
    private User(UserId id, EmailAddress email, string firstName, string lastName)
        : base(id)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }
    
    public static Result<User> TryCreate(
        string email,
        string firstName,
        string lastName) =>
        EmailAddress.TryCreate(email)
            .Bind(validEmail =>
                new User(UserId.NewUniqueV7(), validEmail, firstName, lastName).ToResult());
    
    public Result<User> UpdateEmail(string newEmail) =>
        EmailAddress.TryCreate(newEmail)
            .Map(validEmail =>
            {
                Email = validEmail;
                return this;
            });
}

public class UserService
{
    private readonly IUserRepository _repository;
    
    public async Task<Result<User>> RegisterUserAsync(
        string email,
        string firstName,
        string lastName)
    {
        return await EmailAddress.TryCreate(email)
            .EnsureAsync(
                async e => !await _repository.EmailExistsAsync(e),
                new Error.Conflict(null, "conflict") { Detail = "Email is already registered" })
            .BindAsync(async validEmail =>
                (await User.TryCreate(validEmail.Value, firstName, lastName))
                    .TapAsync(user => _repository.AddAsync(user)));
    }
}
```

### Email Validation

Comprehensive email validation examples:

```csharp
public class EmailValidator
{
    public void ValidateEmails()
    {
        // Valid emails
        var email1 = EmailAddress.TryCreate("user@example.com");
        // Success
        
        var email2 = EmailAddress.TryCreate("john.doe+tag@company.co.uk");
        // Success - supports plus addressing and subdomains
        
        // Invalid emails
        var invalid1 = EmailAddress.TryCreate("not-an-email");
        // Error: "Email address is not valid."
        
        var invalid2 = EmailAddress.TryCreate("user@");
        // Error: "Email address is not valid."
        
        var invalid3 = EmailAddress.TryCreate("@example.com");
        // Error: "Email address is not valid."
        
        var invalid4 = EmailAddress.TryCreate("");
        // Error: "Email address is not valid."
        
        var invalid5 = EmailAddress.TryCreate(null);
        // Error: "Email address is not valid."
    }
    
    // Using custom field name for better error messages
    public void ValidateWithFieldName()
    {
        var result = EmailAddress.TryCreate("invalid", "contactEmail");
        // Error field will be "contactEmail" instead of default "email"
    }
    
    // Using in Combine operations
    public Result<User> CreateUserWithMultipleEmails(
        string primaryEmail,
        string alternateEmail)
    {
        return EmailAddress.TryCreate(primaryEmail, "primaryEmail")
            .Combine(EmailAddress.TryCreate(alternateEmail, "alternateEmail"))
            .Bind((primary, alternate) =>
                User.TryCreate(primary, alternate));
    }
}
```

## Generated Code Deep Dive

### RequiredString Generation

Understanding what gets generated:

```csharp
// Your declaration
public partial class TrackingId : RequiredString
{
}

// Generated code by source generator (TrackingId.g.cs)
// <auto-generated/>
namespace YourNamespace;
using Trellis;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

#nullable enable
[JsonConverter(typeof(ParsableJsonConverter<TrackingId>))]
public partial class TrackingId : RequiredString, IParsable<TrackingId>, ITryCreatable<TrackingId>
{
    private TrackingId(string value) : base(value)
    {
    }

    public static explicit operator TrackingId(string trackingId) => Create(trackingId);

    public static TrackingId Parse(string s, IFormatProvider? provider)
    {
        var r = TryCreate(s);
        if (r.IsFailure)
        {
            var val = (Error.UnprocessableContent)r.Error;
            throw new FormatException(val.FieldErrors[0].Details[0]);
        }
        return r.Value;
    }

    public static bool TryParse(
        [NotNullWhen(true)] string? s, 
        IFormatProvider? provider, 
        [MaybeNullWhen(false)] out TrackingId result)
    {
        var r = TryCreate(s);
        if (r.IsFailure)
        {
            result = default;
            return false;
        }

        result = r.Value;
        return true;
    }

    public static Result<TrackingId> TryCreate(string? requiredStringOrNothing, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity("TrackingId.TryCreate");
        var field = !string.IsNullOrEmpty(fieldName)
            ? (fieldName.Length == 1 ? fieldName.ToLowerInvariant() : char.ToLowerInvariant(fieldName[0]) + fieldName[1..])
            : "trackingId";
        return requiredStringOrNothing
            .EnsureNotNullOrWhiteSpace(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Tracking Id cannot be empty." })))
            .Map(str => new TrackingId(str));
    }
}
```

### RequiredString Generation with `[StringLength]`

When `[StringLength]` is applied, the generated `TryCreate` includes `.Ensure()` length checks:

```csharp
// Your declaration
[StringLength(50, MinimumLength = 3)]
public partial class ProductName : RequiredString<ProductName> { }

// Generated TryCreate (simplified)
public static Result<ProductName> TryCreate(string? value, string? fieldName = null)
{
    using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity("ProductName.TryCreate");
    var field = !string.IsNullOrEmpty(fieldName)
        ? (fieldName.Length == 1 ? fieldName.ToLowerInvariant() : char.ToLowerInvariant(fieldName[0]) + fieldName[1..])
        : "productName";
    return value
        .EnsureNotNullOrWhiteSpace(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Product Name cannot be empty." })))
        .Ensure(str => str.Length >= 3, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Product Name must be at least 3 characters." })))
        .Ensure(str => str.Length <= 50, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Product Name must be 50 characters or fewer." })))
        .Map(str => new ProductName(str));
}
```

### RequiredGuid Generation

Understanding GUID generation:

```csharp
// Your declaration
public partial class EmployeeId : RequiredGuid
{
}

// Generated code by source generator (EmployeeId.g.cs)
// <auto-generated/>
namespace YourNamespace;
using Trellis;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

#nullable enable
[JsonConverter(typeof(ParsableJsonConverter<EmployeeId>))]
public partial class EmployeeId : RequiredGuid, IParsable<EmployeeId>, ITryCreatable<EmployeeId>
{
    private EmployeeId(Guid value) : base(value)
    {
    }

    public static explicit operator EmployeeId(Guid employeeId) => Create(employeeId);

    public static EmployeeId Parse(string s, IFormatProvider? provider)
    {
        var r = TryCreate(s);
        if (r.IsFailure)
        {
            var val = (Error.UnprocessableContent)r.Error;
            throw new FormatException(val.FieldErrors[0].Details[0]);
        }
        return r.Value;
    }

    public static bool TryParse(
        [NotNullWhen(true)] string? s, 
        IFormatProvider? provider, 
        [MaybeNullWhen(false)] out EmployeeId result)
    {
        var r = TryCreate(s);
        if (r.IsFailure)
        {
            result = default;
            return false;
        }

        result = r.Value;
        return true;
    }

    public static EmployeeId NewUnique() => new(Guid.NewGuid());

    public static Result<EmployeeId> TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity("EmployeeId.TryCreate");
        var field = !string.IsNullOrEmpty(fieldName)
            ? (fieldName.Length == 1 ? fieldName.ToLowerInvariant() : char.ToLowerInvariant(fieldName[0]) + fieldName[1..])
            : "employeeId";
        return requiredGuidOrNothing
            .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Employee Id cannot be empty." })))
            .Ensure(x => x != Guid.Empty, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Employee Id cannot be empty." })))
            .Map(guid => new EmployeeId(guid));
     }

    public static Result<EmployeeId> TryCreate(string? stringOrNull, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity("EmployeeId.TryCreate");
        var field = !string.IsNullOrEmpty(fieldName)
            ? (fieldName.Length == 1 ? fieldName.ToLowerInvariant() : char.ToLowerInvariant(fieldName[0]) + fieldName[1..])
            : "employeeId";
        Guid parsedGuid = Guid.Empty;
        return stringOrNull
            .ToResult(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Employee Id cannot be empty." })))
            .Ensure(
                x => Guid.TryParse(x, out parsedGuid), 
                new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)" })))
            .Ensure(_ => parsedGuid != Guid.Empty, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Employee Id cannot be empty." })))
            .Map(guid => new EmployeeId(parsedGuid));
    }
}
```

### IParsable Implementation

Using IParsable<T> interface:

```csharp
public partial class ProductId : RequiredGuid
{
}

public class ProductService
{
    // Generic parsing using IParsable
    public T ParseGeneric<T>(string input) where T : IParsable<T>
    {
        return T.Parse(input, null);
    }
    
    // Usage
    public void UseGenericParsing()
    {
        var productId = ParseGeneric<ProductId>("550e8400-e29b-41d4-a716-446655440000");
        Console.WriteLine($"Parsed product ID: {productId}");
    }
    
    // TryParse pattern
    public bool TryParseProductId(string input, out ProductId? result)
    {
        return ProductId.TryParse(input, null, out result);
    }
    
    // Using in ASP.NET Core model binding
    [HttpGet("{id}")]
    public ActionResult<Product> GetProduct(ProductId id)
    {
        // Model binder automatically uses IParsable<ProductId>
        var product = _repository.GetById(id);
        return product.ToActionResult(this);
    }
}
```

## Advanced Patterns

### Combining Value Objects

Creating complex validation chains:

```csharp
public partial class FirstName : RequiredString
{
}

public partial class LastName : RequiredString
{
}

public partial class UserId : RequiredGuid
{
}

public class CreateUserRequest
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
}

public class UserService
{
    public Result<User> CreateUser(CreateUserRequest request)
    {
        // Combine multiple value object validations
        return FirstName.TryCreate(request.FirstName)
            .Combine(LastName.TryCreate(request.LastName))
            .Combine(EmailAddress.TryCreate(request.Email))
            .Bind((firstName, lastName, email) =>
                User.TryCreate(
                    UserId.NewUniqueV7(),
                    firstName,
                    lastName,
                    email));
    }
    
    // Async version
    public async Task<Result<User>> CreateUserAsync(CreateUserRequest request)
    {
        return await FirstName.TryCreate(request.FirstName)
            .Combine(LastName.TryCreate(request.LastName))
            .Combine(EmailAddress.TryCreate(request.Email))
            .BindAsync(async (firstName, lastName, email) =>
                await User.TryCreateAsync(
                    UserId.NewUniqueV7(),
                    firstName,
                    lastName,
                    email));
    }
}
```

### Custom Field Names

Using the optional fieldName parameter for better error messages:

```csharp
public partial class CustomerId : RequiredGuid
{
}

public partial class CustomerName : RequiredString
{
}

public class CustomerService
{
    public Result<Customer> CreateCustomer(CreateCustomerRequest request)
    {
        // Use fieldName parameter to customize error messages
        return CustomerId.TryCreate(request.Id, "customer.id")
            .Combine(CustomerName.TryCreate(request.Name, "customer.name"))
            .Combine(EmailAddress.TryCreate(request.Email, "customer.email"))
            .Bind((id, name, email) => Customer.TryCreate(id, name, email));
    }
}

// Validation error example:
// When request.Name is empty, error will be:
// {
//   "code": "validation.error",
//   "detail": "Customer Name cannot be empty.",
//   "fieldErrors": [{ "field": "customer.name", "details": ["Customer Name cannot be empty."] }]
// }
```

## Integration Examples

### Entity Framework Core

Configuring value objects with EF Core:

```csharp
public partial class UserId : RequiredGuid
{
}

public partial class OrderId : RequiredGuid
{
}

public class User : Entity<UserId>
{
    public EmailAddress Email { get; private set; }
    public string FirstName { get; }
    public string LastName { get; }
    
    // EF Core requires parameterless constructor
    private User() : base(UserId.NewUniqueV7())
    {
    }
    
    private User(UserId id, EmailAddress email, string firstName, string lastName)
        : base(id)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }
}

// EF Core configuration
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        
        // Convert UserId to Guid for database
        builder.Property(u => u.Id)
            .HasConversion(
                id => id.Value, // To database
                guid => (UserId)guid) // From database
            .IsRequired();
        
        // Convert EmailAddress to string for database
        builder.Property(u => u.Email)
            .HasConversion(
                email => email.Value,
                str => EmailAddress.Create(str))
            .HasMaxLength(100)
            .IsRequired();
    }
}
```

### ASP.NET Core

Using value objects in ASP.NET Core:

```csharp
// Model binding automatically uses IParsable<T>
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _repository;
    
    // UserId automatically parsed from route parameter
    [HttpGet("{id}")]
    public async Task<ActionResult<User>> GetUserAsync(
        UserId id)
    {
        var user = await _repository.GetByIdAsync(id)
            .ToResultAsync(new Error.NotFound(ResourceRef.For("User", id)) { Detail = $"User {id} not found" });
        
        return user.ToActionResult(this);
    }
    
    // Request body with value objects
    public record CreateUserRequest(
        string Email,
        string FirstName,
        string LastName
    );
    
    [HttpPost]
    public async Task<ActionResult<User>> CreateUserAsync(
        [FromBody] CreateUserRequest request)
    {
        var emailResult = await EmailAddress.TryCreate(request.Email)
            .BindAsync(validEmail =>
                User.TryCreateAsync(validEmail, request.FirstName, request.LastName));
        
        if (emailResult.IsFailure)
            return emailResult.ToActionResult(this);
        
        var user = emailResult.Value;
        await _repository.AddAsync(user);
        
        return user.ToActionResult(this);
    }
}
```

### JSON Serialization

Value objects include built-in JSON serialization support via the `[JsonConverter(typeof(ParsableJsonConverter<T>))]` attribute that is automatically added by the source generator:

```csharp
public partial class UserId : RequiredGuid
{
}

public partial class CustomerName : RequiredString
{
}

// The generated code already includes:
// [JsonConverter(typeof(ParsableJsonConverter<UserId>))]
// [JsonConverter(typeof(ParsableJsonConverter<CustomerName>))]

// Usage in DTOs - serialization works automatically
public record UserDto(
    UserId Id,
    CustomerName Name,
    EmailAddress Email
);

// JSON output:
// {
//   "id": "550e8400-e29b-41d4-a716-446655440000",
//   "name": "John Doe",
//   "email": "john@example.com"
// }

// For manual DTO mapping when needed:
public record UserResponseDto(
    string Id,
    string Email,
    string FirstName,
    string LastName
)
{
    public static UserResponseDto FromUser(User user) =>
        new(
            user.Id.Value.ToString(),
            user.Email.Value,
            user.FirstName,
            user.LastName);
    
    public Result<User> ToUser() =>
        UserId.TryCreate(Id)
            .Combine(EmailAddress.TryCreate(Email))
            .Bind((userId, email) =>
                User.TryCreate(userId, email, FirstName, LastName));
}
