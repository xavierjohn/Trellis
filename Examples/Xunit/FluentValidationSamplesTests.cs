using Trellis.Testing;
using Trellis.FluentValidation;

namespace Example.Tests;

using System.Collections.Immutable;
using FluentValidation;
using Trellis;
using Xunit;
using static Trellis.ValidationError;

/// <summary>
/// Comprehensive tests validating all examples from FluentValidation\SAMPLES.md
/// These tests prove that the sample code patterns work correctly.
/// </summary>
public class FluentValidationSamplesTests
{
    #region Test Data and Mock Domain Objects

    // Simple value objects for testing
    public record EmailAddress
    {
        public string Value { get; }
        private EmailAddress(string value) => Value = value;

        public static Result<EmailAddress> TryCreate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.EmailAddress>(Error.Validation("Email cannot be empty", nameof(value)));
            if (!value.Contains('@'))
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.EmailAddress>(Error.Validation("Email must contain @", nameof(value)));
            return Result.Ok(new EmailAddress(value));
        }
    }

    public record Username
    {
        public string Value { get; }
        private Username(string value) => Value = value;

        public static Result<Username> TryCreate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.Username>(Error.Validation("Username cannot be empty", nameof(value)));
            if (value.Length is < 3 or > 20)
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.Username>(Error.Validation("Username must be between 3 and 20 characters", nameof(value)));
            return Result.Ok(new Username(value));
        }
    }

    public record PhoneNumber
    {
        public string Value { get; }
        private PhoneNumber(string value) => Value = value;

        public static Result<PhoneNumber> TryCreate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.PhoneNumber>(Error.Validation("Phone number cannot be empty", nameof(value)));
            return Result.Ok(new PhoneNumber(value));
        }
    }

    public record CreateUserRequest(
        string Email,
        string Username,
        string Password);

    // Mock repositories for async validation tests
    public interface IUserRepository
    {
        Task<bool> ExistsEmailAsync(string email, CancellationToken ct);
        Task<bool> ExistsUsernameAsync(string username, CancellationToken ct);
    }

    public class MockUserRepository : IUserRepository
    {
        private readonly HashSet<string> _emails = new() { "existing@example.com" };
        private readonly HashSet<string> _usernames = new() { "existinguser" };

        public Task<bool> ExistsEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult(_emails.Contains(email));

        public Task<bool> ExistsUsernameAsync(string username, CancellationToken ct) =>
            Task.FromResult(_usernames.Contains(username));
    }

    public interface IEmailService
    {
        Task<bool> IsValidDomainAsync(string domain, CancellationToken ct);
    }

    public class MockEmailService : IEmailService
    {
        private readonly HashSet<string> _blockedDomains = new() { "spam.com", "blocked.net" };

        public Task<bool> IsValidDomainAsync(string domain, CancellationToken ct) =>
            Task.FromResult(!_blockedDomains.Contains(domain));
    }

    #endregion

    #region Async Validation Tests

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

            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8);
        }

        private async Task<bool> BeUniqueEmail(string email, CancellationToken ct)
        {
            var exists = await _repository.ExistsEmailAsync(email, ct);
            return !exists;
        }

        private async Task<bool> BeUniqueUsername(string username, CancellationToken ct)
        {
            var exists = await _repository.ExistsUsernameAsync(username, ct);
            return !exists;
        }

        private async Task<bool> BeValidDomain(string email, CancellationToken ct)
        {
            var domain = email.Split('@').LastOrDefault();
            if (string.IsNullOrEmpty(domain))
                return false;

            return await _emailService.IsValidDomainAsync(domain, ct);
        }
    }

    [Fact]
    public async Task AsyncValidation_ValidRequest_Succeeds()
    {
        // Arrange
        var repository = new MockUserRepository();
        var emailService = new MockEmailService();
        var validator = new CreateUserValidator(repository, emailService);
        var request = new CreateUserRequest(
            Email: "newuser@example.com",
            Username: "newuser123",
            Password: "SecurePass123");

        // Act
        var result = await validator.ValidateToResultAsync(request, cancellationToken: CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be(request);
    }

    [Fact]
    public async Task AsyncValidation_DuplicateEmail_Fails()
    {
        // Arrange
        var repository = new MockUserRepository();
        var emailService = new MockEmailService();
        var validator = new CreateUserValidator(repository, emailService);
        var request = new CreateUserRequest(
            Email: "existing@example.com",
            Username: "newuser123",
            Password: "SecurePass123");

        // Act
        var result = await validator.ValidateToResultAsync(request, cancellationToken: CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Email" && e.Details.Contains("Email is already registered"));
    }

    [Fact]
    public async Task AsyncValidation_BlockedDomain_Fails()
    {
        // Arrange
        var repository = new MockUserRepository();
        var emailService = new MockEmailService();
        var validator = new CreateUserValidator(repository, emailService);
        var request = new CreateUserRequest(
            Email: "user@spam.com",
            Username: "newuser123",
            Password: "SecurePass123");

        // Act
        var result = await validator.ValidateToResultAsync(request, cancellationToken: CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Email" && e.Details.Contains("Email domain is not allowed"));
    }

    [Fact]
    public async Task AsyncValidation_DuplicateUsername_Fails()
    {
        // Arrange
        var repository = new MockUserRepository();
        var emailService = new MockEmailService();
        var validator = new CreateUserValidator(repository, emailService);
        var request = new CreateUserRequest(
            Email: "newuser@example.com",
            Username: "existinguser",
            Password: "SecurePass123");

        // Act
        var result = await validator.ValidateToResultAsync(request, cancellationToken: CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Username" && e.Details.Contains("Username is already taken"));
    }

    [Fact]
    public async Task AsyncValidation_InvalidUsername_Fails()
    {
        // Arrange
        var repository = new MockUserRepository();
        var emailService = new MockEmailService();
        var validator = new CreateUserValidator(repository, emailService);
        var request = new CreateUserRequest(
            Email: "newuser@example.com",
            Username: "ab",  // Too short
            Password: "SecurePass123");

        // Act
        var result = await validator.ValidateToResultAsync(request, cancellationToken: CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e => e.FieldName == "Username");
    }

    [Fact]
    public async Task AsyncValidation_WithCancellationToken_Succeeds()
    {
        // Arrange
        var repository = new MockUserRepository();
        var emailService = new MockEmailService();
        var validator = new CreateUserValidator(repository, emailService);
        var request = new CreateUserRequest(
            Email: "newuser@example.com",
            Username: "newuser123",
            Password: "SecurePass123");
        using var cts = new CancellationTokenSource();

        // Act
        var result = await validator.ValidateToResultAsync(
            request,
            cancellationToken: cts.Token);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Integration with Combine Tests

    public record OrderLineRequest(string ProductId, int Quantity, decimal Price, string ProductName);

    public record ProductId
    {
        public string Value { get; }
        private ProductId(string value) => Value = value;

        public static Result<ProductId> TryCreate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.ProductId>(Error.Validation("Product ID cannot be empty", nameof(value)));
            return Result.Ok(new ProductId(value));
        }
    }

    public record Quantity
    {
        public int Value { get; }
        private Quantity(int value) => Value = value;

        public static Result<Quantity> TryCreate(int value)
        {
            if (value <= 0)
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.Quantity>(Error.Validation("Quantity must be positive", nameof(value)));
            if (value > 1000)
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.Quantity>(Error.Validation("Quantity cannot exceed 1000", nameof(value)));
            return Result.Ok(new Quantity(value));
        }
    }

    public record Price
    {
        public decimal Amount { get; }
        private Price(decimal amount) => Amount = amount;

        public static Result<Price> TryCreate(decimal value)
        {
            if (value <= 0)
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.Price>(Error.Validation("Price must be positive", nameof(value)));
            return Result.Ok(new Price(value));
        }
    }

    public record OrderLine
    {
        public ProductId ProductId { get; }
        public string ProductName { get; }
        public Price Price { get; }
        public Quantity Quantity { get; }

        private OrderLine(ProductId productId, string productName, Price price, Quantity quantity)
        {
            ProductId = productId;
            ProductName = productName;
            Price = price;
            Quantity = quantity;
        }

        public static Result<OrderLine> TryCreate(
            ProductId productId,
            string productName,
            Price price,
            Quantity quantity)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return Result.Fail<Example.Tests.FluentValidationSamplesTests.OrderLine>(Error.Validation("Product name cannot be empty"));

            return Result.Ok(new OrderLine(productId, productName, price, quantity));
        }
    }

    [Fact]
    public void IntegrationWithCombine_ValidOrderLine_Succeeds()
    {
        // Arrange
        var orderLineRequest = new OrderLineRequest(
            ProductId: "PROD-001",
            Quantity: 5,
            Price: 29.99m,
            ProductName: "Test Product");

        // Act - Validate and create value objects, then create order line
        var result = ProductId.TryCreate(orderLineRequest.ProductId)
            .Combine(Quantity.TryCreate(orderLineRequest.Quantity))
            .Combine(Price.TryCreate(orderLineRequest.Price))
            .Bind((productId, quantity, price) =>
                OrderLine.TryCreate(productId, orderLineRequest.ProductName, price, quantity));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().ProductId.Value.Should().Be("PROD-001");
        result.Unwrap().Quantity.Value.Should().Be(5);
        result.Unwrap().Price.Amount.Should().Be(29.99m);
    }

    [Fact]
    public void IntegrationWithCombine_InvalidQuantity_Fails()
    {
        // Arrange
        var orderLineRequest = new OrderLineRequest(
            ProductId: "PROD-001",
            Quantity: 0,  // Invalid
            Price: 29.99m,
            ProductName: "Test Product");

        // Act
        var result = ProductId.TryCreate(orderLineRequest.ProductId)
            .Combine(Quantity.TryCreate(orderLineRequest.Quantity))
            .Combine(Price.TryCreate(orderLineRequest.Price))
            .Bind((productId, quantity, price) =>
                OrderLine.TryCreate(productId, orderLineRequest.ProductName, price, quantity));

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "value" && e.Details.Contains("Quantity must be positive"));
    }

    [Fact]
    public void IntegrationWithCombine_MultipleInvalid_CollectsAllErrors()
    {
        // Arrange
        var orderLineRequest = new OrderLineRequest(
            ProductId: "",  // Invalid
            Quantity: -1,   // Invalid
            Price: -5.0m,   // Invalid
            ProductName: "Test Product");

        // Act
        var result = ProductId.TryCreate(orderLineRequest.ProductId)
            .Combine(Quantity.TryCreate(orderLineRequest.Quantity))
            .Combine(Price.TryCreate(orderLineRequest.Price))
            .Bind((productId, quantity, price) =>
                OrderLine.TryCreate(productId, orderLineRequest.ProductName, price, quantity));

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        // Combine groups all errors into a single FieldError with Details array
        validationError.FieldErrors.Should().HaveCount(1);
        validationError.FieldErrors[0].Details.Should().HaveCount(3);
        validationError.FieldErrors[0].Details.Should().Contain(d => d.Contains("Product ID"));
        validationError.FieldErrors[0].Details.Should().Contain(d => d.Contains("Quantity"));
        validationError.FieldErrors[0].Details.Should().Contain(d => d.Contains("Price"));
    }

    [Fact]
    public void IntegrationWithCombine_TraverseMultipleItems_Succeeds()
    {
        // Arrange
        var requests = new List<OrderLineRequest>
        {
            new("PROD-001", 5, 29.99m, "Product 1"),
            new("PROD-002", 3, 49.99m, "Product 2"),
            new("PROD-003", 1, 99.99m, "Product 3")
        };

        // Act
        var result = requests.Traverse(req =>
            ProductId.TryCreate(req.ProductId)
                .Combine(Quantity.TryCreate(req.Quantity))
                .Combine(Price.TryCreate(req.Price))
                .Bind((productId, quantity, price) =>
                    OrderLine.TryCreate(productId, req.ProductName, price, quantity)));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().HaveCount(3);
    }

    [Fact]
    public void IntegrationWithCombine_TraverseWithOneInvalid_Fails()
    {
        // Arrange
        var requests = new List<OrderLineRequest>
        {
            new("PROD-001", 5, 29.99m, "Product 1"),
            new("PROD-002", 0, 49.99m, "Product 2"),  // Invalid quantity
            new("PROD-003", 1, 99.99m, "Product 3")
        };

        // Act
        var result = requests.Traverse(req =>
            ProductId.TryCreate(req.ProductId)
                .Combine(Quantity.TryCreate(req.Quantity))
                .Combine(Price.TryCreate(req.Price))
                .Bind((productId, quantity, price) =>
                    OrderLine.TryCreate(productId, req.ProductName, price, quantity)));

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Conditional Validation Tests

    public enum OrderType { Physical, Digital }
    public enum ShippingMethod { Standard, Express }
    public enum CustomerType { Individual, Business }

    public record OrderRequest(
        OrderType OrderType,
        string? ShippingAddress,
        string? Email,
        ShippingMethod ShippingMethod,
        decimal TotalAmount,
        CustomerType CustomerType,
        string? TaxId);

    public class OrderValidator : AbstractValidator<OrderRequest>
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

            // Express shipping has restrictions
            RuleFor(x => x.TotalAmount)
                .LessThanOrEqualTo(10000)
                .When(x => x.ShippingMethod == ShippingMethod.Express)
                .WithMessage("Express shipping not available for orders over $10,000");

            // Business orders require tax ID
            RuleFor(x => x.TaxId)
                .NotEmpty()
                .When(x => x.CustomerType == CustomerType.Business)
                .WithMessage("Tax ID required for business orders");
        }
    }

    [Fact]
    public void ConditionalValidation_PhysicalOrderWithAddress_Succeeds()
    {
        // Arrange
        var validator = new OrderValidator();
        var request = new OrderRequest(
            OrderType: OrderType.Physical,
            ShippingAddress: "123 Main St",
            Email: null,
            ShippingMethod: ShippingMethod.Standard,
            TotalAmount: 100m,
            CustomerType: CustomerType.Individual,
            TaxId: null);

        // Act
        var result = validator.ValidateToResult(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ConditionalValidation_PhysicalOrderWithoutAddress_Fails()
    {
        // Arrange
        var validator = new OrderValidator();
        var request = new OrderRequest(
            OrderType: OrderType.Physical,
            ShippingAddress: null,
            Email: null,
            ShippingMethod: ShippingMethod.Standard,
            TotalAmount: 100m,
            CustomerType: CustomerType.Individual,
            TaxId: null);

        // Act
        var result = validator.ValidateToResult(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "ShippingAddress" &&
            e.Details.Contains("Shipping address is required for physical orders"));
    }

    [Fact]
    public void ConditionalValidation_DigitalOrderWithEmail_Succeeds()
    {
        // Arrange
        var validator = new OrderValidator();
        var request = new OrderRequest(
            OrderType: OrderType.Digital,
            ShippingAddress: null,
            Email: "customer@example.com",
            ShippingMethod: ShippingMethod.Standard,
            TotalAmount: 100m,
            CustomerType: CustomerType.Individual,
            TaxId: null);

        // Act
        var result = validator.ValidateToResult(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ConditionalValidation_DigitalOrderWithoutEmail_Fails()
    {
        // Arrange
        var validator = new OrderValidator();
        var request = new OrderRequest(
            OrderType: OrderType.Digital,
            ShippingAddress: null,
            Email: null,
            ShippingMethod: ShippingMethod.Standard,
            TotalAmount: 100m,
            CustomerType: CustomerType.Individual,
            TaxId: null);

        // Act
        var result = validator.ValidateToResult(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        // FluentValidation's default message is "'Email' must not be empty."
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Email" &&
            e.Details.Any(d => d.Contains("must not be empty")));
    }

    [Fact]
    public void ConditionalValidation_ExpressShippingOverLimit_Fails()
    {
        // Arrange
        var validator = new OrderValidator();
        var request = new OrderRequest(
            OrderType: OrderType.Physical,
            ShippingAddress: "123 Main St",
            Email: null,
            ShippingMethod: ShippingMethod.Express,
            TotalAmount: 15000m,
            CustomerType: CustomerType.Individual,
            TaxId: null);

        // Act
        var result = validator.ValidateToResult(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "TotalAmount" &&
            e.Details.Contains("Express shipping not available for orders over $10,000"));
    }

    [Fact]
    public void ConditionalValidation_BusinessOrderWithTaxId_Succeeds()
    {
        // Arrange
        var validator = new OrderValidator();
        var request = new OrderRequest(
            OrderType: OrderType.Physical,
            ShippingAddress: "123 Main St",
            Email: null,
            ShippingMethod: ShippingMethod.Standard,
            TotalAmount: 100m,
            CustomerType: CustomerType.Business,
            TaxId: "12-3456789");

        // Act
        var result = validator.ValidateToResult(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ConditionalValidation_BusinessOrderWithoutTaxId_Fails()
    {
        // Arrange
        var validator = new OrderValidator();
        var request = new OrderRequest(
            OrderType: OrderType.Physical,
            ShippingAddress: "123 Main St",
            Email: null,
            ShippingMethod: ShippingMethod.Standard,
            TotalAmount: 100m,
            CustomerType: CustomerType.Business,
            TaxId: null);

        // Act
        var result = validator.ValidateToResult(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "TaxId" &&
            e.Details.Contains("Tax ID required for business orders"));
    }

    #endregion

    #region Custom Error Messages Tests

    public record Product(string Name, decimal Price, int Stock, decimal? Discount = null);

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

            // Complex messages with multiple properties
            RuleFor(x => x.Discount)
                .LessThanOrEqualTo(x => x.Price * 0.5m)
                .When(x => x.Discount.HasValue)
                .WithMessage(x =>
                    $"Discount ${x.Discount} exceeds 50% of price ${x.Price}");
        }
    }

    [Fact]
    public void CustomErrorMessages_InvalidPrice_ShowsActualValue()
    {
        // Arrange
        var validator = new ProductValidator();
        var product = new Product("Test Product", -5.0m, 100);

        // Act
        var result = validator.ValidateToResult(product);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Price" &&
            e.Details.Any(d => d.Contains("$-5") && d.Contains("must be greater than $0")));
    }

    [Fact]
    public void CustomErrorMessages_ExcessiveStock_ShowsActualValue()
    {
        // Arrange
        var validator = new ProductValidator();
        var product = new Product("Test Product", 10.0m, 15000);

        // Act
        var result = validator.ValidateToResult(product);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Stock" &&
            e.Details.Any(d => d.Contains("15000") && d.Contains("exceeds maximum of 10,000")));
    }

    [Fact]
    public void CustomErrorMessages_InvalidNameLength_ShowsActualLength()
    {
        // Arrange
        var validator = new ProductValidator();
        var product = new Product("AB", 10.0m, 100);

        // Act
        var result = validator.ValidateToResult(product);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Name" &&
            e.Details.Any(d => d.Contains("current: 2")));
    }

    [Fact]
    public void CustomErrorMessages_ExcessiveDiscount_ShowsBothValues()
    {
        // Arrange
        var validator = new ProductValidator();
        var product = new Product("Test Product", 100.0m, 50, Discount: 60.0m);

        // Act
        var result = validator.ValidateToResult(product);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Discount" &&
            e.Details.Any(d => d.Contains("$60") && d.Contains("$100")));
    }

    #endregion

    #region Collection Validation Tests

    public record BatchOrder(List<string> OrderIds);

    public class BatchOrderValidator : AbstractValidator<BatchOrder>
    {
        public BatchOrderValidator()
        {
            // Validate collection itself
            RuleFor(x => x.OrderIds)
                .NotEmpty()
                .WithMessage("Batch must contain at least one order")
                .Must(orders => orders.Count <= 100)
                .WithMessage(x =>
                    $"Batch contains {x.OrderIds.Count} orders, maximum is 100");

            // Validate no duplicates
            RuleFor(x => x.OrderIds)
                .Must(orders => orders.Distinct().Count() == orders.Count)
                .WithMessage("Batch contains duplicate order IDs");
        }
    }

    [Fact]
    public void CollectionValidation_ValidBatch_Succeeds()
    {
        // Arrange
        var validator = new BatchOrderValidator();
        var batch = new BatchOrder(new List<string> { "ORD-001", "ORD-002", "ORD-003" });

        // Act
        var result = validator.ValidateToResult(batch);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CollectionValidation_EmptyBatch_Fails()
    {
        // Arrange
        var validator = new BatchOrderValidator();
        var batch = new BatchOrder(new List<string>());

        // Act
        var result = validator.ValidateToResult(batch);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "OrderIds" &&
            e.Details.Contains("Batch must contain at least one order"));
    }

    [Fact]
    public void CollectionValidation_TooManyOrders_Fails()
    {
        // Arrange
        var validator = new BatchOrderValidator();
        var orderIds = Enumerable.Range(1, 101).Select(i => $"ORD-{i:D3}").ToList();
        var batch = new BatchOrder(orderIds);

        // Act
        var result = validator.ValidateToResult(batch);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "OrderIds" &&
            e.Details.Any(d => d.Contains("101") && d.Contains("maximum is 100")));
    }

    [Fact]
    public void CollectionValidation_DuplicateOrders_Fails()
    {
        // Arrange
        var validator = new BatchOrderValidator();
        var batch = new BatchOrder(new List<string> { "ORD-001", "ORD-002", "ORD-001" });

        // Act
        var result = validator.ValidateToResult(batch);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "OrderIds" &&
            e.Details.Contains("Batch contains duplicate order IDs"));
    }

    #endregion

    #region Nested Object Validation Tests

    public record Address(string Street, string City, string PostalCode, string Country);
    public record ContactInfo(string Email, string Phone);

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
        }
    }

    public record Customer(string Name, ContactInfo ContactInfo, Address BillingAddress);

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
        }
    }

    [Fact]
    public void NestedValidation_ValidCustomer_Succeeds()
    {
        // Arrange
        var validator = new CustomerValidator();
        var customer = new Customer(
            Name: "John Doe",
            ContactInfo: new ContactInfo("john@example.com", "+12345678901"),
            BillingAddress: new Address("123 Main St", "Springfield", "12345", "US"));

        // Act
        var result = validator.ValidateToResult(customer);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NestedValidation_InvalidContactInfo_Fails()
    {
        // Arrange
        var validator = new CustomerValidator();
        var customer = new Customer(
            Name: "John Doe",
            ContactInfo: new ContactInfo("not-an-email", "invalid-phone"),
            BillingAddress: new Address("123 Main St", "Springfield", "12345", "US"));

        // Act
        var result = validator.ValidateToResult(customer);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        // Both Email and Phone should have validation errors
        validationError.FieldErrors.Should().Contain(e => e.FieldName.Contains("Email"));
        validationError.FieldErrors.Should().Contain(e => e.FieldName.Contains("Phone"));
    }

    [Fact]
    public void NestedValidation_InvalidAddress_Fails()
    {
        // Arrange
        var validator = new CustomerValidator();
        var customer = new Customer(
            Name: "John Doe",
            ContactInfo: new ContactInfo("john@example.com", "+12345678901"),
            BillingAddress: new Address("", "Springfield", "ABCDE", "USA"));

        // Act
        var result = validator.ValidateToResult(customer);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e => e.FieldName.Contains("Street"));
        validationError.FieldErrors.Should().Contain(e => e.FieldName.Contains("PostalCode"));
        validationError.FieldErrors.Should().Contain(e => e.FieldName.Contains("Country"));
    }

    [Fact]
    public void NestedValidation_NullContactInfo_Fails()
    {
        // Arrange
        var validator = new CustomerValidator();
        var customer = new Customer(
            Name: "John Doe",
            ContactInfo: null!,
            BillingAddress: new Address("123 Main St", "Springfield", "12345", "US"));

        // Act
        var result = validator.ValidateToResult(customer);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "ContactInfo" &&
            e.Details.Contains("Contact information is required"));
    }

    #endregion

    #region Null Value Validation Tests

    [Fact]
    public void NullValueValidation_WithDefaultMessage_Fails()
    {
        // Arrange
        string? value = null;
        var validator = new InlineValidator<string?>
        {
            v => v.RuleFor(x => x)
                .NotEmpty()
        };

        // Act
        var result = validator.ValidateToResult(value);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "value" &&
            e.Details.Contains("'value' must not be empty."));
    }

    [Fact]
    public void NullValueValidation_WithCustomMessage_Fails()
    {
        // Arrange
        string? alias = null;
        var validator = new InlineValidator<string?>
        {
            v => v.RuleFor(x => x)
                .NotEmpty()
        };

        // Act
        var result = validator.ValidateToResult(alias, paramName: "Alias", message: "Hello There");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        // ValidateToResult uses custom paramName and message for null values
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "Alias" &&
            e.Details.Contains("Hello There"));
    }

    [Fact]
    public async Task NullValueValidation_AsyncVersion_Fails()
    {
        // Arrange
        string? value = null;
        var validator = new InlineValidator<string?>
        {
            v => v.RuleFor(x => x)
                .NotEmpty()
        };

        // Act
        var result = await validator.ValidateToResultAsync(value, cancellationToken: CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.UnwrapError();
        validationError.FieldErrors.Should().Contain(e =>
            e.FieldName == "value" &&
            e.Details.Contains("'value' must not be empty."));
    }

    #endregion
}
